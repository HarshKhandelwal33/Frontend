using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GearboxDemo
{
    [DisallowMultipleComponent]
    public sealed class ScrewInstallationController : MonoBehaviour
    {
        [Tooltip("Ordered Screw_01 through Screw_04.")]
        public GearboxPart[] screws = new GearboxPart[4];
        [Tooltip("Ordered ScrewTarget_01 through ScrewTarget_04.")]
        public AssemblyTarget[] screwTargets = new AssemblyTarget[4];
        public LidInstallationController lidInstallation;
        public Transform finalGearboxRoot;
        public HumanRobotHandover handover;
        public RobotArmController robotArm;
        public RobotPoseController robotPoses;

        [Tooltip("Joint1 through Joint6 for lifting a Screw clear of the tray.")]
        public float[] liftJointDegrees = new float[6];
        [Tooltip("Joint1 through Joint6 for carrying a Screw above the gearbox.")]
        public float[] aboveGearboxJointDegrees = new float[6];
        [Min(0.01f)] public float approachHeight = 0.12f;
        [Min(0.1f)] public float alignmentDuration = 0.55f;
        [Min(0.1f)] public float insertionDuration = 1.1f;
        [Min(0f)] public float delayBetweenScrews = 0.45f;
        [Min(1)] public int insertionTurns = 4;
        public Vector3 localScrewAxis = Vector3.up;
        [Min(1f)] public float robotTimeout = 25f;

        public bool IsRunning { get; private set; }
        public bool GearboxAssemblyComplete { get; private set; }
        public int CompletedScrewCount { get; private set; }
        public bool LastScrewSucceeded => currentScrewSucceeded;

        private bool currentScrewSucceeded;

        private void Update()
        {
            if (GetComponent<GearboxAssemblyManager>() != null) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.sKey.wasPressedThisFrame)
                InstallAllScrews();
        }

        public void InstallAllScrews()
        {
            if (!IsRunning && !GearboxAssemblyComplete && isActiveAndEnabled)
                StartCoroutine(InstallationSequence());
        }

        public void ResetOperationState()
        {
            StopAllCoroutines();
            IsRunning = false;
            GearboxAssemblyComplete = false;
            CompletedScrewCount = 0;
            currentScrewSucceeded = false;
        }

        public void MarkGearboxAssemblyComplete()
        {
            CompletedScrewCount = 0;
            foreach (GearboxPart screw in screws)
                if (screw != null && screw.assembled) CompletedScrewCount++;
            GearboxAssemblyComplete = CompletedScrewCount == 4;
            IsRunning = false;
            if (GearboxAssemblyComplete) Debug.Log("GEARBOX ASSEMBLY COMPLETE", this);
        }

        private IEnumerator InstallationSequence()
        {
            if (!ReferencesAreValid()) yield break;
            if (!lidInstallation.LidInstalled)
            {
                Debug.LogError("Screw installation requires the Lid installation first.", this);
                yield break;
            }

            IsRunning = true;
            CompletedScrewCount = 0;
            GearboxAssemblyComplete = false;

            for (int i = 0; i < screws.Length; i++)
            {
                if (screws[i].assembled)
                {
                    CompletedScrewCount++;
                    continue;
                }

                yield return InstallScrew(screws[i], screwTargets[i]);
                if (!currentScrewSucceeded)
                {
                    IsRunning = false;
                    yield break;
                }

                CompletedScrewCount++;
                if (i < screws.Length - 1 && delayBetweenScrews > 0f)
                    yield return new WaitForSeconds(delayBetweenScrews);
            }

            GearboxAssemblyComplete = true;
            IsRunning = false;
            Debug.Log("GEARBOX ASSEMBLY COMPLETE", this);
        }

        public IEnumerator InstallScrew(GearboxPart screw, AssemblyTarget target)
        {
            currentScrewSucceeded = false;
            if (screw == null || target == null || screw.partType != GearboxPartType.Screw ||
                target.acceptedPart != GearboxPartType.Screw || screw.assemblyTarget != target)
            {
                Debug.LogError("Screw and target pairing is invalid.", this);
                yield break;
            }

            robotPoses.RequestPose(robotPoses.traySafe);
            yield return WaitForRobotPose("TraySafe");
            if (robotPoses.Busy) { StopCurrentScrew("Robot did not reach the screw tray."); yield break; }

            handover.AttachToRobot(screw);
            yield return WaitForHandover();
            if (!RobotHolds(screw)) { StopCurrentScrew("Robot could not pick " + screw.partId + "."); yield break; }

            handover.PresentPartToWorker(screw);
            yield return WaitForHandover();
            if (screw.transform.parent != handover.workerRightHandHoldPoint)
            { StopCurrentScrew("Worker did not receive screw."); yield break; }
            yield return handover.WorkerReachToward(target.transform.position);

            BeginControlledMotion(screw);
            Vector3 approachPosition = target.transform.position + target.transform.up * approachHeight;
            yield return MovePart(screw, approachPosition, target.transform.rotation, alignmentDuration);
            yield return InsertWithRotation(screw, target);

            screw.transform.SetParent(finalGearboxRoot, true);
            SetExactTargetTransform(screw, target.transform);
            if (!target.TryPlace(screw))
            {
                StopCurrentScrew("Screw target rejected placement.");
                yield break;
            }
            SetExactTargetTransform(screw, target.transform);

            handover.ReleasePart(screw);
            yield return WaitForHandover();
            if (!screw.assembled || handover.IsBusy)
            {
                StopCurrentScrew("Could not finalize " + screw.partId + ".");
                yield break;
            }
            currentScrewSucceeded = true;
        }

        private void BeginControlledMotion(GearboxPart screw)
        {
            screw.transform.SetParent(finalGearboxRoot, true);
            handover.robotGripper.Release();
            GrippableObject grippable = screw.GetComponent<GrippableObject>();
            if (grippable.CanBeGrabbed()) grippable.OnGrabbed();
            Rigidbody body = screw.GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.detectCollisions = false;
        }

        private IEnumerator MovePart(GearboxPart part, Vector3 targetPosition, Quaternion targetRotation, float duration)
        {
            Vector3 startPosition = part.transform.position;
            Quaternion startRotation = part.transform.rotation;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Smooth01(elapsed / duration);
                part.transform.SetPositionAndRotation(
                    Vector3.Lerp(startPosition, targetPosition, t),
                    Quaternion.Slerp(startRotation, targetRotation, t));
                SyncBody(part);
                handover.FollowWorkerHand(part.gripPoint.position);
                yield return null;
            }
            part.transform.SetPositionAndRotation(targetPosition, targetRotation);
            SyncBody(part);
        }

        private IEnumerator InsertWithRotation(GearboxPart screw, AssemblyTarget target)
        {
            Vector3 startPosition = screw.transform.position;
            Vector3 axis = localScrewAxis.sqrMagnitude > 0.0001f ? localScrewAxis.normalized : Vector3.up;
            float elapsed = 0f;
            while (elapsed < insertionDuration)
            {
                elapsed += Time.deltaTime;
                float normalized = Mathf.Clamp01(elapsed / insertionDuration);
                float t = Smooth01(normalized);
                Quaternion turn = Quaternion.AngleAxis(360f * insertionTurns * t, axis);
                screw.transform.SetPositionAndRotation(
                    Vector3.Lerp(startPosition, target.transform.position, t),
                    target.transform.rotation * turn);
                SyncBody(screw);
                handover.FollowWorkerHand(screw.gripPoint.position);
                yield return null;
            }
            SetExactTargetTransform(screw, target.transform);
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

        private bool RobotHolds(GearboxPart screw)
        {
            return handover.robotGripper.HeldObject is Component held && held.GetComponent<GearboxPart>() == screw;
        }

        private static void SetExactTargetTransform(GearboxPart part, Transform target)
        {
            part.transform.SetPositionAndRotation(target.position, target.rotation);
            SyncBody(part);
        }

        private static void SyncBody(GearboxPart part)
        {
            Rigidbody body = part.GetComponent<Rigidbody>();
            body.position = part.transform.position;
            body.rotation = part.transform.rotation;
        }

        private bool ReferencesAreValid()
        {
            bool valid = screws != null && screws.Length == 4 && screwTargets != null && screwTargets.Length == 4 &&
                         lidInstallation != null && finalGearboxRoot != null && handover != null &&
                         handover.robotGripper != null && robotArm != null && robotPoses != null && robotPoses.traySafe != null &&
                         liftJointDegrees != null && liftJointDegrees.Length == 6 &&
                         aboveGearboxJointDegrees != null && aboveGearboxJointDegrees.Length == 6;
            if (valid)
            {
                for (int i = 0; i < 4; i++)
                    valid &= screws[i] != null && screwTargets[i] != null &&
                             screws[i].partId == "Screw_0" + (i + 1) &&
                             screws[i].assemblyTarget == screwTargets[i];
            }
            if (!valid) Debug.LogError("Screw installation references are invalid.", this);
            return valid;
        }

        private void StopCurrentScrew(string reason)
        {
            currentScrewSucceeded = false;
            Debug.LogError("Screw installation stopped: " + reason, this);
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return AssemblyMotion.Ease(value);
        }
    }
}
