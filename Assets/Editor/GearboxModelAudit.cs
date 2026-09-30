using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GearboxDemo.Editor
{
    public static class GearboxModelAudit
    {
        public static void CapturePreview()
        {
            EditorSceneManager.OpenScene(GearboxDemoBuilder.ScenePath);
            Camera camera = Camera.main;
            if (camera == null) throw new InvalidOperationException("Scene has no Main Camera.");
            SavePreview(camera, "gearbox-workcell-preview.png");
            camera.transform.position = new Vector3(-1.8f, 3f, -1.6f);
            camera.transform.LookAt(new Vector3(-1.8f, 1.05f, 0.3f));
            camera.fieldOfView = 38;
            SavePreview(camera, "gearbox-tray-preview.png");
            // Do not save this temporary camera pose to the scene.
        }

        private static void SavePreview(Camera camera, string path)
        {
            var render = new RenderTexture(1280, 900, 24);
            var pixels = new Texture2D(1280, 900, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = render;
                camera.Render();
                RenderTexture.active = render;
                pixels.ReadPixels(new Rect(0, 0, 1280, 900), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previous;
                render.Release();
                UnityEngine.Object.DestroyImmediate(render);
                UnityEngine.Object.DestroyImmediate(pixels);
            }
        }

        public static void Run()
        {
            var report = new StringBuilder("Imported gearbox geometry (Unity metres)\n");
            foreach (string path in Directory.GetFiles("Assets/Models/Gearbox", "*.fbx"))
            {
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) throw new InvalidOperationException("Cannot import " + path);
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                try
                {
                    Bounds bounds = Measure(instance.transform);
                    var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                    report.AppendLine($"{Path.GetFileName(path)}: center={bounds.center.ToString("F6")} size={bounds.size.ToString("F6")} rootScale={instance.transform.localScale} rootRotation={instance.transform.localEulerAngles} importScale={importer.globalScale} fileScale={importer.fileScale}");
                }
                finally { UnityEngine.Object.DestroyImmediate(instance); }
            }
            File.WriteAllText("gearbox-model-audit.txt", report.ToString());
            Debug.Log(report.ToString());
        }

        public static Bounds Measure(Transform root)
        {
            bool found = false;
            Bounds result = default;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                Bounds mesh = filter.sharedMesh.bounds;
                Matrix4x4 matrix = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 corner = matrix.MultiplyPoint3x4(mesh.center + Vector3.Scale(mesh.extents, new Vector3(x, y, z)));
                            if (!found) { result = new Bounds(corner, Vector3.zero); found = true; }
                            else result.Encapsulate(corner);
                        }
            }
            if (!found || result.size.sqrMagnitude < 1e-12f)
                throw new InvalidOperationException("No usable mesh bounds found under " + root.name);
            return result;
        }
    }
}
