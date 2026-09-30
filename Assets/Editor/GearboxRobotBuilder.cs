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
    public static class GearboxRobotBuilder
    {
        [MenuItem("Tools/Gearbox Demo/Build Robot")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before building the robot.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != GearboxDemoBuilder.ScenePath) throw new InvalidOperationException("Open the GearboxAssembly scene first.");
            Transform root = scene.GetRootGameObjects().Single(g => g.name == "Gearbox Assembly Workcell").transform;
            Transform tray = root.Find("PartsTray");
            Transform station = root.Find("AssemblyStation") ?? root.Find("Assembly Area");
            if (tray == null || station == null) throw new InvalidOperationException("PartsTray and AssemblyStation (or existing Assembly Area) are required.");
            Bounds trayBounds = tray.Find("Tray Base").GetComponent<Collider>().bounds;
            Transform bench = root.Find("Workbench") ?? station;
            Bounds benchBounds = bench.GetComponentsInChildren<Collider>().First(c => c.name == "Tabletop").bounds;
            Bounds matBounds = station.GetComponentsInChildren<Collider>().First().bounds;
            var parts = root.GetComponentsInChildren<GearboxPart>();
            if (parts.Length != 7) throw new InvalidOperationException("Expected seven existing gearbox parts. Build Robot never recreates parts.");
            float partSize = parts.Max(p => p.GetComponent<Collider>().bounds.size.x);
            float highestPart = parts.Max(p => p.GetComponent<Collider>().bounds.max.y);
            float thickness = Mathf.Clamp(partSize * 0.50f, 0.10f, 0.16f);
            Vector3 basePosition = new Vector3((trayBounds.center.x + matBounds.center.x) / 2, 0,
                (trayBounds.center.z + matBounds.center.z) / 2);
            float shoulderHeight = Mathf.Max(trayBounds.max.y, benchBounds.max.y) + thickness * 1.5f;
            float farthest = 0;
            foreach (Bounds bounds in new[] { trayBounds, matBounds })
                foreach (float x in new[] { bounds.min.x, bounds.max.x })
                    foreach (float z in new[] { bounds.min.z, bounds.max.z })
                        farthest = Mathf.Max(farthest, Vector2.Distance(new Vector2(basePosition.x, basePosition.z), new Vector2(x, z)));
            // Size for the actual part grips and installation references, not unused tray corners.
            var reachPoints = parts.Select(p => p.gripPoint.position)
                .Concat(station.GetComponentsInChildren<AssemblyTarget>().Select(t => t.transform.position)).ToArray();
            float requiredReach = reachPoints.Max(p => Vector2.Distance(new Vector2(basePosition.x, basePosition.z), new Vector2(p.x, p.z)));
            float reach = requiredReach * 1.06f;
            float upper = reach * 0.52f, forearm = reach * 0.48f;
            float toolLength = thickness * 2.5f;
            float safeHeight = highestPart + thickness * 2.5f;
            var report = new StringBuilder();
            report.AppendLine($"Tray bounds: {trayBounds.size:F4}; workstation tabletop: {benchBounds.size:F4}; assembly mat: {matBounds.size:F4}");
            foreach (var p in parts) report.AppendLine($"{p.partType}: existing collider bounds {p.GetComponent<Collider>().bounds.size:F4}");
            report.AppendLine($"Base world: {basePosition:F4}; shoulder height {shoulderHeight:F3} m; farthest tray/mat corner radius {farthest:F3} m");
            report.AppendLine($"Actual grip/assembly-reference radius {requiredReach:F3} m; compact design uses 6% margin over these working positions.");
            report.AppendLine($"Upper link {upper:F3} m; forearm {forearm:F3} m; wrist/tool {toolLength:F3} m; planar reach {reach:F3} m; safe tool height {safeHeight:F3} m");

            Transform old = root.Find("RobotArm");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            var robot = Group("RobotArm", root, basePosition - root.position);
            Undo.RegisterCreatedObjectUndo(robot.gameObject, "Build Robot");
            Material shell = MaterialAsset("RobotPearl", new Color(0.82f, 0.87f, 0.89f));
            Material accent = MaterialAsset("RobotTeal", new Color(0.025f, 0.67f, 0.72f));
            Material dark = MaterialAsset("RobotDark", new Color(0.10f, 0.14f, 0.18f));
            Transform baseTransform = Group("Base", robot, Vector3.zero);
            Drum("MountingFoot", baseTransform, new Vector3(0, 0.04f, 0), thickness * 3, 0.08f, Vector3.up, dark);
            Rounded("Pedestal", baseTransform, new Vector3(0, shoulderHeight * 0.47f, 0), thickness * 1.5f, shoulderHeight * 0.94f, shell);
            Drum("BaseAccentCollar", baseTransform, Vector3.up * shoulderHeight * 0.86f, thickness * 1.56f, 0.035f, Vector3.up, accent);
            ArticulationBody baseBody = baseTransform.gameObject.AddComponent<ArticulationBody>();
            baseBody.immovable = true; baseBody.mass = 30;
            var arm = robot.gameObject.AddComponent<RobotArmController>();
            var poseController = robot.gameObject.AddComponent<RobotPoseController>();
            robot.gameObject.AddComponent<RobotDebugController>();
            Vector3[] axes = { Vector3.up, Vector3.right, Vector3.right, Vector3.up, Vector3.right, Vector3.up };
            Vector3[] offsets = { Vector3.up * shoulderHeight, Vector3.zero, Vector3.up * upper, Vector3.up * forearm, Vector3.up * thickness * 0.5f, Vector3.up * thickness * 0.5f };
            float[] masses = { 6, 4.5f, 3, 0.9f, 0.6f, 0.4f };
            float[] low = { -180, -80, -135, -180, -180, -180 };
            float[] high = { 180, 120, 135, 180, 180, 180 };
            Transform parent = baseTransform;
            for (int i = 0; i < 6; i++)
            {
                Transform joint = Group("Joint" + (i + 1), parent, offsets[i]);
                var body = joint.gameObject.AddComponent<ArticulationBody>();
                body.jointType = ArticulationJointType.RevoluteJoint;
                body.anchorPosition = Vector3.zero;
                body.anchorRotation = Quaternion.FromToRotation(Vector3.right, axes[i]);
                body.matchAnchors = false;
                body.parentAnchorPosition = offsets[i];
                body.parentAnchorRotation = body.anchorRotation;
                body.twistLock = ArticulationDofLock.LimitedMotion;
                body.mass = masses[i]; body.useGravity = true;
                body.linearDamping = 0.05f; body.angularDamping = 0.1f;
                body.jointFriction = 0.05f;
                body.maxJointVelocity = 1.5f;
                body.solverIterations = 24; body.solverVelocityIterations = 12;
                body.xDrive = new ArticulationDrive { lowerLimit = low[i], upperLimit = high[i], stiffness = 16000, damping = 1800, forceLimit = 3500 };
                var controller = joint.gameObject.AddComponent<RobotJointController>();
                controller.localAxis = axes[i]; arm.joints[i] = controller;
                float length = i == 1 ? upper : i == 2 ? forearm : thickness * 0.5f;
                var link = Group(i == 5 ? "Wrist" : "Link" + (i + 1), joint, Vector3.zero);
                Drum("JointHousing", link, Vector3.zero, thickness * 1.55f, thickness * 1.25f, axes[i], dark);
                Drum("TealJointSeal", link, axes[i] * thickness * 0.56f, thickness * 1.57f, thickness * 0.12f, axes[i], accent);
                Drum("JointEndCap", link, axes[i] * thickness * 0.67f, thickness * 1.27f, thickness * 0.12f, axes[i], shell);
                if (i == 1 || i == 2)
                {
                    Rounded("RoundedLinkShell", link, new Vector3(0, length / 2, 0), thickness, length, shell);
                    Rounded("GraphiteLinkInset", link, new Vector3(0, length / 2, -thickness * 0.42f), thickness * 0.36f, length * 0.62f, dark);
                    Rounded("TealLinkDetail", link, new Vector3(thickness * 0.3f, length * 0.7f, -thickness * 0.43f), thickness * 0.09f, length * 0.18f, accent);
                }
                parent = joint;
            }
            Transform wrist = parent.Find("Wrist");
            Transform end = Group("EndEffector", wrist, Vector3.up * thickness * 0.5f);
            Transform gripper = Group("Gripper", end, Vector3.zero);
            Box("GripperPalm", gripper, Vector3.zero, new Vector3(thickness * 1.3f, thickness * 0.45f, thickness * 0.7f), dark);
            arm.gripPoint = Group("GripPoint", gripper, Vector3.up * thickness);
            Box("LeftFinger", gripper, new Vector3(-thickness * 0.6f, thickness * 0.5f, 0), new Vector3(thickness * 0.18f, thickness, thickness * 0.35f), shell);
            Box("RightFinger", gripper, new Vector3(thickness * 0.6f, thickness * 0.5f, 0), new Vector3(thickness * 0.18f, thickness, thickness * 0.35f), shell);
            poseController.home = Pose("Home", new float[] { 0, -65, 130, 0, 115, 0 });
            poseController.traySafe = Pose("TraySafe", SafeAngles(trayBounds.center, basePosition, shoulderHeight, safeHeight, upper, forearm, toolLength, -15, 15));
            poseController.assemblySafe = Pose("AssemblySafe", SafeAngles(matBounds.center, basePosition, shoulderHeight, safeHeight, upper, forearm, toolLength, 15, -15));
            poseController.humanSafe = Pose("HumanSafe", new float[] { 180, -65, 130, -20, 115, 20 });
            foreach (RobotPose pose in new[] { poseController.home, poseController.traySafe, poseController.assemblySafe, poseController.humanSafe })
                report.AppendLine(pose.poseName + ": " + string.Join(", ", pose.jointDegrees.Select(v => v.ToString("F2"))));
            for (int i = 0; i < 6; i++)
            {
                float angle = poseController.home.jointDegrees[i];
                arm.joints[i].transform.localRotation = Quaternion.AngleAxis(angle, axes[i]); // Editor construction only.
                arm.joints[i].SetTarget(angle);
            }
            Transform marker = root.Find("Robot Placeholder Position");
            GearboxGripperSetup.Configure(arm);
            if (marker != null) { Undo.RecordObject(marker.gameObject, "Hide robot position marker"); marker.gameObject.SetActive(false); }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Robot scene could not be saved.");
            File.WriteAllText("robot-build-report.txt", report.ToString()); Debug.Log(report.ToString());
        }

        private static float[] SafeAngles(Vector3 destination, Vector3 origin, float shoulderHeight, float safeHeight, float upper, float lower, float tool, float roll, float tip)
        {
            Vector3 delta = destination - origin;
            float radius = new Vector2(delta.x, delta.z).magnitude;
            float height = safeHeight + tool - shoulderHeight;
            float cosElbow = (radius * radius + height * height - upper * upper - lower * lower) / (2 * upper * lower);
            if (Mathf.Abs(cosElbow) > 1) throw new InvalidOperationException("Measured safe station pose is outside robot reach.");
            float elbow = Mathf.Acos(cosElbow);
            float shoulder = Mathf.Atan2(radius, height) - Mathf.Atan2(lower * Mathf.Sin(elbow), upper + lower * Mathf.Cos(elbow));
            // Wrist roll is modest; this is a safe hover configuration, not precise Cartesian IK.
            return new[] { Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg, shoulder * Mathf.Rad2Deg, elbow * Mathf.Rad2Deg, roll, 180 - (shoulder + elbow) * Mathf.Rad2Deg, tip };
        }
        private static RobotPose Pose(string name, float[] angles)
        {
            string path = "Assets/ScriptableObjects/Robot" + name + ".asset";
            var pose = AssetDatabase.LoadAssetAtPath<RobotPose>(path);
            if (pose == null) { pose = ScriptableObject.CreateInstance<RobotPose>(); AssetDatabase.CreateAsset(pose, path); }
            pose.poseName = name; pose.jointDegrees = angles; EditorUtility.SetDirty(pose); return pose;
        }
        private static Material MaterialAsset(string name, Color color)
        {
            string path = "Assets/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Standard")) { color = color };
            AssetDatabase.CreateAsset(material, path); return material;
        }
        private static Transform Group(string name, Transform parent, Vector3 position)
        {
            var obj = new GameObject(name); obj.transform.SetParent(parent, false); obj.transform.localPosition = position; return obj.transform;
        }
        private static void Box(string name, Transform parent, Vector3 position, Vector3 size, Material material)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube); obj.name = name; obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position; obj.transform.localScale = size;
            obj.GetComponent<Renderer>().sharedMaterial = material;
        }
        private static void Rounded(string name, Transform parent, Vector3 position, float diameter, float length, Material material)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Capsule); obj.name = name;
            obj.transform.SetParent(parent, false); obj.transform.localPosition = position;
            obj.transform.localScale = new Vector3(diameter, length / 2, diameter);
            obj.GetComponent<Renderer>().sharedMaterial = material;
        }
        private static void Drum(string name, Transform parent, Vector3 position, float diameter, float length, Vector3 axis, Material material)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cylinder); obj.name = name;
            obj.transform.SetParent(parent, false); obj.transform.localPosition = position;
            obj.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis);
            obj.transform.localScale = new Vector3(diameter, length / 2, diameter);
            obj.GetComponent<Renderer>().sharedMaterial = material;
            // Convex cylinders are valid on moving articulation links.
            var meshCollider = obj.GetComponent<MeshCollider>();
            if (meshCollider != null) meshCollider.convex = true;
        }
        public static void RunBatch() { EditorSceneManager.OpenScene(GearboxDemoBuilder.ScenePath); Build(); }

        public static void CapturePreview()
        {
            EditorSceneManager.OpenScene(GearboxDemoBuilder.ScenePath);
            Camera camera = Camera.main;
            var target = new RenderTexture(1280, 900, 24);
            var pixels = new Texture2D(1280, 900, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1280, 900), 0, 0); pixels.Apply();
                File.WriteAllBytes("robot-workcell-preview.png", pixels.EncodeToPNG());
                camera.transform.position = new Vector3(3.4f, 2.8f, -4.3f);
                camera.transform.LookAt(new Vector3(-0.2f, 1.15f, 0.3f));
                camera.fieldOfView = 38;
                camera.Render();
                pixels.ReadPixels(new Rect(0, 0, 1280, 900), 0, 0); pixels.Apply();
                File.WriteAllBytes("robot-detail-preview.png", pixels.EncodeToPNG());
                var robot = UnityEngine.Object.FindAnyObjectByType<RobotArmController>();
                var renderers = robot.GetComponentsInChildren<Renderer>();
                Bounds bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                File.WriteAllText("robot-visual-report.txt", $"Authored Home bounds: {bounds.size:F3}; maximum height: {bounds.max.y:F3} m\n");
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous; target.Release();
                UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
            }
        }
    }
}
