import tempfile
import unittest
from pathlib import Path

from fastapi.testclient import TestClient

from backend.app import create_app


class BackendTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.path = Path(self.tmp.name) / "test.db"
        self.prompts = []

        async def tutor(messages):
            self.prompts.append(messages)
            return "The robot supplies the pin carrier to the worker."

        self.tutor = tutor
        self.client = TestClient(create_app(self.path, tutor))
        self.session = self.client.post("/sessions", json={}).json()
        self.url = "/sessions/" + self.session["id"]

    def tearDown(self):
        self.client.close()
        self.tmp.cleanup()

    def action(self, action, request_id="a"):
        return self.client.post(self.url + "/actions", json={"action": action, "request_id": request_id})

    def acknowledge(self, command, success=True, count=None):
        return self.client.post(self.url + "/results", json={
            "command_id": command["id"], "run_id": command["run_id"],
            "expected_revision": command["expected_revision"], "success": success,
            "completed": command["target_completed"] if count is None else count})

    def test_progress_requires_ack_and_duplicates_do_not_execute_twice(self):
        response = self.action("ready").json()
        command = response["session"]["pending"]
        self.assertEqual(response["session"]["completed"], 0)
        self.assertEqual(self.action("ready").json(), response)
        self.assertEqual(self.action("ready", "another").status_code, 409)
        self.assertEqual(self.acknowledge(command, count=2).status_code, 409)
        self.assertEqual(self.acknowledge(command).json()["completed"], 1)
        self.assertEqual(self.acknowledge(command).json()["revision"], 1)

    def test_go_back_requests_real_restore_and_waits_for_confirmation(self):
        command = self.action("ready").json()["session"]["pending"]
        self.acknowledge(command)
        response = self.client.post(self.url + "/messages", json={"text": "Go back to the previous step!", "request_id": "back"}).json()
        command = response["session"]["pending"]
        self.assertEqual(command["type"], "restore_checkpoint")
        self.assertEqual(response["session"]["completed"], 1)
        self.assertEqual(self.acknowledge(command).json()["completed"], 0)

    def test_fault_blocks_progress_until_reset_and_reset_changes_run(self):
        command = self.action("ready").json()["session"]["pending"]
        self.acknowledge(command, success=False)
        self.assertEqual(self.action("ready", "blocked").status_code, 409)
        reset = self.action("reset", "reset").json()["session"]["pending"]
        restored = self.acknowledge(reset).json()
        self.assertNotEqual(restored["run_id"], self.session["run_id"])
        self.assertEqual(restored["status"], "ready")

    def test_questions_use_grounding_and_cannot_move_assembly(self):
        response = self.client.post(self.url + "/messages", json={"text": "Explain this operation", "request_id": "q"})
        self.assertEqual(response.status_code, 200)
        self.assertEqual(response.json()["session"]["completed"], 0)
        self.assertTrue(response.json()["sources"])
        self.assertIn("pin_carrier", self.prompts[0][1]["content"])
        self.client.post(self.url + "/messages", json={"text": "Explain this operation", "request_id": "q"})
        self.assertEqual(len(self.prompts), 1)

    def test_persistence_and_workflow_validation(self):
        self.assertEqual(self.client.post("/sessions", json={"workflow_version": "wrong"}).status_code, 422)
        other = TestClient(create_app(self.path, self.tutor))
        self.assertEqual(other.get(self.url).json()["id"], self.session["id"])
        other.close()

    def test_all_thirteen_operations(self):
        for i in range(13):
            command = self.action("ready", str(i)).json()["session"]["pending"]
            self.assertEqual(self.acknowledge(command).json()["completed"], i + 1)
        self.assertEqual(self.action("ready", "finished").status_code, 409)

    def test_back_supersedes_inflight_execution_and_rejects_late_result(self):
        old = self.action("ready").json()["session"]["pending"]
        back = self.action("previous", "back").json()["session"]["pending"]
        self.assertEqual(back["supersedes"], old["id"])
        self.assertEqual(back["target_completed"], 0)
        self.assertEqual(self.acknowledge(old).status_code, 409)
        self.assertEqual(self.acknowledge(back).json()["completed"], 0)

    def test_stop_can_cancel_execution_at_boundary(self):
        self.action("ready")
        stop = self.action("stop", "stop").json()["session"]["pending"]
        result = self.acknowledge(stop, count=1)
        self.assertEqual(result.status_code, 200)
        self.assertEqual(result.json()["status"], "stopped")
        self.assertEqual(self.action("ready", "blocked").status_code, 409)

    def test_voice_is_opt_in_and_empty_by_default(self):
        state = self.client.get("/voice/status").json()
        self.assertFalse(state["enabled"])
        self.assertFalse(state["listening"])
        self.assertEqual(state["utterances"], [])

    def test_stop_supersedes_a_restore_already_applied_by_unity(self):
        command = self.action("ready").json()["session"]["pending"]
        self.acknowledge(command)
        self.action("previous", "back")
        stop = self.action("stop", "stop").json()["session"]["pending"]
        response = self.acknowledge(stop, count=0)
        self.assertEqual(response.status_code, 200)
        self.assertEqual(response.json()["completed"], 0)

    def test_stop_supersedes_a_restore_already_applied_by_unity(self):
        command = self.action("ready").json()["session"]["pending"]
        self.acknowledge(command)
        self.action("previous", "back")
        stop = self.action("stop", "stop").json()["session"]["pending"]
        response = self.acknowledge(stop, count=0)
        self.assertEqual(response.status_code, 200)
        self.assertEqual(response.json()["completed"], 0)

    def test_question_about_a_command_does_not_execute_it(self):
        response = self.client.post(self.url + "/messages", json={"text": "What happens if I say go back?", "request_id": "question"})
        self.assertEqual(response.status_code, 200)
        self.assertIsNone(response.json()["session"]["pending"])


if __name__ == "__main__":
    unittest.main()
