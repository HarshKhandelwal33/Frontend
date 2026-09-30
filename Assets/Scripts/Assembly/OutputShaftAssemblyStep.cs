using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GearboxDemo
{
    [DisallowMultipleComponent]
    public sealed class OutputShaftAssemblyStep : MonoBehaviour
    {
        public GearboxPart pinCarrier;
        public GearboxPart outputShaft;
        public AssemblyTarget pinCarrierTarget;
        public AssemblyTarget outputShaftTarget;
        public Transform assembledGearbox;
        public HumanRobotHandover handover;
        public RobotPoseController robotPoses;

        [Min(0.1f)] public float workerInstallDuration = 1.2f;
        [Min(1f)] public float operationTimeout = 25f;

        public bool IsRunning { get; private set; }

        private void Update()
        {
            if (GetComponent<GearboxAssemblyManager>() != null) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.oKey.wasPressedThisFrame)
                RunOutputShaftAssembly();
        }

        public void RunOutputShaftAssembly()
        {
            if (!IsRunning && isActiveAndEnabled)
                StartCoroutine(AssemblyRoutine());
        }

        public void ResetOperationState()
        {
            StopAllCoroutines();
            IsRunning = false;
        }

        private IEnumerator AssemblyRoutine()
        {
            if (!ReferencesAreValid() || outputShaft.assembled)
                yield break;

            IsRunning = true;

            robotPoses.RequestPose(robotPoses.traySafe);
            yield return WaitForRobotPose("TraySafe");
            if (robotPoses.Busy) { Fail("Robot did not reach TraySafe."); yield break; }

            handover.AttachToRobot(outputShaft);
            yield return WaitForHandover();
            if (!RobotHoldsOutputShaft()) { Fail("Robot could not pick the Output Shaft."); yield break; }

            handover.PresentPartToWorker(outputShaft);
            yield return WaitForHandover();
            if (outputShaft.transform.parent != handover.workerRightHandHoldPoint)
            {
                Fail("Worker did not receive the Output Shaft.");
                yield break;
            }

            yield return handover.WorkerReachToward(outputShaftTarget.transform.position);
            yield return MoveOutputShaftToTarget();

            outputShaft.transform.SetParent(assembledGearbox, true);
            outputShaft.transform.SetPositionAndRotation(outputShaftTarget.transform.position, outputShaftTarget.transform.rotation);
            Rigidbody body = outputShaft.GetComponent<Rigidbody>();
            body.position = outputShaft.transform.position;
            body.rotation = outputShaft.transform.rotation;

            if (!outputShaftTarget.TryPlace(outputShaft))
            {
                Fail("Output carrier target rejected placement.");
                yield break;
            }
            outputShaft.transform.SetPositionAndRotation(outputShaftTarget.transform.position, outputShaftTarget.transform.rotation);
            body.position = outputShaft.transform.position;
            body.rotation = outputShaft.transform.rotation;

            handover.ReleasePart(outputShaft);
            yield return WaitForHandover();
            IsRunning = false;
        }

        private IEnumerator MoveOutputShaftToTarget()
        {
            outputShaft.transform.SetParent(assembledGearbox, true);
            yield return AssemblyMotion.Seat(outputShaft.transform, outputShaftTarget.transform, workerInstallDuration,
                () => handover.FollowWorkerHand(outputShaft.gripPoint.position));
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

        private bool RobotHoldsOutputShaft()
        {
            return handover.robotGripper.HeldObject is Component held && held.GetComponent<GearboxPart>() == outputShaft;
        }

        private bool ReferencesAreValid()
        {
            bool valid = pinCarrier != null && pinCarrier.assembled && pinCarrierTarget != null &&
                         outputShaft != null && outputShaft.partType == GearboxPartType.OutputShaft &&
                         outputShaftTarget != null && outputShaftTarget.acceptedPart == GearboxPartType.OutputShaft &&
                         assembledGearbox != null && handover != null && robotPoses != null && robotPoses.traySafe != null;
            if (!valid) Debug.LogError("Output Shaft assembly references or initial Pin Carrier state are invalid.", this);
            return valid;
        }

        private void Fail(string message)
        {
            IsRunning = false;
            Debug.LogError("Output Shaft assembly stopped: " + message, this);
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return AssemblyMotion.Ease(value);
        }
    }
}
