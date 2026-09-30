using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GearboxDemo.Editor
{
    public static class GearboxOutputShaftStepSetup
    {
        [MenuItem("Tools/Gearbox Demo/Setup Output Shaft Assembly")]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before setting up Output Shaft assembly.");

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != GearboxDemoBuilder.ScenePath)
                throw new InvalidOperationException("Open Assets/Scenes/GearboxAssembly.unity first.");

            Transform workcell = scene.GetRootGameObjects().Single(g => g.name == "Gearbox Assembly Workcell").transform;
            Transform assembly = workcell.Find("GearboxAssembly");
            Transform station = assembly != null ? assembly.Find("AssemblyStation") : null;
            Transform assembledRoot = station != null ? station.Find("AssembledGearbox") : null;
            AssemblyTarget pinTarget = FindTarget(station, "PinCarrierTarget", GearboxPartType.PinCarrier);
            AssemblyTarget shaftTarget = FindTarget(station, "OutputShaftTarget", GearboxPartType.OutputShaft);
            GearboxPart[] parts = assembly != null ? assembly.GetComponentsInChildren<GearboxPart>(true) : Array.Empty<GearboxPart>();
            GearboxPart pinCarrier = parts.SingleOrDefault(p => p.partType == GearboxPartType.PinCarrier);
            GearboxPart outputShaft = parts.SingleOrDefault(p => p.partType == GearboxPartType.OutputShaft);
            HumanRobotHandover handover = workcell.GetComponentInChildren<HumanRobotHandover>(true);
            RobotPoseController poses = workcell.GetComponentInChildren<RobotPoseController>(true);

            if (assembly == null || station == null || assembledRoot == null || pinTarget == null || shaftTarget == null ||
                pinCarrier == null || outputShaft == null || handover == null || poses == null)
                throw new InvalidOperationException("The existing gearbox, targets, robot, and handover system are required.");

            // The Pin Carrier is the fixed starting condition for this one assembly operation.
            Undo.SetTransformParent(pinCarrier.transform, assembledRoot, "Place Pin Carrier in Phase One fixture");
            pinCarrier.transform.SetPositionAndRotation(pinTarget.transform.position, pinTarget.transform.rotation);
            pinCarrier.assemblyTarget = pinTarget;
            pinCarrier.assembled = true;
            pinCarrier.canBePicked = false;
            Rigidbody pinBody = pinCarrier.GetComponent<Rigidbody>();
            pinBody.isKinematic = true;
            pinBody.useGravity = false;
            pinBody.detectCollisions = true;
            EditorUtility.SetDirty(pinCarrier);
            EditorUtility.SetDirty(pinBody);
            var serializedPinTarget = new SerializedObject(pinTarget);
            serializedPinTarget.FindProperty("occupant").objectReferenceValue = pinCarrier;
            serializedPinTarget.ApplyModifiedPropertiesWithoutUndo();

            OutputShaftAssemblyStep step = assembly.GetComponent<OutputShaftAssemblyStep>();
            if (step == null) step = Undo.AddComponent<OutputShaftAssemblyStep>(assembly.gameObject);
            step.pinCarrier = pinCarrier;
            step.outputShaft = outputShaft;
            step.pinCarrierTarget = pinTarget;
            step.outputShaftTarget = shaftTarget;
            step.assembledGearbox = assembledRoot;
            step.handover = handover;
            step.robotPoses = poses;
            EditorUtility.SetDirty(step);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Could not save Output Shaft assembly setup.");
            Debug.Log("Output Shaft assembly step prepared. Press O in Play Mode to run it.");
        }

        private static AssemblyTarget FindTarget(Transform station, string name, GearboxPartType type)
        {
            if (station == null) return null;
            return station.GetComponentsInChildren<AssemblyTarget>(true)
                .SingleOrDefault(target => target.name == name && target.acceptedPart == type);
        }

        public static void RunBatch()
        {
            EditorSceneManager.OpenScene(GearboxDemoBuilder.ScenePath);
            Setup();
        }
    }
}
