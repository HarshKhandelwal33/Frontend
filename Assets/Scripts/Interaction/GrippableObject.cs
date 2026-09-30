using UnityEngine;

namespace GearboxDemo
{
    [DisallowMultipleComponent, RequireComponent(typeof(GearboxPart), typeof(Rigidbody))]
    public sealed class GrippableObject : MonoBehaviour, IGrippable
    {
        public Transform GripPoint => GetComponent<GearboxPart>().gripPoint;
        public bool IsGrabbed { get; private set; }
        private bool previousKinematic, previousGravity;
        private bool previousDetectCollisions;
        private RigidbodyInterpolation previousInterpolation;
        private CollisionDetectionMode previousCollisionMode;

        private void OnEnable() => GetComponent<GearboxPart>().ResetCompleted += OnReset;
        private void OnDisable()
        {
            GetComponent<GearboxPart>().ResetCompleted -= OnReset;
            OnReleased();
        }

        public bool CanBeGrabbed()
        {
            var part = GetComponent<GearboxPart>();
            return isActiveAndEnabled && GripPoint != null && part.canBePicked && !part.installed && !IsGrabbed;
        }

        // Attachment/parenting belongs to the future gripper controller.
        public void OnGrabbed()
        {
            if (!CanBeGrabbed()) return;
            var body = GetComponent<Rigidbody>();
            previousKinematic = body.isKinematic;
            previousGravity = body.useGravity;
            previousDetectCollisions = body.detectCollisions;
            previousInterpolation = body.interpolation;
            previousCollisionMode = body.collisionDetectionMode;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.detectCollisions = false;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.isKinematic = true;
            body.useGravity = false;
            IsGrabbed = true;
        }

        public void OnReleased()
        {
            if (!IsGrabbed) return;
            IsGrabbed = false;
            var body = GetComponent<Rigidbody>();
            if (GetComponent<GearboxPart>().installed)
            {
                body.detectCollisions = previousDetectCollisions;
                return;
            }
            body.isKinematic = previousKinematic;
            body.useGravity = previousGravity;
            body.collisionDetectionMode = previousCollisionMode;
            body.interpolation = previousInterpolation;
            body.detectCollisions = previousDetectCollisions;
        }

        private void OnReset(GearboxPart part) => IsGrabbed = false;
    }
}
