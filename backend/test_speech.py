import io
import unittest
from types import SimpleNamespace
from unittest.mock import patch

from backend.whisper_listener import Utterances, transcript
from backend.voice import WindowsVoice


class SpeechTests(unittest.TestCase):
    def test_silence_is_not_an_utterance(self):
        collector = Utterances()
        self.assertTrue(all(collector.push(b"s", False) is None for _ in range(1000)))

    def test_onset_padding_and_end_of_speech(self):
        collector = Utterances()
        for _ in range(5):
            collector.push(b"p", False)
        for _ in range(3):
            collector.push(b"v", True)
        for _ in range(23):
            self.assertIsNone(collector.push(b"s", False))
        self.assertEqual(collector.push(b"s", False), b"p" * 5 + b"v" * 3 + b"s" * 24)
        self.assertIsNone(collector.push(b"s", False))

    def test_noise_blip_is_rejected_and_continuous_speech_is_bounded(self):
        collector = Utterances()
        collector.push(b"n", True)
        for _ in range(24):
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
            self.assertTrue(spawn.call_args.args[0][-1].endswith("whisper_listener.py"))
            voice.close()


if __name__ == "__main__":
    unittest.main()
