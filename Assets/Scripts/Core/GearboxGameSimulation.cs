using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GearboxDemo
{
    [DefaultExecutionOrder(200)]
    public sealed partial class GearboxGameSimulation : MonoBehaviour
    {
        public AssemblySequence Tree { get; private set; }
        public string ActionName { get; private set; } = "Preparing workcell";
        public string Fault { get; private set; }
        public int Completed => Tree == null ? 0 : Tree.Index;
        public bool Running { get; private set; }
        public bool Paused { get; private set; }
        public bool Ready { get; private set; }
        public bool EmergencyStopped { get; private set; }
        public bool TrainingMode { get; set; }
        public void CycleCamera() { cameraMode = (cameraMode + 1) % 3; }
        private readonly Dictionary<int, Checkpoint> checkpoints = new Dictionary<int, Checkpoint>();
        public Transform Holder { get; private set; }
        public Transform Payload { get; private set; }
        public GearboxPart ActivePart { get; private set; }
        public float MaxRobotError { get; private set; }
        public float MaxHandError { get; private set; }
        public int HandoverCount { get; private set; }
        public GearboxAssemblyManager References => m;
        public GameRobotIK Robot => robot;
        public Vector3 FirstOrigin => firstOrigin;
        public Vector3 FinalOrigin => finalOrigin;

        private GearboxAssemblyManager m;
        private GameRobotIK robot;
        private Transform worker, hand, upperArm, lowerArm, handBone;
        private Animator animator;
        private Transform rack;
        private readonly Dictionary<GearboxPart, Pose> starts = new Dictionary<GearboxPart, Pose>();
        private Vector3 firstOrigin, finalOrigin;
        private readonly Quaternion down = Quaternion.Euler(180, 0, 0);
        private Transform[] bones;
        private Quaternion[] boneRest;
        private Vector3 carryOffset;
        private Quaternion carryRotation;
        private bool singleStep;
        private int stopAt;
        private Camera cameraView;
        private int cameraMode = 1;
        private Vector3 lookAt;
        private float speed = 1;
        private GUIStyle title, small, nodeText;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var manager = FindFirstObjectByType<GearboxAssemblyManager>();
            if (manager != null && manager.GetComponent<GearboxGameSimulation>() == null)
                manager.gameObject.AddComponent<GearboxGameSimulation>();
        }

        private void Start()
        {
            m = GetComponent<GearboxAssemblyManager>();
            // One owner for the simulation: old coroutines, keyboard shortcuts and HUDs cannot race the tree.
            foreach (var behaviour in m.GetComponents<MonoBehaviour>()) if (behaviour != this) behaviour.enabled = false;
            m.handover.enabled = false;
            if (m.robotPoses != null) m.robotPoses.enabled = false;
            foreach (var debug in m.handover.robotArm.GetComponentsInChildren<RobotDebugController>()) debug.enabled = false;
            worker = m.phaseTwoInsertion.worker;
            animator = m.handover.workerAnimator;
            worker.GetComponent<WorkerMovement>().enabled = false;
            animator.enabled = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            hand = m.handover.workerRightHandHoldPoint;
            upperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            lowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            handBone = animator.GetBoneTransform(HumanBodyBones.RightHand);
            // Imported humanoid bones may be scaled by 100. Configure the palm offset in world metres.
            hand.SetParent(handBone, false);
            hand.position = handBone.position + handBone.right * 0.025f;
            hand.localRotation = Quaternion.identity;
            bones = animator.GetComponentsInChildren<Transform>();
            boneRest = bones.Select(b => b.localRotation).ToArray();
            robot = new GameRobotIK(m.handover.robotArm, m.handover.robotGripper);
            m.handover.robotGripper.enabled = false; // Tree owns attachment and fingers.
            InitializeCollaboration();
            rack = new GameObject("Loose parts • start rack").transform;
            rack.SetParent(transform, true);
            cameraView = Camera.main;
            cameraView.nearClipPlane = 0.025f;
            firstOrigin = new Vector3(1.03f, 1.105f, 0.67f);
            finalOrigin = new Vector3(1.48f, 1.105f, 0.67f);
            ConfigureLayout();
            ResetGame();
            Ready = true;
            // Entering Play Mode demonstrates the process instead of leaving an unexplained static scene.
            TrainingMode = !Application.isBatchMode && !Environment.GetCommandLineArgs().Contains("--gearbox-demo");
            if (TrainingMode)
            {
                cameraMode = 0;
                new GameObject("Voice training interface").AddComponent<GearboxTrainingClient>();
            }
            else Play();
        }

        private void ConfigureLayout()
        {
            var station = m.phaseOne.InternalAssemblyRoot.parent;
            m.phaseOne.InternalAssemblyRoot.position = firstOrigin;
            m.phaseTwoInsertion.internalAssemblyTarget.position = finalOrigin;
            string[] fixtures = { "Reference Fixture One", "Reference Fixture Two" };
            for (int i = 0; i < 2; i++)
            {
                var fixture = station.Find(fixtures[i]);
                if (fixture != null) fixture.position = i == 0 ? firstOrigin : finalOrigin;
            }
            var parts = m.allParts.OrderBy(p => p.partType).ThenBy(p => p.partId).ToArray();
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i];
                p.transform.SetParent(rack, true);
                starts[p] = new Pose(new Vector3(-2.28f + i % 4 * 0.32f, 1.052f, -0.015f + i / 4 * 0.3f), Quaternion.identity);
                // Place the actual collider underside above the tray; CAD root pivots are not support points.
                p.transform.SetPositionAndRotation(starts[p].position, starts[p].rotation);
                Physics.SyncTransforms();
                var trayBase = GameObject.Find("Tray Base").GetComponent<Collider>();
                float bottom = p.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).Min(c => c.bounds.min.y);
                Vector3 supported = starts[p].position;
                supported.y += Mathf.Max(0, trayBase.bounds.max.y + environmentClearance - bottom);
                starts[p] = new Pose(supported, starts[p].rotation);
                int planet = Array.IndexOf(m.planetGearStep.planetGears, p);
                int screw = Array.IndexOf(m.screwInstallation.screws, p);
                Quaternion rotation = Quaternion.Euler(0, planet >= 0 ? planet * 120 : screw >= 0 ? screw * 90 : 0, 0);
                bool internalPart = IsInternal(p);
                p.assemblyTarget.transform.SetPositionAndRotation((internalPart ? firstOrigin : finalOrigin) - rotation * p.transform.Find("Visual").localPosition, rotation);
                var body = p.GetComponent<Rigidbody>();
                body.isKinematic = true; body.useGravity = false; body.interpolation = RigidbodyInterpolation.None;
                body.detectCollisions = false;
            }
            m.lidInstallation.lid.gripPoint.localPosition = lidGraspPosition;
            m.lidInstallation.lid.gripPoint.localRotation = Quaternion.Euler(lidGraspEuler);
            // Support the carrier's actual underside, which sits above the CAD assembly origin.
            var oldFixture = station.Find("Reference Fixture One");
            if (oldFixture != null) oldFixture.gameObject.SetActive(false);
            float supportTop = m.phaseOne.pinCarrierTarget.transform.position.y;
            for (int i = 0; i < 3; i++)
            {
                float angle = (30f + i * 120f) * Mathf.Deg2Rad;
                var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                post.name = "Carrier fixture contact " + (i + 1);
                post.transform.SetParent(transform,true);
                post.transform.position = new Vector3(firstOrigin.x + Mathf.Cos(angle)*0.07f, (supportTop+1.03f)*0.5f, firstOrigin.z + Mathf.Sin(angle)*0.07f);
                post.transform.localScale = new Vector3(0.014f,(supportTop-1.03f)*0.5f,0.014f);
                post.GetComponent<Renderer>().material.color = new Color(0.1f,0.26f,0.3f);
            }
            // Fixture supports must meet the actual housing underside, not the CAD assembly origin.
            // The original support tops intersected the housing during its seating trajectory.
            var housing = m.phaseTwoInsertion.housing;
            housing.transform.SetPositionAndRotation(housing.assemblyTarget.transform.position, housing.assemblyTarget.transform.rotation);
            Physics.SyncTransforms();
            float housingBottom = housing.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).Min(c => c.bounds.min.y);
            Vector3 housingCenter = housing.GetComponent<Collider>().bounds.center;
            float benchTop = FindObjectsByType<Collider>().Where(c => c.name == "Tabletop" &&
                c.bounds.min.x <= housingCenter.x && c.bounds.max.x >= housingCenter.x &&
                c.bounds.min.z <= housingCenter.z && c.bounds.max.z >= housingCenter.z).Max(c => c.bounds.max.y);
            if (housingBottom <= benchTop) throw new InvalidOperationException("Housing assembly target intersects the tabletop");
            Transform housingFixture = station.Find("Reference Fixture Two");
            if (housingFixture != null)
                foreach (Transform support in housingFixture)
                {
                    float height = housingBottom - benchTop;
                    Vector3 scale = support.localScale; scale.y = height / housingFixture.lossyScale.y; support.localScale = scale;
                    Vector3 position = support.position; position.y = benchTop + height * 0.5f; support.position = position;
                }
            housing.transform.SetPositionAndRotation(starts[housing].position, starts[housing].rotation);
            // A restrained, readable workbench surface rather than the old luminous green mat.
            var mat = GameObject.Find("Assembly Mat");
            if (mat != null)
            {
                FitAssemblyMat(mat.transform);
                mat.GetComponent<Renderer>().material.color = new Color(0.12f, 0.2f, 0.23f);
            }
        }

        public static void FitAssemblyMat(Transform mat)
        {
            // Cover the tested station footprints at Z=0.67 while staying inside the worktop.
            // Preserve the support surface height and all robot/worker assembly targets.
            Vector3 position = mat.localPosition; position.z = 0.1f; mat.localPosition = position;
            Vector3 scale = mat.localScale; scale.z = 0.96f; mat.localScale = scale;
        }

        public void ResetGame()
        {
            StopAllCoroutines(); Time.timeScale = 1; speed = 1;
            EmergencyStopped = false; ProtectiveStopped = false; SafetyReason = null;
            Phase = CollaborativePhase.Idle; closeInteraction = false; hasActiveHandoverTarget = false; trajectory.Clear();
            receivingRig.Rest();
            var workerMovement = worker.GetComponent<WorkerMovement>();
            workerMovement.ResetMotion(); workerMovement.enabled = false;
            Payload = null; Holder = null; ActivePart = null; Fault = null; Running = false; Paused = false;
            HandoverCount = 0; MaxHandError = 0; MaxRobotError = 0;
            m.phaseOne.InternalAssemblyRoot.SetParent(transform, true);
            m.phaseOne.InternalAssemblyRoot.SetPositionAndRotation(firstOrigin, Quaternion.identity);
            foreach (var target in m.allTargets) target.ClearOccupancy();
            foreach (var p in m.allParts)
            {
                p.transform.SetParent(rack, true); p.transform.SetPositionAndRotation(starts[p].position, starts[p].rotation);
                p.assembled = false; p.canBePicked = true;
                var body = p.GetComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                body.interpolation = RigidbodyInterpolation.None; body.detectCollisions = false;
                Sync(p.transform);
            }
            RefreshHousingCollision();
            for (int i = 0; i < bones.Length; i++) bones[i].localRotation = boneRest[i];
            worker.SetPositionAndRotation(new Vector3(firstOrigin.x + 0.25f, 0, 1.16f), Quaternion.Euler(0,180,0));
            robot.Reset(); SetFingers(m.handover.robotGripper.openHalfGap);
            BuildTree(); ActionName = "Ready • all parts on rack";
            checkpoints.Clear();
            SaveCheckpoint();
        }

        private static bool IsInternal(GearboxPart p) => p.partType != GearboxPartType.Housing && p.partType != GearboxPartType.Lid && p.partType != GearboxPartType.Screw;
        private AssemblyAction Do(string name, Func<IEnumerator> routine) => new AssemblyAction(name, this, routine, text => { ActionName = text; if (text.StartsWith("Stopped:")) ProtectiveStop(text); }, operationTimeout);
        private void BuildTree()
        {
            Tree = new AssemblySequence("Gearbox assembly");
            var internalParts = new[] { m.phaseOne.pinCarrier, m.phaseOne.spurGear, m.planetGearStep.planetGears[0], m.planetGearStep.planetGears[1], m.planetGearStep.planetGears[2], m.outputShaftStep.outputShaft };
            foreach (var p in internalParts) Tree.Children.Add(WorkerOperation(p));
            Tree.Children.Add(RobotOperation(m.phaseTwoInsertion.housing));
            Tree.Children.Add(new AssemblySequence("Insert internal assembly",
                new AssemblyCondition("Housing and internals ready", () => m.phaseTwoInsertion.housing.assembled && internalParts.All(p => p.assembled)),
                Do("Robot • grip internal assembly", PickInternal),
                Do("Robot • align and insert in housing", InsertInternal)));
            Tree.Children.Add(RobotOperation(m.lidInstallation.lid));
            foreach (int i in new[] { 0, 2, 1, 3 }) Tree.Children.Add(WorkerOperation(m.screwInstallation.screws[i]));
        }
        private AssemblySequence WorkerOperation(GearboxPart p) => new AssemblySequence(p.displayName + " • worker",
            new AssemblyCondition("Part available / fixture clear", () => !p.assembled && !p.assemblyTarget.occupied && Payload == null),
            Do("Robot • approach and pick " + p.displayName, () => Pick(p)),
            Do("Handover • " + p.displayName, () => Transfer(p)),
            Do("Worker • " + (p.partType == GearboxPartType.Screw ? "thread and tighten " : "align and mount ") + p.displayName, () => WorkerPlace(p)));
        private AssemblySequence RobotOperation(GearboxPart p) => new AssemblySequence(p.displayName + " • robot",
            new AssemblyCondition("Part available", () => !p.assembled && Payload == null),
            Do("Robot • approach and pick " + p.displayName, () => Pick(p)),
            Do("Robot • align and seat " + p.displayName, () => RobotPlace(p)));

        public void EmergencyStop()
        {
            EmergencyStopped = true; Phase = CollaborativePhase.Stopped;
            m.handover.robotArm.HoldPosition();
            Running = false; Paused = true; singleStep = false;
            StopAllCoroutines(); Time.timeScale = 0;
            Fault = "Emergency stop • reset required";
        }
        public void Play() { if (EmergencyStopped || ProtectiveStopped || Fault != null || (!Ready && Tree == null)) return; singleStep = false; Paused = false; Running = true; Time.timeScale = speed; }
        public void Pause() { Paused = true; Time.timeScale = 0; }
        public void Step() { if (EmergencyStopped || ProtectiveStopped || Fault != null || Tree == null) return; singleStep = true; stopAt = Completed + 1; Paused = false; Running = true; Time.timeScale = speed; }
        private void Update()
        {
            if (!Ready) return;
            var keys = Keyboard.current;
            if (keys != null && !TrainingMode)
            {
                if (keys.escapeKey.wasPressedThisFrame) EmergencyStop();
                if (keys.spaceKey.wasPressedThisFrame) { if (Running && !Paused) Pause(); else Play(); }
                if (keys.rKey.wasPressedThisFrame) ResetGame();
                if (keys.cKey.wasPressedThisFrame) cameraMode = (cameraMode + 1) % 3;
            }
            var mouse = Mouse.current;
            if (!TrainingMode && mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                Vector2 p = mouse.position.ReadValue(); p.y = Screen.height - p.y;
                float y = Screen.height - 62;
                if (new Rect(24,y,110,36).Contains(p)) Play();
                else if (new Rect(144,y,90,36).Contains(p)) Pause();
                else if (new Rect(244,y,90,36).Contains(p)) Step();
                else if (new Rect(344,y,90,36).Contains(p)) ResetGame();
                else if (new Rect(444,y,130,36).Contains(p)) cameraMode = (cameraMode + 1) % 3;
                else if (new Rect(584,y,90,36).Contains(p)) { speed = speed == 1 ? 0.5f : speed == 0.5f ? 2 : 1; if (!Paused) Time.timeScale = speed; }
                else if (new Rect(694,y,140,36).Contains(p)) EmergencyStop();
            }
            if (!Running || Paused) return;
            try { EvaluateSafety(); } catch (Exception e) { ProtectiveStop(e.Message); return; }
            int before = Completed;
            var result = Tree.Tick();
            if (Completed != before) SaveCheckpoint();
            if (result == BehaviourStatus.Failure) ProtectiveStop(Fault ?? "Assembly prerequisite failed; reset required");
            if (result == BehaviourStatus.Success) { Running = false; ActionName = "Assembly complete • 12 parts installed"; }
            if (singleStep && Completed >= stopAt) { Running = false; singleStep = false; }
        }

        private IEnumerator MoveRobot(Vector3 position, Quaternion rotation, float seconds)
        {
            CheckTracking(); EvaluateSafety(); CheckEnvironment();
            float[] from = m.handover.robotArm.joints.Select(j => j.Angle).ToArray();
            Vector3 startPosition = robot.Grip.position; Quaternion startRotation = robot.Grip.rotation;
            int samples = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(startPosition, position) / 0.04f), Mathf.CeilToInt(Quaternion.Angle(startRotation, rotation) / 8));
            var path = new System.Collections.Generic.List<float[]> { from };
            for (int sample = 1; sample <= samples; sample++)
            {
                float fraction = (float)sample / samples;
                float[] next = robot.Plan(Vector3.Lerp(startPosition, position, fraction), Quaternion.Slerp(startRotation, rotation, fraction), path[path.Count - 1]);
                robot.ValidateTrajectory(path[path.Count - 1], next, CheckPredictedCollider, Holder == robot.Grip ? Payload : null);
                path.Add(next);
            }
            float[] goal = path[path.Count - 1];
            float[] frame = new float[6];
            float duration = Mathf.Max(seconds, 0.1f, Vector3.Distance(startPosition, position) * 1.875f / maxCartesianSpeed);
            for (int j = 0; j < 6; j++)
            {
                float distance = Mathf.Abs(goal[j] - from[j]);
                duration = Mathf.Max(duration, distance * 1.875f / maxJointSpeed,
                    Mathf.Sqrt(distance * 5.774f / maxJointAcceleration));
            }
            float deadline = Time.time + duration / closeSpeedScale + handoverTimeout;
            for (float t = 0; t < 1;)
            {
                EvaluateSafety(); CheckEnvironment();
                if (Time.time > deadline) throw new TimeoutException("Robot trajectory timeout");
                t = Mathf.Min(1, t + Time.deltaTime * motionScale / duration);
                float smooth = AssemblyMotion.Ease(t) * samples;
                int segment = Mathf.Min(samples - 1, Mathf.FloorToInt(smooth));
                float fraction = smooth - segment;
                for (int j = 0; j < 6; j++) frame[j] = Mathf.Lerp(path[segment][j], path[segment + 1][j], fraction);
                robot.SpeedLimit = maxJointSpeed * motionScale; robot.AccelerationLimit = maxJointAcceleration;
                robot.SetAngles(frame);
                if (Payload != null) Sync(Payload);
                trajectory.Add(robot.Grip.position);
                if (trajectory.Count > 512) trajectory.RemoveAt(0);
                yield return new WaitForFixedUpdate();
            }
            while (Vector3.Distance(robot.Grip.position, position) > 0.008f ||
                Quaternion.Angle(robot.Grip.rotation, rotation) > 3 ||
                m.handover.robotArm.joints.Any(j => Mathf.Abs(j.Body.jointVelocity[0]) > 0.02f))
            {
                EvaluateSafety(); CheckEnvironment();
                robot.SpeedLimit = maxJointSpeed * motionScale;
                robot.SetAngles(goal);
                if (Time.time > deadline) throw new TimeoutException("Articulation failed to settle at target " + position + " actual=" + robot.Grip.position + " angle=" + Quaternion.Angle(robot.Grip.rotation, rotation) + " joints=" + string.Join(",", m.handover.robotArm.joints.Select((j, index) => j.Angle.ToString("F1") + "/" + j.Target.ToString("F1") + "/" + goal[index].ToString("F1"))));
                yield return new WaitForFixedUpdate();
            }
            MaxRobotError = Mathf.Max(MaxRobotError, Vector3.Distance(robot.Grip.position, position));
        }
        private IEnumerator TravelRobot(Vector3 destination, Quaternion rotation)
        {
            Phase = CollaborativePhase.Transport;
            float safeY = Mathf.Max(1.72f, destination.y + 0.16f);
            if (Mathf.Abs(robot.Grip.position.x - robot.Root.position.x) > 0.45f)
                yield return MoveRobot(new Vector3(robot.Grip.position.x, safeY, robot.Grip.position.z), robot.Grip.rotation, 0.65f);
            if ((robot.Grip.position.x - robot.Root.position.x) * (destination.x - robot.Root.position.x) <= 0 || Mathf.Abs(robot.Grip.position.x - robot.Root.position.x) < 0.45f)
                yield return MoveRobot(new Vector3(robot.Root.position.x, 1.85f, robot.Root.position.z + 0.65f), down, 1.5f);
            yield return MoveRobot(new Vector3(destination.x, safeY, destination.z), rotation, 1.6f);
            yield return MoveRobot(destination, rotation, 0.8f);
        }
        private IEnumerator Pick(GearboxPart p)
        {
            Phase = CollaborativePhase.Approach;
            receivingRig.Rest();
            ActivePart = p; SetFingers(m.handover.robotGripper.openHalfGap);
            yield return TravelRobot(p.gripPoint.position + Vector3.up * 0.16f, p.gripPoint.rotation);
            Phase = CollaborativePhase.Grasp;
            yield return MoveRobot(p.gripPoint.position, p.gripPoint.rotation, 0.7f);
            float gap = p.partType == GearboxPartType.Lid ? lidGraspHalfGap : m.handover.robotGripper.RequiredHalfGap(p.GetComponent<GrippableObject>());
            if (gap > m.handover.robotGripper.openHalfGap) throw new InvalidOperationException("Part exceeds configured gripper opening");
            yield return Fingers(gap);
            if (Vector3.Distance(robot.Grip.position, p.gripPoint.position) > graspPositionTolerance) throw new InvalidOperationException("Robot grasp failed");
            Attach(p.transform, robot.Grip);
            Phase = CollaborativePhase.Lift;
            yield return MoveRobot(robot.Grip.position + Vector3.up * 0.22f, robot.Grip.rotation, 0.7f);
        }
        private void Attach(Transform payload, Transform holder)
        {
            Payload = payload; Holder = holder;
            payload.SetParent(holder, true);
            carryOffset = holder.InverseTransformPoint(payload.position);
            carryRotation = Quaternion.Inverse(holder.rotation) * payload.rotation;
        }
        private void DesiredGrip(Vector3 rootPosition, Quaternion rootRotation, out Vector3 position, out Quaternion rotation)
        {
            rotation = rootRotation * Quaternion.Inverse(carryRotation);
            position = rootPosition - rotation * Vector3.Scale(carryOffset, Holder.lossyScale);
        }
        private IEnumerator Transfer(GearboxPart p)
        {
            CheckTracking(); receivingRig.ClearConfirmation();
            yield return WalkTo(receivingStance != null ? receivingStance.position : new Vector3(p.assemblyTarget.transform.position.x + 0.25f, 0, Mathf.Max(1.18f, p.assemblyTarget.transform.position.z + 0.46f)));
            Vector3 presentation = handoverTarget != null ? handoverTarget.position : new Vector3(p.assemblyTarget.transform.position.x, 1.49f, worker.position.z - 0.4f);
            Quaternion orientation = handoverTarget != null ? handoverTarget.rotation : Quaternion.Euler(90, 0, 0);
            hasActiveHandoverTarget = true; activeHandoverPosition = presentation; activeHandoverRotation = orientation;
            if (!receivingRig.CanReach(presentation + orientation * workerGraspOffset)) throw new InvalidOperationException("Handover unreachable: configure receiving stance / target");
            // Keep the receiving hand clear of wrist links during robot approach.
            // The hand moves to the part only after the articulation has settled.
            Vector3 waitingHand = upperArm.position + Vector3.down * 0.35f + worker.right * 0.08f - worker.forward * 0.05f;
            yield return MoveHand(waitingHand, hand.rotation, 0.65f);
            yield return TravelRobot(presentation, orientation);
            Phase = CollaborativePhase.Handover;
            closeInteraction = true;
            Phase = CollaborativePhase.Receiving;
            animator.SetTrigger("HandOver");
            yield return MoveHand(WorkerGraspPosition(p), p.gripPoint.rotation, 0.9f);
            float deadline = Time.time + handoverTimeout, stable = 0;
            while (true)
            {
                EvaluateSafety(); CheckEnvironment();
                if (Time.time >= deadline) throw new TimeoutException("Worker grasp confirmation timeout; robot retains part" + ReceivingDiagnostics(p));
                bool valid = ValidReceivingPose(p);
                stable = valid ? stable + Time.deltaTime : 0;
                if (!valid) receivingRig.ClearConfirmation();
                if (stable >= graspDwell && simulatedGraspSensor) receivingRig.ConfirmGrasp();
                if (stable >= graspDwell && receivingRig.GraspConfirmed) break;
                yield return null;
            }
            Phase = CollaborativePhase.Release;
            Vector3 beforePosition = p.transform.position; Quaternion beforeRotation = p.transform.rotation;
            Attach(p.transform, hand);
            if (Vector3.Distance(beforePosition, p.transform.position) > 0.00001f || Quaternion.Angle(beforeRotation, p.transform.rotation) > 0.01f)
                throw new InvalidOperationException("Ownership transfer changed world pose");
            HandoverCount++;
            yield return Fingers(m.handover.robotGripper.openHalfGap);
            Phase = CollaborativePhase.Retreat;
            Vector3 away = retreatDirection; away.y = 0;
            if (away.sqrMagnitude < 0.001f) throw new InvalidOperationException("Invalid retreat direction; configure a horizontal withdrawal");
            yield return MoveRobot(robot.Grip.position + away.normalized * retreatDistance + Vector3.up * retreatLift, orientation, 0.7f);
            closeInteraction = false;
            Phase = CollaborativePhase.Assembly;
        }
        private IEnumerator WorkerPlace(GearboxPart p)
        {
            receivingRig.AssemblyPose = true;
            yield return new WaitForSeconds(0.5f);
            animator.SetTrigger("Place");
            Transform target = p.assemblyTarget.transform;
            DesiredGrip(target.position + Vector3.up * 0.12f, target.rotation, out Vector3 above, out Quaternion q);
            yield return MoveHand(above, q, 1.0f);
            yield return new WaitForSeconds(0.25f);
            DesiredGrip(target.position, target.rotation, out Vector3 end, out q);
            if (p.partType == GearboxPartType.Screw)
            {
                // The worker maintains the axial contact while the screw turns around its own centre.
                Vector3 start = hand.position;
                for (float t = 0; t < 1;)
                {
                    t = Mathf.Min(1,t + Time.deltaTime / 2.6f); float s = AssemblyMotion.Ease(t);
                    SolveHand(Vector3.Lerp(start,end,s), q);
                    p.transform.localRotation = carryRotation * Quaternion.AngleAxis(1440*s, Vector3.up);
                    Sync(p.transform); yield return null;
                }
                p.transform.localRotation = carryRotation;
            }
            else yield return MoveHand(end, q, 1.25f);
            Commit(p, IsInternal(p) ? m.phaseOne.InternalAssemblyRoot : m.phaseTwoInsertion.finalGearboxRoot);
            yield return MoveHand(hand.position + Vector3.up * 0.16f, down, 0.6f);
            // Robot remains stationary while the worker withdraws from the shared task area.
            yield return MoveHand(upperArm.position + new Vector3(0, -0.25f, -0.12f), down, 0.8f);
        }
        private IEnumerator RobotPlace(GearboxPart p)
        {
            var t = p.assemblyTarget.transform;
            DesiredGrip(t.position + Vector3.up * 0.18f,t.rotation,out Vector3 pos,out Quaternion q);
            yield return TravelRobot(pos,q);
            yield return new WaitForSeconds(0.3f);
            if (p.partType == GearboxPartType.Lid) Phase = CollaborativePhase.Insertion;
            DesiredGrip(t.position,t.rotation,out pos,out q);
            yield return MoveRobot(pos,q,1.5f);
            Commit(p,m.phaseTwoInsertion.finalGearboxRoot);
            yield return Fingers(m.handover.robotGripper.openHalfGap);
            yield return MoveRobot(robot.Grip.position + Vector3.up * 0.22f,robot.Grip.rotation,0.8f);
        }
        private void Commit(GearboxPart p, Transform parent)
        {
            float gap = Vector3.Distance(p.assemblyAnchor.position,p.assemblyTarget.transform.position);
            if (gap > 0.018f) throw new InvalidOperationException(p.partId + " seating gap " + gap);
            p.transform.SetParent(parent,true);
            if (!p.assemblyTarget.TryPlace(p)) throw new InvalidOperationException("Target rejected " + p.partId);
            RefreshHousingCollision();
            Sync(p.transform); Payload = null; Holder = null;
        }
        private IEnumerator PickInternal()
        {
            ActivePart = m.outputShaftStep.outputShaft;
            yield return TravelRobot(ActivePart.gripPoint.position + Vector3.up * 0.16f,ActivePart.gripPoint.rotation);
            yield return MoveRobot(ActivePart.gripPoint.position,ActivePart.gripPoint.rotation,0.8f);
            yield return Fingers(0.035f);
            Attach(m.phaseOne.InternalAssemblyRoot,robot.Grip);
            yield return MoveRobot(robot.Grip.position + Vector3.up * 0.3f,robot.Grip.rotation,1);
        }
        private IEnumerator InsertInternal()
        {
            DesiredGrip(finalOrigin + Vector3.up * 0.25f,Quaternion.identity,out Vector3 pos,out Quaternion q);
            yield return TravelRobot(pos,q);
            yield return new WaitForSeconds(0.3f);
            Phase = CollaborativePhase.Insertion;
            DesiredGrip(finalOrigin,Quaternion.identity,out pos,out q);
            yield return MoveRobot(pos,q,2);
            var root = m.phaseOne.InternalAssemblyRoot;
            root.SetParent(m.phaseTwoInsertion.finalGearboxRoot,true);
            if (Vector3.Distance(root.position,finalOrigin) > 0.012f) throw new InvalidOperationException("Internal assembly not seated");
            root.SetPositionAndRotation(finalOrigin,Quaternion.identity); Sync(root);
            Payload = null; Holder = null;
            yield return Fingers(m.handover.robotGripper.openHalfGap);
            yield return MoveRobot(robot.Grip.position + Vector3.up * 0.24f,q,0.8f);
        }

        private IEnumerator WalkTo(Vector3 destination)
        {
            var movement = worker.GetComponent<WorkerMovement>();
            movement.enabled = true; movement.MoveTo(destination);
            float deadline = Time.time + handoverTimeout;
            while (Vector3.Distance(new Vector3(worker.position.x, 0, worker.position.z), new Vector3(destination.x, 0, destination.z)) > 0.09f)
            {
                CheckTracking();
                if (Time.time > deadline) throw new TimeoutException("Worker stance blocked or unreachable");
                yield return null;
            }
            movement.Stop();
            while (movement.IsMoving)
            { if (Time.time > deadline) throw new TimeoutException("Worker did not settle at stance"); yield return null; }
            movement.enabled = false;
            Vector3 forward = (handoverTarget != null ? handoverTarget.position : new Vector3(destination.x, 1.49f, destination.z - 0.4f)) - worker.position;
            forward.y = 0;
            if (forward.sqrMagnitude > 0.001f)
            {
                Quaternion facing = Quaternion.LookRotation(forward);
                while (Quaternion.Angle(worker.rotation, facing) > 1)
                { worker.rotation = Quaternion.RotateTowards(worker.rotation, facing, 120 * Time.deltaTime); yield return null; }
            }
        }
        private IEnumerator MoveHand(Vector3 target, Quaternion rotation, float duration)
        {
            if (!receivingRig.CanReach(target)) throw new InvalidOperationException("Worker target outside plausible reach; configure stance. Target=" + target + " shoulder=" + upperArm.position);
            Vector3 from = hand.position; Quaternion start = hand.rotation;
            for (float t = 0; t < 1;)
            {
                t = Mathf.Min(1,t+Time.deltaTime/duration); float s=AssemblyMotion.Ease(t);
                SolveHand(Vector3.Lerp(from,target,s),Quaternion.Slerp(start,rotation,s));
                if (Payload != null && Holder == hand) Sync(Payload);
                yield return null;
            }
            yield return new WaitForSeconds(receivingRig.blendSeconds);
            float error=Vector3.Distance(hand.position,target); MaxHandError=Mathf.Max(MaxHandError,error);
            if (error>0.018f) throw new InvalidOperationException("Worker reach error " + error.ToString("F3"));
        }
        private void SolveHand(Vector3 target, Quaternion rotation)
        {
            receivingRig.Reach(target, rotation);
        }
        private static void Aim(Transform bone,Vector3 end,Vector3 target)
        {
            Vector3 a=end-bone.position,b=target-bone.position;
            if(a.sqrMagnitude>0.000001f && b.sqrMagnitude>0.000001f) bone.rotation=Quaternion.FromToRotation(a,b)*bone.rotation;
        }
        private IEnumerator Fingers(float gap)
        {
            float from=Mathf.Abs(robot.LeftFinger.localPosition.x);
            float duration = Mathf.Max(0.1f, Mathf.Abs(gap - from) * 1.875f / Mathf.Max(0.01f, m.handover.robotGripper.fingerSpeed));
            for(float t=0;t<1;) { t=Mathf.Min(1,t+Time.deltaTime/duration); SetFingers(Mathf.Lerp(from,gap,AssemblyMotion.Ease(t))); yield return null; }
        }
        private void SetFingers(float gap)
        {
            var a=robot.LeftFinger.localPosition; var b=robot.RightFinger.localPosition;
            a.x=-gap;b.x=gap;robot.LeftFinger.localPosition=a;robot.RightFinger.localPosition=b;
        }
        private static void Sync(Transform root)
        { foreach(var b in root.GetComponentsInChildren<Rigidbody>()) { b.position=b.transform.position;b.rotation=b.transform.rotation; } }

        private void LateUpdate()
        {
            if(!Ready || cameraView==null) return;
            Vector3 focus=Payload!=null ? Payload.position : ActivePart!=null ? ActivePart.transform.position : new Vector3(0,1.2f,0.4f);
            Vector3 position;
            if(cameraMode==0) { focus=new Vector3(-0.2f,1.0f,0.4f);position=new Vector3(4.6f,4.2f,-6.0f); }
            else if(cameraMode==1) { focus=Vector3.Lerp(focus,robot.Grip.position,0.2f);position=focus+new Vector3(1.15f,0.8f,-1.55f); }
            else { focus=(Completed<7?firstOrigin:finalOrigin)+Vector3.up*0.1f;position=focus+new Vector3(-0.62f,0.55f,-0.85f); }
            float blend=1-Mathf.Exp(-3*Time.unscaledDeltaTime);
            lookAt=Vector3.Lerp(lookAt,focus,blend);
            cameraView.transform.position=Vector3.Lerp(cameraView.transform.position,position,blend);
            cameraView.transform.rotation=Quaternion.Slerp(cameraView.transform.rotation,Quaternion.LookRotation(lookAt-cameraView.transform.position),blend);
            cameraView.fieldOfView=Mathf.Lerp(cameraView.fieldOfView,cameraMode==0?43:46,blend);
        }
        private void OnGUI()
        {
            if(!Ready || TrainingMode)return;
            if(title==null) { title=new GUIStyle(GUI.skin.label){fontSize=23,fontStyle=FontStyle.Bold}; small=new GUIStyle(GUI.skin.label){fontSize=13}; nodeText=new GUIStyle(small){wordWrap=true}; }
            GUI.color=new Color(0.07f,0.1f,0.15f,0.97f);GUI.DrawTexture(new Rect(0,0,Screen.width,102),Texture2D.whiteTexture);
            GUI.color=Color.white;GUI.Label(new Rect(24,12,600,34),"GEARBOX  /  ASSEMBLY LAB",title);
            GUI.Label(new Rect(24,48,Screen.width-48,24),Fault??ActionName,small);
            GUI.color=new Color(0.1f,0.2f,0.27f);GUI.DrawTexture(new Rect(24,83,Screen.width-48,5),Texture2D.whiteTexture);
            GUI.color=new Color(0.12f,0.85f,0.72f);GUI.DrawTexture(new Rect(24,83,(Screen.width-48)*Completed/13f,5),Texture2D.whiteTexture);
            float x=Screen.width-266;
            GUI.color=new Color(0.07f,0.1f,0.15f,0.94f);GUI.DrawTexture(new Rect(x,114,250,420),Texture2D.whiteTexture);
            GUI.color=Color.white;GUI.Label(new Rect(x+14,125,224,22),"LIVE BEHAVIOUR TREE",small);
            for(int i=0;i<Tree.Children.Count;i++)
            {
                var n=Tree.Children[i];GUI.color=n.Status==BehaviourStatus.Success?new Color(0.2f,0.85f,0.7f):n.Status==BehaviourStatus.Running?new Color(1,0.78f,0.3f):Color.gray;
                GUI.Label(new Rect(x+14,156+i*26,224,25),(i+1).ToString("00")+"  "+n.Name,nodeText);
            }
            GUI.color=Color.white;
            float y=Screen.height-62;
            GUI.Box(new Rect(12,y-12,834,60),GUIContent.none);
            string[] labels={"PLAY / RESUME","PAUSE","STEP","RESET","CAMERA [C]",speed.ToString("0.0")+"x","STOP [ESC]"};
            float[] xs={24,144,244,344,444,584,694};float[] widths={110,90,90,90,130,90,140};
            for(int i=0;i<labels.Length;i++)GUI.Box(new Rect(xs[i],y,widths[i],36),labels[i],GUI.skin.button);
        }
    }
}
