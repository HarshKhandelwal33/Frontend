using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.LowLevel;

namespace GearboxDemo.Editor
{
    [InitializeOnLoad]
    public static class GearboxGripperChecks
    {
        private const string Key = "Gearbox.GripperChecks";
        private static bool active;
        private static int phase;
        private static float phaseTime;
        private static RobotGripper gripper;
        private static RobotPoseController poses;
        private static GearboxPart part;
        private static GrippableObject target;
        private static Rigidbody body;
        private static Vector3 transportStart;
        private static PlayerLoopSystem originalLoop;

        static GearboxGripperChecks() { EditorApplication.playModeStateChanged += State; }
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch-only integration checks.");
            EditorSceneManager.OpenScene(GearboxDemoBuilder.ScenePath);
            GearboxGripperSetup.Setup(); GearboxGripperSetup.Setup();
            Require(UnityEngine.Object.FindObjectsByType<RobotGripper>().Length == 1, "Setup duplicates gripper");
            File.WriteAllText("gripper-playmode-report.txt", "PASS repeatable setup and seven prefab recognition checks\n");
            SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode();
        }
        private static void State(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                gripper = UnityEngine.Object.FindAnyObjectByType<RobotGripper>();
                poses = UnityEngine.Object.FindAnyObjectByType<RobotPoseController>();
                part = UnityEngine.Object.FindObjectsByType<GearboxPart>().Single(p => p.partType == GearboxPartType.Housing);
                target = part.GetComponent<GrippableObject>(); body = part.GetComponent<Rigidbody>();
                active = true; phase = 0; phaseTime = Time.time; Time.timeScale = 4;
                originalLoop = PlayerLoop.GetCurrentPlayerLoop();
                var loop = PlayerLoop.GetCurrentPlayerLoop();
                for (int i = 0; i < loop.subSystemList.Length; i++)
                    if (loop.subSystemList[i].type == typeof(UnityEngine.PlayerLoop.Update))
                        loop.subSystemList[i].subSystemList = loop.subSystemList[i].subSystemList.Concat(new[] { new PlayerLoopSystem { type = typeof(GearboxGripperChecks), updateDelegate = Tick } }).ToArray();
                PlayerLoop.SetPlayerLoop(loop);
            }
            if (state == PlayModeStateChange.ExitingPlayMode) PlayerLoop.SetPlayerLoop(originalLoop);
            if (state == PlayModeStateChange.EnteredEditMode) { SessionState.SetBool(Key, false); EditorApplication.Exit(SessionState.GetInt(Key + ".Exit", 1)); }
        }
        private static void Tick()
        {
            if (!active || !EditorApplication.isPlaying) return;
            try
            {
                if (Time.time - phaseTime > 50) throw new InvalidOperationException("Gripper test timeout at phase " + phase);
                if (Time.time - phaseTime < 1) return;
                if (phase == 0)
                {
                    foreach (GearboxPartType type in Enum.GetValues(typeof(GearboxPartType)))
                    { gripper.selectedPart = type; Require(gripper.ValidateSelectedPart(), "Selected recognition " + type); }
                    Require(!gripper.Grab(target) && gripper.HeldObject == null, "Distant grab rejected");
                    StagePartAtSocket();
                    part.transform.RotateAround(part.gripPoint.position, gripper.attachmentPoint.up, 60);
                    Physics.SyncTransforms();
                    Require(!gripper.Grab(target), "Misaligned grab rejected");
                    StagePartAtSocket();
                    Require(gripper.Grab(target), "Nearby aligned grab accepted");
                    Require(!gripper.Grab(target), "Double grab rejected");
                    CheckAttachment();
                    transportStart = part.transform.position;
                    poses.RequestPose(poses.traySafe);
                    Next(1);
                }
                else if (phase == 1 && !poses.Busy)
                {
                    CheckAttachment();
                    Require(Vector3.Distance(transportStart, part.transform.position) > 0.5f, "Part transported with robot");
                    Require(gripper.leftFinger.localPosition.x > -gripper.openHalfGap + 0.005f, "Fingers visibly closed to object width");
                    File.AppendAllText("gripper-playmode-report.txt", "PASS distance/orientation rejection, configured GripPoint alignment, deterministic transport, finger closure\n");
                    gripper.Release();
                    Require(gripper.HeldObject == null && !target.IsGrabbed && !body.isKinematic && body.useGravity && body.detectCollisions && body.interpolation == RigidbodyInterpolation.Interpolate && !part.installed, "Free release restores dynamic physics");
                    part.ResetPart();
                    Require(part.transform.localPosition.sqrMagnitude < 1e-7f, "Reset returns part to its tray start");
                    poses.RequestPose(poses.home); Next(2);
                }
                else if (phase == 2 && !poses.Busy)
                {
                    StagePartAtSocket();
                    Require(gripper.TryGrab(), "Nearest eligible part acquired");
                    part.ResetPart();
                    Require(gripper.HeldObject == null && !target.IsGrabbed && !body.isKinematic && body.detectCollisions, "Reset while held clears ownership and restores physics");
                    StagePartAtSocket(); Require(gripper.Grab(target), "Regrab after reset");
                    var installTarget = gripper.releaseTargets.Single(t => t.acceptedPart == part.partType);
                    // Transient test fixture only; do not modify the saved target/arm or define a pickup sequence.
                    installTarget.transform.SetPositionAndRotation(part.assemblyAnchor.position, part.assemblyAnchor.rotation);
                    gripper.Release();
                    Require(part.installed && installTarget.occupied && body.isKinematic && !body.useGravity, "Release validates and installs through AssemblyTarget");
                    part.ResetPart(); Require(!installTarget.occupied && !part.installed, "Reset releases occupancy");
                    StagePartAtSocket(); Require(gripper.Grab(target), "Regrab for external installation");
                    part.MarkInstalled();
                    Require(gripper.HeldObject == null && body.isKinematic && body.detectCollisions, "External installation clears attachment and restores contacts");
                    part.ResetPart();
                    StagePartAtSocket(); Require(gripper.Grab(target), "Regrab for disable cleanup");
                    gripper.enabled = false;
                    Require(gripper.HeldObject == null && !body.isKinematic && !target.IsGrabbed, "Disable releases held part");
                    part.ResetPart();
                    gripper.enabled = true; gripper.Open(); Next(3);
                }
                else if (phase == 3)
                {
                    Require(Mathf.Abs(gripper.leftFinger.localPosition.x + gripper.openHalfGap) < 0.001f, "Fingers visibly open");
                    foreach (var robotCollider in gripper.robotRoot.GetComponentsInChildren<Collider>())
                        Require(!Physics.GetIgnoreCollision(part.GetComponent<Collider>(), robotCollider), "Robot contacts restored after release/reset");
                    File.AppendAllText("gripper-playmode-report.txt", "PASS free release, gravity restoration, opening, assembly validation, occupied-target reset, held reset, disable cleanup and collision restoration\nALL CHECKS PASSED\n");
                    Finish(0);
                }
            }
            catch (Exception e) { File.AppendAllText("gripper-playmode-report.txt", "FAIL " + e + "\n"); Debug.LogException(e); Finish(1); }
        }
        private static void StagePartAtSocket()
        {
            Quaternion localGrip = Quaternion.Inverse(part.transform.rotation) * part.gripPoint.rotation;
            part.transform.rotation = gripper.attachmentPoint.rotation * Quaternion.Inverse(localGrip);
            part.transform.position += gripper.attachmentPoint.position - part.gripPoint.position;
            body.position = part.transform.position; body.rotation = part.transform.rotation;
            body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
        }
        private static void CheckAttachment()
        {
            Require(ReferenceEquals(gripper.HeldObject, target) && target.IsGrabbed && body.isKinematic && !body.useGravity && !body.detectCollisions, "Held physics state");
            Require(Vector3.Distance(gripper.attachmentPoint.position, part.gripPoint.position) < 0.003f, "Part GripPoint position alignment");
            Require(Quaternion.Angle(gripper.attachmentPoint.rotation, part.gripPoint.rotation) < 0.3f, "Part GripPoint rotation alignment");
        }
        private static void Next(int next) { phase = next; phaseTime = Time.time; }
        private static void Finish(int code) { active = false; Time.timeScale = 1; SessionState.SetInt(Key + ".Exit", code); EditorApplication.ExitPlaymode(); }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
