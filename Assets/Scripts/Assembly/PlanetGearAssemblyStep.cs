using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GearboxDemo
{
    [DisallowMultipleComponent]
    public sealed class PlanetGearAssemblyStep : MonoBehaviour
    {
        [Tooltip("Ordered PlanetGear_01, PlanetGear_02, PlanetGear_03.")]
        public GearboxPart[] planetGears = new GearboxPart[3];
        [Tooltip("Ordered PlanetGearTarget_01, PlanetGearTarget_02, PlanetGearTarget_03.")]
        public AssemblyTarget[] planetGearTargets = new AssemblyTarget[3];
        public GearboxPart outputShaft;
        public Transform assembledGearbox;
        public HumanRobotHandover handover;
        public RobotPoseController robotPoses;

        [Min(0.1f)] public float workerInstallDuration = 1.1f;
        [Min(0f)] public float delayBetweenGears = 0.65f;
        [Min(1f)] public float operationTimeout = 25f;

        public bool IsRunning { get; private set; }
        public int CompletedGearCount { get; private set; }
        public bool LastPartSucceeded => currentPartSucceeded;

        private bool currentPartSucceeded;

        private void Update()
        {
            if (GetComponent<GearboxAssemblyManager>() != null) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.gKey.wasPressedThisFrame)
                RunPlanetGearAssembly();
        }

        public void RunPlanetGearAssembly()
        {
            if (!IsRunning && isActiveAndEnabled)
                StartCoroutine(AssemblySequence());
        }

        public void ResetOperationState()
        {
            StopAllCoroutines();
            IsRunning = false;
            CompletedGearCount = 0;
            currentPartSucceeded = false;
        }

        private IEnumerator AssemblySequence()
        {
            if (!ReferencesAreValid()) yield break;
            if (!outputShaft.assembled)
            {
                Debug.LogError("Planetary gear assembly requires the Output Shaft operation to be completed first.", this);
                yield break;
            }

            IsRunning = true;
            CompletedGearCount = 0;
            for (int i = 0; i < planetGears.Length; i++)
            {
                GearboxPart gear = planetGears[i];
                AssemblyTarget target = planetGearTargets[i];
                if (gear.assembled)
                {
                    CompletedGearCount++;
                    continue;
                }

                yield return AssemblePart(gear, target);
                if (!currentPartSucceeded)
                {
                    IsRunning = false;
                    yield break;
                }

                CompletedGearCount++;
                if (i < planetGears.Length - 1 && delayBetweenGears > 0f)
                    yield return new WaitForSeconds(delayBetweenGears);
            }
            IsRunning = false;
        }

        public IEnumerator AssemblePart(GearboxPart part, AssemblyTarget target)
        {
            currentPartSucceeded = false;
            if (part == null || target == null || part.partType != target.acceptedPart || part.assemblyTarget != target)
            {
                Debug.LogError("Gearbox part and assembly target pairing is invalid.", this);
                yield break;
            }

            robotPoses.RequestPose(robotPoses.traySafe);
            yield return WaitForRobotPose("TraySafe");
            if (robotPoses.Busy)
            {
                Debug.LogError("Worker-assisted assembly stopped: robot did not reach TraySafe.", this);
                yield break;
            }

            handover.AttachToRobot(part);
            yield return WaitForHandover();
            if (!RobotHolds(part))
            {
                Debug.LogError("Worker-assisted assembly stopped: robot could not pick " + part.partId + ".", this);
                yield break;
            }

            handover.PresentPartToWorker(part);
            yield return WaitForHandover();
            if (part.transform.parent != handover.workerRightHandHoldPoint)
            {
                Debug.LogError("Worker-assisted assembly stopped: worker did not receive " + part.partId + ".", this);
                yield break;
            }

            yield return handover.WorkerReachToward(target.transform.position);
            yield return MovePartToTarget(part, target.transform);

            part.transform.SetParent(assembledGearbox, true);
            SetExactTargetTransform(part, target.transform);
            if (!target.TryPlace(part))
            {
                Debug.LogError("Placement rejected for " + part.partId, this);
                yield break;
            }
            SetExactTargetTransform(part, target.transform);

            handover.ReleasePart(part);
            yield return WaitForHandover();
            if (handover.IsBusy || !part.assembled)
            {
                Debug.LogError("Worker-assisted assembly stopped while finalizing " + part.partId + ".", this);
                yield break;
            }

            currentPartSucceeded = true;
        }

        private IEnumerator MovePartToTarget(GearboxPart part, Transform target)
        {
            part.transform.SetParent(assembledGearbox, true);
            yield return AssemblyMotion.Seat(part.transform, target, workerInstallDuration,
                () => handover.FollowWorkerHand(part.gripPoint.position));
        }

        private static void SetExactTargetTransform(GearboxPart part, Transform target)
        {
            part.transform.SetPositionAndRotation(target.position, target.rotation);
            Rigidbody body = part.GetComponent<Rigidbody>();
            body.position = part.transform.position;
            body.rotation = part.transform.rotation;
        }

        private IEnumerator WaitForRobotPose(string expectedStatus)
        {
            float deadline = Time.time + operationTimeout;
            do { yield return null; }
            while ((robotPoses.Busy || robotPoses.Status != expectedStatus) && Time.time < deadline);
        }

        private IEnumerator WaitForHandover()
        {
            float deadline = Time.time + operationTimeout;
            do { yield return null; }
            while (handover.IsBusy && Time.time < deadline);
        }

        private bool RobotHolds(GearboxPart part)
        {
            return handover.robotGripper.HeldObject is Component held && held.GetComponent<GearboxPart>() == part;
        }

        private bool ReferencesAreValid()
        {
            bool valid = planetGears != null && planetGears.Length == 3 &&
                         planetGearTargets != null && planetGearTargets.Length == 3 &&
                         outputShaft != null && assembledGearbox != null && handover != null &&
                         robotPoses != null && robotPoses.traySafe != null;
            if (valid)
            {
                for (int i = 0; i < 3; i++)
                    valid &= planetGears[i] != null && planetGearTargets[i] != null &&
                             planetGears[i].partId == "PlanetGear_0" + (i + 1) &&
                             planetGears[i].assemblyTarget == planetGearTargets[i];
            }
            if (!valid) Debug.LogError("Planetary gear assembly references are invalid.", this);
            return valid;
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return AssemblyMotion.Ease(value);
        }
    }
}
