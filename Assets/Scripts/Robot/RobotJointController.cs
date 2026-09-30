using UnityEngine;

namespace GearboxDemo
{
    [DisallowMultipleComponent, RequireComponent(typeof(ArticulationBody))]
    public sealed class RobotJointController : MonoBehaviour
    {
        public Vector3 localAxis = Vector3.right;
        private ArticulationBody body;
        public ArticulationBody Body => body != null ? body : body = GetComponent<ArticulationBody>();
        public float Angle => Body.dofCount == 1 ? Body.jointPosition[0] * Mathf.Rad2Deg : Body.xDrive.target;
        public float Target => Body.xDrive.target;
        public bool Accepts(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= Body.xDrive.lowerLimit && value <= Body.xDrive.upperLimit;
        public void SetTarget(float degrees)
        {
            var drive = Body.xDrive;
            drive.target = Mathf.Clamp(degrees, drive.lowerLimit, drive.upperLimit);
            Body.xDrive = drive;
        }
    }
}
