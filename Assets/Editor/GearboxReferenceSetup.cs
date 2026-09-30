using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GearboxDemo.Editor
{
    public static class GearboxReferenceSetup
    {
        [MenuItem("Tools/Gearbox Demo/Apply Reference Assembly and Presentation")]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            var m = UnityEngine.Object.FindFirstObjectByType<GearboxAssemblyManager>();
            if (m == null) throw new InvalidOperationException("Open the prepared gearbox scene.");
            Transform station = m.phaseOne.InternalAssemblyRoot.parent;
            Vector3 originOne = station.position + new Vector3(-0.18f, 0.075f, 0);
            Vector3 originTwo = station.position + new Vector3(0.18f, 0.075f, 0);
            m.phaseOne.InternalAssemblyRoot.SetPositionAndRotation(originOne, Quaternion.identity);
            m.phaseTwoInsertion.internalAssemblyTarget.SetPositionAndRotation(originTwo, Quaternion.identity);
            foreach (var p in m.allParts)
            {
                Transform visual = p.transform.Find("Visual");
                if (visual == null) throw new InvalidOperationException("Missing compensated CAD Visual: " + p.name);
                bool internalPart = p.partType != GearboxPartType.Housing && p.partType != GearboxPartType.Lid && p.partType != GearboxPartType.Screw;
                int index = Array.IndexOf(m.planetGearStep.planetGears, p);
                int screw = Array.IndexOf(m.screwInstallation.screws, p);
                Quaternion turn = Quaternion.Euler(0, index >= 0 ? index * 120f : screw >= 0 ? screw * 90f : 0, 0);
                // Undo the tray recentering: each CAD file retains its original assembly-space origin.
                p.assemblyTarget.transform.SetPositionAndRotation((internalPart ? originOne : originTwo) - turn * visual.localPosition, turn);
                p.assemblyTarget.ClearOccupancy();
                if (p.partType == GearboxPartType.PinCarrier || p.partType == GearboxPartType.Housing)
                {
                    p.transform.SetPositionAndRotation(p.assemblyTarget.transform.position, p.assemblyTarget.transform.rotation);
                    p.assembled = false;
                    p.canBePicked = true;
                    var body = p.GetComponent<Rigidbody>();
                    body.interpolation = RigidbodyInterpolation.None;
                    body.isKinematic = true;
                    body.useGravity = false;
                }
                foreach (var r in p.GetComponentsInChildren<Renderer>())
                {
                    r.sharedMaterial = Material(p.partType == GearboxPartType.Housing || p.partType == GearboxPartType.Lid ? "ReferenceAluminium" : "ReferenceSteel",
                        p.partType == GearboxPartType.Housing || p.partType == GearboxPartType.Lid ? new Color(0.56f, 0.61f, 0.67f) : new Color(0.3f, 0.34f, 0.39f), 0.78f, 0.4f);
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
            }
            m.phaseTwoInsertion.internalAssemblyGripPoint.SetPositionAndRotation(originOne + Vector3.up * 0.18f, Quaternion.Euler(180, 0, 0));
            Fixture(station, "Reference Fixture One", originOne);
            Fixture(station, "Reference Fixture Two", originTwo);
            m.planetGearStep.workerInstallDuration = 1.6f;
            m.outputShaftStep.workerInstallDuration = 1.8f;
            m.screwInstallation.insertionDuration = 2.8f;
            m.screwInstallation.alignmentDuration = 1.2f;
            m.phaseTwoInsertion.insertionDuration = 2.1f;
            m.handover.transferDuration = 1.2f;
            m.handover.reachDuration = 1.1f;
            m.autoPlayStepDelay = 0.6f;
            if (m.GetComponent<AssemblyPresentation>() == null) m.gameObject.AddComponent<AssemblyPresentation>();
            QualitySettings.antiAliasing = 4;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.High;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.69f, 0.8f);
            RenderSettings.ambientEquatorColor = new Color(0.34f, 0.38f, 0.43f);
            RenderSettings.ambientGroundColor = new Color(0.18f, 0.19f, 0.22f);
            foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            { light.shadows = LightShadows.Soft; light.shadowBias = 0.02f; }
            EditorSceneManager.MarkSceneDirty(m.gameObject.scene);
            EditorSceneManager.SaveScene(m.gameObject.scene);
            AssetDatabase.SaveAssets();
        }

        private static Material Material(string name, Color color, float metal, float smooth)
        {
            string path = "Assets/Materials/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat, path); }
            mat.color = color; mat.SetFloat("_Metallic", metal); mat.SetFloat("_Glossiness", smooth);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void Fixture(Transform station, string name, Vector3 origin)
        {
            var old = station.Find(name);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var root = new GameObject(name).transform;
            root.SetParent(station, true); root.position = origin;
            // Open central clearance for the projecting input shaft.
            for (int i = 0; i < 4; i++)
            {
                var jaw = GameObject.CreatePrimitive(PrimitiveType.Cube);
                jaw.name = "Fixture support " + (i + 1); jaw.transform.SetParent(root, false);
                jaw.transform.localPosition = new Vector3(i % 2 == 0 ? -0.115f : 0.115f, -0.036f, i < 2 ? -0.1f : 0.1f);
                jaw.transform.localScale = new Vector3(0.045f, 0.072f, 0.055f);
                jaw.GetComponent<Renderer>().sharedMaterial = Material("ReferenceFixture", new Color(0.12f, 0.22f, 0.27f), 0.45f, 0.3f);
            }
        }

        public static void RunBatch()
        {
            EditorSceneManager.OpenScene(GearboxDemoBuilder.ScenePath);
            Setup();
        }
    }
}
