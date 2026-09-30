using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GearboxDemo.Editor
{
    public static class GearboxPhaseTwoInsertionSetup
    {
        [MenuItem("Tools/Gearbox Demo/Setup Phase Two Insertion")]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before setting up Phase Two insertion.");

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != GearboxDemoBuilder.ScenePath)
                throw new InvalidOperationException("Open Assets/Scenes/GearboxAssembly.unity first.");

            Transform workcell = scene.GetRootGameObjects().Single(g => g.name == "Gearbox Assembly Workcell").transform;
            Transform assembly = workcell.Find("GearboxAssembly");
            Transform station = assembly != null ? assembly.Find("AssemblyStation") : null;
            Transform internalRoot = station != null ? station.Find("AssembledGearbox") : null;
            Transform phase2Fixture = station != null ? station.Find("Phase2Fixture") : null;
            PhaseOneAssemblyController phaseOne = assembly != null ? assembly.GetComponent<PhaseOneAssemblyController>() : null;
            GearboxPart[] parts = assembly != null ? assembly.GetComponentsInChildren<GearboxPart>(true) : Array.Empty<GearboxPart>();
            GearboxPart housing = parts.SingleOrDefault(part => part.partType == GearboxPartType.Housing);
            AssemblyTarget housingTarget = station != null ? station.GetComponentsInChildren<AssemblyTarget>(true)
                .SingleOrDefault(target => target.name == "HousingTarget") : null;
            Transform internalTarget = FindDescendant(station, "InternalAssemblyTarget");
            RobotArmController robotArm = workcell.GetComponentInChildren<RobotArmController>(true);
            RobotPoseController robotPoses = workcell.GetComponentInChildren<RobotPoseController>(true);
            HumanRobotHandover handover = workcell.GetComponentInChildren<HumanRobotHandover>(true);
            Transform worker = workcell.Find("Worker");

            if (assembly == null || station == null || internalRoot == null || phase2Fixture == null || phaseOne == null ||
                housing == null || housingTarget == null || internalTarget == null || robotArm == null ||
                robotPoses == null || handover == null || worker == null)
                throw new InvalidOperationException("Existing Phase One, Housing, Phase Two targets, robot, and Worker are required.");

            Transform finalRoot = EnsureChild(phase2Fixture, "FinalGearbox", out _);
            finalRoot.localPosition = Vector3.zero;
            finalRoot.localRotation = Quaternion.identity;

            Undo.SetTransformParent(housing.transform, finalRoot, "Position Housing for Phase Two");
            housing.transform.SetPositionAndRotation(housingTarget.transform.position, housingTarget.transform.rotation);
            housing.assemblyTarget = housingTarget;
            housing.assembled = true;
            housing.canBePicked = false;
            Rigidbody housingBody = housing.GetComponent<Rigidbody>();
            housingBody.isKinematic = true;
            housingBody.useGravity = false;
            housingBody.detectCollisions = true;
            EditorUtility.SetDirty(housing);
            EditorUtility.SetDirty(housingBody);
            var serializedHousingTarget = new SerializedObject(housingTarget);
            serializedHousingTarget.FindProperty("occupant").objectReferenceValue = housing;
            serializedHousingTarget.ApplyModifiedPropertiesWithoutUndo();

            Transform observation = EnsureChild(workcell, "WorkerObservationPosition", out bool observationCreated);
            if (observationCreated) observation.SetPositionAndRotation(worker.position, worker.rotation);

            Transform gripPoint = EnsureChild(internalRoot, "InternalAssemblyGripPoint", out bool gripCreated);
            if (gripCreated)
            {
                GearboxPart pinCarrier = parts.Single(part => part.partType == GearboxPartType.PinCarrier);
                gripPoint.SetPositionAndRotation(pinCarrier.gripPoint.position, pinCarrier.gripPoint.rotation);
            }

            PhaseTwoInsertionController controller = assembly.GetComponent<PhaseTwoInsertionController>();
            if (controller == null) controller = Undo.AddComponent<PhaseTwoInsertionController>(assembly.gameObject);
            controller.phaseOne = phaseOne;
            controller.housing = housing;
            controller.housingTarget = housingTarget;
            controller.internalAssemblyRoot = internalRoot;
            controller.internalAssemblyTarget = internalTarget;
            controller.finalGearboxRoot = finalRoot;
            controller.internalAssemblyGripPoint = gripPoint;
            controller.robotHoldPoint = handover.robotHoldPoint;
            controller.robotArm = robotArm;
            controller.robotPoses = robotPoses;
            controller.worker = worker;
            controller.workerObservationPosition = observation;
            controller.insertionApproachJointDegrees = (float[])robotPoses.assemblySafe.jointDegrees.Clone();
            controller.liftJointDegrees = (float[])robotPoses.assemblySafe.jointDegrees.Clone();
            controller.liftJointDegrees[1] = ClampToJoint(robotArm.joints[1], controller.liftJointDegrees[1] - 12f);
            controller.liftJointDegrees[2] = ClampToJoint(robotArm.joints[2], controller.liftJointDegrees[2] - 10f);
            EditorUtility.SetDirty(controller);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Could not save Phase Two insertion setup.");
            Debug.Log("Phase Two robot insertion prepared. Press I after completing Phase One.");
        }

        private static float ClampToJoint(RobotJointController joint, float value)
        {
            ArticulationDrive drive = joint.Body.xDrive;
            return Mathf.Clamp(value, drive.lowerLimit, drive.upperLimit);
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            return root == null ? null : root.GetComponentsInChildren<Transform>(true).SingleOrDefault(t => t.name == name);
        }

        private static Transform EnsureChild(Transform parent, string name, out bool created)
        {
            Transform child = parent.Find(name);
            created = child == null;
            if (!created) return child;
            var obj = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(obj, "Create " + name);
            obj.transform.SetParent(parent, false);
            return obj.transform;
        }

        public static void RunBatch()
        {
            EditorSceneManager.OpenScene(GearboxDemoBuilder.ScenePath);
            Setup();
        }
    }
}
