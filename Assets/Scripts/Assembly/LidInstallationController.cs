using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GearboxDemo
{
    [DisallowMultipleComponent]
    public sealed class LidInstallationController : MonoBehaviour
    {
        public PhaseTwoInsertionController phaseTwoInsertion;
        public GearboxPart lid;
        public AssemblyTarget lidTarget;
        public Transform finalGearboxRoot;
        public HumanRobotHandover handover;
        public RobotArmController robotArm;
        public RobotPoseController robotPoses;

        [Tooltip("Joint1 through Joint6 for lifting the Lid clear of the tray.")]
        public float[] liftJointDegrees = new float[6];
        [Tooltip("Joint1 through Joint6 for carrying the Lid above the Housing.")]
        public float[] aboveHousingJointDegrees = new float[6];
        [Min(0.01f)] public float alignmentHeight = 0.18f;
        [Min(0.1f)] public float alignmentDuration = 0.7f;
        [Min(0.1f)] public float loweringDuration = 1.1f;
        [Min(1f)] public float robotTimeout = 25f;

        public bool IsRunning { get; private set; }
        public bool LidInstalled { get; private set; }

        private void Update()
        {
            if (GetComponent<GearboxAssemblyManager>() != null) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.lKey.wasPressedThisFrame)
                InstallLid();
        }

        public void InstallLid()
        {
            if (!IsRunning && !LidInstalled && isActiveAndEnabled)
                StartCoroutine(InstallationRoutine());
        }

        public void ResetOperationState()
        {
            StopAllCoroutines();
            IsRunning = false;
            LidInstalled = false;
        }

        private IEnumerator InstallationRoutine()
        {
            if (!ReferencesAreValid()) yield break;
            if (!phaseTwoInsertion.InternalAssemblyInserted)
            {
                Debug.LogError("Lid installation requires the Phase Two internal assembly insertion first.", this);
                yield break;
            }

            IsRunning = true;

            robotPoses.RequestPose(robotPoses.traySafe);
            yield return WaitForRobotPose("TraySafe");
            if (robotPoses.Busy) { StopInstallation("Robot did not reach the Lid pickup area."); yield break; }

            handover.AttachToRobot(lid);
            yield return WaitForHandover();
            if (!RobotHoldsLid()) { StopInstallation("Robot could not pick the Lid."); yield break; }

            yield return MoveRobot(liftJointDegrees);
            if (robotArm.IsMoving) { StopInstallation("Lid lift timed out."); yield break; }

            yield return MoveRobot(aboveHousingJointDegrees);
            if (robotArm.IsMoving) { StopInstallation("Robot movement above the Housing timed out."); yield break; }

            BeginControlledLidMotion();
            Vector3 alignedPosition = lidTarget.transform.position + lidTarget.transform.up * alignmentHeight;
            yield return MoveLid(alignedPosition, lidTarget.transform.rotation, alignmentDuration);
            yield return MoveLid(lidTarget.transform.position, lidTarget.transform.rotation, loweringDuration);

            lid.transform.SetParent(finalGearboxRoot, true);
            SetExactTargetTransform();
            if (!lidTarget.TryPlace(lid))
            {
                StopInstallation("Lid target rejected placement.");
                yield break;
            }
            SetExactTargetTransform();

            handover.ReleasePart(lid);
            yield return WaitForHandover();
            LidInstalled = lid.assembled;
            IsRunning = false;
            if (LidInstalled) Debug.Log("LID INSTALLATION COMPLETE", this);
        }

        private void BeginControlledLidMotion()
        {
            handover.robotGripper.Release();
            GrippableObject grippable = lid.GetComponent<GrippableObject>();
            if (grippable.CanBeGrabbed()) grippable.OnGrabbed();
            Rigidbody body = lid.GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.detectCollisions = false;
        }

        private IEnumerator MoveLid(Vector3 targetPosition, Quaternion targetRotation, float duration)
        {
            Vector3 startPosition = lid.transform.position;
            Quaternion startRotation = lid.transform.rotation;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Smooth01(elapsed / duration);
                lid.transform.SetPositionAndRotation(
                    Vector3.Lerp(startPosition, targetPosition, t),
                    Quaternion.Slerp(startRotation, targetRotation, t));
                SyncBody();
                yield return null;
            }
            lid.transform.SetPositionAndRotation(targetPosition, targetRotation);
            SyncBody();
        }

        private IEnumerator MoveRobot(float[] jointDegrees)
        {
            robotArm.MoveTo(jointDegrees);
            float deadline = Time.time + robotTimeout;
            while ((robotArm.IsMoving || robotArm.MaxError > 2f) && Time.time < deadline)
                yield return new WaitForFixedUpdate();
        }

        private IEnumerator WaitForRobotPose(string expectedStatus)
        {
            float deadline = Time.time + robotTimeout;
            do { yield return null; }
            while ((robotPoses.Busy || robotPoses.Status != expectedStatus) && Time.time < deadline);
        }

        private IEnumerator WaitForHandover()
        {
            float deadline = Time.time + robotTimeout;
            do { yield return null; }
            while (handover.IsBusy && Time.time < deadline);
        }

        private bool RobotHoldsLid()
        {
            return handover.robotGripper.HeldObject is Component held && held.GetComponent<GearboxPart>() == lid;
        }

        private void SetExactTargetTransform()
        {
            lid.transform.SetPositionAndRotation(lidTarget.transform.position, lidTarget.transform.rotation);
            SyncBody();
        }

        private void SyncBody()
        {
            Rigidbody body = lid.GetComponent<Rigidbody>();
            body.position = lid.transform.position;
            body.rotation = lid.transform.rotation;
        }

        private bool ReferencesAreValid()
        {
            bool valid = phaseTwoInsertion != null && lid != null && lid.partType == GearboxPartType.Lid &&
                         lidTarget != null && lidTarget.acceptedPart == GearboxPartType.Lid && lid.assemblyTarget == lidTarget &&
                         finalGearboxRoot != null && handover != null && handover.robotGripper != null &&
                         robotArm != null && robotPoses != null && robotPoses.traySafe != null &&
                         liftJointDegrees != null && liftJointDegrees.Length == 6 &&
                         aboveHousingJointDegrees != null && aboveHousingJointDegrees.Length == 6;
            if (!valid) Debug.LogError("Lid installation references are invalid.", this);
            return valid;
        }

        private void StopInstallation(string reason)
        {
            IsRunning = false;
            Debug.LogError("Lid installation stopped: " + reason, this);
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return AssemblyMotion.Ease(value);
        }
    }
}
