# Gearbox voice training backend

One Python service for the existing 13-operation Unity workflow. Includes SQLite session persistence, deterministic teaching commands, operation-based and keyword retrieval, and an Ollama tutor adapter. No model installation is performed by this project.

## Start on Windows

From the repository root, with Python 3.10 or newer:

```powershell
python -m venv backend/.venv
backend/.venv/Scripts/python.exe -m pip install -r backend/requirements.txt
backend/.venv/Scripts/python.exe -m uvicorn backend.app:app --host 127.0.0.1 --port 8000
```

Open http://127.0.0.1:8000/docs for the interactive API. Use one server worker: this prototype serializes database updates with a process-local lock. Keep it bound to localhost; authentication and multi-user deployment are outside this prototype.

Optional configuration in the terminal before starting:

```powershell
$env:OLLAMA_URL = 'http://127.0.0.1:11434'
$env:OLLAMA_MODEL = 'qwen3:8b'
```

`TRAINING_DB` optionally sets the database path. The default is `backend/data/sessions.db`. `/health` checks the service, not model availability. Without Ollama, workflow APIs work but tutor questions return HTTP 503; there is no pretend model response.

## API walkthrough

1. `POST /sessions` with `{}` creates a session at zero completed operations. Unity must reset to an empty workcell before binding to this session. Persist the returned session ID on the client to reconnect.
2. `POST /sessions/{id}/messages` with `{"text":"Explain this step","request_id":"unique-1"}` retrieves instructions and calls the tutor. The returned text is suitable for text-to-speech. Sources describe retrieved context; they are not verified claim-by-claim citations.
3. Send `{"text":"I'm ready","request_id":"unique-2"}` to the same endpoint. This issues a command without calling the model. Alternatively use `/actions` with `{"action":"ready","request_id":"unique-2"}`.
4. Unity polls `GET /sessions/{id}/commands`. A pending command stays available until its result arrives. Unity must deduplicate by command ID and never execute a duplicate twice.
5. After execution, Unity posts `/results` with `command_id`, `run_id`, `expected_revision` copied from the command, `success`, and its actual `completed` count. Optional `error` captures failure details. The backend advances only after a matching successful result.
6. Request the next explanation explicitly. Progress acknowledgement does not automatically call the LLM.

Commands: `ready`, `previous`, `pause`, `resume`, `reset`, `stop`, `repeat`. Recognized English transcript phrases include “I'm ready”, “next step”, “go back”, “go back to the previous step”, “pause”, “resume”, “start again”, “stop”, and “repeat the explanation”. Exact normalized matching prevents a question such as “What happens if I say go back?” from moving the assembly. Other wording is treated as a tutor question. Client actions are the reliable fallback.

## Unity integration contract and current limitations

- The root Unity project now polls these endpoints and restores runtime checkpoints. Local speech is exposed through `/voice/microphone`, `/voice/status`, `/voice/speak`, and `/voice/silence`; see `../TRAINING_SETUP.md` for setup.
- `execute_operation` targets one additional completed operation. `restore_checkpoint` targets one fewer completed operation: after operation 3, go back restores two completed operations so operation 3 can be demonstrated again. Pause or cancel motion before restoring the world.
- Unity must validate `run_id`, revision, operation ID, prerequisites, and checkpoint availability before executing. Report failure if restoration is unsupported; do not report success without restoring the actual world.
- A result increments the state revision. Reset also creates a new run ID. Duplicate results are idempotent; conflicting duplicates and stale results are rejected.
- One command may be pending at a time. Previous/reset/stop may supersede it; delayed results for superseded commands are rejected. Unity polls the replacement and cancels old command handling. Pause/resume during motion are local Unity controls, keeping the operation pending until completion. Stop acts locally immediately, then synchronizes with the backend. Do not rely on HTTP as an emergency-stop mechanism.
- Reconnect by fetching the session and polling its pending command. If Unity has lost its state, do not blindly execute pending work: recover its local result/checkpoint or report failure and reset. No automatic retry of physical/simulated motion is provided.
- Commands and questions require unique request IDs. Retry with the same ID for network failures; a new user action needs a new ID. Cached responses are historical snapshots; fetch the session for current state.
- `repeat` repeats the last tutor response without moving the assembly. `previous` requests an actual checkpoint restore.
- The local speech adapter consumes the default microphone and plays spoken text through the default output device on the same PC. Recognition starts only after explicit opt-in and expires without a renewable 20-second lease. `/voice/status` drains queued transcripts, so it is intended for one Unity client. Subtitles remain available when speech is unavailable. English commands use exact normalized matching; open questions are passed to the LLM.
- Knowledge is derived from the current simulation code and documentation. It is not mechanically validated expert guidance. Replace/expand it with reviewed instructions before evaluating technical answer accuracy. No torque or tolerance values are supplied.
- History and idempotency records grow with the session; suitable for short local demonstrations. Cleanup and retention controls are future work.

## Open-source speech recognition

Run `backend/start.ps1` to install faster-whisper, SoundDevice and WebRTC VAD,
download the `small.en` model into `backend/data/speech-models`, then start the server.
Preparation does not open the microphone. After the first download, inference is local;
recordings are held in memory and are not uploaded or saved. Enable the microphone in
Unity to listen. The existing 20-second renewable lease and pause during tutor playback remain.

Defaults: English, CPU INT8, four inference threads. Set `$env:WHISPER_MODEL = 'base.en'`
before starting for a smaller, faster alternative. Set `$env:SPEECH_ENGINE = 'windows'`
to explicitly select the previous recognizer. There is no silent fallback.
For multilingual recognition, use a multilingual model such as `small` and set
`WHISPER_LANGUAGE` to the desired language; assembly command phrases still use English.

Speech is segmented after approximately 720 ms of silence, with 300 ms onset padding
and a 15-second maximum. Audio collected during transcription is discarded to avoid
replaying stale commands; wait for listening again before speaking. A model is loaded
each time recognition resumes, including after spoken tutor replies, so resuming has
some startup delay. `confidence` is a decoder likelihood heuristic, not a calibrated
probability. The worker rejects high no-speech probability or poor decoder likelihood;
exact command matching is still performed by the existing backend.

Speech-to-text uses [faster-whisper](https://github.com/SYSTRAN/faster-whisper)
and [WebRTC VAD](https://github.com/daanzu/py-webrtcvad-wheels).
Text-to-speech continues to use Windows System.Speech.
Test with `backend/.venv/Scripts/python.exe -m unittest backend.test_speech backend.test_backend -v`.
Microphone recognition quality and latency need a trial with your actual voice and room.

### Backend workflow tests

```powershell
backend/.venv/Scripts/python.exe -m unittest backend.test_backend -v
```

Tests inject a tutor stub strictly inside the test process. They cover command acknowledgements, duplicates, stale counts, backward restoration requests, fault/reset behavior, retrieval context, persistence, and all 13 operations. Real model quality and Unity behavior need separate integration testing.
