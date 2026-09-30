using System.Collections.Generic;
using UnityEngine;

namespace GearboxDemo
{
    /// <summary>Six-axis damped-least-squares IK for the game presentation rig.</summary>
    public sealed class GameRobotIK
    {
        public Transform Root { get; private set; }
        public Transform Grip { get; private set; }
        public Transform LeftFinger, RightFinger;
        private readonly Transform[] joints = new Transform[6];
        private readonly Vector3[] axes = new Vector3[6];
        private readonly Quaternion[] rest = new Quaternion[6];
        private readonly float[] angles = new float[6];
        private readonly float[] home = new float[6];
        private readonly float[] lower = new float[6], upper = new float[6];
        private readonly float[,] jac = new float[6, 6];
        private readonly float[,] system = new float[6, 7];
        private const float OrientationWeight = 0.22f;

        public GameRobotIK(RobotArmController source, RobotGripper gripper)
        {
            var map = new Dictionary<Transform, Transform>();
            Root = Clone(source.transform, source.transform.parent, map);
            Root.name = "Game Robot • Cartesian IK";
            Grip = map[gripper.attachmentPoint];
            LeftFinger = map[gripper.leftFinger]; RightFinger = map[gripper.rightFinger];
            for (int i = 0; i < 6; i++)
            {
                joints[i] = map[source.joints[i].transform]; axes[i] = source.joints[i].localAxis;
                home[i] = angles[i] = source.joints[i].Target;
                // The authored robot uses axis-angle rotations from identity at every joint.
                // Articulation jointPosition and Transform can differ during the first physics frame.
                rest[i] = Quaternion.identity;
                lower[i] = source.joints[i].Body.xDrive.lowerLimit;
                upper[i] = source.joints[i].Body.xDrive.upperLimit;
            }
            source.gameObject.SetActive(false);
            // Keep the upper link above the worktop: the authored 120-degree shoulder limit
            // permits an elbow-down solution through the bench. Use the overhead branch.
            upper[1] = Mathf.Min(upper[1], 80f);
            Reset();
        }

        private static Transform Clone(Transform source, Transform parent, Dictionary<Transform, Transform> map)
        {
            var t = new GameObject(source.name).transform;
            t.SetParent(parent, false); t.localPosition = source.localPosition;
            t.localRotation = source.localRotation; t.localScale = source.localScale; map[source] = t;
            var mesh = source.GetComponent<MeshFilter>(); var renderer = source.GetComponent<MeshRenderer>();
            if (mesh != null && renderer != null)
            {
                t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh.sharedMesh;
                var r = t.gameObject.AddComponent<MeshRenderer>(); r.sharedMaterials = renderer.sharedMaterials;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            foreach (Transform child in source) Clone(child, t, map);
            return t;
        }

        public void Reset()
        { for (int i = 0; i < 6; i++) { angles[i] = home[i]; Apply(i); } }
        private void Apply(int i) => joints[i].localRotation = rest[i] * Quaternion.AngleAxis(angles[i], axes[i]);
        public float[] Angles => (float[])angles.Clone();
        public Vector3[] JointPositions => System.Array.ConvertAll(joints, joint => joint.position);
        public void SetAngles(float[] pose)
        { for(int i=0;i<6;i++) { angles[i]=pose[i]; Apply(i); } }
        public float[] Plan(Vector3 position, Quaternion rotation)
        {
            var original=Angles;
            float best=float.MaxValue; float[] solution=null;
            float yaw=Mathf.Atan2(position.x-joints[0].position.x,position.z-joints[0].position.z)*Mathf.Rad2Deg;
            for(int attempt=0;attempt<7;attempt++)
            {
                if(attempt==0)SetAngles(original);
                else
                {
                    float elbow=attempt<=3?-100:100;
                    float shoulder=attempt%3==0?-55:attempt%3==1?0:55;
                    SetAngles(new[]{yaw,shoulder,elbow,0f,180f-shoulder-elbow,0f});
                }
                float error=Solve(position,rotation,180);
                error+=Quaternion.Angle(Grip.rotation,rotation)*0.002f;
                if(error<best) { best=error;solution=Angles; }
                if(best<0.002f)break;
            }
            SetAngles(original);
            if(best>0.018f)throw new System.InvalidOperationException("No reachable gripper pose at "+position+" (residual "+best.ToString("F3")+")");
            return solution;
        }
        private static Vector3 RotationVector(Quaternion q)
        {
            if (q.w < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            q.ToAngleAxis(out float degrees, out Vector3 axis);
            if (float.IsNaN(axis.x) || degrees < 0.0001f) return Vector3.zero;
            return axis * degrees * Mathf.Deg2Rad;
        }

        public float Solve(Vector3 position, Quaternion rotation, int iterations = 28)
        {
            for (int n = 0; n < iterations; n++)
            {
                Vector3 p = Grip.position; Quaternion r = Grip.rotation;
                Vector3 delta = position - p;
                Vector3 turn = RotationVector(rotation * Quaternion.Inverse(r)) * OrientationWeight;
                if (delta.magnitude < 0.0006f && turn.magnitude < 0.002f) break;
                float[] error = { delta.x, delta.y, delta.z, turn.x, turn.y, turn.z };
                for (int j = 0; j < 6; j++)
                {
                    angles[j] += 0.25f; Apply(j);
                    Vector3 dp = (Grip.position - p) / (0.25f * Mathf.Deg2Rad);
                    Vector3 dr = RotationVector(Grip.rotation * Quaternion.Inverse(r)) * (OrientationWeight / (0.25f * Mathf.Deg2Rad));
                    angles[j] -= 0.25f; Apply(j);
                    jac[0,j]=dp.x; jac[1,j]=dp.y; jac[2,j]=dp.z;
                    jac[3,j]=dr.x; jac[4,j]=dr.y; jac[5,j]=dr.z;
                }
                for (int i = 0; i < 6; i++)
                {
                    for (int j = 0; j < 6; j++)
                    {
                        float v = i == j ? 0.0012f : 0;
                        for (int k = 0; k < 6; k++) v += jac[k,i] * jac[k,j];
                        system[i,j] = v;
                    }
                    system[i,6] = 0;
                    for (int k = 0; k < 6; k++) system[i,6] += jac[k,i] * error[k];
                }
                for (int i = 0; i < 6; i++)
                {
                    float divisor = system[i,i];
                    for (int j = i; j <= 6; j++) system[i,j] /= divisor;
                    for (int k = 0; k < 6; k++) if (k != i)
                    { float f = system[k,i]; for (int j = i; j <= 6; j++) system[k,j] -= f * system[i,j]; }
                }
                for (int i = 0; i < 6; i++)
                { angles[i] = Mathf.Clamp(angles[i] + Mathf.Clamp(system[i,6] * Mathf.Rad2Deg, -6, 6), lower[i], upper[i]); Apply(i); }
            }
            return Vector3.Distance(position, Grip.position);
        }
    }
}
