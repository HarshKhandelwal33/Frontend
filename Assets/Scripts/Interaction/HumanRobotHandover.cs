using System;
using System.Collections;
using UnityEngine;

namespace GearboxDemo
{
    [DisallowMultipleComponent]
    public sealed class HumanRobotHandover : MonoBehaviour
    {
        public Transform robotWorkerHandoverPoint;
        public Transform robotEndEffector;
        public Transform robotHoldPoint;
        public Transform workerRightHandHoldPoint;
        public Animator workerAnimator;
        public RobotArmController robotArm;
        public RobotGripper robotGripper;

        [Tooltip("Joint1 through Joint6 for the handover presentation pose.")]
        public float[] handoverJointDegrees = new float[6];
        [Min(0.1f)] public float robotTimeout = 15f;
        [Min(0.1f)] public float reachDuration = 0.8f;
        [Min(0.1f)] public float transferDuration = 0.45f;

        public GearboxPart CurrentPart { get; private set; }
        public bool IsBusy { get; private set; }

        private Transform rightUpperArm;
        private Transform rightLowerArm;
        private Quaternion upperRestRotation;
        private Quaternion lowerRestRotation;
        private Coroutine activeRoutine;
        private bool resumeWorkerAnimator;

        private void Awake()
        {
            if (workerAnimator != null && workerAnimator.isHuman)
            {
                rightUpperArm = workerAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                rightLowerArm = workerAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                Transform rightHand = workerAnimator.GetBoneTransform(HumanBodyBones.RightHand);
                if (workerRightHandHoldPoint == null && rightHand != null)
                    workerRightHandHoldPoint = rightHand.Find("WorkerRightHandHoldPoint");
                if (rightUpperArm != null) upperRestRotation = rightUpperArm.localRotation;
                if (rightLowerArm != null) lowerRestRotation = rightLowerArm.localRotation;
            }
        }

        public void PresentPartToWorker(GearboxPart part)
        {
            if (!CanStart(part)) return;
            activeRoutine = StartCoroutine(PresentRoutine(part));
        }

        public void WorkerReceivePart(GearboxPart part)
        {
            if (!CanStart(part)) return;
            activeRoutine = StartCoroutine(WorkerReceiveRoutine(part, true));
        }

        public void AttachToRobot(GearboxPart part)
        {
            if (!CanStart(part)) return;
            activeRoutine = StartCoroutine(AttachToRobotRoutine(part, true));
        }

        public void AttachToWorker(GearboxPart part)
        {
            if (!CanStart(part)) return;
            activeRoutine = StartCoroutine(AttachToWorkerRoutine(part, true));
        }

        public void ReleasePart(GearboxPart part)
        {
            if (part == null) return;
            if (activeRoutine != null) StopCoroutine(activeRoutine);
            StartCoroutine(ReleaseRoutine(part));
        }

        public IEnumerator WorkerReachToward(Vector3 worldPosition)
        {
            yield return ReachWorkerArm(worldPosition);
        }

        public void FollowWorkerHand(Vector3 target)
        {
            if (rightUpperArm == null || rightLowerArm == null) return;
            for (int i = 0; i < 5; i++)
            {
                AimBone(rightLowerArm, workerRightHandHoldPoint.position, target, 180f);
                AimBone(rightUpperArm, workerRightHandHoldPoint.position, target, 180f);
            }
        }

        public void ResetHandoverState()
        {
            StopAllCoroutines();
            activeRoutine = null;
            IsBusy = false;
            CurrentPart = null;
            robotGripper?.Open();
            if (rightUpperArm != null) rightUpperArm.localRotation = upperRestRotation;
            if (rightLowerArm != null) rightLowerArm.localRotation = lowerRestRotation;
            ResumeWorkerAnimator();
        }

        private bool CanStart(GearboxPart part)
        {
            if (!isActiveAndEnabled || IsBusy || part == null) return false;
            if (part.assembled) return false;
            if (robotArm == null || robotGripper == null || robotHoldPoint == null ||
                workerRightHandHoldPoint == null || robotWorkerHandoverPoint == null) return false;
            return true;
        }

