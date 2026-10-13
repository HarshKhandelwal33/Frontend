"""Local opt-in microphone worker. JSON-lines protocol shared with speech.ps1."""
import collections
import json
import math
import os
from pathlib import Path
import queue
import sys
import threading
import time


def emit(**item):
    print(json.dumps(item, ensure_ascii=False), flush=True)


class AudioFrames:
    """Bounded capture buffer; discard audio during decoding and recover dropped input."""
    def __init__(self, capacity=100):
        self.frames = queue.Queue(maxsize=capacity)
        self.overflow = threading.Event()
        self.accepting = threading.Event()
        self.accepting.set()

    def capture(self, data, count, timing, status):
        if not self.accepting.is_set():
            return
        if getattr(status, "input_overflow", False):
            self.overflow.set()
            return
        try:
            self.frames.put_nowait(bytes(data))
        except queue.Full:
            self.overflow.set()

    def discard(self):
        while True:
            try:
                self.frames.get_nowait()
            except queue.Empty:
                break
        self.overflow.clear()

    def begin_decode(self):
        self.accepting.clear()
        self.discard()

    def end_decode(self):
        self.discard()
        self.accepting.set()


class Utterances:
    """30-ms frames: retain onset padding, end after 720-ms silence, cap at 15s."""
    def __init__(self):
        self.padding = collections.deque(maxlen=10)
        self.frames = []
        self.silence = 0
        self.voiced = 0

    def push(self, frame, speech):
        if not self.frames:
            self.padding.append(frame)
            if not speech:
                return None
            self.frames = list(self.padding)
            self.padding.clear()
        else:
            self.frames.append(frame)
        self.voiced += int(speech)
        self.silence = 0 if speech else self.silence + 1
        if self.silence < 24 and len(self.frames) < 500:
            return None
        audio = b"".join(self.frames) if self.voiced >= 3 else None
        self.frames = []
        self.voiced = self.silence = 0
        return audio


def transcript(segments):
    segments = list(segments)
    text = " ".join(s.text.strip() for s in segments).strip()
    # Decoder likelihood is a heuristic score, not a calibrated confidence.
    score = math.exp(min(0, min((s.avg_logprob for s in segments), default=-100)))
    accepted = bool(text) and all(s.no_speech_prob < 0.6 and s.avg_logprob > -1 for s in segments)
    return dict(text=text, confidence=score, accepted=accepted, engine="faster-whisper")


def main():
    from faster_whisper import WhisperModel
    cache = Path(__file__).parent / "data" / "speech-models"
    model = WhisperModel(os.getenv("WHISPER_MODEL", "small.en"), device="cpu", compute_type="int8",
                         cpu_threads=4, download_root=str(cache), local_files_only="--prepare" not in sys.argv)
    if "--prepare" in sys.argv:
        emit(prepared=True, model=os.getenv("WHISPER_MODEL", "small.en"))
        return
    if "--transcribe" in sys.argv:
        source = sys.argv[sys.argv.index("--transcribe") + 1]
        segments, _ = model.transcribe(source, language=os.getenv("WHISPER_LANGUAGE", "en"),
            beam_size=1, condition_on_previous_text=False, vad_filter=True)
        emit(**transcript(segments))
        return
    # Import and open input only in the opt-in listener subprocess.
    import numpy as np
    import sounddevice as sd
    import webrtcvad
    capture_buffer = AudioFrames()

    vad = webrtcvad.Vad(2)
    collector = Utterances()
    next_level = 0
    with sd.RawInputStream(samplerate=16000, blocksize=480, channels=1, dtype="int16",
                          latency="high", callback=capture_buffer.capture):
        emit(ready=True)
        while True:
            frame = capture_buffer.frames.get(timeout=5)
            if capture_buffer.overflow.is_set():
                # A damaged utterance must not become a stale or partial command.
                capture_buffer.begin_decode()
                collector = Utterances()
                capture_buffer.end_decode()
                emit(audio_level=0, audio_state="Listening")
                continue
            samples = np.frombuffer(frame, dtype=np.int16).astype(np.float32) / 32768
            speech = vad.is_speech(frame, 16000)
            if time.monotonic() >= next_level:
                emit(audio_level=min(100, int(np.sqrt(np.mean(samples ** 2)) * 500)),
                     audio_state="Speech" if speech else "Silence")
                next_level = time.monotonic() + 0.1
            audio = collector.push(frame, speech)
            if audio is None:
                continue
            capture_buffer.begin_decode()
            emit(audio_level=0, audio_state="Transcribing")
            segments, _ = model.transcribe(np.frombuffer(audio, dtype=np.int16).astype(np.float32) / 32768,
                language=os.getenv("WHISPER_LANGUAGE", "en"), beam_size=1,
                condition_on_previous_text=False, vad_filter=True)
            result = transcript(segments)
            if result["text"]:
                emit(**result)
            # Do not replay old commands captured while decoding.
            collector = Utterances()
            capture_buffer.end_decode()


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        emit(error=f"Local Whisper speech failed: {exc}. Run backend/start.ps1 to install dependencies and prepare the model.")
        sys.exit(1)
