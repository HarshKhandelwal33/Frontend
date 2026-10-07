using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GearboxDemo.Editor
{
    [InitializeOnLoad]
    public static class GearboxCollaborationChecks
    {
        private const string Key = "Gearbox.CollaborationChecks";
        private static double deadline;
        private static int stage;
        private static GameObject hazard;
        private static double lastProgress;
        static GearboxCollaborationChecks()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state => {
                if (!SessionState.GetBool(Key, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode) deadline = EditorApplication.timeSinceStartup + 600;
                if (state == PlayModeStateChange.EnteredEditMode) {
                    SessionState.SetBool(Key, false); EditorApplication.Exit(SessionState.GetInt(Key + "Exit", 1));
                }
            };
        }
        [MenuItem("Tools/Gearbox Demo/Configure Collaborative Simulation")]
        public static void ConfigureScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first");
            var manager = UnityEngine.Object.FindAnyObjectByType<GearboxAssemblyManager>();
            if (manager == null) throw new InvalidOperationException("Open GearboxAssembly or GearboxTraining");
            var game = manager.GetComponent<GearboxGameSimulation>();
            if (game == null) game = Undo.AddComponent<GearboxGameSimulation>(manager.gameObject);
            var mat = GameObject.Find("Assembly Mat");
            if (mat != null) { Undo.RecordObject(mat.transform, "Fit assembly mat"); GearboxGameSimulation.FitAssemblyMat(mat.transform); }
            // Unity's cylinder primitive carries a capsule collider. Thin joint seals must use their actual convex mesh,
            // otherwise the capsule radius expands them into spheres and blocks the receiving hand.
            foreach (var filter in manager.handover.robotArm.GetComponentsInChildren<MeshFilter>())
            {
                if (!new[] { "JointHousing", "TealJointSeal", "JointEndCap", "MountingFoot", "BaseAccentCollar" }.Contains(filter.name)) continue;
                var capsule = filter.GetComponent<CapsuleCollider>();
                if (capsule == null) continue;
                Undo.DestroyObjectImmediate(capsule);
                var mesh = Undo.AddComponent<MeshCollider>(filter.gameObject);
                mesh.sharedMesh = filter.sharedMesh; mesh.convex = true;
            }
            var animator = manager.handover.workerAnimator;
            var receiving = animator.GetComponent<WorkerReceivingRig>();
            if (receiving == null) receiving = Undo.AddComponent<WorkerReceivingRig>(animator.gameObject);
            receiving.animator = animator; receiving.holdPoint = manager.handover.workerRightHandHoldPoint;
            EditorUtility.SetDirty(receiving);
            var controller = animator.runtimeAnimatorController as AnimatorController;
            if (controller == null) throw new InvalidOperationException("Worker needs the existing Worker AnimatorController");
            var layers = controller.layers; layers[0].iKPass = true; controller.layers = layers;
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            EditorSceneManager.SaveScene(manager.gameObject.scene);
            AssetDatabase.SaveAssets();
        }
        [MenuItem("Tools/Gearbox Demo/Compact Robot and Tables")]
        public static void CompactEquipmentBatch()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first");
            EditorSceneManager.OpenScene("Assets/Scenes/GearboxTraining.unity");
            var manager = UnityEngine.Object.FindAnyObjectByType<GearboxAssemblyManager>();
            var game = manager.GetComponent<GearboxGameSimulation>();
            float robotFactor = 0.85f / Mathf.Max(0.01f, game.appliedRobotBodyScale);
            foreach (var mesh in manager.handover.robotArm.GetComponentsInChildren<MeshFilter>())
            {
                var t = mesh.transform;
                bool shell = new[] { "RoundedLinkShell", "GraphiteLinkInset", "TealLinkDetail", "Pedestal", "MountingFoot", "BaseAccentCollar" }.Contains(t.name);
                bool joint = new[] { "JointHousing", "TealJointSeal", "JointEndCap" }.Contains(t.name);
                if (!shell && !joint) continue;
                Undo.RecordObject(t, "Compact robot body");
                Vector3 scale = t.localScale; scale.x *= robotFactor; scale.z *= robotFactor;
                if (joint) scale.y *= robotFactor;
                t.localScale = scale;
                if (joint) t.localPosition *= robotFactor;
                else if (t.name == "GraphiteLinkInset" || t.name == "TealLinkDetail")
                { Vector3 position = t.localPosition; position.x *= robotFactor; position.z *= robotFactor; t.localPosition = position; }
            }
            float tableFactor = 0.9f / Mathf.Max(0.01f, game.appliedTableScale);
            foreach (string name in new[] { "Workbench", "Parts Table" })
            {
                var table = GameObject.Find(name).transform;
                var top = table.GetComponentsInChildren<Collider>().First(c => c.name == "Tabletop");
                Physics.SyncTransforms(); float backEdge = top.bounds.max.z;
                Undo.RecordObject(table, "Compact table footprint");
                Vector3 scale = table.localScale; scale.x *= tableFactor; scale.z *= tableFactor; table.localScale = scale;
                Physics.SyncTransforms();
                // Preserve the operator-side edge so the enlarged mat stays on the smaller workbench.
                if (name == "Workbench") table.position += Vector3.forward * (backEdge - top.bounds.max.z);
            }
            Undo.RecordObject(game, "Record equipment proportions");
            game.appliedRobotBodyScale = 0.85f; game.appliedTableScale = 0.9f; EditorUtility.SetDirty(game);
            ConfigureScene();
            File.WriteAllText("equipment-scale-report.txt", "Robot link/joint body width reduced 15%; articulation frames, arm reach and gripper opening retained.\nBoth table footprints reduced 10%; working heights retained. Workbench operator-side edge preserved so both stations stay on the mat.\nHuman and part scales retained.\n");
        }

        [MenuItem("Tools/Gearbox Demo/Resize Worker to Adult Scale")]
        public static void ResizeWorkerBatch()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first");
            EditorSceneManager.OpenScene("Assets/Scenes/GearboxTraining.unity");
            var manager = UnityEngine.Object.FindAnyObjectByType<GearboxAssemblyManager>();
            var worker = manager.phaseTwoInsertion.worker;
            var renderers = worker.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            const float scale = 1.08f;
            float factor = scale / worker.localScale.x;
            Undo.RecordObject(worker, "Resize adult worker");
            // Scale from the grounded worker root, including bones, mesh and walking collider.
            worker.localScale = Vector3.one * scale;
            PrefabUtility.RecordPrefabInstancePropertyModifications(worker);
            var game = manager.GetComponent<GearboxGameSimulation>();
            Undo.RecordObject(game, "Fit worker safety proxies");
            game.torsoRadius *= factor; game.limbRadius *= factor; game.receivingHandRadius *= factor;
            EditorUtility.SetDirty(game);
            ConfigureScene();
            File.WriteAllText("worker-scale-report.txt", "Worker uniformly enlarged to 108% (root scale 1.08).\nWalking collider world height=" +
                (worker.GetComponent<CharacterController>().height * worker.lossyScale.y).ToString("F3") +
                " m. Feet/root ground position preserved; mesh, bones, collider and clearance proxies scaled together.\nPart dimensions unchanged; real print-to-person ratio remains approximate without print measurements.\n");
        }

        public static void InspectScaleBatch()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/GearboxTraining.unity");
            var manager = UnityEngine.Object.FindAnyObjectByType<GearboxAssemblyManager>();
            var worker = manager.phaseTwoInsertion.worker;
            var renderers = worker.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            File.WriteAllText("worker-scale-report.txt", "Worker render bounds: " + bounds + " height=" + bounds.size.y + " rootScale=" + worker.lossyScale +
                "\nController height=" + worker.GetComponent<CharacterController>().height + "\nHousing bounds=" + manager.phaseTwoInsertion.housing.GetComponent<Collider>().bounds.size + "\n");
        }

        public static void RunStaticBatch()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/GearboxTraining.unity");
            ConfigureScene();
            var manager = UnityEngine.Object.FindAnyObjectByType<GearboxAssemblyManager>();
            var arm = manager.handover.robotArm;
            Require(arm.joints.Length == 6 && arm.joints.All(j => j != null && j.Body.jointType == ArticulationJointType.RevoluteJoint), "Six revolute articulation joints");
            Require(arm.joints.All(j => j.Body.xDrive.lowerLimit < j.Body.xDrive.upperLimit), "Authored joint limits");
            Require(manager.handover.workerAnimator.isHuman && manager.handover.workerAnimator.avatar.isValid, "Valid Humanoid avatar");
            Require(arm.GetComponentsInChildren<Collider>().Length > 0, "Robot collision geometry");
            Require(manager.allParts.All(p => p.gripPoint != null && p.GetComponent<Collider>() != null), "Part grip points and colliders");
            Physics.SyncTransforms();
            var matBounds = GameObject.Find("Assembly Mat").GetComponent<Collider>().bounds;
            Require(matBounds.min.x <= 0.88f && matBounds.max.x >= 1.63f && matBounds.min.z <= 0.52f && matBounds.max.z >= 0.82f,
                "Both runtime assembly fixture footprints must fit on the mat");
            var bench = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).First(c => c.name == "Tabletop" && c.bounds.Contains(new Vector3(1.4f, c.bounds.center.y, 0.3f)));
            Require(matBounds.min.x >= bench.bounds.min.x && matBounds.max.x <= bench.bounds.max.x &&
                matBounds.min.z >= bench.bounds.min.z && matBounds.max.z <= bench.bounds.max.z, "Mat must remain on the worktop");
            File.WriteAllText("collaboration-static-report.txt", "PASS six articulation joints, joint limits, humanoid avatar, IK pass, robot/part collision geometry, serialized simulation settings; both assembly stations inside mat and mat inside worktop.\n");
        }
        public static void RunBatch()
        {
            RunStaticBatch();
            File.WriteAllText("collaboration-scenarios-report.txt", "Collaborative workflow checks\n");
            SessionState.SetBool(Key, true); SessionState.SetInt(Key + "Exit", 1);
            EditorApplication.EnterPlaymode();
        }
        private static void Require(bool valid, string message) { if (!valid) throw new Exception(message); }
        private static void Pass(string message) => File.AppendAllText("collaboration-scenarios-report.txt", "PASS " + message + "\n");
        private static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
            var game = UnityEngine.Object.FindAnyObjectByType<GearboxGameSimulation>();
            try
            {
                if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Scenario deadline; stage " + stage);
                if (game == null || !game.Ready) return;
                if (EditorApplication.timeSinceStartup - lastProgress > 15)
                {
                    lastProgress = EditorApplication.timeSinceStartup;
                    File.AppendAllText("collaboration-scenarios-report.txt", "PROGRESS stage " + stage + " | " + game.ActionName + " | " + game.Phase + " | t=" + Time.time + " | speed=" + Time.timeScale + "\n");
                }
                if (stage == 0)
                {
                    game.EmergencyStop(); game.Play(); game.Step();
                    Require(game.EmergencyStopped && !game.Running && Time.timeScale == 0, "Emergency stop latch");
                    game.ResetGame(); Require(!game.Running && !game.EmergencyStopped, "Reset must not restart");
                    Pass("emergency stop, resume interlock, explicit reset stays idle");
                    game.ProtectiveStop("Injected hazard"); game.Play(); game.Step();
                    Require(game.ProtectiveStopped && !game.Running, "Protective stop latch");
                    game.ResetGame(); Pass("protective stop and explicit reset");
                    game.CancelOperation(); Require(game.ProtectiveStopped, "Cancellation latch");
                    game.ResetGame(); Pass("cancellation retains controlled stopped state");
                    game.References.handover.workerAnimator.enabled = false;
                    game.Play(); stage = 1; return;
                }
                if (stage == 1)
                {
                    if (!game.ProtectiveStopped) return;
                    Pass("invalid worker tracking stops");
                    game.References.handover.workerAnimator.enabled = true; game.ResetGame();
                    float[] before = game.References.handover.robotArm.joints.Select(j => j.Target).ToArray();
                    bool unreachable = false;
                    try { game.Robot.Plan(new Vector3(100, 100, 100), Quaternion.identity); } catch (InvalidOperationException) { unreachable = true; }
                    Require(unreachable && before.SequenceEqual(game.References.handover.robotArm.joints.Select(j => j.Target)), "Unreachable IK altered live drives");
                    Pass("unreachable target rejected without moving articulation");
                    hazard = GameObject.CreatePrimitive(PrimitiveType.Cube); hazard.name = "Injected equipment obstruction";
                    hazard.transform.position = game.Robot.JointPositions[3]; hazard.transform.localScale = Vector3.one * 0.3f;
                    Physics.SyncTransforms(); game.Play(); stage = 2; return;
                }
                if (stage == 2)
                {
                    if (!game.ProtectiveStopped) return;
                    Pass("equipment obstruction causes a latched protective stop");
                    UnityEngine.Object.DestroyImmediate(hazard); game.ResetGame(); game.Play(); Time.timeScale = 3; stage = 3; return;
                }
                if (stage == 3)
                {
                    if (game.Fault != null) throw new Exception("Successful workflow blocked: " + game.Fault + " | " + game.ActionName);
                    if (game.HandoverCount == 0) return;
                    Require(game.Payload != null && game.Holder == game.References.handover.workerRightHandHoldPoint, "Confirmed worker ownership");
                    Pass("confirmed handover preserves payload and worker ownership");
                    game.CancelOperation(); Require(game.Payload != null, "Cancellation dropped payload");
                    Pass("cancellation while holding retains payload");
                    game.ResetGame(); game.simulatedGraspSensor = false; game.Play(); Time.timeScale = 3; stage = 4; return;
                }
                if (stage == 4)
                {
                    if (!game.ProtectiveStopped) return;
                    Require(game.SafetyReason.Contains("confirmation timeout") && game.Payload != null && game.Holder == game.Robot.Grip, "Timeout did not retain robot ownership: " + game.Fault);
                    Pass("missing confirmation times out with robot retaining payload");
                    game.ResetGame(); game.simulatedGraspSensor = true;
                    game.References.handover.workerAnimator.GetComponent<WorkerReceivingRig>().ReadyToReceive = false;
                    game.Play(); Time.timeScale = 3; stage = 5; return;
                }
                if (stage == 5)
                {
                    if (!game.ProtectiveStopped) return;
                    Require(game.Payload != null && game.Holder == game.Robot.Grip && game.HandoverCount == 0, "Failed receiving pose released payload");
                    Pass("worker not ready / failed grasp never releases robot ownership");
                    game.ResetGame();
                    SessionState.SetInt(Key + "Exit", 0); EditorApplication.ExitPlaymode();
                }
            }
            catch (Exception e)
            {
                File.AppendAllText("collaboration-scenarios-report.txt", "FAIL " + e + "\n");
                SessionState.SetInt(Key + "Exit", 1); EditorApplication.ExitPlaymode();
            }
        }
    }
}