        private IEnumerator PresentRoutine(GearboxPart part)
        {
            IsBusy = true;
            yield return AttachToRobotRoutine(part, false);
            if (!RobotHolds(part)) { Finish(); yield break; }

            robotArm.MoveTo(handoverJointDegrees);
            float deadline = Time.time + robotTimeout;
            while ((robotArm.IsMoving || robotArm.MaxError > 2f) && Time.time < deadline)
                yield return new WaitForFixedUpdate();
            if (Time.time >= deadline)
            {
                Debug.LogError("Robot handover pose timed out.", this);
                Finish();
                yield break;
            }

            yield return ReachWorkerArm(robotHoldPoint.position);
            yield return AttachToWorkerRoutine(part, false);
            Finish();
        }

        private IEnumerator WorkerReceiveRoutine(GearboxPart part, bool ownsBusyState)
        {
            if (ownsBusyState) IsBusy = true;
            yield return ReachWorkerArm(robotHoldPoint.position);
            yield return AttachToWorkerRoutine(part, false);
            if (ownsBusyState) Finish();
        }

        private IEnumerator AttachToRobotRoutine(GearboxPart part, bool ownsBusyState)
        {
            if (ownsBusyState) IsBusy = true;
            if (RobotHolds(part))
            {
                CurrentPart = part;
                if (ownsBusyState) Finish();
                yield break;
            }

            var grippable = part.GetComponent<GrippableObject>();
            Rigidbody body = part.GetComponent<Rigidbody>();
            if (grippable == null || body == null || !grippable.CanBeGrabbed())
            {
                if (ownsBusyState) Finish();
                yield break;
            }

            part.transform.SetParent(robotWorkerHandoverPoint.parent, true);
            bool gravity = body.useGravity;
            bool kinematic = body.isKinematic;
            bool contacts = body.detectCollisions;
            body.useGravity = false;
            body.detectCollisions = false;
            body.isKinematic = true;
            yield return SmoothAlignGrip(part.transform, grippable.GripPoint, robotHoldPoint, transferDuration);
            body.isKinematic = kinematic;
            body.useGravity = gravity;
            body.detectCollisions = contacts;

            if (!robotGripper.Grab(grippable))
                Debug.LogError("Part reached RobotHoldPoint but the robot gripper rejected it.", part);
            else
                CurrentPart = part;
            if (ownsBusyState) Finish();
        }

        private IEnumerator AttachToWorkerRoutine(GearboxPart part, bool ownsBusyState)
        {
            if (ownsBusyState) IsBusy = true;
            if (RobotHolds(part)) robotGripper.Release();

            var grippable = part.GetComponent<GrippableObject>();
            if (grippable != null && grippable.CanBeGrabbed()) grippable.OnGrabbed();
            Rigidbody body = part.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.useGravity = false;
                body.detectCollisions = false;
                body.isKinematic = true;
            }

            // Preserve the exact visible world pose at ownership transfer.
            part.transform.SetParent(workerRightHandHoldPoint, true);
            Vector3 startPosition = part.transform.localPosition;
            Quaternion startRotation = part.transform.localRotation;
            float elapsed = 0;
            while (elapsed < transferDuration)
            {
                elapsed += Time.deltaTime;
                float t = Smooth01(elapsed / transferDuration);
                part.transform.localPosition = Vector3.Lerp(startPosition, Vector3.zero, t);
                part.transform.localRotation = Quaternion.Slerp(startRotation, Quaternion.identity, t);
                yield return null;
            }
            part.transform.localPosition = Vector3.zero;
            part.transform.localRotation = Quaternion.identity;
            robotGripper.Open();
            CurrentPart = part;
            if (ownsBusyState) Finish();
        }

