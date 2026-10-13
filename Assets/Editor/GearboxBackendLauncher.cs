using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GearboxDemo.Editor
{
    [InitializeOnLoad]
    public static class GearboxBackendLauncher
    {
        static GearboxBackendLauncher()
        {
            EditorApplication.delayCall += () => {
                var flag = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "backend", "data", "stop-backend");
                if (File.Exists(flag)) { File.Delete(flag); StopBackend(); }
            };
            EditorApplication.playModeStateChanged += state => {
                if (state == PlayModeStateChange.ExitingEditMode &&
                    !Application.isBatchMode &&
                    EditorSceneManager.GetActiveScene().name == "GearboxTraining")
                    StartBackend();
            };
        }

        [MenuItem("Tools/Gearbox Demo/Stop Training Backend")]
        public static void StopBackend()
        {
            var root = Directory.GetParent(Application.dataPath).FullName;
            var log = Path.Combine(root, "backend", "data", "local-backend.log");
            if (!File.Exists(log)) return;
            string contents;
            using (var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream)) contents = reader.ReadToEnd();
            var matches = System.Text.RegularExpressions.Regex.Matches(contents, @"Started server process \[(\d+)\]");
            for (int i = matches.Count - 1; i >= 0; i--) {
                try {
                    var process = Process.GetProcessById(int.Parse(matches[i].Groups[1].Value));
                    var executable = process.MainModule.FileName;
                    var config = File.ReadAllText(Path.Combine(root, "backend", ".venv", "pyvenv.cfg"));
                    var home = System.Text.RegularExpressions.Regex.Match(config, @"(?m)^home = (.+)$").Groups[1].Value.Trim();
                    if (!executable.StartsWith(Path.Combine(root, "backend", ".venv"), StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(executable, Path.Combine(home, "python.exe"), StringComparison.OrdinalIgnoreCase)) continue;
                    process.Kill();
                    UnityEngine.Debug.Log("Training backend stopped.");
                } catch (ArgumentException) { }
                catch (Exception e) { UnityEngine.Debug.LogWarning("Could not stop backend: " + e.Message); }
            }
        }

        [MenuItem("Tools/Gearbox Demo/Start Training Backend")]
        public static void StartBackend()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor) return;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GEARBOX_BACKEND_URL"))) return;
            var root = Directory.GetParent(Application.dataPath).FullName;
            var python = Path.Combine(root, "backend", ".venv", "Scripts", "python.exe");
            if (!File.Exists(python)) {
                UnityEngine.Debug.LogError("Backend Python environment missing. Run backend/start.ps1 first.");
                return;
            }
            try {
                var info = new ProcessStartInfo(python, "backend/serve_local.py") {
                    WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true
                };
                Process.Start(info);
                UnityEngine.Debug.Log("Training backend startup requested. Logs: backend/data/local-backend.log");
            } catch (Exception e) {
                UnityEngine.Debug.LogError("Could not start training backend: " + e.Message);
            }
        }
    }
}
