using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace GearboxDemo.Editor
{
    /// <summary>Rebuildable static workcell. No assembly logic or moving actors.</summary>
    public static class GearboxDemoBuilder
    {
        public const string ScenePath = "Assets/Scenes/GearboxAssembly.unity";
        private static readonly string[] Folders =
        {
            "Assets/Scenes", "Assets/Models/Gearbox", "Assets/Prefabs/Robot",
            "Assets/Prefabs/Human", "Assets/Prefabs/Gearbox", "Assets/Prefabs/Workstation",
            "Assets/Scripts/Core", "Assets/Scripts/Assembly", "Assets/Scripts/Robot",
            "Assets/Scripts/Human", "Assets/Scripts/Interaction", "Assets/Scripts/UI",
            "Assets/Editor", "Assets/Materials", "Assets/ScriptableObjects"
        };

        [MenuItem("Tools/Gearbox Demo/Build Demo Scene")]
        public static void BuildDemoScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before building the demo.");

            if (!Application.isBatchMode)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null &&
                    !EditorUtility.DisplayDialog("Rebuild Gearbox Demo",
                        "Replace the generated scene at " + ScenePath + "? Manual scene edits will be replaced.",
                        "Rebuild", "Cancel")) return;
            }

            foreach (string folder in Folders) EnsureFolder(folder);

            Material floor = GetMaterial("Floor", new Color(0.20f, 0.23f, 0.26f));
            Material steel = GetMaterial("Steel", new Color(0.35f, 0.40f, 0.45f));
            Material top = GetMaterial("Worktop", new Color(0.69f, 0.73f, 0.76f));
            Material tray = GetMaterial("Tray", new Color(0.12f, 0.18f, 0.23f));
            Material assembly = GetMaterial("Assembly", new Color(0.12f, 0.65f, 0.43f));
            Material robot = GetMaterial("RobotPosition", new Color(1f, 0.57f, 0.08f));
            Material human = GetMaterial("HumanPosition", new Color(0.12f, 0.55f, 0.95f));

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Transform root = new GameObject("Gearbox Assembly Workcell").transform;
            Box("Floor", root, new Vector3(0, -0.10f, 0), new Vector3(8, 0.2f, 6), floor);

            // Coordinates are metres. The operator stands behind the bench (+Z).
            Table("Workbench", root, new Vector3(1.4f, 0, 0.3f), 2.2f, 1.2f, top, steel);
            Table("Parts Table", root, new Vector3(-1.8f, 0, 0.3f), 1.6f, 1.2f, top, steel);
            Transform partsTray = Group("Parts Tray", root, new Vector3(-1.8f, 1.025f, 0.3f));
            Box("Tray Base", partsTray, Vector3.zero, new Vector3(1.4f, 0.05f, 1), tray);
            Box("Tray Rim Front", partsTray, new Vector3(0, 0.075f, -0.48f), new Vector3(1.4f, 0.1f, 0.04f), tray);
            Box("Tray Rim Back", partsTray, new Vector3(0, 0.075f, 0.48f), new Vector3(1.4f, 0.1f, 0.04f), tray);
            Box("Tray Rim Left", partsTray, new Vector3(-0.68f, 0.075f, 0), new Vector3(0.04f, 0.1f, 0.92f), tray);
            Box("Tray Rim Right", partsTray, new Vector3(0.68f, 0.075f, 0), new Vector3(0.04f, 0.1f, 0.92f), tray);

            Transform area = Group("Assembly Area", root, new Vector3(1.4f, 1f, 0.3f));
            Box("Assembly Mat", area, new Vector3(0, 0.015f, 0), new Vector3(1.3f, 0.03f, 0.8f), assembly);
            Group("Assembly Origin", area, new Vector3(0, 0.03f, 0));
            PositionMarker("Robot Placeholder Position", root, new Vector3(-0.25f, 0, -0.65f), robot);
            Transform operatorPosition = PositionMarker("Human Placeholder Position", root, new Vector3(1.4f, 0, 1.65f), human);
            operatorPosition.localRotation = Quaternion.Euler(0, 180, 0);

            Transform cameraTransform = Group("Main Camera", root, new Vector3(6.8f, 6.2f, -8.5f));
            cameraTransform.LookAt(new Vector3(0, 0.5f, 0.35f));
            Camera camera = cameraTransform.gameObject.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.fieldOfView = 45;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.11f, 0.14f, 0.18f);

            Transform lightTransform = Group("Directional Light", root, Vector3.zero);
            lightTransform.rotation = Quaternion.Euler(50, -35, 0);
            Light light = lightTransform.gameObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.63f);

            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("Could not save demo scene: " + ScenePath);
            Selection.activeGameObject = root.gameObject;
            Debug.Log("Gearbox demo built and saved: " + ScenePath);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }

        private static Material GetMaterial(string name, Color color)
        {
            string path = "Assets/Materials/GearboxDemo_" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            Shader shader = Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("Standard shader is unavailable.");
            Material material = new Material(shader) { name = "GearboxDemo_" + name, color = color };
            material.SetFloat("_Glossiness", 0.25f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Transform Group(string name, Transform parent, Vector3 position)
        {
            Transform group = new GameObject(name).transform;
            group.SetParent(parent, false);
            group.localPosition = position;
            return group;
        }

        private static void Box(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = name;
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position;
            obj.transform.localScale = scale;
            obj.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static void Table(string name, Transform parent, Vector3 position, float width, float depth, Material top, Material steel)
        {
            Transform table = Group(name, parent, position);
            Box("Tabletop", table, new Vector3(0, 0.95f, 0), new Vector3(width, 0.1f, depth), top);
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    Box("Leg " + (x < 0 ? "Left " : "Right ") + (z < 0 ? "Front" : "Back"), table,
                        new Vector3(x * (width / 2 - 0.12f), 0.45f, z * (depth / 2 - 0.12f)),
                        new Vector3(0.1f, 0.9f, 0.1f), steel);
        }

        private static Transform PositionMarker(string name, Transform parent, Vector3 position, Material material)
        {
            Transform marker = Group(name, parent, position);
            Box("Position Footprint", marker, new Vector3(0, 0.015f, 0), new Vector3(0.7f, 0.03f, 0.7f), material);
            Box("Facing Indicator", marker, new Vector3(0, 0.035f, 0.4f), new Vector3(0.12f, 0.02f, 0.2f), material);
            return marker;
        }
    }
}
