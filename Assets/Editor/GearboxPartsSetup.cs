using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GearboxDemo.Editor
{
    public static class GearboxPartsSetup
    {
        public sealed class Definition
        {
            public readonly GearboxPartType Type;
            public readonly string File;
            public readonly float Mass;
            public Definition(GearboxPartType type, string file, float mass) { Type = type; File = file; Mass = mass; }
            public string ModelPath => "Assets/Models/Gearbox/" + File + ".fbx";
            public string PrefabPath => "Assets/Prefabs/Gearbox/Gearbox_" + Type + ".prefab";
        }

        public static readonly Definition[] Parts =
        {
            new Definition(GearboxPartType.Housing, "planetarygearbox-housing_30_54-1", 1.5f),
            new Definition(GearboxPartType.OutputShaft, "planetarygearbox-outputshaft-1", 0.6f),
            new Definition(GearboxPartType.PinCarrier, "planetarygearbox-pin_carrier-1", 0.4f),
            new Definition(GearboxPartType.PlanetGear, "planetarygearbox-gear_2_30_18-3", 0.2f),
            new Definition(GearboxPartType.SpurGear, "planetarygearbox-spur_2_30_18-2", 0.25f),
            new Definition(GearboxPartType.Lid, "planetarygearbox-lid-1", 0.5f),
            new Definition(GearboxPartType.Screw, "planetarygearbox-screw-1", 0.05f)
        };

        // Uniform presentation scale for the complete set, preserving relative dimensions.
        // These FBXs share Z as their assembly axis and have assembly-space origin offsets.
        private const float PresentationScale = 0.4f;

        [MenuItem("Tools/Gearbox Demo/Setup Gearbox Parts")]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before setting up parts.");
            string[] missing = Parts.Where(p => AssetDatabase.LoadAssetAtPath<GameObject>(p.ModelPath) == null)
                .Select(p => p.ModelPath).ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException("Gearbox setup stopped. Missing or unimportable canonical FBX assets:\n" + string.Join("\n", missing));

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != GearboxDemoBuilder.ScenePath)
                throw new InvalidOperationException("Open " + GearboxDemoBuilder.ScenePath + " before setting up gearbox parts.");
            Transform workcell = scene.GetRootGameObjects().SingleOrDefault(g => g.name == "Gearbox Assembly Workcell")?.transform;
            Transform tray = workcell != null ? workcell.Find("PartsTray") ?? workcell.Find("Parts Tray") : null;
            Transform area = workcell != null ? workcell.Find("Assembly Area") : null;
            if (tray == null || area == null || tray.Find("Tray Base") == null)
                throw new InvalidOperationException("Expected Parts Tray, Tray Base, and Assembly Area. Build the demo environment first.");

            // Preflight geometry before changing the scene or any prefab.
            foreach (Definition definition in Parts)
                GearboxModelAudit.Measure(AssetDatabase.LoadAssetAtPath<GameObject>(definition.ModelPath).transform);

            var report = new StringBuilder("Gearbox prefab setup — all seven canonical FBXs found\n");
            foreach (Definition definition in Parts) CreateOrUpdatePrefab(definition, report);

            Undo.SetCurrentGroupName("Setup Gearbox Parts");
            Undo.RecordObject(tray.gameObject, "Name PartsTray");
            tray.name = "PartsTray";
            Transform robotTargets = Child(workcell, "RobotTargets", out _);
            Transform pickupTargets = Child(robotTargets, "PickupTargets", out _);
            Transform assemblyTargets = Child(area, "AssemblyTargets", out _);
            float trayTop = tray.InverseTransformPoint(tray.Find("Tray Base").GetComponent<Collider>().bounds.max).y;

            for (int i = 0; i < Parts.Length; i++)
            {
                Definition definition = Parts[i];
                Transform start = Child(tray, definition.Type + "Start", out bool newStart);
                if (newStart) start.localPosition = new Vector3(-0.48f + (i % 4) * 0.32f, trayTop + 0.006f, i < 4 ? -0.22f : 0.22f);
                GearboxPart part = start.GetComponentInChildren<GearboxPart>(true);
                if (part == null)
                {
                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(definition.PrefabPath), start);
                    Undo.RegisterCreatedObjectUndo(instance, "Place gearbox part");
                    instance.transform.localPosition = Vector3.zero;
                    instance.transform.localRotation = Quaternion.identity;
                    part = instance.GetComponent<GearboxPart>();
                }
                Transform pickup = Child(pickupTargets, definition.Type + "Pickup", out bool newPickup);
                if (newPickup) pickup.SetPositionAndRotation(part.gripPoint.position, part.gripPoint.rotation);
                Transform target = Child(assemblyTargets, definition.Type + "Target", out bool newTarget);
                if (newTarget)
                {
                    // Staging references only, deliberately separated, not a claimed mechanical stack/order.
                    target.localPosition = new Vector3(-0.48f + (i % 4) * 0.32f, 0.035f, i < 4 ? -0.2f : 0.2f);
                    Undo.AddComponent<AssemblyTarget>(target.gameObject).acceptedPart = definition.Type;
                }
                else if (target.GetComponent<AssemblyTarget>() == null)
                    Undo.AddComponent<AssemblyTarget>(target.gameObject).acceptedPart = definition.Type;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            Validate();
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Failed to save gearbox scene.");
            File.WriteAllText("gearbox-setup-report.txt", report.ToString());
            Debug.Log(report.ToString());
        }

        private static void CreateOrUpdatePrefab(Definition definition, StringBuilder report)
        {
            bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(definition.PrefabPath) != null;
            GameObject root = existing ? PrefabUtility.LoadPrefabContents(definition.PrefabPath) : new GameObject("Gearbox_" + definition.Type);
            try
            {
                Transform visual = root.transform.Find("Visual");
                if (visual == null)
                {
                    visual = new GameObject("Visual").transform;
                    visual.SetParent(root.transform, false);
                    PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(definition.ModelPath), visual);
                    visual.localRotation = Quaternion.Euler(-90, 0, 0);
                    visual.localScale = Vector3.one * PresentationScale;
                    Bounds uncentered = GearboxModelAudit.Measure(root.transform);
                    visual.localPosition = new Vector3(-uncentered.center.x, -uncentered.min.y, -uncentered.center.z);
                }
                // All compensation is on our Visual wrapper, never on the nested FBX transform/importer.
                Bounds bounds = GearboxModelAudit.Measure(root.transform);
                GearboxPart part = root.GetComponent<GearboxPart>() ?? root.AddComponent<GearboxPart>();
                part.partType = definition.Type;
                if (string.IsNullOrEmpty(part.partId)) part.partId = definition.Type + "_01";
                if (string.IsNullOrEmpty(part.displayName)) part.displayName = ObjectNames.NicifyVariableName(definition.Type.ToString());
                Transform grip = root.transform.Find("GripPoint");
                if (grip == null)
                {
                    grip = new GameObject("GripPoint").transform;
                    grip.SetParent(root.transform, false);
                    grip.localPosition = new Vector3(bounds.center.x, bounds.max.y + 0.015f, bounds.center.z);
                    grip.localRotation = Quaternion.Euler(180, 0, 0);
                }
                Transform anchor = root.transform.Find("AssemblyAnchor");
                if (anchor == null)
                {
                    anchor = new GameObject("AssemblyAnchor").transform;
                    anchor.SetParent(root.transform, false);
                    anchor.localPosition = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                }
                part.gripPoint = grip;
                part.assemblyAnchor = anchor;
                if (root.GetComponent<GrippableObject>() == null) root.AddComponent<GrippableObject>();
                if (root.GetComponent<Collider>() == null)
                {
                    BoxCollider collider = root.AddComponent<BoxCollider>();
                    collider.center = bounds.center;
                    collider.size = bounds.size;
                    collider.contactOffset = 0.001f;
                }
                Rigidbody body = root.GetComponent<Rigidbody>();
                if (!existing)
                {
                    body.mass = definition.Mass;
                    body.linearDamping = 0.15f;
                    body.angularDamping = 0.6f;
                    body.useGravity = true;
                    body.isKinematic = false;
                    body.interpolation = RigidbodyInterpolation.Interpolate;
                    body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                    body.solverIterations = 12;
                    body.solverVelocityIterations = 4;
                }
                PrefabUtility.SaveAsPrefabAsset(root, definition.PrefabPath);
                report.AppendLine($"{definition.Type}: {root.GetComponent<Collider>().GetType().Name}; mesh bounds={bounds.size.ToString("F4")} m; mass={body.mass:F2} kg; presentation scale={PresentationScale}. Grip/anchor and existing tuning preserved.");
            }
            finally
            {
                if (existing) PrefabUtility.UnloadPrefabContents(root);
                else UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Transform Child(Transform parent, string name, out bool created)
        {
            Transform result = parent.Find(name);
            created = result == null;
            if (!created) return result;
            var obj = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(obj, "Create " + name);
            obj.transform.SetParent(parent, false);
            return obj.transform;
        }

        [MenuItem("Tools/Gearbox Demo/Validate Gearbox Parts")]
        public static void Validate()
        {
            var report = new StringBuilder("GEARBOX VALIDATION\n");
            int errors = 0;
            Action<bool, string> check = (ok, message) => { report.AppendLine((ok ? "PASS " : "FAIL ") + message); if (!ok) errors++; };
            Scene scene = SceneManager.GetActiveScene();
            Transform workcell = scene.GetRootGameObjects().SingleOrDefault(g => g.name == "Gearbox Assembly Workcell")?.transform;
            GearboxPart[] sceneParts = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<GearboxPart>(true)).ToArray();
            check(sceneParts.Length == 7, "Exactly seven scene parts");
            check(sceneParts.Select(p => p.partId).Distinct().Count() == sceneParts.Length, "Unique scene part IDs");
            foreach (Definition definition in Parts)
            {
                GearboxPart[] matching = sceneParts.Where(p => p.partType == definition.Type).ToArray();
                check(matching.Length == 1, definition.Type + " exists once");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(definition.PrefabPath);
                check(prefab != null, definition.Type + " prefab exists");
                foreach (GameObject obj in matching.Select(p => p.gameObject).Concat(prefab == null ? Array.Empty<GameObject>() : new[] { prefab }))
                {
                    GearboxPart part = obj.GetComponent<GearboxPart>();
                    string label = obj.name + (EditorUtility.IsPersistent(obj) ? " prefab" : " scene");
                    check(part != null && part.partType == definition.Type && !string.IsNullOrEmpty(part.partId), label + " identity");
                    check(obj.GetComponent<GrippableObject>() != null, label + " GrippableObject");
                    check(part != null && part.gripPoint != null && part.gripPoint.IsChildOf(obj.transform), label + " GripPoint");
                    check(part != null && part.assemblyAnchor != null && part.assemblyAnchor.IsChildOf(obj.transform), label + " AssemblyAnchor");
                    var body = obj.GetComponent<Rigidbody>();
                    check(body != null && !body.isKinematic && body.useGravity && body.mass > 0, label + " dynamic Rigidbody with gravity");
                    var colliders = obj.GetComponentsInChildren<Collider>(true);
                    check(colliders.Any(c => c.enabled && !c.isTrigger) && colliders.All(c => !(c is MeshCollider mesh) || mesh.convex), label + " solid, moving-body-safe collider");
                    Transform visual = obj.transform.Find("Visual");
                    check(visual != null && visual.childCount == 1 && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(visual.GetChild(0).gameObject) == definition.ModelPath, label + " canonical nested FBX");
                }
                check(workcell != null && workcell.Find("RobotTargets/PickupTargets/" + definition.Type + "Pickup") != null, definition.Type + " pickup target");
                Transform target = workcell != null ? workcell.Find("Assembly Area/AssemblyTargets/" + definition.Type + "Target") : null;
                check(target != null && target.GetComponent<AssemblyTarget>() != null && target.GetComponent<AssemblyTarget>().acceptedPart == definition.Type, definition.Type + " assembly target mapping");
            }
            report.AppendLine($"Result: {errors} error(s).");
            File.WriteAllText("gearbox-validation-report.txt", report.ToString());
            if (errors > 0) { Debug.LogError(report.ToString()); throw new InvalidOperationException("Gearbox validation failed. See Console/report."); }
            Debug.Log(report.ToString());
        }

        public static void RunBatch()
        {
            EditorSceneManager.OpenScene(GearboxDemoBuilder.ScenePath);
            Setup();
        }
    }
}
