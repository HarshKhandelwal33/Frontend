using System;
using System.Linq;
using UnityEngine;

namespace GearboxDemo
{
    [DisallowMultipleComponent]
    public sealed class RobotGripper : MonoBehaviour
    {
        public enum DebugCommand { None, Open, Close, GrabSelected, GrabNearest, Release }
        public Transform attachmentPoint;
        public Transform leftFinger, rightFinger;
        public Transform robotRoot;
        public AssemblyTarget[] releaseTargets = Array.Empty<AssemblyTarget>();
        [Min(0.001f)] public float captureDistance = 0.08f;
        [Range(0, 180)] public float captureAngle = 30;
        [Min(0.01f)] public float openHalfGap = 0.16f;
        [Min(0)] public float closedHalfGap = 0.018f;
        [Min(0.01f)] public float fingerSpeed = 0.25f;
        public GearboxPartType selectedPart = GearboxPartType.Housing;
        public string LastResult { get; private set; } = "Open; nothing held";
        public IGrippable HeldObject => held == null ? null : held;
        public bool IsOpen { get; private set; } = true;

        private GrippableObject held;
        private GearboxPart heldPart;
        private Rigidbody heldBody;
        private Vector3 gripLocalPosition;
        private Quaternion gripLocalRotation;
        private float fingerTarget;
        private DebugCommand pendingDebugCommand;

        private void Awake() => fingerTarget = openHalfGap;
        public void QueueDebugCommand(DebugCommand command) => pendingDebugCommand = command;
        private void Update()
        {
            var command = pendingDebugCommand; pendingDebugCommand = DebugCommand.None;
            switch (command)
            {
                case DebugCommand.Open: Open(); break;
                case DebugCommand.Close: Close(); break;
                case DebugCommand.GrabSelected: GrabSelected(); break;
                case DebugCommand.GrabNearest: TryGrab(); break;
                case DebugCommand.Release: Release(); break;
            }
        }

        public void Open()
        {
            if (!Application.isPlaying) return;
            Release(); IsOpen = true; fingerTarget = openHalfGap;
        }

        public void Close()
        {
            if (!Application.isPlaying) return;
            IsOpen = false;
            fingerTarget = held == null ? closedHalfGap : RequiredHalfGap(held);
        }

        public bool TryGrab()
        {
            if (!Application.isPlaying || attachmentPoint == null || held != null) return false;
            // Distance ordering plus a stable part ID tie-break; selection debug uses GrabSelected.
            foreach (var candidate in FindObjectsByType<GrippableObject>()
                         .Where(g => g.GripPoint != null && g.CanBeGrabbed())
                         .OrderBy(g => Vector3.SqrMagnitude(g.GripPoint.position - attachmentPoint.position))
                         .ThenBy(g => g.GetComponent<GearboxPart>().partId, StringComparer.Ordinal))
                if (Grab(candidate)) return true;
            LastResult = "No available part GripPoint within capture tolerances.";
            return false;
        }

        public bool Grab(IGrippable target)
        {
            if (!Application.isPlaying || !isActiveAndEnabled) return false;
            if (held != null) { LastResult = "Release the current part first."; return false; }
            if (!(target is GrippableObject candidate) || candidate == null || !Recognizes(candidate))
            { LastResult = "Target is not a configured gearbox grippable."; return false; }
            if (attachmentPoint == null || !candidate.CanBeGrabbed())
            { LastResult = "Missing attachment point or part is unavailable."; return false; }
            if (Vector3.Distance(attachmentPoint.position, candidate.GripPoint.position) > captureDistance ||
                Quaternion.Angle(attachmentPoint.rotation, candidate.GripPoint.rotation) > captureAngle)
            { LastResult = "Move the arm closer and align its GripPoint orientation first."; return false; }

            // Measure offsets from the configured GripPoint; mesh pivots are never grasp targets.
            Transform partRoot = candidate.transform;
            gripLocalPosition = partRoot.InverseTransformPoint(candidate.GripPoint.position);
            gripLocalRotation = Quaternion.Inverse(partRoot.rotation) * candidate.GripPoint.rotation;
            if (RequiredHalfGap(candidate) > openHalfGap)
            { LastResult = "Part is too wide for the configured finger opening."; return false; }
            held = candidate; heldPart = candidate.GetComponent<GearboxPart>(); heldBody = candidate.GetComponent<Rigidbody>();
            candidate.OnGrabbed();
            if (!candidate.IsGrabbed) { ClearAttachment(); return false; }
            heldPart.ResetCompleted += OnPartReset;
            heldPart.Installed += OnPartInstalled;
            AlignHeld(); Close();
            LastResult = "Holding " + heldPart.displayName + " at its configured GripPoint.";
            return true;
        }

        public void Release() => Detach(true);

