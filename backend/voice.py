"""Local Windows speech adapter. No microphone access until explicitly enabled."""
import atexit
import json
import os
import queue
import subprocess
import threading
import time
import time
from pathlib import Path

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel, Field


class MicRequest(BaseModel):
    enabled: bool


class SpeakRequest(BaseModel):
    text: str = Field(min_length=1, max_length=6000)


class WindowsVoice:
    def __init__(self):
        self.lock = threading.RLock()
        self.enabled = False
        self.speaking = False
        self.ready = False
        self.error = ""
        self.listener = None
        self.lease_until = 0
        self.lease_until = 0
        self.speaker = None
        self.events = queue.Queue(maxsize=20)
        atexit.register(self.close)
        self.shutdown = threading.Event()
        threading.Thread(target=self.watch_lease, daemon=True).start()

    def watch_lease(self):
        while not self.shutdown.wait(2):
            with self.lock:
                if self.enabled and time.monotonic() > self.lease_until:
                    self.enabled = False
                    self.stop_listener()
        self.shutdown = threading.Event()
        threading.Thread(target=self.watch_lease, daemon=True).start()

    def watch_lease(self):
        while not self.shutdown.wait(2):
            with self.lock:
                if self.enabled and time.monotonic() > self.lease_until:
                    self.enabled = False
                    self.stop_listener()

    def spawn(self, mode):
        if os.name != "nt":
            raise RuntimeError("Voice adapter requires Windows")
        return subprocess.Popen(["powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
            "-File", str(Path(__file__).with_name("speech.ps1")), "-Mode", mode,
            "-Culture", os.getenv("SPEECH_CULTURE", "en-US")],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            text=True, encoding="utf-8", creationflags=subprocess.CREATE_NO_WINDOW)

    def stop_listener(self):
        process = self.listener
        self.listener = None
        self.ready = False
        if process and process.poll() is None:
            process.terminate()
            process.wait(timeout=5)
        while not self.events.empty():
            try:
                self.events.get_nowait()
            except queue.Empty:
                break

    def start_listener(self):
        if not self.enabled or self.speaking or self.listener:
            return
        self.error = ""
        try:
            self.listener = self.spawn("listen")
            threading.Thread(target=self.read_listener, args=(self.listener,), daemon=True).start()
        except (OSError, RuntimeError) as exc:
            self.error = str(exc)
            self.enabled = False

    def read_listener(self, process):
        try:
            for line in process.stdout:
                try:
                    item = json.loads(line)
                except ValueError:
                    continue
                with self.lock:
                    if process != self.listener:
                        continue
                    if item.get("ready"):
                        self.ready = True
                    elif "error" in item:
                        self.error = item["error"]
                    elif self.ready and item.get("confidence", 0) >= 0.55 and not self.events.full():
                        self.events.put(item)
            with self.lock:
                if process == self.listener:
                    self.listener = None
                    self.ready = False
                    self.enabled = False
                    self.error = self.error or "Speech recognition stopped. Check installed speech language and microphone."
        finally:
            process.stdout.close()
            process.stdin.close()

    def mic(self, enabled):
        with self.lock:
            self.enabled = enabled
            self.lease_until = time.monotonic() + 20
            self.lease_until = time.monotonic() + 20
            if enabled:
                self.start_listener()
            else:
                self.stop_listener()

    def speak(self, text):
        with self.lock:
            if self.speaking:
                raise HTTPException(409, "Tutor is already speaking")
            self.stop_listener()
            try:
                self.speaker = self.spawn("speak")
            except (OSError, RuntimeError) as exc:
                self.start_listener()
                raise HTTPException(503, str(exc)) from exc
            self.speaking = True
            threading.Thread(target=self.finish_speech, args=(self.speaker, text), daemon=True).start()

    def finish_speech(self, process, text):
        try:
            output, _ = process.communicate(text, timeout=120)
            with self.lock:
                if process.returncode:
                    self.error = output.strip()[:500] or "Speech playback failed"
        except subprocess.TimeoutExpired:
            process.kill()
            process.communicate()
        finally:
            with self.lock:
                if self.speaker == process:
                    self.speaking = False
                    self.speaker = None
                    self.start_listener()

    def status(self):
        with self.lock:
            events = []
            while not self.events.empty():
                try:
                    events.append(self.events.get_nowait())
                except queue.Empty:
                    break
            return {"enabled": self.enabled, "listening": self.ready, "speaking": self.speaking,
                    "error": self.error, "utterances": events}

    def close(self):
        self.shutdown.set()
        self.shutdown.set()
        with self.lock:
            self.enabled = False
            self.stop_listener()
            if self.speaker and self.speaker.poll() is None:
                self.speaker.terminate()


def voice_router():
    router = APIRouter(prefix="/voice", tags=["Local Windows voice"])
    voice = WindowsVoice()

    @router.post("/microphone")
    def microphone(body: MicRequest):
        voice.mic(body.enabled)
        return {"enabled": voice.enabled}

    @router.get("/status")
    def status():
        return voice.status()

    @router.post("/speak")
    def speak(body: SpeakRequest):
        voice.speak(body.text)
        return {"accepted": True}

    @router.post("/silence")
    def silence():
        with voice.lock:
            if voice.speaker and voice.speaker.poll() is None:
                voice.speaker.terminate()
        return {"accepted": True}

    return router, voice
