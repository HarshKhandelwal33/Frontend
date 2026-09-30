using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GearboxDemo.Editor
{
    public static class GearboxAssemblyPreparation
    {
        private sealed class PartSpec
        {
            public string Name;
            public GearboxPartType Type;
            public string PrefabPath;
            public string TargetName;
            public string Phase;
        }

        private static readonly PartSpec[] Specs =
        {
            Spec("PinCarrier", GearboxPartType.PinCarrier, "Gearbox_PinCarrier", "PinCarrierTarget", "Phase1"),
            Spec("OutputShaft", GearboxPartType.OutputShaft, "Gearbox_OutputShaft", "OutputShaftTarget", "Phase1"),
            Spec("PlanetGear_01", GearboxPartType.PlanetGear, "Gearbox_Gear", "PlanetGearTarget_01", "Phase1"),
            Spec("PlanetGear_02", GearboxPartType.PlanetGear, "Gearbox_Gear", "PlanetGearTarget_02", "Phase1"),
            Spec("PlanetGear_03", GearboxPartType.PlanetGear, "Gearbox_Gear", "PlanetGearTarget_03", "Phase1"),
            Spec("SpurGear", GearboxPartType.SpurGear, "Gearbox_SpurGear", "SpurGearTarget", "Phase1"),
            Spec("Housing", GearboxPartType.Housing, "Gearbox_Housing", "HousingTarget", "Phase2"),
            Spec("Lid", GearboxPartType.Lid, "Gearbox_Lid", "LidTarget", "Phase2"),
            Spec("Screw_01", GearboxPartType.Screw, "Gearbox_Screw", "ScrewTarget_01", "Phase2"),
            Spec("Screw_02", GearboxPartType.Screw, "Gearbox_Screw", "ScrewTarget_02", "Phase2"),
            Spec("Screw_03", GearboxPartType.Screw, "Gearbox_Screw", "ScrewTarget_03", "Phase2"),
            Spec("Screw_04", GearboxPartType.Screw, "Gearbox_Screw", "ScrewTarget_04", "Phase2")
        };

        [MenuItem("Tools/Gearbox Demo/Prepare Gearbox Assembly")]
        public static void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before preparing gearbox assembly.");

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != GearboxDemoBuilder.ScenePath)
                throw new InvalidOperationException("Open Assets/Scenes/GearboxAssembly.unity first.");

            Transform workcell = scene.GetRootGameObjects().Single(g => g.name == "Gearbox Assembly Workcell").transform;
            Transform physicalTray = workcell.Find("PartsTray");
            Transform existingArea = workcell.Find("Assembly Area");
            if (physicalTray == null || physicalTray.Find("Tray Base") == null || existingArea == null)
                throw new InvalidOperationException("Existing PartsTray and Assembly Area are required.");

            foreach (PartSpec spec in Specs)
                if (AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath) == null)
                    throw new InvalidOperationException("Missing gearbox prefab: " + spec.PrefabPath);

            Undo.SetCurrentGroupName("Prepare Gearbox Assembly");
            Transform assembly = EnsureChild(workcell, "GearboxAssembly");
            Transform partsTray = EnsureChild(assembly, "PartsTray");
            Transform station = EnsureChild(assembly, "AssemblyStation");
            Transform phase1Fixture = EnsureChild(station, "Phase1Fixture");
            Transform phase2Fixture = EnsureChild(station, "Phase2Fixture");
            Transform targets = EnsureChild(station, "AssemblyTargets");
            Transform phase1 = EnsureChild(targets, "Phase1");
            Transform phase2 = EnsureChild(targets, "Phase2");
            EnsureChild(station, "AssembledGearbox");

            Transform assemblyOrigin = existingArea.Find("Assembly Origin");
            Vector3 stationPosition = assemblyOrigin != null ? assemblyOrigin.position : existingArea.position;
            station.SetPositionAndRotation(stationPosition, existingArea.rotation);
            station.localScale = Vector3.one;
            phase1Fixture.localPosition = phase2Fixture.localPosition = Vector3.zero;
            phase1Fixture.localRotation = phase2Fixture.localRotation = Quaternion.identity;

            // Remove the previous single-instance scene parts and provisional targets only.
            foreach (GearboxPart part in workcell.GetComponentsInChildren<GearboxPart>(true))
                Undo.DestroyObjectImmediate(part.gameObject);
            Transform oldTargets = existingArea.Find("AssemblyTargets");
            if (oldTargets != null) Undo.DestroyObjectImmediate(oldTargets.gameObject);
            ClearChildren(partsTray);
            ClearChildren(phase1);
            ClearChildren(phase2);

            var targetByName = new Dictionary<string, AssemblyTarget>();
            foreach (PartSpec spec in Specs)
            {
                Transform phase = spec.Phase == "Phase1" ? phase1 : phase2;
                Transform targetTransform = EnsureChild(phase, spec.TargetName);
                targetTransform.localPosition = Vector3.zero;
                targetTransform.localRotation = Quaternion.identity;
                AssemblyTarget target = Undo.AddComponent<AssemblyTarget>(targetTransform.gameObject);
                target.acceptedPart = spec.Type;
                targetByName.Add(spec.TargetName, target);
            }
            Transform internalTarget = EnsureChild(phase2, "InternalAssemblyTarget");
            internalTarget.localPosition = Vector3.zero;
            internalTarget.localRotation = Quaternion.identity;

            Bounds trayBounds = physicalTray.Find("Tray Base").GetComponent<Collider>().bounds;
            float trayTop = trayBounds.max.y + 0.008f;
            const int columns = 4;
            float spacingX = trayBounds.size.x / columns;
            float spacingZ = trayBounds.size.z / 3f;
            for (int i = 0; i < Specs.Length; i++)
            {
                PartSpec spec = Specs[i];
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, partsTray);
                Undo.RegisterCreatedObjectUndo(instance, "Create " + spec.Name);
                instance.name = spec.Name;
                float x = trayBounds.min.x + spacingX * (0.5f + i % columns);
                float z = trayBounds.min.z + spacingZ * (0.5f + i / columns);
                instance.transform.SetPositionAndRotation(new Vector3(x, trayTop, z), Quaternion.identity);
                GearboxPart part = instance.GetComponent<GearboxPart>();
                part.partType = spec.Type;
                part.partId = spec.Name;
                part.displayName = ObjectNames.NicifyVariableName(spec.Name);
                part.assembled = false;
                part.canBePicked = true;
                part.assemblyTarget = targetByName[spec.TargetName];
                EditorUtility.SetDirty(part);
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            Validate(workcell);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Could not save prepared gearbox scene.");
        }

        private static void Validate(Transform workcell)
        {
            Transform root = workcell.Find("GearboxAssembly");
            if (root == null) throw new InvalidOperationException("GearboxAssembly hierarchy is missing.");
            GearboxPart[] parts = root.GetComponentsInChildren<GearboxPart>(true);
            if (parts.Length != Specs.Length) throw new InvalidOperationException("Expected 12 gearbox part instances.");
            foreach (PartSpec spec in Specs)
            {
                GearboxPart part = parts.SingleOrDefault(p => p.partId == spec.Name);
                if (part == null || part.partType != spec.Type || part.assemblyTarget == null || part.assemblyTarget.name != spec.TargetName)
                    throw new InvalidOperationException("Invalid part/target mapping: " + spec.Name);
            }
            string report = "PASS 12 gearbox instances, 12 typed targets plus InternalAssemblyTarget, unique IDs, and explicit target assignments.\n" +
                "All target local transforms are identity for manual adjustment; no final assembly coordinates were inferred.\n";
            File.WriteAllText("assembly-preparation-report.txt", report);
            Debug.Log(report);
        }

        private static PartSpec Spec(string name, GearboxPartType type, string prefab, string target, string phase)
        {
            return new PartSpec { Name = name, Type = type, PrefabPath = "Assets/Prefabs/Gearbox/" + prefab + ".prefab", TargetName = target, Phase = phase };
        }

        private static Transform EnsureChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null) return child;
            var obj = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(obj, "Create " + name);
            obj.transform.SetParent(parent, false);
            return obj.transform;
        }

        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(parent.GetChild(i).gameObject);
        }

        public static void RunBatch()
        {
            EditorSceneManager.OpenScene(GearboxDemoBuilder.ScenePath);
            Prepare();
        }
    }
}