        private IEnumerator ReleaseRoutine(GearboxPart part)
        {
            IsBusy = true;
            if (RobotHolds(part)) robotGripper.Release();
            if (part.transform.IsChildOf(workerRightHandHoldPoint))
                part.transform.SetParent(robotWorkerHandoverPoint.parent, true);
            part.GetComponent<GrippableObject>()?.OnReleased();
            Rigidbody body = part.GetComponent<Rigidbody>();
            if (body != null && !part.assembled)
            {
                body.isKinematic = false;
                body.useGravity = true;
                body.detectCollisions = true;
            }
            CurrentPart = null;
            yield return RestoreWorkerArm();
            Finish();
        }

        private IEnumerator ReachWorkerArm(Vector3 target)
        {
            if (rightUpperArm == null || rightLowerArm == null)
                yield break;
            // Existing target-driven assembly reaches own the bones until release/reset.
            // Suspend idle animation so it cannot overwrite that pose or move held parts.
            if (workerAnimator != null && workerAnimator.enabled && workerAnimator.runtimeAnimatorController != null)
            {
                workerAnimator.GetComponentInParent<WorkerMovement>()?.Stop();
                resumeWorkerAnimator = true;
                workerAnimator.enabled = false;
            }
            float elapsed = 0;
            while (elapsed < reachDuration)
            {
                elapsed += Time.deltaTime;
                float step = 240f * Time.deltaTime;
                AimBone(rightUpperArm, rightLowerArm.position, target, step);
                AimBone(rightLowerArm, workerRightHandHoldPoint.position, target, step);
                yield return null;
            }
        }

        private IEnumerator RestoreWorkerArm()
        {
            if (rightUpperArm == null || rightLowerArm == null)
            { ResumeWorkerAnimator(); yield break; }
            Quaternion upperStart = rightUpperArm.localRotation;
            Quaternion lowerStart = rightLowerArm.localRotation;
            float elapsed = 0;
            while (elapsed < reachDuration)
            {
                elapsed += Time.deltaTime;
                float t = Smooth01(elapsed / reachDuration);
                rightUpperArm.localRotation = Quaternion.Slerp(upperStart, upperRestRotation, t);
                rightLowerArm.localRotation = Quaternion.Slerp(lowerStart, lowerRestRotation, t);
                yield return null;
            }
            ResumeWorkerAnimator();
        }

        private void ResumeWorkerAnimator()
        {
            if (resumeWorkerAnimator && workerAnimator != null) workerAnimator.enabled = true;
            resumeWorkerAnimator = false;
        }

        private static IEnumerator SmoothAlignGrip(Transform part, Transform partGrip, Transform destination, float duration)
        {
            Vector3 gripLocalPosition = part.InverseTransformPoint(partGrip.position);
            Quaternion gripLocalRotation = Quaternion.Inverse(part.rotation) * partGrip.rotation;
            Vector3 startPosition = part.position;
            Quaternion startRotation = part.rotation;
            Quaternion targetRotation = destination.rotation * Quaternion.Inverse(gripLocalRotation);
            Vector3 targetPosition = destination.position - targetRotation * Vector3.Scale(gripLocalPosition, part.lossyScale);
            float elapsed = 0;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Smooth01(elapsed / duration);
                part.SetPositionAndRotation(Vector3.Lerp(startPosition, targetPosition, t), Quaternion.Slerp(startRotation, targetRotation, t));
                yield return null;
            }
            part.SetPositionAndRotation(targetPosition, targetRotation);
        }

        private static void AimBone(Transform bone, Vector3 currentEnd, Vector3 target, float maxDegrees)
        {
            Vector3 current = currentEnd - bone.position;
            Vector3 desired = target - bone.position;
            if (current.sqrMagnitude < 0.000001f || desired.sqrMagnitude < 0.000001f) return;
            Quaternion targetRotation = Quaternion.FromToRotation(current, desired) * bone.rotation;
            bone.rotation = Quaternion.RotateTowards(bone.rotation, targetRotation, maxDegrees);
        }

        private bool RobotHolds(GearboxPart part)
        {
            return robotGripper.HeldObject is Component held && held.GetComponent<GearboxPart>() == part;
        }

        private void Finish()
        {
            IsBusy = false;
            activeRoutine = null;
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return AssemblyMotion.Ease(value);
        }
    }
}
