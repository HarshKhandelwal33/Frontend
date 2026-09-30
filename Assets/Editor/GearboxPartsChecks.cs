using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GearboxDemo.Editor
{
    /// <summary>Explicit batch-only checks. Normal Play Mode never runs these tests.</summary>
    [InitializeOnLoad]
    public static class GearboxPartsChecks
    {
        private const string RunningKey = "GearboxDemo.BatchChecks";
        private static double deadline;
        private static bool checking;

        static GearboxPartsChecks()
        {
            EditorApplication.playModeStateChanged += OnPlayState;
            EditorApplication.update += Tick;
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("RunBatch is a batch-only verification entry point.");
            EditorSceneManager.OpenScene(GearboxDemoBuilder.ScenePath);
            GearboxPartsSetup.Setup();
            var before = UnityEngine.Object.FindObjectsByType<Transform>();
            int count = before.Length;
            var grips = UnityEngine.Object.FindObjectsByType<GearboxPart>()
                .ToDictionary(p => p.partType, p => p.gripPoint.localPosition);
            string[] guids = GearboxPartsSetup.Parts.Select(p => AssetDatabase.AssetPathToGUID(p.PrefabPath)).ToArray();
            GearboxPartsSetup.Setup();
            Require(UnityEngine.Object.FindObjectsByType<Transform>().Length == count, "Idempotence: scene object count changed");
            foreach (GearboxPart part in UnityEngine.Object.FindObjectsByType<GearboxPart>())
                Require(part.gripPoint.localPosition == grips[part.partType], "Idempotence: grip changed");
            Require(guids.SequenceEqual(GearboxPartsSetup.Parts.Select(p => AssetDatabase.AssetPathToGUID(p.PrefabPath))), "Idempotence: prefab GUID changed");
            ValidateInitialSpacing();
            File.WriteAllText("gearbox-playmode-report.txt", "PASS repeated setup: stable object count, grip positions and prefab GUIDs\nPASS initial part spacing and tray containment\n");
            SessionState.SetBool(RunningKey, true);
            EditorApplication.EnterPlaymode();
        }

        private static void ValidateInitialSpacing()
        {
            Physics.SyncTransforms();
            var parts = UnityEngine.Object.FindObjectsByType<GearboxPart>();
            Bounds tray = GameObject.Find("Tray Base").GetComponent<Collider>().bounds;
            for (int i = 0; i < parts.Length; i++)
            {
                Bounds bounds = parts[i].GetComponent<Collider>().bounds;
                Require(bounds.min.y >= tray.max.y && bounds.min.y - tray.max.y < 0.015f, parts[i].name + " tray surface clearance");
                Require(bounds.min.x > tray.min.x + 0.04f && bounds.max.x < tray.max.x - 0.04f &&
                        bounds.min.z > tray.min.z + 0.04f && bounds.max.z < tray.max.z - 0.04f, parts[i].name + " inside tray rims");
                for (int j = i + 1; j < parts.Length; j++)
                {
                    Bounds padded = bounds;
                    padded.Expand(0.025f);
                    Require(!padded.Intersects(parts[j].GetComponent<Collider>().bounds), "Initial parts too close: " + parts[i].name + "/" + parts[j].name);
                }
            }
        }

        private static void OnPlayState(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(RunningKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                checking = true;
                deadline = EditorApplication.timeSinceStartup + 45;
            }
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(RunningKey, false);
                EditorApplication.Exit(SessionState.GetInt(RunningKey + ".ExitCode", 1));
            }
        }

        private static void Tick()
        {
            if (!checking || !EditorApplication.isPlaying) return;
            if (Time.time < 8f && EditorApplication.timeSinceStartup < deadline) return;
            checking = false;
            try
            {
                Require(Time.time >= 8f, "Play Mode timed out before eight simulated seconds");
                RunRuntimeChecks();
                SessionState.SetInt(RunningKey + ".ExitCode", 0);
            }
            catch (Exception exception)
            {
                File.AppendAllText("gearbox-playmode-report.txt", "FAIL " + exception + "\n");
                Debug.LogException(exception);
                SessionState.SetInt(RunningKey + ".ExitCode", 1);
            }
            finally { EditorApplication.ExitPlaymode(); }
        }

        private static void RunRuntimeChecks()
        {
            var report = new StringBuilder();
            var parts = UnityEngine.Object.FindObjectsByType<GearboxPart>();
            Require(parts.Length == 7, "Seven parts in Play Mode");
            var targets = UnityEngine.Object.FindObjectsByType<AssemblyTarget>();
            float trayTop = GameObject.Find("Tray Base").GetComponent<Collider>().bounds.max.y;
            foreach (GearboxPart part in parts)
            {
                Rigidbody body = part.GetComponent<Rigidbody>();
                Bounds bounds = part.GetComponent<Collider>().bounds;
                Vector3 start = part.transform.parent.position;
                float drift = Vector2.Distance(new Vector2(start.x, start.z), new Vector2(part.transform.position.x, part.transform.position.z));
                Require(!body.isKinematic && body.useGravity && !part.installed, part.name + " remains dynamic and uninstalled");
                Require(Mathf.Abs(bounds.min.y - trayTop) < 0.012f && drift < 0.025f && body.linearVelocity.magnitude < 0.05f, part.name + " stable on tray");
                Require(part.transform.position.y < start.y - 0.001f, part.name + " settled under gravity");
                report.AppendLine($"PASS {part.partType}: stable after 8s, bottom={bounds.min.y:F4}, drift={drift:F5}, speed={body.linearVelocity.magnitude:F5}");
            }
            foreach (GearboxPart part in parts)
            {
                var grippable = part.GetComponent<GrippableObject>();
                AssemblyTarget target = targets.Single(t => t.acceptedPart == part.partType);
                Require(!target.TryPlace(part), "Distant part rejected");
                AssemblyTarget wrong = targets.First(t => t.acceptedPart != part.partType);
                Require(!wrong.TryPlace(part), "Wrong type rejected");
                Require(grippable.CanBeGrabbed(), "Available before grab");
                grippable.OnGrabbed();
                Require(grippable.IsGrabbed && !grippable.CanBeGrabbed() && part.GetComponent<Rigidbody>().isKinematic, "Grab state");
                grippable.OnReleased();
                Require(!grippable.IsGrabbed && !part.GetComponent<Rigidbody>().isKinematic && part.GetComponent<Rigidbody>().useGravity, "Release restores physics");

                // Check orientation rejection and anchor-relative snapping, including a held part.
                grippable.OnGrabbed();
                part.transform.rotation = target.transform.rotation * Quaternion.Inverse(part.assemblyAnchor.localRotation);
                part.transform.position += target.transform.position - part.assemblyAnchor.position;
                part.transform.RotateAround(target.transform.position, Vector3.up, 45);
                Require(!target.TryPlace(part), "Wrong orientation rejected");
                part.transform.RotateAround(target.transform.position, Vector3.up, -45);
                part.transform.position += Vector3.right * 0.005f;
                int notifications = 0;
                Action<AssemblyTarget, GearboxPart> listener = (t, p) => notifications++;
                target.PartInstalled += listener;
                Require(target.TryPlace(part), "Valid placement accepted");
                target.PartInstalled -= listener;
                Require(notifications == 1 && target.occupied && part.installed && !grippable.IsGrabbed && !grippable.CanBeGrabbed(), "Installed state and notification");
                Require(Vector3.Distance(part.assemblyAnchor.position, target.transform.position) < 0.0001f, "Anchor snap alignment");
                Require(!target.TryPlace(part), "Occupied target rejected");
                part.MarkUninstalled();
                Require(!target.occupied && !part.installed && grippable.CanBeGrabbed(), "Uninstall releases occupancy");
                Require(target.TryPlace(part), "Can reinstall after uninstall");
                part.ResetPart();
                Require(!target.occupied && !part.installed && grippable.CanBeGrabbed() && !part.GetComponent<Rigidbody>().isKinematic, "Reset releases target and restores physics");
                Require(part.transform.localPosition.sqrMagnitude < 1e-8f, "Reset restores start pose");
                grippable.OnGrabbed();
                part.ResetPart();
                grippable.OnReleased();
                Require(!grippable.IsGrabbed && !part.GetComponent<Rigidbody>().isKinematic, "Reset while held clears stale grip state");
                report.AppendLine("PASS " + part.partType + ": grab/release, wrong type/distance/rotation, snap, notification, occupancy, uninstall and reset");
            }
            File.AppendAllText("gearbox-playmode-report.txt", report + "ALL CHECKS PASSED\n");
            Debug.Log(report + "ALL CHECKS PASSED");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}

