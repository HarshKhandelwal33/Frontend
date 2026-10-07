using UnityEngine;

namespace GearboxDemo
{
    // Must live beside the Animator to receive OnAnimatorIK callbacks.
    [DefaultExecutionOrder(250)]
    public sealed class WorkerReceivingRig : MonoBehaviour
    {
        public Animator animator;
        public Transform holdPoint;
        [Range(0, 1)] public float reachFraction = 0.95f;
        [Min(0.01f)] public float blendSeconds = 0.35f;
        public bool ReadyToReceive = true;
        [Range(0, 30), Tooltip("Additive forward torso posture during tabletop assembly; arm reach remains checked.")]
        public float assemblyLeanDegrees = 25;
        public bool AssemblyPose;
        private float lean;
        private Transform spine, workerRoot;
        public bool GraspConfirmed { get; private set; }
        public bool TrackingValid => isActiveAndEnabled && animator != null && animator.isActiveAndEnabled && animator.isHuman &&
            animator.avatar != null && animator.avatar.isValid && holdPoint != null && gameObject.activeInHierarchy;
        public Vector3 Target { get; private set; }
        public Quaternion Rotation { get; private set; }
        public bool Reaching { get; private set; }
        private float weight;
        private Transform shoulder, elbow, wrist;
        private Quaternion wristToIK = Quaternion.identity;
        private Quaternion requestedIK;
        private bool calibrateAfterSolve;
        private bool calibrated;

        public void Initialize(Animator value, Transform palm)
        {
            animator = value; holdPoint = palm;
            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            workerRoot = animator.GetComponentInParent<WorkerMovement>().transform;
            shoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            elbow = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            wrist = animator.GetBoneTransform(HumanBodyBones.RightHand);
        }
        public bool CanReach(Vector3 target)
        {
            if (!TrackingValid || shoulder == null || elbow == null || wrist == null) return false;
            float length = Vector3.Distance(shoulder.position, elbow.position) + Vector3.Distance(elbow.position, wrist.position);
            return Vector3.Distance(shoulder.position, target) <= length * reachFraction + Vector3.Distance(wrist.position, holdPoint.position);
        }
        public void Reach(Vector3 target, Quaternion rotation)
        { Target = target; Rotation = rotation; Reaching = true; }
        public void ConfirmGrasp() { if (ReadyToReceive && TrackingValid && Reaching) GraspConfirmed = true; }
        public void ClearConfirmation() => GraspConfirmed = false;
        public void Rest() { Reaching = false; GraspConfirmed = false; AssemblyPose = false; }
        private void OnAnimatorIK(int layer)
        {
            if (!TrackingValid) return;
            lean = Mathf.MoveTowards(lean, AssemblyPose ? assemblyLeanDegrees : 0, 60 * Time.deltaTime);
            if (spine != null && lean > 0.001f)
            {
                Quaternion world = Quaternion.AngleAxis(lean, workerRoot.right) * spine.rotation;
                animator.SetBoneLocalRotation(HumanBodyBones.Spine, Quaternion.Inverse(spine.parent.rotation) * world);
            }
            weight = Mathf.MoveTowards(weight, Reaching ? 1 : 0, Time.deltaTime / Mathf.Max(0.01f, blendSeconds));
            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, weight);
            animator.SetIKRotationWeight(AvatarIKGoal.RightHand, weight);
            if (Reaching)
            {
                Quaternion boneRotation = Rotation * Quaternion.Inverse(Quaternion.Inverse(wrist.rotation) * holdPoint.rotation);
                Vector3 offset = wrist.InverseTransformPoint(holdPoint.position);
                animator.SetIKPosition(AvatarIKGoal.RightHand, Target - boneRotation * Vector3.Scale(offset, wrist.lossyScale));
                requestedIK = boneRotation * wristToIK;
                animator.SetIKRotation(AvatarIKGoal.RightHand, requestedIK);
                if (!calibrated && weight >= 0.999f) calibrateAfterSolve = true;
                animator.SetLookAtWeight(0.5f * weight, 0f, 0.7f, 0.7f, 0.5f);
                animator.SetLookAtPosition(Target);
            }
            else animator.SetLookAtWeight(0);
        }
        private void LateUpdate()
        {
            if (!calibrateAfterSolve || !TrackingValid) return;
            // Measure the avatar goal-to-imported-bone offset after the first full-weight IK solve.
            // GetIKRotation before the solve can still contain a previous goal rather than the solved wrist frame.
            wristToIK = Quaternion.Inverse(wrist.rotation) * requestedIK;
            calibrated = true; calibrateAfterSolve = false;
        }
        private void OnDrawGizmosSelected()
        {
            if (shoulder == null || elbow == null || wrist == null) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(shoulder.position, (Vector3.Distance(shoulder.position, elbow.position) + Vector3.Distance(elbow.position, wrist.position)) * reachFraction);
            if (Reaching) { Gizmos.color = Color.cyan; Gizmos.DrawWireSphere(Target, 0.025f); }
        }
    }
}
