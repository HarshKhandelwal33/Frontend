using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GearboxDemo
{
    [DisallowMultipleComponent]
    public sealed class PhaseTwoInsertionController : MonoBehaviour
    {
        public PhaseOneAssemblyController phaseOne;
        public GearboxPart housing;
        public AssemblyTarget housingTarget;
        public Transform internalAssemblyRoot;
        public Transform internalAssemblyTarget;
        public Transform finalGearboxRoot;
        public Transform internalAssemblyGripPoint;
        public Transform robotHoldPoint;
        public RobotArmController robotArm;
        public RobotPoseController robotPoses;
        public Transform worker;
        public Transform workerObservationPosition;

        [Tooltip("Joint1 through Joint6 for lifting the internal assembly.")]
        public float[] liftJointDegrees = new float[6];
        [Tooltip("Joint1 through Joint6 for approaching the Housing.")]
        public float[] insertionApproachJointDegrees = new float[6];
        [Min(0.1f)] public float workerMoveDuration = 0.8f;
        [Min(0.1f)] public float pickupDuration = 0.65f;
        [Min(0.1f)] public float insertionDuration = 1.4f;
        [Min(1f)] public float robotTimeout = 25f;

        public bool IsRunning { get; private set; }
        public bool InternalAssemblyInserted { get; private set; }

        private Rigidbody[] payloadBodies;
        private Collider[] payloadColliders;
        private Vector3 payloadPositionInHold;
        private Quaternion payloadRotationInHold;
        private bool payloadAttached;

        private void Update()
        {
            if (GetComponent<GearboxAssemblyManager>() != null) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.iKey.wasPressedThisFrame)
                StartPhaseTwoInsertion();
        }

        public void StartPhaseTwoInsertion()
        {
            if (!IsRunning && !InternalAssemblyInserted && isActiveAndEnabled)
                StartCoroutine(InsertionRoutine());
        }

        public void ResetOperationState()
        {
            StopAllCoroutines();
            payloadAttached = false;
            IsRunning = false;
            InternalAssemblyInserted = false;
            if (payloadBodies != null) RestorePayloadPhysics();
        }

        private IEnumerator InsertionRoutine()
        {
            if (!ReferencesAreValid()) yield break;
            if (!phaseOne.PhaseOneComplete)
            {
                Debug.LogError("Phase Two insertion requires PHASE ONE COMPLETE.", this);
                yield break;
            }

            IsRunning = true;
            yield return MoveWorkerToObservationPosition();
            PositionHousing();

            robotPoses.RequestPose(robotPoses.assemblySafe);
            yield return WaitForRobotPose("AssemblySafe");
            if (robotPoses.Busy) { StopInsertion("Robot did not reach the internal assembly."); yield break; }

            PreparePayloadPhysics();
            yield return AlignPayloadGripToRobot();
            AttachPayloadFollower();

            yield return MoveRobotWithPayload(liftJointDegrees);
            if (robotArm.IsMoving) { StopInsertion("Robot lift timed out."); yield break; }

            yield return MoveRobotWithPayload(insertionApproachJointDegrees);
            if (robotArm.IsMoving) { StopInsertion("Robot housing approach timed out."); yield break; }

            payloadAttached = false;
            yield return MovePayloadToInternalTarget();
            internalAssemblyRoot.SetParent(finalGearboxRoot, true);
            SetInternalAssemblyExactTarget();
            RestorePayloadPhysics();

            InternalAssemblyInserted = true;
            IsRunning = false;
            Debug.Log("PHASE TWO INSERTION COMPLETE", this);
        }

        private IEnumerator MoveWorkerToObservationPosition()
        {
            Vector3 startPosition = worker.position;
            Quaternion startRotation = worker.rotation;
            float elapsed = 0f;
            while (elapsed < workerMoveDuration)
            {
                elapsed += Time.deltaTime;
                float t = Smooth01(elapsed / workerMoveDuration);
                worker.SetPositionAndRotation(
                    Vector3.Lerp(startPosition, workerObservationPosition.position, t),
                    Quaternion.Slerp(startRotation, workerObservationPosition.rotation, t));
                yield return null;
            }
            worker.SetPositionAndRotation(workerObservationPosition.position, workerObservationPosition.rotation);
        }

        private void PositionHousing()
        {
            housing.transform.SetParent(finalGearboxRoot, true);
            housing.transform.SetPositionAndRotation(housingTarget.transform.position, housingTarget.transform.rotation);
            Rigidbody body = housing.GetComponent<Rigidbody>();
            body.position = housing.transform.position;
            body.rotation = housing.transform.rotation;
            body.isKinematic = true;
            body.useGravity = false;
            if (housingTarget.Occupant != housing)
            {
                housing.assembled = false;
                housing.canBePicked = true;
                if (!housingTarget.TryPlace(housing))
                {
                    housing.assembled = true;
                    housing.canBePicked = false;
                }
            }
            else
            {
                housing.assembled = true;
                housing.canBePicked = false;
            }
        }

        private void PreparePayloadPhysics()
        {
            payloadBodies = internalAssemblyRoot.GetComponentsInChildren<Rigidbody>(true);
            payloadColliders = internalAssemblyRoot.GetComponentsInChildren<Collider>(true);
            foreach (Rigidbody body in payloadBodies)
            {
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                body.interpolation = RigidbodyInterpolation.None;
                body.isKinematic = true;
                body.useGravity = false;
                body.detectCollisions = false;
            }
            foreach (Collider collider in payloadColliders) collider.enabled = false;
        }

        private IEnumerator AlignPayloadGripToRobot()
        {
            Vector3 gripLocalPosition = internalAssemblyRoot.InverseTransformPoint(internalAssemblyGripPoint.position);
            Quaternion gripLocalRotation = Quaternion.Inverse(internalAssemblyRoot.rotation) * internalAssemblyGripPoint.rotation;
            Vector3 startPosition = internalAssemblyRoot.position;
            Quaternion startRotation = internalAssemblyRoot.rotation;
            Quaternion targetRotation = robotHoldPoint.rotation * Quaternion.Inverse(gripLocalRotation);
            Vector3 targetPosition = robotHoldPoint.position - targetRotation * Vector3.Scale(gripLocalPosition, internalAssemblyRoot.lossyScale);
            float elapsed = 0f;
            while (elapsed < pickupDuration)
            {
                elapsed += Time.deltaTime;
                float t = Smooth01(elapsed / pickupDuration);
                internalAssemblyRoot.SetPositionAndRotation(
                    Vector3.Lerp(startPosition, targetPosition, t),
                    Quaternion.Slerp(startRotation, targetRotation, t));
                SyncPayloadBodies();
                yield return null;
            }
            internalAssemblyRoot.SetPositionAndRotation(targetPosition, targetRotation);
            SyncPayloadBodies();
        }

        private void AttachPayloadFollower()
        {
            payloadPositionInHold = robotHoldPoint.InverseTransformPoint(internalAssemblyRoot.position);
            payloadRotationInHold = Quaternion.Inverse(robotHoldPoint.rotation) * internalAssemblyRoot.rotation;
            payloadAttached = true;
        }

        private IEnumerator MoveRobotWithPayload(float[] jointDegrees)
        {
            robotArm.MoveTo(jointDegrees);
            float deadline = Time.time + robotTimeout;
            while ((robotArm.IsMoving || robotArm.MaxError > 2f) && Time.time < deadline)
            {
                FollowRobotHoldPoint();
                yield return new WaitForFixedUpdate();
            }
            FollowRobotHoldPoint();
        }

        private void FollowRobotHoldPoint()
        {
            if (!payloadAttached) return;
            internalAssemblyRoot.SetPositionAndRotation(
                robotHoldPoint.TransformPoint(payloadPositionInHold),
                robotHoldPoint.rotation * payloadRotationInHold);
            SyncPayloadBodies();
        }

        private IEnumerator MovePayloadToInternalTarget()
        {
            yield return AssemblyMotion.Seat(internalAssemblyRoot, internalAssemblyTarget, insertionDuration);
        }

        private void SetInternalAssemblyExactTarget()
        {
            internalAssemblyRoot.SetPositionAndRotation(internalAssemblyTarget.position, internalAssemblyTarget.rotation);
            SyncPayloadBodies();
        }

        private void RestorePayloadPhysics()
        {
            foreach (Rigidbody body in payloadBodies)
            {
                body.isKinematic = true;
                body.useGravity = false;
                body.detectCollisions = true;
            }
            foreach (Collider collider in payloadColliders) collider.enabled = true;
        }

        private void SyncPayloadBodies()
        {
            if (payloadBodies == null) return;
            foreach (Rigidbody body in payloadBodies)
            {
                body.position = body.transform.position;
                body.rotation = body.transform.rotation;
            }
        }

        private IEnumerator WaitForRobotPose(string expectedStatus)
        {
            float deadline = Time.time + robotTimeout;
            do { yield return null; }
            while ((robotPoses.Busy || robotPoses.Status != expectedStatus) && Time.time < deadline);
        }

        private bool ReferencesAreValid()
        {
            var errors = new List<string>();
            if (phaseOne == null) errors.Add("Phase One controller is missing");
            if (housing == null) errors.Add("Housing is missing");
            if (housingTarget == null) errors.Add("Housing target is missing");
            if (internalAssemblyRoot == null) errors.Add("Internal assembly root is missing");
            if (internalAssemblyTarget == null) errors.Add("Internal assembly target is missing");
            if (finalGearboxRoot == null) errors.Add("Final gearbox root is missing");
            if (internalAssemblyGripPoint == null) errors.Add("Internal assembly grip point is missing");
            else if (internalAssemblyRoot != null && !internalAssemblyGripPoint.IsChildOf(internalAssemblyRoot))
                errors.Add("Internal assembly grip point is outside the internal assembly root");
            if (robotHoldPoint == null) errors.Add("Robot hold point is missing");
            if (robotArm == null) errors.Add("Robot arm is missing");
            if (robotPoses == null) errors.Add("Robot pose controller is missing");
            else if (robotPoses.assemblySafe == null) errors.Add("AssemblySafe robot pose is missing");
            if (worker == null) errors.Add("Worker is missing");
            if (workerObservationPosition == null) errors.Add("Worker observation position is missing");
            if (liftJointDegrees == null || liftJointDegrees.Length != 6) errors.Add("Lift pose must contain six joint values");
            if (insertionApproachJointDegrees == null || insertionApproachJointDegrees.Length != 6)
                errors.Add("Insertion approach pose must contain six joint values");
            if (phaseOne != null && internalAssemblyRoot != null && phaseOne.InternalAssemblyRoot != internalAssemblyRoot)
                errors.Add("Phase One and Phase Two use different internal assembly roots");
            if (phaseOne != null && internalAssemblyRoot != null)
            {
                GearboxPart[] expectedParts =
                {
                    phaseOne.pinCarrier,
                    phaseOne.outputShaftStep != null ? phaseOne.outputShaftStep.outputShaft : null,
                    phaseOne.planetGearStep != null && phaseOne.planetGearStep.planetGears != null && phaseOne.planetGearStep.planetGears.Length > 0 ? phaseOne.planetGearStep.planetGears[0] : null,
                    phaseOne.planetGearStep != null && phaseOne.planetGearStep.planetGears != null && phaseOne.planetGearStep.planetGears.Length > 1 ? phaseOne.planetGearStep.planetGears[1] : null,
                    phaseOne.planetGearStep != null && phaseOne.planetGearStep.planetGears != null && phaseOne.planetGearStep.planetGears.Length > 2 ? phaseOne.planetGearStep.planetGears[2] : null,
                    phaseOne.spurGear
                };
                string[] expectedNames = { "Pin Carrier", "Output Shaft", "Planet Gear 1", "Planet Gear 2", "Planet Gear 3", "Spur Gear" };
                for (int i = 0; i < expectedParts.Length; i++)
                {
                    GearboxPart part = expectedParts[i];
                    if (part == null) errors.Add(expectedNames[i] + " reference is missing");
                    else if (!part.assembled) errors.Add(expectedNames[i] + " is not assembled");
                    else if (!part.transform.IsChildOf(internalAssemblyRoot))
                        errors.Add(expectedNames[i] + " is not parented under the internal assembly root");
                }
            }
            if (errors.Count == 0) return true;
            Debug.LogError("Phase Two insertion is invalid:\n- " + string.Join("\n- ", errors), this);
            return false;
        }

        private void StopInsertion(string reason)
        {
            payloadAttached = false;
            RestorePayloadPhysics();
            IsRunning = false;
            Debug.LogError("Phase Two insertion stopped: " + reason, this);
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return AssemblyMotion.Ease(value);
        }
    }
}
