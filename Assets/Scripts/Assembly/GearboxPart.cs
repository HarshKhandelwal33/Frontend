using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace GearboxDemo
{
    [DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
    public sealed class GearboxPart : MonoBehaviour
    {
        public GearboxPartType partType;
        public string partId;
        public string displayName;
        public Transform gripPoint;
        public Transform assemblyAnchor;
        [FormerlySerializedAs("installed")]
        public bool assembled;
        public bool canBePicked = true;
        public AssemblyTarget assemblyTarget;

        public bool installed { get => assembled; set => assembled = value; }

        [Header("Saved initial transform")]
        [SerializeField] private Vector3 originalPosition;
        [SerializeField] private Quaternion originalRotation = Quaternion.identity;
        [SerializeField] private Transform originalParent;

        public event Action<GearboxPart> Installed;
        public event Action<GearboxPart> Uninstalled;
        public event Action<GearboxPart> ResetCompleted;

        private Vector3 initialLocalPosition, initialWorldPosition, initialScale;
        private Quaternion initialLocalRotation, initialWorldRotation;
        private bool initialInstalled, initialPickable, initialKinematic, initialGravity, captured;
        private RigidbodyInterpolation initialInterpolation;
        private CollisionDetectionMode initialCollisionMode;
        private bool initialDetectCollisions;
        private Rigidbody Body => GetComponent<Rigidbody>();

        private void Awake() => SaveInitialTransform();

        public void SaveInitialTransform()
        {
            originalParent = transform.parent;
            originalPosition = transform.position;
            originalRotation = transform.rotation;
            CaptureInitialState();
        }

        // Call explicitly if a future spawner changes the starting pose after Awake.
        public void CaptureInitialState()
        {
            originalParent = transform.parent;
            originalPosition = transform.position;
            originalRotation = transform.rotation;
            initialLocalPosition = transform.localPosition;
            initialLocalRotation = transform.localRotation;
            initialWorldPosition = transform.position;
            initialWorldRotation = transform.rotation;
            initialScale = transform.localScale;
            initialInstalled = assembled;
            initialPickable = canBePicked;
            initialKinematic = Body.isKinematic;
            initialGravity = Body.useGravity;
            initialInterpolation = Body.interpolation;
            initialCollisionMode = Body.collisionDetectionMode;
            initialDetectCollisions = Body.detectCollisions;
            captured = true;
        }

        public void MarkInstalled()
        {
            if (assembled) return;
            StopMotion();
            assembled = true;
            canBePicked = false;
            Body.isKinematic = true;
            Body.useGravity = false;
            Installed?.Invoke(this);
        }

        public void MarkAsAssembled() => MarkInstalled();

        public void MarkUninstalled()
        {
            assembled = false;
            canBePicked = captured ? initialPickable : true;
            Body.isKinematic = captured && initialKinematic;
            Body.useGravity = !captured || initialGravity;
            if (captured) { Body.collisionDetectionMode = initialCollisionMode; Body.interpolation = initialInterpolation; Body.detectCollisions = initialDetectCollisions; }
            Uninstalled?.Invoke(this);
        }

        public void ResetPart()
        {
            if (!captured) CaptureInitialState();
            StopMotion();
            Uninstalled?.Invoke(this);
            transform.SetParent(originalParent, false);
            if (originalParent != null)
            {
                transform.localPosition = initialLocalPosition;
                transform.localRotation = initialLocalRotation;
            }
            else transform.SetPositionAndRotation(initialWorldPosition, initialWorldRotation);
            transform.localScale = initialScale;
            assembled = initialInstalled;
            canBePicked = initialPickable;
            Body.position = transform.position;
            Body.rotation = transform.rotation;
            Body.isKinematic = initialKinematic;
            Body.useGravity = initialGravity;
            Body.collisionDetectionMode = initialCollisionMode;
            Body.interpolation = initialInterpolation;
            Body.detectCollisions = initialDetectCollisions;
            ResetCompleted?.Invoke(this);
        }

        private void StopMotion()
        {
            if (Body.isKinematic) return;
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
        }
    }
}
