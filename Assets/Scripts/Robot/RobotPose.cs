using UnityEngine;

namespace GearboxDemo
{
    [CreateAssetMenu(menuName = "Gearbox Demo/Robot Pose")]
    public sealed class RobotPose : ScriptableObject
    {
        public string poseName;
        [Tooltip("Joint1 through Joint6, in degrees; never a Cartesian pickup pose.")]
        public float[] jointDegrees = new float[6];
    }
}
