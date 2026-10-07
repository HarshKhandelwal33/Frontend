# Run the voice gearbox trainer

## On the target Windows PC

1. Clone this repository and open its root folder in Unity **6000.6.0f1**. Open **`Assets/Scenes/GearboxTraining.unity`**. This new scene uses the prepared workcell from `thesis/` and the current root scripts. Both original assembly scenes are preserved; the original root assembly scene lacks the completed simulation manager.
2. Install Python **3.10 or newer** and ensure `python` works in a terminal.
3. From the repository root, run:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File backend/start.ps1
   ```

   This creates a project-local Python environment, installs backend dependencies, and serves the API on `http://127.0.0.1:8000`. Leave this terminal running. Dependencies require internet on first setup. Stop the server with Ctrl+C.

4. Install Ollama and download/start the model separately:

   ```powershell
   ollama run qwen3:8b
   ```

   The backend defaults to that model and `http://127.0.0.1:11434`. It uses a 4,096-token context and non-thinking mode. No model files are included in GitHub.

5. Check Windows speech components:

   ```powershell
   powershell.exe -NoProfile -File backend/speech.ps1 -Mode check
   ```

   The first version uses Windows `System.Speech` with an **en-US desktop speech recognizer** and an installed desktop voice. If the recognizer list is empty, install the English (United States) speech feature in Windows language settings. Set your default input/output devices and permit desktop applications to access the microphone. Recognition quality depends on the installed engine and microphone; this is a replaceable speech adapter, not a neural speech model.

6. Open the Unity scene and press Play. Training starts idle, connects to the backend, and requests the first explanation. Click **Mic OFF** to enable listening. No microphone is started automatically.

## Controls

- Say **“I'm ready”** or **“next step”** to execute one operation. The next operation waits for readiness again.
- Ask an assembly question. The backend retrieves the current instructions and sends them to Qwen. The response is spoken and shown as optional subtitles.
- Say **“go back”** to restore the previous operation's starting checkpoint. After step 3 completes, this restores two completed operations and lets step 3 be repeated. During an operation, it cancels motion and returns to the preceding operation's start; during the first operation it restores the initial state.
- Say **“pause”** or **“resume”** to control an in-progress demonstration locally. Pausing does not cancel it.
- Say **“repeat the explanation”** to replay speech without moving assembly parts.
- Say **“start again”** to reset assembly. Reset clears runtime checkpoints and begins a new backend run.
- Say **“stop”**, press **Escape**, or click **STOP** for immediate local stop. Reset is required afterward. This is a simulated stop, not an industrial emergency-stop implementation.
- The compact interface provides Pause/Resume, Reset, Camera, Stop, Repeat, microphone toggle, speaker mute, and subtitles. Progression and going back are voice commands; there is no visible Next/Ready button. Tap the microphone while disconnected to retry the backend connection.

The compact panel displays listening, thinking, or speaking state and the last recognized utterance. Listening is suppressed while the tutor thinks or speaks to prevent feedback. The Windows adapter receives a renewable microphone lease: listening shuts down after 20 seconds without a Unity heartbeat. A microphone failure appears on screen. Use headphones if room audio still affects recognition.

## Connection and recovery

The Unity bridge uses `http://127.0.0.1:8000` by default. To override, set `GEARBOX_BACKEND_URL` **before launching Unity**. Backend settings are `OLLAMA_URL`, `OLLAMA_MODEL`, `TRAINING_DB`, and optional `SPEECH_CULTURE` (default `en-US`; command phrases remain English).

Backend progress changes only after Unity acknowledges completion. The bridge caches command results to avoid executing a retried command twice. Previous/reset/stop can supersede a pending operation. A lost backend connection pauses running motion; click **Connect**, then **Resume** if needed. Do not restart the backend mid-request unless necessary. A new Unity Play session resets the world and creates a new training session; runtime checkpoints are not persisted across Unity restarts.

If Ollama is unavailable, tutor questions show an error instead of fabricated answers. Recognized readiness/back commands and the deterministic workflow can still be exercised without Qwen. If a tutor request is in progress, wait for it to finish or fail before confirming readiness.

## Verification

Backend checks:

```powershell
backend/.venv/Scripts/python.exe -m unittest backend.test_backend -v
```

Unity checkpoint checks (close the project's editor first):

```powershell
& 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe' -batchmode -projectPath (Get-Location).Path -executeMethod GearboxDemo.Editor.GearboxTrainingChecks.RunBatch -logFile training-unity-check.log
```

Do not add `-quit`: the runner exits after Play Mode checks. Report: `training-check-report.txt`. A screen capture is requested as `training-ui-preview.png` when the editor supports capturing the Game view; batch mode may not produce it. To verify live backend command execution, keep the Python server running and use `GearboxDemo.Editor.GearboxIntegrationChecks.RunBatch` as the execute method. Its report is `training-integration-report.txt`. The older automatic demonstration is available by launching Unity with `--gearbox-demo`; existing batch simulation checks retain demo mode.

The LLM's answer quality, end-to-end spoken interaction, and rendering performance must be checked on the target GPU PC. The knowledge file describes the implemented kinematic demonstration and contains no verified engineering torque/tolerance data.

## Files to push

Push `backend/`, the modified Unity scripts, their `.meta` files, this setup guide, and the training architecture. Git ignores Python environments, SQLite runtime data, and Unity caches. The nested `thesis/` folder is an older duplicate and is not updated by this implementation. No Git push has been performed automatically.
