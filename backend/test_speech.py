import io
import unittest
from types import SimpleNamespace
from unittest.mock import patch, Mock

from backend.whisper_listener import AudioFrames, ListenerControl, Utterances, transcript
from backend.voice import WindowsVoice


class SpeechTests(unittest.TestCase):
    def test_decode_audio_is_discarded_without_overflow_or_stale_commands(self):
        buffer = AudioFrames(capacity=2)
        buffer.capture(b"before", 0, None, None)
        buffer.begin_decode()
        for _ in range(1000):
            buffer.capture(b"stale", 0, None, SimpleNamespace(input_overflow=True))
        self.assertTrue(buffer.frames.empty())
        self.assertFalse(buffer.overflow.is_set())
        buffer.end_decode()
        buffer.capture(b"fresh", 0, None, None)
        self.assertEqual(buffer.frames.get_nowait(), b"fresh")

    def test_real_capture_overflow_can_recover(self):
        buffer = AudioFrames(capacity=1)
        buffer.capture(b"first", 0, None, None)
        buffer.capture(b"overflow", 0, None, None)
        self.assertTrue(buffer.overflow.is_set())
        buffer.begin_decode()
        buffer.end_decode()
        self.assertFalse(buffer.overflow.is_set())
        buffer.capture(b"new", 0, None, None)
        self.assertEqual(buffer.frames.get_nowait(), b"new")

    def test_silence_is_not_an_utterance(self):
        collector = Utterances()
        self.assertTrue(all(collector.push(b"s", False) is None for _ in range(1000)))

    def test_onset_padding_and_end_of_speech(self):
        collector = Utterances()
        for _ in range(5):
            collector.push(b"p", False)
        for _ in range(3):
            collector.push(b"v", True)
        for _ in range(11):
            self.assertIsNone(collector.push(b"s", False))
        self.assertEqual(collector.push(b"s", False), b"p" * 5 + b"v" * 3 + b"s" * 12)
        self.assertIsNone(collector.push(b"s", False))

    def test_noise_blip_is_rejected_and_continuous_speech_is_bounded(self):
        collector = Utterances()
        collector.push(b"n", True)
        for _ in range(12):
            self.assertIsNone(collector.push(b"s", False))
        output = [collector.push(b"v", True) for _ in range(500)]
        self.assertEqual(output[-1], b"v" * 500)

    def test_decoder_rejects_empty_and_low_quality_results(self):
        self.assertFalse(transcript([])["accepted"])
        segment = SimpleNamespace(text="Next step.", avg_logprob=-0.4, no_speech_prob=0.1)
        self.assertTrue(transcript([segment])["accepted"])
        segment.no_speech_prob = 0.9
        self.assertFalse(transcript([segment])["accepted"])
        segment.no_speech_prob = 0.1
        segment.avg_logprob = -1.2
        self.assertFalse(transcript([segment])["accepted"])

    def test_listener_only_queues_accepted_results(self):
        voice = WindowsVoice()
        voice.enabled = True
        process = SimpleNamespace(stdout=io.StringIO(
            '{"ready":true}\n{"text":"noise","confidence":0.9,"accepted":false}\n'
            '{"text":"next step","confidence":0.4,"accepted":true}\n'), stdin=io.StringIO())
        voice.listener = process
        voice.read_listener(process)
        self.assertEqual(voice.events.get_nowait()["text"], "next step")
        self.assertTrue(voice.events.empty())
        voice.close()

    def test_no_microphone_on_construction_and_correct_engine_process(self):
        with patch("backend.voice.subprocess.Popen") as spawn:
            voice = WindowsVoice()
            spawn.assert_not_called()
            with patch.dict("os.environ", {"SPEECH_ENGINE": "faster-whisper"}):
                voice.spawn("listen")
            self.assertTrue(spawn.call_args.args[0][-2].endswith("whisper_listener.py"))
            self.assertEqual(spawn.call_args.args[0][-1], "--controlled")
            voice.close()

    def test_pause_resume_reuses_listener_and_shutdown_terminates_it(self):
        process = Mock()
        process.poll.return_value = None
        process.stdin = io.StringIO()
        voice = WindowsVoice()
        voice.listener = process
        voice.enabled = True
        with patch.dict("os.environ", {"SPEECH_ENGINE": "faster-whisper"}), patch.object(voice, "spawn") as spawn:
            voice.mic(False)
            self.assertIs(voice.listener, process)
            process.terminate.assert_not_called()
            voice.mic(True)
            spawn.assert_not_called()
            self.assertIn('{"enabled": false}', process.stdin.getvalue())
            self.assertIn('{"enabled": true}', process.stdin.getvalue())
            voice.close()
            process.terminate.assert_called_once()

    def test_pause_invalidates_old_transcription_even_after_resume(self):
        control = ListenerControl(enabled=True)
        old = control.generation
        control.set_enabled(False)
        control.set_enabled(True)
        self.assertFalse(control.valid(old))
        self.assertTrue(control.valid(control.generation))
        control.close()
        self.assertFalse(control.wait_enabled())

    def test_paused_listener_does_not_deliver_stale_transcripts(self):
        voice = WindowsVoice()
        process = SimpleNamespace(stdout=io.StringIO('{"ready":true}\n{"text":"next step","accepted":true}\n'), stdin=io.StringIO())
        voice.listener = process
        voice.read_listener(process)
        self.assertTrue(voice.events.empty())
        voice.close()


if __name__ == "__main__":
    unittest.main()
