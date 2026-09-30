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
    public static class GearboxWorkerBuilder
    {
        private const string Folder = "Assets/Prefabs/Human";
        private const string PrefabPath = Folder + "/Worker.prefab";
        private const string AvatarPath = Folder + "/WorkerHumanoid.asset";

        [MenuItem("Tools/Gearbox Demo/Create or Place Worker")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != GearboxDemoBuilder.ScenePath) throw new InvalidOperationException("Open GearboxAssembly first.");
            Transform root = scene.GetRootGameObjects().Single(g => g.name == "Gearbox Assembly Workcell").transform;
            if (root.Find("Worker") != null)
            {
                Selection.activeGameObject = root.Find("Worker").gameObject;
                Validate();
                Debug.Log("Existing Worker reused. Inspector position and rotation preserved.");
                return;
            }
            var floor = root.Find("Floor").GetComponent<Collider>().bounds;
            var bench = root.Find("Workbench/Tabletop").GetComponent<Collider>().bounds;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) prefab = CreatePrefab();
            GameObject worker = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            Undo.RegisterCreatedObjectUndo(worker, "Place Worker");
            worker.name = "Worker";
            worker.transform.SetPositionAndRotation(new Vector3(bench.center.x, floor.max.y, bench.max.z + 0.65f), Quaternion.Euler(0, 180, 0));
            Transform marker = root.Find("Human Placeholder Position");
            if (marker != null) { Undo.RecordObject(marker.gameObject, "Hide replaced human marker"); marker.gameObject.SetActive(false); }
            Validate();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save Worker placement.");
            Selection.activeGameObject = worker;
        }

        private static GameObject CreatePrefab()
        {
            var worker = new GameObject("Worker");
            try
            {
                Transform model = Node("WorkerModel", worker.transform, Vector3.zero);
                var bones = new Dictionary<string, Transform>();
                Transform Bone(string name, Transform parent, Vector3 pos)
                {
                    Transform bone = Node(name, parent, pos); bones.Add(name, bone); return bone;
                }
                Transform hips = Bone("Hips", model, new Vector3(0, 0.94f, 0));
                Transform spine = Bone("Spine", hips, new Vector3(0, 0.16f, 0));
                Transform chest = Bone("Chest", spine, new Vector3(0, 0.22f, 0));
                Transform neck = Bone("Neck", chest, new Vector3(0, 0.19f, 0));
                Transform head = Bone("Head", neck, new Vector3(0, 0.13f, 0));
                foreach (string side in new[] { "Left", "Right" })
                {
                    float sign = side == "Left" ? -1 : 1;
                    Transform shoulder = Bone(side + "Shoulder", chest, new Vector3(sign * 0.15f, 0.10f, 0));
                    Transform upper = Bone(side + "UpperArm", shoulder, new Vector3(sign * 0.10f, 0, 0));
                    Transform lower = Bone(side + "LowerArm", upper, new Vector3(sign * 0.27f, 0, 0));
                    Bone(side + "Hand", lower, new Vector3(sign * 0.24f, 0, 0));
                    Transform thigh = Bone(side + "UpperLeg", hips, new Vector3(sign * 0.115f, -0.08f, 0));
                    Transform shin = Bone(side + "LowerLeg", thigh, new Vector3(0, -0.38f, 0));
                    Transform foot = Bone(side + "Foot", shin, new Vector3(0, -0.40f, 0));
                    Bone(side + "Toes", foot, new Vector3(0, -0.015f, 0.15f));
                }
                // Calibrate the humanoid in a T-pose before posing the static model naturally.
                var description = new HumanDescription
                {
                    human = bones.Select(pair => new HumanBone { humanName = pair.Key, boneName = pair.Value.name, limit = new HumanLimit { useDefaultValues = true } }).ToArray(),
                    skeleton = model.GetComponentsInChildren<Transform>().Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray(),
                    upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                    armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0, hasTranslationDoF = false
                };
                Avatar avatar = AvatarBuilder.BuildHumanAvatar(model.gameObject, description);
                avatar.name = "Worker Humanoid";
                if (!avatar.isValid || !avatar.isHuman) throw new InvalidOperationException("Generated Worker humanoid avatar is invalid.");
                AssetDatabase.CreateAsset(avatar, AvatarPath);
                Animator animator = model.gameObject.AddComponent<Animator>();
                animator.avatar = avatar;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Folder + "/Worker.controller");

                Material navy = Material("WorkerNavy", new Color(0.08f, 0.17f, 0.25f));
                Material vest = Material("WorkerHiVis", new Color(1f, 0.42f, 0.045f));
                Material helmet = Material("WorkerHelmet", new Color(1f, 0.72f, 0.09f));
                Material skin = Material("WorkerSkin", new Color(0.60f, 0.37f, 0.23f));
                Material dark = Material("WorkerBoots", new Color(0.06f, 0.07f, 0.08f));
                Material reflective = Material("WorkerReflective", new Color(0.80f, 0.88f, 0.87f));
                Material lens = Material("WorkerGlasses", new Color(0.19f, 0.35f, 0.41f));
                Shape("TrousersWaist", PrimitiveType.Capsule, hips, new Vector3(0, -0.035f, 0), new Vector3(0.33f, 0.13f, 0.24f), navy);
                Shape("Jacket", PrimitiveType.Capsule, spine, new Vector3(0, 0.13f, 0), new Vector3(0.43f, 0.22f, 0.26f), navy);
                Shape("SafetyVest", PrimitiveType.Capsule, spine, new Vector3(0, 0.14f, 0.014f), new Vector3(0.445f, 0.20f, 0.275f), vest);
                Shape("ReflectiveBeltFront", PrimitiveType.Cube, spine, new Vector3(0, 0.07f, 0.15f), new Vector3(0.36f, 0.04f, 0.013f), reflective);
                Shape("ReflectiveBeltBack", PrimitiveType.Cube, spine, new Vector3(0, 0.07f, -0.135f), new Vector3(0.36f, 0.04f, 0.013f), reflective);
                foreach (float x in new[] { -0.10f, 0.10f })
                    Shape("ReflectiveShoulder" + (x < 0 ? "Left" : "Right"), PrimitiveType.Cube, spine, new Vector3(x, 0.22f, 0.15f), new Vector3(0.035f, 0.20f, 0.012f), reflective);
                Shape("Zipper", PrimitiveType.Cube, spine, new Vector3(0, 0.19f, 0.155f), new Vector3(0.012f, 0.24f, 0.012f), dark);
                Shape("NeckSkin", PrimitiveType.Capsule, neck, new Vector3(0, 0.06f, 0), new Vector3(0.11f, 0.065f, 0.11f), skin);
                Shape("HeadMesh", PrimitiveType.Sphere, head, new Vector3(0, 0.06f, 0), new Vector3(0.205f, 0.25f, 0.205f), skin);
                Shape("Nose", PrimitiveType.Sphere, head, new Vector3(0, 0.055f, 0.105f), new Vector3(0.034f, 0.044f, 0.044f), skin);
                Shape("SafetyGlassesFrame", PrimitiveType.Cube, head, new Vector3(0, 0.093f, 0.10f), new Vector3(0.18f, 0.05f, 0.026f), dark);
                foreach (float x in new[] { -0.045f, 0.045f })
                    Shape("Lens" + (x < 0 ? "Left" : "Right"), PrimitiveType.Cube, head, new Vector3(x, 0.093f, 0.116f), new Vector3(0.065f, 0.03f, 0.01f), lens);
                Shape("HardHatShell", PrimitiveType.Sphere, head, new Vector3(0, 0.162f, 0), new Vector3(0.235f, 0.15f, 0.245f), helmet);
                Shape("HardHatBrim", PrimitiveType.Cylinder, head, new Vector3(0, 0.127f, 0.018f), new Vector3(0.27f, 0.009f, 0.30f), helmet);
                foreach (string side in new[] { "Left", "Right" })
                {
                    float sign = side == "Left" ? -1 : 1;
                    Segment(side + "Sleeve", bones[side + "UpperArm"], new Vector3(sign * 0.27f, 0, 0), 0.125f, navy);
                    Segment(side + "Forearm", bones[side + "LowerArm"], new Vector3(sign * 0.24f, 0, 0), 0.10f, navy);
                    Shape(side + "Glove", PrimitiveType.Capsule, bones[side + "Hand"], new Vector3(sign * 0.045f, 0, 0), new Vector3(0.10f, 0.055f, 0.08f), dark);
                    Segment(side + "TrouserThigh", bones[side + "UpperLeg"], new Vector3(0, -0.38f, 0), 0.16f, navy);
                    Segment(side + "TrouserShin", bones[side + "LowerLeg"], new Vector3(0, -0.40f, 0), 0.135f, navy);
                    Shape(side + "Boot", PrimitiveType.Cube, bones[side + "Foot"], new Vector3(0, 0, 0.065f), new Vector3(0.16f, 0.16f, 0.31f), dark);
                }
                bones["LeftUpperArm"].localRotation = Quaternion.Euler(0, -5, 78);
                bones["RightUpperArm"].localRotation = Quaternion.Euler(0, 5, -78);
                bones["LeftLowerArm"].localRotation = Quaternion.Euler(0, 12, 0);
                bones["RightLowerArm"].localRotation = Quaternion.Euler(0, -12, 0);
                CompleteClothing(worker);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(worker, PrefabPath);
                AssetDatabase.SaveAssets();
                return prefab;
            }
            finally { UnityEngine.Object.DestroyImmediate(worker); }
        }

        private static Transform Node(string name, Transform parent, Vector3 position)
        { var obj = new GameObject(name); obj.transform.SetParent(parent, false); obj.transform.localPosition = position; return obj.transform; }
        private static GameObject Shape(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            GameObject obj = GameObject.CreatePrimitive(type); obj.name = name; obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position; obj.transform.localScale = scale; obj.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>()); // Static visual avatar only; no interaction physics.
            return obj;
        }
        private static void Segment(string name, Transform parent, Vector3 end, float diameter, Material material)
        {
            var obj = Shape(name, PrimitiveType.Capsule, parent, end / 2, new Vector3(diameter, end.magnitude / 2, diameter), material);
            obj.transform.localRotation = Quaternion.FromToRotation(Vector3.up, end.normalized);
        }
        private static Material Material(string name, Color color)
        {
            string path = "Assets/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Standard")) { name = name, color = color };
            material.SetFloat("_Glossiness", 0.18f); AssetDatabase.CreateAsset(material, path); return material;
        }
        public static void Validate()
        {
            if (UnityEngine.Object.FindObjectsByType<Transform>().Count(t => t.name == "Worker") != 1)
                throw new InvalidOperationException("Expected exactly one Worker scene object.");
            var worker = GameObject.Find("Worker");
            if (worker == null) throw new InvalidOperationException("Worker is missing.");
            var animator = worker.GetComponentInChildren<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new InvalidOperationException("Worker Animator/Humanoid avatar validation failed.");
            var renderers = worker.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds; foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            // Imported skin bounds include every possible pose; validate the standing footprint with its capsule.
            var character = worker.GetComponent<CharacterController>();
            bool skinned = worker.GetComponentInChildren<SkinnedMeshRenderer>() != null;
            if (skinned && character != null) bounds = character.bounds;
            float floorTop = GameObject.Find("Floor").GetComponent<Collider>().bounds.max.y;
            if (Mathf.Abs(bounds.min.y - floorTop) > 0.005f) throw new InvalidOperationException("Worker feet are not on the floor.");
            foreach (var collider in UnityEngine.Object.FindObjectsByType<Collider>())
            {
                if (!collider.enabled || collider.name == "Floor" || collider.transform.IsChildOf(worker.transform)) continue;
                if (bounds.Intersects(collider.bounds)) throw new InvalidOperationException("Worker overlaps " + collider.name);
            }
            string report = $"PASS one visible Worker; Animator; valid Humanoid avatar; feet on floor at {floorTop:F3} m; no overlap with scene colliders.\nWorker bounds {bounds.size:F3}, position {worker.transform.position:F3}.\nAnimator controller: {animator.runtimeAnimatorController}.\n";
            File.WriteAllText("worker-validation-report.txt", report); Debug.Log(report);
        }
        public static void RunBatch() { EditorSceneManager.OpenScene(GearboxDemoBuilder.ScenePath); Build(); Build(); }

        private static void CompleteClothing(GameObject worker)
        {
            var bones = worker.GetComponentsInChildren<Transform>().ToDictionary(t => t.name, t => t);
            Material navy = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/WorkerNavy.mat");
            void Add(string name, string bone, PrimitiveType type, Vector3 position, Vector3 scale)
            { if (!bones.ContainsKey(name)) Shape(name, type, bones[bone], position, scale, navy); }
            Add("JacketShoulderYoke", "Chest", PrimitiveType.Sphere, new Vector3(0, 0.09f, 0), new Vector3(0.41f, 0.19f, 0.25f));
            Add("JacketCollar", "Neck", PrimitiveType.Cylinder, new Vector3(0, 0.015f, 0), new Vector3(0.15f, 0.04f, 0.15f));
            foreach (string side in new[] { "Left", "Right" })
            {
                Add(side + "ElbowJoin", side + "LowerArm", PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.105f);
                Add(side + "KneeJoin", side + "LowerLeg", PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.135f);
            }
        }

        public static void FinishVisualsBatch()
        {
            var prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
            try { CompleteClothing(prefab); PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath); }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(GearboxDemoBuilder.ScenePath);
            Validate(); EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            CapturePreview();
        }

        public static void CapturePreview()
        {
            EditorSceneManager.OpenScene(GearboxDemoBuilder.ScenePath);
            Validate();
            Camera camera = Camera.main;
            RenderTexture previous = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            var render = new RenderTexture(1200, 900, 24);
            var pixels = new Texture2D(1200, 900, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = render; RenderTexture.active = render;
                camera.Render(); pixels.ReadPixels(new Rect(0, 0, 1200, 900), 0, 0); pixels.Apply();
                File.WriteAllBytes("worker-workcell-preview.png", pixels.EncodeToPNG());
                Vector3 position = GameObject.Find("Worker").transform.position;
                camera.transform.position = position + new Vector3(2.3f, 2.05f, -1.25f);
                camera.transform.LookAt(position + Vector3.up * 0.95f);
                camera.fieldOfView = 48;
                camera.Render(); pixels.ReadPixels(new Rect(0, 0, 1200, 900), 0, 0); pixels.Apply();
                File.WriteAllBytes("worker-preview.png", pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget; RenderTexture.active = previous; render.Release();
                UnityEngine.Object.DestroyImmediate(render); UnityEngine.Object.DestroyImmediate(pixels);
            }
            // Preview camera pose is intentionally not saved to the scene.
        }
    }
}
