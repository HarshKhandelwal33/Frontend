using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GearboxDemo.Editor
{
    public static class GearboxRealisticScale
    {
        [MenuItem("Tools/Gearbox Demo/Match Video Proportions")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            EditorSceneManager.OpenScene("Assets/Scenes/GearboxTraining.unity");
            var manager = UnityEngine.Object.FindAnyObjectByType<GearboxAssemblyManager>();
            var report = new StringBuilder("Video-relative proportions; estimated, not measured physical calibration.\n");
            float factor = 0.12f / GearboxModelAudit.Measure(manager.phaseTwoInsertion.housing.transform).size.x;
            foreach (var part in manager.allParts)
            {
                Undo.RegisterFullObjectHierarchyUndo(part.gameObject, "Match video part size");
                // Keep unit root scale: assembly targets use the Visual wrapper's CAD compensation.
                foreach (Transform child in part.transform)
                {
                    child.localPosition *= factor;
                    if (child.name == "Visual") child.localScale *= factor;
                }
                foreach (var box in part.GetComponents<BoxCollider>()) { box.center *= factor; box.size *= factor; }
                PrefabUtility.RecordPrefabInstancePropertyModifications(part.transform.Find("Visual"));
                PrefabUtility.RecordPrefabInstancePropertyModifications(part.gripPoint);
                PrefabUtility.RecordPrefabInstancePropertyModifications(part.assemblyAnchor);
                foreach (var box in part.GetComponents<BoxCollider>()) PrefabUtility.RecordPrefabInstancePropertyModifications(box);
                var bounds = GearboxModelAudit.Measure(part.transform);
                report.AppendLine(part.partId + " bounds (m): " + bounds.size.ToString("F4"));
            }
            var worker = manager.phaseTwoInsertion.worker;
            var capsule = worker.GetComponent<CharacterController>();
            float workerFactor = 1.8f / (capsule.height * worker.lossyScale.y);
            Undo.RecordObject(worker, "Adult worker height");
            worker.localScale *= workerFactor;
            PrefabUtility.RecordPrefabInstancePropertyModifications(worker);
            var game = manager.GetComponent<GearboxGameSimulation>();
            Undo.RecordObject(game, "Fit scaled grasp geometry");
            game.lidGraspPosition *= factor;
            game.lidGraspHalfGap = (game.lidGraspHalfGap - 0.01f) * factor + 0.01f;
            var gripper = manager.handover.robotGripper.transform;
            Undo.RecordObject(gripper, "Small part gripper tooling");
            game.lidGraspHalfGap *= gripper.localScale.x;
            gripper.localScale = Vector3.one;
            foreach (var finger in new[] { manager.handover.robotGripper.leftFinger, manager.handover.robotGripper.rightFinger })
            {
                Undo.RecordObject(finger, "Short precision fingers");
                finger.localScale = new Vector3(.009f, .09f, .02f);
            }
            var palm = gripper.Find("GripperPalm");
            if (palm != null) { Undo.RecordObject(palm, "Compact tool palm"); palm.localScale = new Vector3(.17f, .045f, .085f); }
            game.workerGraspOffset *= factor;
            game.torsoRadius *= workerFactor; game.limbRadius *= workerFactor; game.receivingHandRadius *= workerFactor;
            EditorUtility.SetDirty(game);
            Physics.SyncTransforms();
            foreach (var part in manager.allParts)
            {
                var bounds = GearboxModelAudit.Measure(part.transform);
                var collider = part.GetComponent<BoxCollider>();
                if (collider == null || Vector3.Distance(collider.size, bounds.size) > 0.002f)
                    throw new InvalidOperationException("Collider does not match scaled part: " + part.partId);
            }
            report.AppendLine("Worker walking envelope: " + (capsule.height * worker.lossyScale.y).ToString("F3") + " m. Robot CAD dimensions and tool frame retained; precision fingers 90 x 9 x 20 mm.");
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            EditorSceneManager.SaveScene(manager.gameObject.scene);
            File.WriteAllText("realistic-scale-report.txt", report.ToString());
            Capture();
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            EditorSceneManager.SaveScene(manager.gameObject.scene);
            GearboxWorkcellScale.Apply();
        }
        private static void Capture()
        {
            var camera = Camera.main;
            camera.transform.position = new Vector3(2.4f, 2.6f, -3.2f);
            camera.transform.LookAt(new Vector3(0, 1.15f, .2f));
            camera.fieldOfView = 42;
            var target = new RenderTexture(1600, 1000, 24);
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            var pixels = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); pixels.Apply();
            File.WriteAllBytes("realistic-scale-preview.png", pixels.EncodeToPNG());
            camera.targetTexture = null; RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(pixels); UnityEngine.Object.DestroyImmediate(target);
        }
        public static void ApplyBatch() { Apply(); EditorApplication.Exit(0); }
    }
}



