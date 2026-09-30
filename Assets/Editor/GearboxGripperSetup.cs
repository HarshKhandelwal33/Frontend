using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GearboxDemo.Editor
{
    public static class GearboxGripperSetup
    {
        [MenuItem("Tools/Gearbox Demo/Setup Robot Gripper")]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != GearboxDemoBuilder.ScenePath) throw new InvalidOperationException("Open GearboxAssembly first.");
            var arm = UnityEngine.Object.FindAnyObjectByType<RobotArmController>();
            if (arm == null) throw new InvalidOperationException("Build Robot first.");
            Configure(arm);
            ValidatePrefabs();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save gripper setup.");
        }

        public static void Configure(RobotArmController arm)
        {
            Transform parent = arm.gripPoint.parent;
            var gripper = parent.GetComponent<RobotGripper>();
            if (gripper == null) gripper = Undo.AddComponent<RobotGripper>(parent.gameObject);
            Undo.RecordObject(gripper, "Configure robot gripper");
            gripper.attachmentPoint = arm.gripPoint;
            gripper.leftFinger = parent.Find("LeftFinger"); gripper.rightFinger = parent.Find("RightFinger");
            gripper.robotRoot = arm.transform;
            gripper.releaseTargets = arm.transform.parent.GetComponentsInChildren<AssemblyTarget>();
            // Accommodate the 24 cm housing/lid while retaining the existing fixed wrist socket.
            foreach (var finger in new[] { gripper.leftFinger, gripper.rightFinger })
            {
                Undo.RecordObject(finger, "Configure visual finger");
                finger.localScale = new Vector3(0.018f, 0.18f, 0.04f);
                finger.localPosition = new Vector3(finger == gripper.leftFinger ? -gripper.openHalfGap : gripper.openHalfGap, 0.09f, 0);
                // Finger motion is visual. Moving collider shapes inside an articulation is unnecessary
                // for deterministic attachment and can invalidate its physics shape buffers.
                foreach (var collider in finger.GetComponents<Collider>())
                { Undo.RecordObject(collider, "Disable visual finger contacts"); collider.enabled = false; }
            }
            Transform palm = parent.Find("GripperPalm");
            Undo.RecordObject(palm, "Widen gripper palm");
            palm.localScale = new Vector3(0.34f, 0.045f, 0.085f);
            EditorUtility.SetDirty(gripper);
        }

        [MenuItem("Tools/Gearbox Demo/Validate Gripper Prefabs")]
        public static void ValidatePrefabs()
        {
            var report = new StringBuilder("Gripper recognition of canonical prefab wrappers\n");
            int errors = 0;
            foreach (var definition in GearboxPartsSetup.Parts)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(definition.PrefabPath);
                var candidate = prefab == null ? null : prefab.GetComponent<GrippableObject>();
                bool valid = RobotGripper.Recognizes(candidate) && candidate.GetComponent<GearboxPart>().partType == definition.Type;
                if (!valid) errors++;
                report.AppendLine((valid ? "PASS " : "FAIL ") + definition.Type + " — " + definition.PrefabPath + " — configured GripPoint, identity, Rigidbody and solid Collider");
            }
            report.AppendLine($"{7 - errors}/7 recognized. No prefab files changed.");
            File.WriteAllText("gripper-recognition-report.txt", report.ToString());
            Debug.Log(report.ToString());
            if (errors > 0) throw new InvalidOperationException("Gripper recognition failed; see report.");
        }
        public static void RunBatch() { EditorSceneManager.OpenScene(GearboxDemoBuilder.ScenePath); Setup(); }
    }

    [CustomEditor(typeof(RobotGripper))]
    public sealed class RobotGripperInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var gripper = (RobotGripper)target;
            EditorGUILayout.HelpBox("Selected Part identifies one existing scene component. Recognition never moves it. Grabbing requires the arm's socket to be near and aligned with that part's GripPoint.", MessageType.Info);
            if (GUILayout.Button("Validate Selected Part")) gripper.ValidateSelectedPart();
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Open / Release")) gripper.QueueDebugCommand(RobotGripper.DebugCommand.Open);
                if (GUILayout.Button("Close Fingers")) gripper.QueueDebugCommand(RobotGripper.DebugCommand.Close);
                if (GUILayout.Button("Try Grab Selected Part")) gripper.QueueDebugCommand(RobotGripper.DebugCommand.GrabSelected);
                if (GUILayout.Button("Try Grab Nearest Eligible Part")) gripper.QueueDebugCommand(RobotGripper.DebugCommand.GrabNearest);
                if (GUILayout.Button("Release / Validate Placement")) gripper.QueueDebugCommand(RobotGripper.DebugCommand.Release);
            }
            EditorGUILayout.LabelField("Held Object", gripper.HeldObject is Component held ? held.name : "None");
            EditorGUILayout.HelpBox(gripper.LastResult, MessageType.None);
            if (Application.isPlaying) Repaint();
        }
    }
}