        private void Detach(bool validatePlacement)
        {
            if (held == null) { ClearAttachment(); return; }
            var released = held;
            var part = heldPart;
            if (heldBody != null && released.IsGrabbed) AlignHeld();
            ClearAttachment();
            released.OnReleased();
            IsOpen = true; fingerTarget = openHalfGap;
            LastResult = "Released " + part.displayName;
            if (!validatePlacement || !Application.isPlaying || part.installed) return;
            foreach (var target in releaseTargets.Where(t => t != null && t.acceptedPart == part.partType)
                         .OrderBy(t => (t.transform.position - part.assemblyAnchor.position).sqrMagnitude))
            {
                if (!target.TryPlace(part)) continue;
                var body = part.GetComponent<Rigidbody>();
                body.position = part.transform.position; body.rotation = part.transform.rotation;
                LastResult = "Placed " + part.displayName + " at " + target.name;
                break;
            }
        }

        private void AlignHeld()
        {
            if (held == null || heldBody == null || attachmentPoint == null) return;
            Quaternion rotation = attachmentPoint.rotation * Quaternion.Inverse(gripLocalRotation);
            Vector3 offset = rotation * Vector3.Scale(gripLocalPosition, held.transform.lossyScale);
            heldBody.rotation = rotation;
            heldBody.position = attachmentPoint.position - offset;
        }

        private void FixedUpdate()
        {
            if (held != null && held.IsGrabbed) AlignHeld();
        }

        private void LateUpdate()
        {
            // Follow the actual solved articulation pose after physics, with no dynamic-joint spring.
            if (held != null)
            {
                if (!held.isActiveAndEnabled || !held.IsGrabbed || attachmentPoint == null) Detach(false);
                else AlignHeld();
            }
            else if (heldBody != null) ClearAttachment();
            AnimateFinger(leftFinger, -fingerTarget);
            AnimateFinger(rightFinger, fingerTarget);
        }

        private void AnimateFinger(Transform finger, float x)
        {
            if (finger == null) return;
            Vector3 position = finger.localPosition;
            position.x = Mathf.MoveTowards(position.x, x, fingerSpeed * Time.deltaTime);
            finger.localPosition = position; // Visual fingers have no ArticulationBody.
        }

        public static bool Recognizes(GrippableObject candidate)
        {
            if (candidate == null) return false;
            var part = candidate.GetComponent<GearboxPart>();
            return part != null && candidate.GripPoint != null && candidate.GripPoint == part.gripPoint &&
                   candidate.GripPoint.IsChildOf(part.transform) && part.assemblyAnchor != null &&
                   candidate.GetComponent<Rigidbody>() != null && candidate.GetComponentsInChildren<Collider>().Any(c => c.enabled && !c.isTrigger);
        }

        public GrippableObject SelectedObject()
        {
            var matches = FindObjectsByType<GrippableObject>().Where(g => g.GetComponent<GearboxPart>().partType == selectedPart).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        public bool ValidateSelectedPart()
        {
            var candidate = SelectedObject();
            bool valid = Recognizes(candidate);
            LastResult = valid ? selectedPart + ": valid grippable; configured GripPoint = " + candidate.GripPoint.name +
                "; available = " + candidate.CanBeGrabbed() : selectedPart + ": missing, ambiguous, or not configured.";
            Debug.Log(LastResult, candidate != null ? candidate.gameObject : gameObject);
            return valid;
        }

        public bool GrabSelected() => Grab(SelectedObject());

        private float RequiredHalfGap(GrippableObject candidate)
        {
            var box = candidate.GetComponent<BoxCollider>();
            if (box == null) return closedHalfGap;
            // Width about the PART grip axis, so changing a part GripPoint also changes grasp width.
            float width = 0;
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = box.transform.TransformPoint(box.center + Vector3.Scale(box.size * 0.5f, new Vector3(x, y, z)));
                width = Mathf.Max(width, Mathf.Abs(Vector3.Dot(corner - candidate.GripPoint.position, candidate.GripPoint.right)));
            }
            return Mathf.Max(closedHalfGap, width + (leftFinger == null ? 0.009f : leftFinger.localScale.x * 0.5f) + 0.003f);
        }

        private void ClearAttachment()
        {
            if (heldPart != null) { heldPart.ResetCompleted -= OnPartReset; heldPart.Installed -= OnPartInstalled; }
            held = null; heldPart = null; heldBody = null;
        }
        private void OnPartReset(GearboxPart part) { ClearAttachment(); IsOpen = true; fingerTarget = openHalfGap; LastResult = "Part reset; attachment cleared."; }
        private void OnPartInstalled(GearboxPart part) { var released = held; ClearAttachment(); released?.OnReleased(); IsOpen = true; fingerTarget = openHalfGap; }
        private void OnDisable() => Detach(false);
        private void OnDrawGizmosSelected()
        {
            if (attachmentPoint == null) return;
            Gizmos.color = Color.cyan; Gizmos.DrawWireSphere(attachmentPoint.position, captureDistance);
            Gizmos.DrawRay(attachmentPoint.position, attachmentPoint.up * 0.15f);
        }
    }
}
