using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GearboxDemo.Editor
{
    public static class GearboxPhaseOneSetup
    {
        [MenuItem("Tools/Gearbox Demo/Setup Complete Phase One")]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before setting up Phase One.");

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != GearboxDemoBuilder.ScenePath)
                throw new InvalidOperationException("Open Assets/Scenes/GearboxAssembly.unity first.");

            Transform workcell = scene.GetRootGameObjects().Single(g => g.name == "Gearbox Assembly Workcell").transform;
            Transform assembly = workcell.Find("GearboxAssembly");
            Transform station = assembly != null ? assembly.Find("AssemblyStation") : null;
            Transform assembledRoot = station != null ? station.Find("AssembledGearbox") : null;
            GearboxPart[] parts = assembly != null ? assembly.GetComponentsInChildren<GearboxPart>(true) : Array.Empty<GearboxPart>();
            AssemblyTarget[] targets = station != null ? station.GetComponentsInChildren<AssemblyTarget>(true) : Array.Empty<AssemblyTarget>();

            GearboxPart pinCarrier = parts.SingleOrDefault(part => part.partType == GearboxPartType.PinCarrier);
            GearboxPart spurGear = parts.SingleOrDefault(part => part.partType == GearboxPartType.SpurGear);
            AssemblyTarget pinTarget = targets.SingleOrDefault(target => target.name == "PinCarrierTarget");
            AssemblyTarget spurTarget = targets.SingleOrDefault(target => target.name == "SpurGearTarget");
            OutputShaftAssemblyStep outputStep = assembly != null ? assembly.GetComponent<OutputShaftAssemblyStep>() : null;
            PlanetGearAssemblyStep planetStep = assembly != null ? assembly.GetComponent<PlanetGearAssemblyStep>() : null;

            if (assembly == null || assembledRoot == null || pinCarrier == null || spurGear == null ||
                pinTarget == null || spurTarget == null || outputStep == null || planetStep == null)
                throw new InvalidOperationException("Existing Phase One parts, targets, and assembly steps are required.");

            PhaseOneAssemblyController controller = assembly.GetComponent<PhaseOneAssemblyController>();
            if (controller == null) controller = Undo.AddComponent<PhaseOneAssemblyController>(assembly.gameObject);
            controller.pinCarrier = pinCarrier;
            controller.pinCarrierTarget = pinTarget;
            controller.outputShaftStep = outputStep;
            controller.planetGearStep = planetStep;
            controller.spurGear = spurGear;
            controller.spurGearTarget = spurTarget;
            controller.assembledGearbox = assembledRoot;
            EditorUtility.SetDirty(controller);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Could not save complete Phase One setup.");
            Debug.Log("Complete Phase One prepared. Press P in Play Mode to run it.");
        }

        public static void RunBatch()
        {
            EditorSceneManager.OpenScene(GearboxDemoBuilder.ScenePath);
            Setup();
        }
    }
}
