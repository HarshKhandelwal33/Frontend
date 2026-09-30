using System;
using UnityEngine;

namespace GearboxDemo
{
    [DisallowMultipleComponent]
    public sealed class AssemblyTarget : MonoBehaviour
    {
        public GearboxPartType acceptedPart;
        [Min(0)] public float positionTolerance = 0.02f;
        [Range(0, 180)] public float rotationTolerance = 15f;
        public bool snapOnValidPlacement = true;
        [SerializeField] private GearboxPart occupant;
        public bool occupied => occupant != null;
        public GearboxPart Occupant => occupant;
        public event Action<AssemblyTarget, GearboxPart> PartInstalled;

        // Explicit opt-in API only: no automatic proximity polling or assembly order.
        public bool TryPlace(GearboxPart part)
        {
            if (occupied || part == null || part.installed || part.partType != acceptedPart || part.assemblyAnchor == null ||
                (part.assemblyTarget != null && part.assemblyTarget != this))
                return false;
            if (Vector3.Distance(part.assemblyAnchor.position, transform.position) > positionTolerance ||
                Quaternion.Angle(part.assemblyAnchor.rotation, transform.rotation) > rotationTolerance)
                return false;
            if (snapOnValidPlacement)
            {
                Quaternion delta = transform.rotation * Quaternion.Inverse(part.assemblyAnchor.rotation);
                part.transform.rotation = delta * part.transform.rotation;
                part.transform.position += transform.position - part.assemblyAnchor.position;
            }
            occupant = part;
            part.Uninstalled += ReleaseOccupancy;
            part.MarkAsAssembled();
            part.GetComponent<GrippableObject>()?.OnReleased();
            PartInstalled?.Invoke(this, part);
            return true;
        }

        public void ClearOccupancy()
        {
            if (occupant != null) occupant.Uninstalled -= ReleaseOccupancy;
            occupant = null;
        }

        private void ReleaseOccupancy(GearboxPart part)
        {
            if (occupant != part) return;
            part.Uninstalled -= ReleaseOccupancy;
            occupant = null;
        }

        private void OnDestroy()
        {
            if (occupant != null) occupant.Uninstalled -= ReleaseOccupancy;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = occupied ? Color.green : Color.cyan;
            Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.008f, positionTolerance));
            Gizmos.DrawRay(transform.position, transform.up * 0.08f);
        }
    }
}
