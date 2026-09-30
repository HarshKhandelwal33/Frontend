using System.Collections;
using UnityEngine;

namespace GearboxDemo
{
    [RequireComponent(typeof(RobotArmController))]
    public sealed class RobotPoseController : MonoBehaviour
    {
        public RobotPose home, traySafe, assemblySafe, humanSafe;
        public string Status { get; private set; } = "Home";
        public bool Busy { get; private set; }
        private RobotPose pending;
        private RobotArmController arm;
        private bool failed;
        private void Awake() => arm = GetComponent<RobotArmController>();
        public void RequestPose(RobotPose pose)
        {
            if (pose == null) return;
            if (Busy) { pending = pose; return; }
            StartCoroutine(Transition(pose));
        }
        private IEnumerator Transition(RobotPose pose)
        {
            Busy = true; failed = false; Status = "Moving to " + pose.poseName;
            // Retract before yawing. Keep the tool high while crossing the workcell.
            float[] folded = (float[])home.jointDegrees.Clone();
            folded[0] = arm.joints[0].Target;
            yield return Stage(folded);
            if (!failed)
            {
                folded[0] = pose.jointDegrees[0];
                yield return Stage(folded);
            }
            if (!failed) yield return Stage(pose.jointDegrees);
            Status = failed ? "Motion stopped: joint tracking timeout" : pose.poseName;
            Busy = false;
            var next = pending; pending = null;
            if (!failed && next != null) RequestPose(next);
        }
        private IEnumerator Stage(float[] angles)
        {
            arm.MoveTo(angles);
            while (arm.IsMoving) yield return new WaitForFixedUpdate();
            float deadline = Time.time + 5;
            while (arm.MaxError > 2 && Time.time < deadline) yield return new WaitForFixedUpdate();
            if (arm.MaxError > 2) { failed = true; Debug.LogError("Robot joint tracking timeout; remaining stages cancelled.", this); }
        }

        public void ResetToHome()
        {
            StopAllCoroutines();
            pending = null;
            failed = false;
            Busy = false;
            Status = "Home";
            if (arm == null) arm = GetComponent<RobotArmController>();
            arm.ResetTo(home.jointDegrees);
        }
    }
}
