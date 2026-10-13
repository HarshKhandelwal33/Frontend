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
    """30-ms frames: retain onset padding, end after 360-ms silence, cap at 15s."""
    def __init__(self, silence_frames=12):
        self.silence_frames = silence_frames
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
        if self.silence < self.silence_frames and len(self.frames) < 500:
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


class ListenerControl:
    """Keep the model warm while opening the microphone only during an active lease."""
    def __init__(self, enabled=False):
        self.condition = threading.Condition()
        self.enabled = enabled
        self.closed = False
        self.generation = 0

    def set_enabled(self, enabled):
        with self.condition:
            if self.enabled != enabled:
                self.generation += 1
            self.enabled = enabled
            self.condition.notify_all()

    def close(self):
        with self.condition:
            self.closed = True
            self.enabled = False
            self.generation += 1
            self.condition.notify_all()

    def wait_enabled(self):
        with self.condition:
            self.condition.wait_for(lambda: self.enabled or self.closed)
            return not self.closed

    def valid(self, generation):
        with self.condition:
            return self.enabled and not self.closed and self.generation == generation

    def read_commands(self, source):
        try:
            for line in source:
                try:
                    message = json.loads(line)
                    if isinstance(message.get("enabled"), bool):
                        self.set_enabled(message["enabled"])
                except (ValueError, AttributeError):
                    continue
        finally:
            self.close()


def main():
    from faster_whisper import WhisperModel
    cache = Path(__file__).parent / "data" / "speech-models"
    model = WhisperModel(os.getenv("WHISPER_MODEL", "base.en"), device="cpu", compute_type="int8",
                         cpu_threads=4, download_root=str(cache), local_files_only="--prepare" not in sys.argv)
    if "--prepare" in sys.argv:
        emit(prepared=True, model=os.getenv("WHISPER_MODEL", "base.en"))
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

    control = ListenerControl(enabled="--controlled" not in sys.argv)
    if "--controlled" in sys.argv:
        threading.Thread(target=control.read_commands, args=(sys.stdin,), daemon=True).start()

    vad = webrtcvad.Vad(2)
    silence_frames = max(6, min(50, int(os.getenv("WHISPER_SILENCE_MS", "360")) // 30))
    while control.wait_enabled():
        generation = control.generation
        collector = Utterances(silence_frames)
        capture_buffer.end_decode()
        with sd.RawInputStream(samplerate=16000, blocksize=480, channels=1, dtype="int16",
                              latency="high", callback=capture_buffer.capture):
            emit(ready=control.valid(generation))
            while control.valid(generation):
                try:
                    frame = capture_buffer.frames.get(timeout=0.2)
                except queue.Empty:
                    continue
                process_frame(frame, capture_buffer, control, generation, collector, vad, model, np)
        capture_buffer.begin_decode()
        emit(ready=False, audio_level=0, audio_state="Paused")


def process_frame(frame, capture_buffer, control, generation, collector, vad, model, np):
    if not control.valid(generation):
        return
    if capture_buffer.overflow.is_set():
        # A damaged utterance must not become a stale or partial command.
        capture_buffer.begin_decode()
        collector.__init__(collector.silence_frames)
        capture_buffer.end_decode()
        emit(audio_level=0, audio_state="Listening")
        return
    samples = np.frombuffer(frame, dtype=np.int16).astype(np.float32) / 32768
    speech = vad.is_speech(frame, 16000)
    if time.monotonic() >= getattr(collector, "next_level", 0):
        emit(audio_level=min(100, int(np.sqrt(np.mean(samples ** 2)) * 500)),
             audio_state="Speech" if speech else "Silence")
        collector.next_level = time.monotonic() + 0.1
    audio = collector.push(frame, speech)
    if audio is None:
        return
    capture_buffer.begin_decode()
    emit(audio_level=0, audio_state="Transcribing")
    started = time.monotonic()
    segments, _ = model.transcribe(np.frombuffer(audio, dtype=np.int16).astype(np.float32) / 32768,
        language=os.getenv("WHISPER_LANGUAGE", "en"), beam_size=1,
        condition_on_previous_text=False, vad_filter=True)
    result = transcript(segments)
    result["transcription_ms"] = round((time.monotonic() - started) * 1000)
    if result["text"] and control.valid(generation):
        emit(**result)
    # Do not replay old commands captured while decoding or across a pause/resume.
    collector.__init__(collector.silence_frames)
    capture_buffer.end_decode()


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        emit(error=f"Local Whisper speech failed: {exc}. Run backend/start.ps1 to install dependencies and prepare the model.")
        sys.exit(1)
