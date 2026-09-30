using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GearboxDemo.Editor
{
    public static class GearboxAssemblyManagerSetup
    {
        [MenuItem("Tools/Gearbox Demo/Setup Complete Simulation Manager")]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before setting up the simulation manager.");

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != GearboxDemoBuilder.ScenePath)
                throw new InvalidOperationException("Open Assets/Scenes/GearboxAssembly.unity first.");

            Transform workcell = scene.GetRootGameObjects().Single(g => g.name == "Gearbox Assembly Workcell").transform;
            Transform assembly = workcell.Find("GearboxAssembly");
            if (assembly == null) throw new InvalidOperationException("Existing GearboxAssembly is required.");

            GearboxAssemblyManager manager = assembly.GetComponent<GearboxAssemblyManager>();
            if (manager == null) manager = Undo.AddComponent<GearboxAssemblyManager>(assembly.gameObject);
            manager.phaseOne = Require<PhaseOneAssemblyController>(assembly);
            manager.outputShaftStep = Require<OutputShaftAssemblyStep>(assembly);
            manager.planetGearStep = Require<PlanetGearAssemblyStep>(assembly);
            manager.phaseTwoInsertion = Require<PhaseTwoInsertionController>(assembly);
            manager.lidInstallation = Require<LidInstallationController>(assembly);
            manager.screwInstallation = Require<ScrewInstallationController>(assembly);
            manager.handover = workcell.GetComponentInChildren<HumanRobotHandover>(true);
            manager.robotPoses = workcell.GetComponentInChildren<RobotPoseController>(true);
            manager.allParts = assembly.GetComponentsInChildren<GearboxPart>(true)
                .OrderBy(part => part.partId, StringComparer.Ordinal).ToArray();
            manager.allTargets = assembly.GetComponentsInChildren<AssemblyTarget>(true)
                .OrderBy(target => target.name, StringComparer.Ordinal).ToArray();
            if (manager.handover == null || manager.robotPoses == null || manager.allParts.Length != 12 || manager.allTargets.Length != 12)
                throw new InvalidOperationException("Manager requires the existing handover, robot, 12 parts, and 12 typed targets.");
            EditorUtility.SetDirty(manager);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Could not save the complete simulation manager.");
            Debug.Log("Complete gearbox simulation manager prepared.");
        }

        private static T Require<T>(Transform assembly) where T : Component
        {
            T component = assembly.GetComponent<T>();
            if (component == null) throw new InvalidOperationException("Missing existing " + typeof(T).Name + ".");
            return component;
        }

        public static void RunBatch()
        {
            EditorSceneManager.OpenScene(GearboxDemoBuilder.ScenePath);
            Setup();
        }
    }
}
