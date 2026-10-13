"""Local opt-in microphone worker. JSON-lines protocol shared with speech.ps1."""
import collections
import json
import math
import os
from pathlib import Path
import queue
import sys
import threading


def emit(**item):
    print(json.dumps(item, ensure_ascii=False), flush=True)


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
    frames = queue.Queue(maxsize=100)
    overflow = threading.Event()

    def capture(data, count, timing, status):
        if status:
            overflow.set()
        try:
            frames.put_nowait(bytes(data))
        except queue.Full:
            overflow.set()

    vad = webrtcvad.Vad(2)
    collector = Utterances()
    with sd.RawInputStream(samplerate=16000, blocksize=480, channels=1, dtype="int16", callback=capture):
        emit(ready=True)
        while True:
            frame = frames.get(timeout=5)
            if overflow.is_set():
                raise RuntimeError("Microphone audio overflow. Try WHISPER_MODEL=base.en or a faster audio device.")
            samples = np.frombuffer(frame, dtype=np.int16).astype(np.float32) / 32768
            speech = vad.is_speech(frame, 16000)
            emit(audio_level=min(100, int(np.sqrt(np.mean(samples ** 2)) * 500)),
                 audio_state="Speech" if speech else "Silence")
            audio = collector.push(frame, speech)
            if audio is None:
                continue
            emit(audio_level=0, audio_state="Transcribing")
            segments, _ = model.transcribe(np.frombuffer(audio, dtype=np.int16).astype(np.float32) / 32768,
                language=os.getenv("WHISPER_LANGUAGE", "en"), beam_size=1,
                condition_on_previous_text=False, vad_filter=True)
            result = transcript(segments)
            if result["text"]:
                emit(**result)
            # Do not replay old commands captured while decoding.
            while not frames.empty():
                frames.get_nowait()
            collector = Utterances()


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        emit(error=f"Local Whisper speech failed: {exc}. Run backend/start.ps1 to install dependencies and prepare the model.")
        sys.exit(1)
