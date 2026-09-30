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
                from[i] = joints[i].Target;
                duration = Mathf.Max(duration, Mathf.Abs(to[i] - from[i]) * 1.875f / Mathf.Max(1, speedDegreesPerSecond));
            }
            elapsed = 0; IsMoving = true;
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
            if (!IsMoving) return;
            elapsed += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float smooth = t * t * t * (t * (t * 6 - 15) + 10);
            for (int i = 0; i < 6; i++) joints[i].SetTarget(Mathf.Lerp(from[i], to[i], smooth));
            if (t >= 1) IsMoving = false;
        }

        private void Awake()
        {
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
