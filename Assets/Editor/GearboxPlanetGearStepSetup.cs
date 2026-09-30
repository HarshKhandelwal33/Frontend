using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GearboxDemo.Editor
{
    public static class GearboxPlanetGearStepSetup
    {
        [MenuItem("Tools/Gearbox Demo/Setup Planetary Gear Assembly")]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before setting up planetary gear assembly.");

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != GearboxDemoBuilder.ScenePath)
                throw new InvalidOperationException("Open Assets/Scenes/GearboxAssembly.unity first.");

            Transform workcell = scene.GetRootGameObjects().Single(g => g.name == "Gearbox Assembly Workcell").transform;
            Transform assembly = workcell.Find("GearboxAssembly");
            Transform station = assembly != null ? assembly.Find("AssemblyStation") : null;
            Transform assembledRoot = station != null ? station.Find("AssembledGearbox") : null;
            GearboxPart[] parts = assembly != null ? assembly.GetComponentsInChildren<GearboxPart>(true) : Array.Empty<GearboxPart>();
            AssemblyTarget[] targets = station != null ? station.GetComponentsInChildren<AssemblyTarget>(true) : Array.Empty<AssemblyTarget>();
            HumanRobotHandover handover = workcell.GetComponentInChildren<HumanRobotHandover>(true);
            RobotPoseController poses = workcell.GetComponentInChildren<RobotPoseController>(true);
            GearboxPart outputShaft = parts.SingleOrDefault(part => part.partType == GearboxPartType.OutputShaft);

            var gears = new GearboxPart[3];
            var gearTargets = new AssemblyTarget[3];
            for (int i = 0; i < 3; i++)
            {
                string suffix = "0" + (i + 1);
                gears[i] = parts.SingleOrDefault(part => part.partId == "PlanetGear_" + suffix);
                gearTargets[i] = targets.SingleOrDefault(target => target.name == "PlanetGearTarget_" + suffix);
            }

            if (assembly == null || assembledRoot == null || outputShaft == null || handover == null || poses == null ||
                gears.Any(gear => gear == null) || gearTargets.Any(target => target == null))
                throw new InvalidOperationException("The existing three planetary gears, targets, handover, and robot are required.");

            PlanetGearAssemblyStep step = assembly.GetComponent<PlanetGearAssemblyStep>();
            if (step == null) step = Undo.AddComponent<PlanetGearAssemblyStep>(assembly.gameObject);
            step.planetGears = gears;
            step.planetGearTargets = gearTargets;
            step.outputShaft = outputShaft;
            step.assembledGearbox = assembledRoot;
            step.handover = handover;
            step.robotPoses = poses;
            EditorUtility.SetDirty(step);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Could not save planetary gear assembly setup.");
            Debug.Log("Planetary gear assembly prepared. Press G in Play Mode after installing the Output Shaft.");
        }

        public static void RunBatch()
        {
            EditorSceneManager.OpenScene(GearboxDemoBuilder.ScenePath);
            Setup();
        }
    }
}
