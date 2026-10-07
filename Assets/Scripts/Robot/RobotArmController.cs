using System;
using UnityEngine;

namespace GearboxDemo
{
    [DisallowMultipleComponent]
    public sealed class RobotArmController : MonoBehaviour
    {
        public RobotJointController[] joints = new RobotJointController[6];
        public Transform gripPoint;
        [Min(1)] public float speedDegreesPerSecond = 35;
        [Min(1)] public float accelerationDegreesPerSecondSquared = 80;
        [Range(0, 1)] public float speedScale = 1;
        [Tooltip("Feedforward joint torques from articulated link masses and gravity. Payload loading is not simulated by kinematic attachment.")]
        public bool gravityCompensation = true;
        private ArticulationBody[][] gravitySubtrees;
        public bool IsMoving { get; private set; }
        public float MaxError { get { float e = 0; foreach (var j in joints) e = Mathf.Max(e, Mathf.Abs(j.Angle - j.Target)); return e; } }
        private float[] from, to;
        private float elapsed, duration;

        public void MoveTo(float[] angles)
        {
            if (angles == null || angles.Length != 6 || joints.Length != 6)
                throw new ArgumentException("Robot requires exactly six configured joint angles.");
            for (int i = 0; i < 6; i++)
                if (joints[i] == null || !joints[i].Accepts(angles[i])) throw new ArgumentException("Invalid target for Joint" + (i + 1));
            from = new float[6]; to = (float[])angles.Clone();
            duration = 0.4f;
            for (int i = 0; i < 6; i++)
            {
                from[i] = joints[i].Angle;
                float distance = Mathf.Abs(to[i] - from[i]);
                duration = Mathf.Max(duration, distance * 1.875f / Mathf.Max(1, speedDegreesPerSecond));
                duration = Mathf.Max(duration, Mathf.Sqrt(distance * 5.774f / Mathf.Max(1, accelerationDegreesPerSecondSquared)));
            }
            elapsed = 0; IsMoving = true;
        }

        public void HoldPosition()
        {
            IsMoving = false;
            foreach (var joint in joints) if (joint != null) joint.SetTarget(joint.Angle);
        }

        public void ResetTo(float[] angles)
        {
            if (angles == null || angles.Length != 6) throw new ArgumentException("Robot reset requires six joint angles.");
            IsMoving = false;
            from = null;
            to = null;
            for (int i = 0; i < 6; i++)
            {
                joints[i].SetTarget(angles[i]);
                joints[i].Body.jointPosition = new ArticulationReducedSpace(angles[i] * Mathf.Deg2Rad);
                joints[i].Body.jointVelocity = new ArticulationReducedSpace(0f);
            }
        }

        private void FixedUpdate()
        {
            if (gravityCompensation && gravitySubtrees != null)
            {
                for (int i = 0; i < joints.Length; i++)
                {
                    var joint = joints[i];
                    Vector3 axis = joint.transform.TransformDirection(joint.localAxis).normalized;
                    Vector3 pivot = joint.transform.TransformPoint(joint.Body.anchorPosition);
                    Vector3 gravityTorque = Vector3.zero;
                    foreach (var link in gravitySubtrees[i])
                        if (link != null && link.useGravity)
                            gravityTorque += Vector3.Cross(link.worldCenterOfMass - pivot, Physics.gravity * link.mass);
                    float limit = joint.Body.xDrive.forceLimit;
                    float compensation = Mathf.Clamp(-Vector3.Dot(gravityTorque, axis), -limit, limit);
                    joint.Body.AddTorque(axis * compensation, ForceMode.Force);
                    var parent = joint.transform.parent.GetComponentInParent<ArticulationBody>();
                    if (parent != null && !parent.isRoot) parent.AddTorque(-axis * compensation, ForceMode.Force);
                }
            }
            if (!IsMoving) return;
            elapsed += Time.fixedDeltaTime * speedScale;
            float t = Mathf.Clamp01(elapsed / duration);
            float smooth = t * t * t * (t * (t * 6 - 15) + 10);
            for (int i = 0; i < 6; i++) joints[i].SetTarget(Mathf.Lerp(from[i], to[i], smooth));
            if (t >= 1) IsMoving = false;
        }

        private void Awake()
        {
            gravitySubtrees = new ArticulationBody[joints.Length][];
            for (int i = 0; i < joints.Length; i++) gravitySubtrees[i] = joints[i].GetComponentsInChildren<ArticulationBody>();
            // Unity starts reduced coordinates at zero unless initialized explicitly.
            // Match the authored Home configuration before the first physics step.
            foreach (var joint in joints)
            {
                joint.Body.jointPosition = new ArticulationReducedSpace(joint.Target * Mathf.Deg2Rad);
                joint.Body.jointVelocity = new ArticulationReducedSpace(0);
            }
            // Ignore only robot-to-robot contacts. Workcell/part collisions remain enabled.
            var colliders = GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
                for (int j = i + 1; j < colliders.Length; j++) Physics.IgnoreCollision(colliders[i], colliders[j]);
        }
    }
}
