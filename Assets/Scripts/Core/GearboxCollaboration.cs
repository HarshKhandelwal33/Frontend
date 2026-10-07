using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace GearboxDemo
{
    public sealed partial class GearboxGameSimulation
    {
        [HideInInspector] public float appliedRobotBodyScale = 1;
        [HideInInspector] public float appliedTableScale = 1;
        public enum CollaborativePhase { Idle, Approach, Grasp, Lift, Transport, Handover, Receiving, Release, Retreat, Assembly, Insertion, Stopped }
        public enum SeparationMode { SpeedAndSeparation, MonitoredStandstill }
        [Header("Collaborative simulation (not safety rated)")]
        public SeparationMode safetyMode = SeparationMode.SpeedAndSeparation;
        [Tooltip("Optional world gripper target; leave empty to use the existing station-relative target.")]
        public Transform handoverTarget;
        [Tooltip("Optional worker stance. Worker walks here before robot transport.")]
        public Transform receivingStance;
        [Tooltip("Demo tuning in metres, not standards-derived protective distances.")]
        [Min(0.001f)] public float stopSeparation = 0.08f;
        [Min(0.001f)] public float slowSeparation = 0.35f;
        [Min(0.001f)] public float environmentClearance = 0.015f;
        [Tooltip("Conservative radius used around each moving link segment for clearance checks.")]
        [Min(0.001f)] public float linkEnvelopeRadius = 0.045f;
        [Min(0.01f)] public float maxCartesianSpeed = 0.3f;
        [Min(1)] public float maxJointSpeed = 35;
        [Min(1)] public float maxJointAcceleration = 80;
        [Range(0.01f, 1)] public float closeSpeedScale = 0.2f;
        [Min(0), Tooltip("Demo solver allowance only for upward housing/support contact at its seating target; never applies to people.")] public float fixtureContactTolerance = 0.003f;
        [Min(0.01f)] public float graspPositionTolerance = 0.018f;
        [Range(1, 180)] public float graspAngleTolerance = 35;
        [Min(0.01f)] public float graspDwell = 0.4f;
        [Min(0.1f)] public float handoverTimeout = 12;
        [Min(1), Tooltip("Whole behaviour-tree action deadline, including reduced-speed transport and retreat.")]
        public float operationTimeout = 180;
        [Min(0.1f), Tooltip("Horizontal withdrawal along retreatDirection before worker assembly starts.")] public float retreatDistance = 0.6f;
        [Tooltip("World-space horizontal withdrawal direction. Prepared cell retreats away from the worker toward -Z.")] public Vector3 retreatDirection = Vector3.back;
        [Min(0)] public float retreatLift = 0.2f;
        [Tooltip("Simulation-only grasp sensor: confirms after continuous valid pose. Disable to require ConfirmWorkerGrasp event.")]
        public bool simulatedGraspSensor = true;
        [Tooltip("Worker palm offset from the robot grip in grip-oriented world metres. Separates the two grasp locations; tune to each workpiece surface.")]
        public Vector3 workerGraspOffset = new Vector3(0, 0.04f, 0);
        public Vector3 WorkerGraspPosition(GearboxPart part) => part.gripPoint.position + part.gripPoint.rotation * workerGraspOffset;
        public CollaborativePhase Phase { get; private set; }
        public bool ProtectiveStopped { get; private set; }
        public string SafetyReason { get; private set; }
        public float Separation { get; private set; } = float.PositiveInfinity;
        [Min(0.001f), Tooltip("Approximate avatar body proxy radius in metres; demo anthropometry only.")] public float torsoRadius = 0.12f;
        [Min(0.001f)] public float limbRadius = 0.045f;
        [Min(0.001f)] public float receivingHandRadius = 0.035f;
        [Tooltip("Prepared CAD lid edge grip in part-local metres, keeping the wrist clear of the protruding shaft.")] public Vector3 lidGraspPosition = new Vector3(0.105f, 0.075f, 0);
        public Vector3 lidGraspEuler = new Vector3(180, 90, 0);
        [Min(0.001f), Tooltip("Finger-centre half gap at this lid chord; includes finger thickness and model clearance.")] public float lidGraspHalfGap = 0.07f;
        private WorkerReceivingRig receivingRig;
        private Collider[] robotSafetyColliders;
        private BoxCollider[] fingerProxies;
        private Vector3[] bodyPoints;
        private bool closeInteraction;
        private bool hasActiveHandoverTarget;
        private Vector3 activeHandoverPosition;
        private Quaternion activeHandoverRotation;
        private float motionScale = 1;
        private string nearestRegion;
        private void RecordSeparation(float distance, string region)
        { if (distance < Separation) { Separation = distance; nearestRegion = region; } }
        private readonly System.Collections.Generic.List<Vector3> trajectory = new System.Collections.Generic.List<Vector3>();

        private void InitializeCollaboration()
        {
            receivingRig = animator.GetComponent<WorkerReceivingRig>();
            if (receivingRig == null) receivingRig = animator.gameObject.AddComponent<WorkerReceivingRig>();
            receivingRig.Initialize(animator, hand);
            robotSafetyColliders = robot.Root.GetComponentsInChildren<Collider>().Where(c => !c.isTrigger).ToArray();
            fingerProxies = new BoxCollider[2];
            var fingers = new[] { robot.LeftFinger, robot.RightFinger };
            for (int i = 0; i < 2; i++)
            {
                var proxy = new GameObject("Gripper finger clearance proxy " + i);
                proxy.transform.SetParent(transform, true);
                fingerProxies[i] = proxy.AddComponent<BoxCollider>(); fingerProxies[i].isTrigger = true;
            }
            robot.ExtraColliders = fingerProxies; robot.ExtraSources = fingers;
            UpdateFingerProxies();
            robotSafetyColliders = robotSafetyColliders.Concat(fingerProxies).ToArray();
            bodyPoints = new Vector3[15];
            foreach (var joint in m.handover.robotArm.joints)
                joint.Body.maxJointVelocity = maxJointSpeed * Mathf.Deg2Rad;
        }
        private void UpdateFingerProxies()
        {
            if (fingerProxies == null) return;
            var fingers = new[] { robot.LeftFinger, robot.RightFinger };
            for (int i = 0; i < 2; i++)
            {
                fingerProxies[i].transform.SetPositionAndRotation(fingers[i].position, fingers[i].rotation);
                Vector3 scale = fingers[i].lossyScale;
                fingerProxies[i].size = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            }
            Physics.SyncTransforms();
        }
        public void ConfirmWorkerGrasp() => receivingRig?.ConfirmGrasp();
        public void CancelOperation() => ProtectiveStop("Operation cancelled; payload retained; explicit reset required");
        public void ProtectiveStop(string reason)
        {
            if (EmergencyStopped || ProtectiveStopped) return;
            ProtectiveStopped = true; SafetyReason = reason; Phase = CollaborativePhase.Stopped;
            Running = false; Paused = true; singleStep = false; StopAllCoroutines();
            m.handover.robotArm.HoldPosition();
            worker.GetComponent<WorkerMovement>()?.Stop();
            Time.timeScale = 0; Fault = "Protective stop: " + reason;
        }
        private string ReceivingDiagnostics(GearboxPart part) =>
            " gap=" + Vector3.Distance(hand.position, WorkerGraspPosition(part)).ToString("F4") +
            " angle=" + Quaternion.Angle(hand.rotation, part.gripPoint.rotation).ToString("F1") +
            " reachable=" + receivingRig.CanReach(WorkerGraspPosition(part)) +
            " jointVelocity=" + m.handover.robotArm.joints.Max(j => Mathf.Abs(j.Body.jointVelocity[0])).ToString("F4") +
            " contact=" + part.GetComponentsInChildren<Collider>().Min(c => Vector3.Distance(c.ClosestPoint(hand.position), hand.position)).ToString("F4");
        private bool ValidReceivingPose(GearboxPart part)
        {
            return receivingRig.TrackingValid && receivingRig.ReadyToReceive && receivingRig.CanReach(WorkerGraspPosition(part)) &&
                Vector3.Distance(hand.position, WorkerGraspPosition(part)) <= graspPositionTolerance &&
                Quaternion.Angle(hand.rotation, part.gripPoint.rotation) <= graspAngleTolerance &&
                m.handover.robotArm.joints.All(j => Mathf.Abs(j.Body.jointVelocity[0]) < 0.02f) &&
                part.GetComponentsInChildren<Collider>().Any(c => c.enabled && !c.isTrigger && Vector3.Distance(c.ClosestPoint(hand.position), hand.position) <= graspPositionTolerance) &&
                part.transform.parent == robot.Grip && Vector3.Distance(robot.Grip.position, part.gripPoint.position) <= graspPositionTolerance && Holder == robot.Grip && Payload == part.transform;
        }
        private static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
            !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
        private void CheckTracking()
        {
            if (!receivingRig.TrackingValid || !worker.gameObject.activeInHierarchy ||
                !Finite(hand.position) || !Finite(worker.position) || !Finite(robot.Grip.position))
                throw new InvalidOperationException("Worker absent or invalid avatar tracking");
        }
        private void EvaluateSafety()
        {
            CheckTracking(); UpdateFingerProxies();
            if (stopSeparation <= 0 || slowSeparation <= stopSeparation || maxJointSpeed <= 0 || maxJointAcceleration <= 0)
                throw new InvalidOperationException("Invalid safeguard configuration");
            var regions = new[] { HumanBodyBones.Hips, HumanBodyBones.Chest, HumanBodyBones.Head, HumanBodyBones.LeftHand, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
                HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
                HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };
            Separation = float.PositiveInfinity;
            for (int i = 0; i < regions.Length; i++)
            {
                var bone = animator.GetBoneTransform(regions[i]);
                if (bone == null) throw new InvalidOperationException("Missing worker body region " + regions[i]);
                bodyPoints[i] = bone.position;
                foreach (var c in robotSafetyColliders.Where(c => c != null && c.enabled))
                    RecordSeparation(Vector3.Distance(c.ClosestPoint(bone.position), bone.position) - (i < 3 ? torsoRadius : limbRadius), regions[i] + " / " + c.name);
                if (Payload != null && Holder == robot.Grip)
                    foreach (var c in Payload.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger))
                        RecordSeparation(Vector3.Distance(c.ClosestPoint(bone.position), bone.position) - (i < 3 ? torsoRadius : limbRadius), regions[i] + " / " + c.name);
            }
            Vector3 receivingHand = animator.GetBoneTransform(HumanBodyBones.RightHand).position;
            foreach (var c in robotSafetyColliders.Where(c => c != null && c.enabled))
            {
                bool intentional = closeInteraction && receivingRig.ReadyToReceive && receivingRig.Reaching &&
                    (c.transform.IsChildOf(m.handover.robotGripper.transform) || fingerProxies.Contains(c));
                if (!intentional)
                    RecordSeparation(Vector3.Distance(c.ClosestPoint(receivingHand), receivingHand) - receivingHandRadius, "RightHand / " + c.name + " body=" + (c.attachedArticulationBody != null ? c.attachedArticulationBody.name : "proxy/part"));
            }
            if (!(closeInteraction && receivingRig.ReadyToReceive && receivingRig.Reaching) && Payload != null && Holder == robot.Grip)
                foreach (var c in Payload.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger))
                    RecordSeparation(Vector3.Distance(c.ClosestPoint(receivingHand), receivingHand) - receivingHandRadius, "RightHand / " + c.name + " body=" + (c.attachedArticulationBody != null ? c.attachedArticulationBody.name : "proxy/part"));
            if (Separation < stopSeparation) throw new InvalidOperationException("Worker–robot clearance violated: " + Separation.ToString("F3") + " (" + nearestRegion + ") grip=" + robot.Grip.position + " hand=" + hand.position + " phase=" + Phase);
            motionScale = closeInteraction || Separation < slowSeparation ? closeSpeedScale : 1;
            if (safetyMode == SeparationMode.MonitoredStandstill && Separation < slowSeparation && Phase != CollaborativePhase.Receiving && Phase != CollaborativePhase.Release)
                throw new InvalidOperationException("Worker entered monitored standstill zone");
        }
        private void CheckPredictedCollider(Collider collider, Vector3 position, Quaternion rotation)
        {
            if (collider.attachedArticulationBody != null && collider.attachedArticulationBody.isRoot) return;
            // Query a conservative world sphere, then narrow-phase using the actual collider shape at the planned pose.
            float radius = collider.bounds.extents.magnitude + environmentClearance;
            Vector3 localCenter = collider.transform.InverseTransformPoint(collider.bounds.center);
            Vector3 center = position + rotation * Vector3.Scale(localCenter, collider.transform.lossyScale);
            foreach (var other in Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Ignore).Concat(m.allParts.SelectMany(p => p.GetComponentsInChildren<Collider>())).Distinct().Where(c => c != null && c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger))
            {
                if (other.transform.IsChildOf(robot.Root) || (Payload != null && other.transform.IsChildOf(Payload))) continue;
                if (other.transform.IsChildOf(worker)) continue; // Body-region separation is checked every movement step.
                if (Phase == CollaborativePhase.Grasp && other.GetComponentInParent<GearboxPart>() == ActivePart) continue;
                if (Physics.ComputePenetration(collider, position, rotation, other, other.transform.position, other.transform.rotation, out Vector3 direction, out float depth) && depth > 0.001f && !IntendedFixtureContact(collider, position, rotation, other, direction, depth) && !IntendedInsertionContact(collider, position, rotation, other))
                    throw new InvalidOperationException("Planned trajectory blocked: " + collider.name + " / " + other.name + " depth=" + depth.ToString("F4") + " predicted=" + position + " obstacle=" + other.bounds + " parent=" + (other.transform.parent != null ? other.transform.parent.name : "root"));
            }
        }
        private bool IntendedInsertionContact(Collider moving, Vector3 position, Quaternion rotation, Collider other)
        {
            // Only named assembly mates during an aligned insertion stroke; never exempts the tool or people.
            if (Phase != CollaborativePhase.Insertion || Holder != robot.Grip || Payload == null || !moving.transform.IsChildOf(Payload)) return false;
            var mate = other.GetComponentInParent<GearboxPart>();
            Vector3 target; float stroke;
            if (Payload == m.phaseOne.InternalAssemblyRoot && mate == m.phaseTwoInsertion.housing) { target = finalOrigin; stroke = 0.25f; }
            else if (Payload == m.lidInstallation.lid.transform && (mate == m.phaseTwoInsertion.housing || (mate != null && mate.assembled && mate.transform.IsChildOf(m.phaseOne.InternalAssemblyRoot))))
            { target = m.lidInstallation.lid.assemblyTarget.transform.position; stroke = 0.18f; }
            else return false;
            Quaternion rootRotation = rotation * Quaternion.Inverse(Quaternion.Inverse(Payload.rotation) * moving.transform.rotation);
            Vector3 rootPosition = position - rootRotation * Vector3.Scale(Payload.InverseTransformPoint(moving.transform.position), Payload.lossyScale);
            Vector3 offset = rootPosition - target;
            return new Vector2(offset.x, offset.z).magnitude <= graspPositionTolerance && offset.y >= -fixtureContactTolerance &&
                offset.y <= stroke + graspPositionTolerance && Quaternion.Angle(rootRotation, Quaternion.identity) < 3;
        }

        private bool IntendedFixtureContact(Collider moving, Vector3 position, Quaternion rotation, Collider support, Vector3 direction, float depth)
        {
            var housing = m.phaseTwoInsertion.housing;
            return moving.transform == housing.transform && support.transform.parent != null &&
                support.transform.parent.name == "Reference Fixture Two" && depth <= fixtureContactTolerance &&
                Vector3.Dot(direction, Vector3.up) > 0.95f &&
                Vector3.Distance(position, housing.assemblyTarget.transform.position) <= graspPositionTolerance &&
                Quaternion.Angle(rotation, housing.assemblyTarget.transform.rotation) < 3;
        }
        private void RefreshHousingCollision()
        {
            var housing = m.phaseTwoInsertion.housing;
            housing.GetComponent<BoxCollider>().enabled = !housing.assembled;
            foreach (var mesh in housing.transform.Find("Visual").GetComponentsInChildren<MeshFilter>())
            {
                var collider = mesh.GetComponent<MeshCollider>();
                if (collider == null) { collider = mesh.gameObject.AddComponent<MeshCollider>(); collider.sharedMesh = mesh.sharedMesh; collider.convex = false; }
                collider.enabled = housing.assembled;
            }
            foreach (var part in m.allParts.Where(p => IsInternal(p)))
            {
                part.GetComponent<BoxCollider>().enabled = !part.assembled;
                foreach (var mesh in part.transform.Find("Visual").GetComponentsInChildren<MeshFilter>())
                {
                    var collider = mesh.GetComponent<MeshCollider>();
                    if (collider == null) { collider = mesh.gameObject.AddComponent<MeshCollider>(); collider.sharedMesh = mesh.sharedMesh; collider.convex = true; }
                    collider.enabled = part.assembled;
                }
            }
            Physics.SyncTransforms();
        }
        private void CheckEnvironment()
        {
            UpdateFingerProxies();
            Physics.SyncTransforms();
            Vector3[] positions = robot.JointPositions;
            for (int i = 2; i < positions.Length; i++)
            {
                foreach (var c in Physics.OverlapCapsule(positions[i - 1], positions[i], linkEnvelopeRadius + environmentClearance, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (c.transform.IsChildOf(robot.Root) || c.transform.IsChildOf(worker) || (Payload != null && c.transform.IsChildOf(Payload))) continue;
                    if (c.GetComponentInParent<GearboxPart>() != null) continue; // Part clearance checked separately below.
                    throw new InvalidOperationException("Robot link clearance hazard: " + c.name);
                }
            }
            // All robot, finger and carried-part collider envelopes are tested against equipment and other parts.
            var moving = robotSafetyColliders.AsEnumerable();
            if (Payload != null && Holder == robot.Grip) moving = moving.Concat(Payload.GetComponentsInChildren<Collider>());
            foreach (var c in moving.Where(c => c != null && c.enabled))
            {
                foreach (var other in Physics.OverlapBox(c.bounds.center, c.bounds.extents + Vector3.one * environmentClearance, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Concat(m.allParts.SelectMany(p => p.GetComponentsInChildren<Collider>())).Distinct().Where(c => c != null && c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger))
                {
                    if (other == c || other.transform.IsChildOf(robot.Root) || other.transform.IsChildOf(worker) ||
                        (Payload != null && other.transform.IsChildOf(Payload))) continue;
                    // Only the active pickup part may intentionally contact the tool during grasp.
                    if (Phase == CollaborativePhase.Grasp && other.GetComponentInParent<GearboxPart>() == ActivePart) continue;
                    if (c.attachedArticulationBody != null && c.attachedArticulationBody.isRoot) continue;
                    if (Physics.ComputePenetration(c, c.transform.position, c.transform.rotation, other, other.transform.position, other.transform.rotation, out Vector3 direction, out float depth) && depth > 0.001f && !IntendedFixtureContact(c, c.transform.position, c.transform.rotation, other, direction, depth) && !IntendedInsertionContact(c, c.transform.position, c.transform.rotation, other))
                        throw new InvalidOperationException("Collision hazard: " + c.name + " / " + other.name + " depth=" + depth.ToString("F4") + " predicted=" + c.transform.position + " obstacle=" + other.bounds + " parent=" + (other.transform.parent != null ? other.transform.parent.name : "root"));
                }
            }
        }
        private void OnDisable()
        {
            if (Application.isPlaying && Ready && Running && !EmergencyStopped && !ProtectiveStopped)
                ProtectiveStop("Simulation interrupted or disabled");
        }
        private void OnDrawGizmosSelected()
        {
            if (handoverTarget != null) { Gizmos.color = Color.cyan; Gizmos.DrawWireSphere(handoverTarget.position, graspPositionTolerance); }
            if (hasActiveHandoverTarget)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(activeHandoverPosition, graspPositionTolerance);
                Gizmos.DrawRay(activeHandoverPosition, activeHandoverRotation * Vector3.up * 0.15f);
            }
            if (robot == null) return;
            Gizmos.color = ProtectiveStopped ? Color.red : Color.yellow;
            foreach (var point in robot.JointPositions) Gizmos.DrawWireSphere(point, slowSeparation);
            Gizmos.color = Color.red;
            foreach (var point in robot.JointPositions) Gizmos.DrawWireSphere(point, stopSeparation);
            Gizmos.color = Color.cyan;
            for (int i = 1; i < trajectory.Count; i++) Gizmos.DrawLine(trajectory[i - 1], trajectory[i]);
        }
    }
}




