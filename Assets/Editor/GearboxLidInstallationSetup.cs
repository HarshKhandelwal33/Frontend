using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GearboxDemo.Editor
{
    public static class GearboxLidInstallationSetup
    {
        [MenuItem("Tools/Gearbox Demo/Setup Lid Installation")]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before setting up Lid installation.");

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != GearboxDemoBuilder.ScenePath)
                throw new InvalidOperationException("Open Assets/Scenes/GearboxAssembly.unity first.");

            Transform workcell = scene.GetRootGameObjects().Single(g => g.name == "Gearbox Assembly Workcell").transform;
            Transform assembly = workcell.Find("GearboxAssembly");
            Transform station = assembly != null ? assembly.Find("AssemblyStation") : null;
            Transform finalRoot = station != null ? station.Find("Phase2Fixture/FinalGearbox") : null;
            GearboxPart lid = assembly != null ? assembly.GetComponentsInChildren<GearboxPart>(true)
                .SingleOrDefault(part => part.partType == GearboxPartType.Lid) : null;
            AssemblyTarget lidTarget = station != null ? station.GetComponentsInChildren<AssemblyTarget>(true)
                .SingleOrDefault(target => target.name == "LidTarget") : null;
            PhaseTwoInsertionController phaseTwo = assembly != null ? assembly.GetComponent<PhaseTwoInsertionController>() : null;
            HumanRobotHandover handover = workcell.GetComponentInChildren<HumanRobotHandover>(true);
            RobotArmController robotArm = workcell.GetComponentInChildren<RobotArmController>(true);
            RobotPoseController robotPoses = workcell.GetComponentInChildren<RobotPoseController>(true);

            if (assembly == null || finalRoot == null || lid == null || lidTarget == null ||
                phaseTwo == null || handover == null || robotArm == null || robotPoses == null)
                throw new InvalidOperationException("Existing Lid, LidTarget, Phase Two insertion, and robot are required.");

            LidInstallationController controller = assembly.GetComponent<LidInstallationController>();
            if (controller == null) controller = Undo.AddComponent<LidInstallationController>(assembly.gameObject);
            controller.phaseTwoInsertion = phaseTwo;
            controller.lid = lid;
            controller.lidTarget = lidTarget;
            controller.finalGearboxRoot = finalRoot;
            controller.handover = handover;
            controller.robotArm = robotArm;
            controller.robotPoses = robotPoses;
            controller.aboveHousingJointDegrees = (float[])robotPoses.assemblySafe.jointDegrees.Clone();
            controller.liftJointDegrees = (float[])robotPoses.traySafe.jointDegrees.Clone();
            controller.liftJointDegrees[1] = ClampToJoint(robotArm.joints[1], controller.liftJointDegrees[1] - 10f);
            controller.liftJointDegrees[2] = ClampToJoint(robotArm.joints[2], controller.liftJointDegrees[2] - 8f);
            EditorUtility.SetDirty(controller);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Could not save Lid installation setup.");
            Debug.Log("Robot Lid installation prepared. Press L after Phase Two insertion.");
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
