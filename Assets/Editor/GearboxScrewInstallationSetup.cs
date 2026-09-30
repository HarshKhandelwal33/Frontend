using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GearboxDemo.Editor
{
    public static class GearboxScrewInstallationSetup
    {
        [MenuItem("Tools/Gearbox Demo/Setup Screw Installation")]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before setting up Screw installation.");

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != GearboxDemoBuilder.ScenePath)
                throw new InvalidOperationException("Open Assets/Scenes/GearboxAssembly.unity first.");

            Transform workcell = scene.GetRootGameObjects().Single(g => g.name == "Gearbox Assembly Workcell").transform;
            Transform assembly = workcell.Find("GearboxAssembly");
            Transform station = assembly != null ? assembly.Find("AssemblyStation") : null;
            Transform finalRoot = station != null ? station.Find("Phase2Fixture/FinalGearbox") : null;
            GearboxPart[] parts = assembly != null ? assembly.GetComponentsInChildren<GearboxPart>(true) : Array.Empty<GearboxPart>();
            AssemblyTarget[] targets = station != null ? station.GetComponentsInChildren<AssemblyTarget>(true) : Array.Empty<AssemblyTarget>();
            var screws = new GearboxPart[4];
            var screwTargets = new AssemblyTarget[4];
            for (int i = 0; i < 4; i++)
            {
                string suffix = "0" + (i + 1);
                screws[i] = parts.SingleOrDefault(part => part.partId == "Screw_" + suffix);
                screwTargets[i] = targets.SingleOrDefault(target => target.name == "ScrewTarget_" + suffix);
            }

            LidInstallationController lidInstallation = assembly != null ? assembly.GetComponent<LidInstallationController>() : null;
            HumanRobotHandover handover = workcell.GetComponentInChildren<HumanRobotHandover>(true);
            RobotArmController robotArm = workcell.GetComponentInChildren<RobotArmController>(true);
            RobotPoseController robotPoses = workcell.GetComponentInChildren<RobotPoseController>(true);
            if (assembly == null || finalRoot == null || lidInstallation == null || handover == null ||
                robotArm == null || robotPoses == null || screws.Any(screw => screw == null) ||
                screwTargets.Any(target => target == null))
                throw new InvalidOperationException("Existing Screws, targets, Lid installation, final gearbox, and robot are required.");

            ScrewInstallationController controller = assembly.GetComponent<ScrewInstallationController>();
            if (controller == null) controller = Undo.AddComponent<ScrewInstallationController>(assembly.gameObject);
            controller.screws = screws;
            controller.screwTargets = screwTargets;
            controller.lidInstallation = lidInstallation;
            controller.finalGearboxRoot = finalRoot;
            controller.handover = handover;
            controller.robotArm = robotArm;
            controller.robotPoses = robotPoses;
            controller.aboveGearboxJointDegrees = (float[])robotPoses.assemblySafe.jointDegrees.Clone();
            controller.liftJointDegrees = (float[])robotPoses.traySafe.jointDegrees.Clone();
            controller.liftJointDegrees[1] = ClampToJoint(robotArm.joints[1], controller.liftJointDegrees[1] - 8f);
            controller.liftJointDegrees[2] = ClampToJoint(robotArm.joints[2], controller.liftJointDegrees[2] - 6f);
            EditorUtility.SetDirty(controller);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Could not save Screw installation setup.");
            Debug.Log("Sequential Screw installation prepared. Press S after installing the Lid.");
        }

        private static float ClampToJoint(RobotJointController joint, float value)
        {
            ArticulationDrive drive = joint.Body.xDrive;
            return Mathf.Clamp(value, drive.lowerLimit, drive.upperLimit);
        }

        public static void RunBatch()
        {
            EditorSceneManager.OpenScene(GearboxDemoBuilder.ScenePath);
            Setup();
        }
    }
}
