using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GearboxDemo.Editor
{
    [InitializeOnLoad]
    public static class GearboxSequenceChecks
    {
        private const string Key = "ReferenceSequenceCheck";
        private static double deadline;
        private static bool started;
        private static int lastStep = -1;
        private static Vector3 assemblyOrigin;
        static GearboxSequenceChecks()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(Key, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode) deadline = EditorApplication.timeSinceStartup + 240;
                if (state == PlayModeStateChange.EnteredEditMode)
                { SessionState.SetBool(Key, false); EditorApplication.Exit(SessionState.GetInt(Key + "Code", 1)); }
            };
        }
        public static void RunBatch()
        {
            GearboxReferenceSetup.RunBatch();
            File.WriteAllText("reference-sequence-report.txt", "Reference sequence play-mode verification\n");
            SessionState.SetBool(Key, true);
            EditorApplication.EnterPlaymode();
        }
        private static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || deadline == 0) return;
            try
            {
                var m = UnityEngine.Object.FindFirstObjectByType<GearboxAssemblyManager>();
                if (!started)
                {
                    if (Time.time < 0.5f) return;
                    started = true; assemblyOrigin = m.phaseOne.InternalAssemblyRoot.position; Time.timeScale = 5; m.StartAutoPlay();
                }
                if (lastStep != m.StepIndex)
                {
                    lastStep = m.StepIndex;
                    File.AppendAllText("reference-sequence-report.txt", "Completed steps: " + lastStep + " | " + m.CurrentPart + "\n");
                }
                if (m.CurrentAction.StartsWith("STOPPED")) throw new Exception(m.CurrentAction);
                if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Sequence timed out: " + m.CurrentAction);
                if (m.StepIndex != 12) return;
                if (m.allParts.Any(p => !p.assembled)) throw new Exception("Not all twelve parts installed.");
                if (m.allTargets.Any(t => !t.occupied)) throw new Exception("Missing target occupancy.");
                foreach (var p in m.allParts)
                {
                    bool internalPart = p.transform.IsChildOf(m.phaseOne.InternalAssemblyRoot);
                    Vector3 expected = p.assemblyTarget.transform.position + (internalPart ? m.phaseTwoInsertion.internalAssemblyTarget.position - assemblyOrigin : Vector3.zero);
                    float error = Vector3.Distance(p.transform.position, expected);
                    if (error > 0.002f) throw new Exception(p.partId + " final CAD alignment error: " + error);
                }
                File.AppendAllText("reference-sequence-report.txt", "PASS final CAD alignment within 2 mm for every part.\n");
                Capture(m);
                File.AppendAllText("reference-sequence-report.txt", "PASS full sequence, twelve installed parts and occupied targets.\n");
                m.ResetSimulation();
                if (m.StepIndex != 0 || m.IsExecuting || m.allTargets.Any(t => t.occupied)) throw new Exception("Reset failed.");
                File.AppendAllText("reference-sequence-report.txt", "PASS reset clears progress and target occupancy.\n");
                SessionState.SetInt(Key + "Code", 0); EditorApplication.ExitPlaymode();
            }
            catch (Exception e)
            {
                File.AppendAllText("reference-sequence-report.txt", "FAIL " + e + "\n");
                SessionState.SetInt(Key + "Code", 1); EditorApplication.ExitPlaymode();
            }
        }

        private static void Capture(GearboxAssemblyManager m)
        {
            Camera camera = Camera.main;
            Vector3 focus = m.phaseTwoInsertion.internalAssemblyTarget.position + Vector3.up * 0.09f;
            camera.transform.position = focus + new Vector3(-0.48f, 0.38f, -0.54f);
            camera.transform.LookAt(focus); camera.fieldOfView = 36;
            var rt = new RenderTexture(1200, 900, 24);
            var previous = RenderTexture.active;
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            var texture = new Texture2D(1200, 900, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1200, 900), 0, 0); texture.Apply();
            File.WriteAllBytes("reference-assembly-preview.png", texture.EncodeToPNG());
            camera.targetTexture = null; RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
