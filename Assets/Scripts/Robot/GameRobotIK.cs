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
        public Collider[] ExtraColliders;
        public Transform[] ExtraSources;
        private readonly Transform[] joints = new Transform[6];
        private readonly Vector3[] axes = new Vector3[6];
        private readonly Quaternion[] rest = new Quaternion[6];
        private readonly float[] angles = new float[6];
        private readonly float[] home = new float[6];
        private readonly float[] lower = new float[6], upper = new float[6];
        private readonly float[,] jac = new float[6, 6];
        private readonly float[,] system = new float[6, 7];
        private RobotArmController articulation;
        private Dictionary<Transform, Transform> planningMap;
        private Transform planningGrip, planningRoot;
        private bool planning;
        private readonly bool cadRig;
        private readonly float[] commandedVelocity = new float[6];
        public float SpeedLimit = 35, AccelerationLimit = 80;
        private const float OrientationWeight = 0.22f;

        public GameRobotIK(RobotArmController source, RobotGripper gripper)
        {
            var map = new Dictionary<Transform, Transform>();
            planningMap = map;
            articulation = source;
            cadRig = source.GetComponent<YaskawaSimulationRig>() != null;
            planningRoot = Clone(source.transform, source.transform.parent, map);
            Root = source.transform;
            Root.name = "Game Robot • Cartesian IK";
            planningGrip = map[gripper.attachmentPoint];
            Grip = gripper.attachmentPoint;
            LeftFinger = gripper.leftFinger; RightFinger = gripper.rightFinger;
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
            foreach (var renderer in planningRoot.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            source.enabled = true; // Tree owns targets; the arm supplies gravity feedforward in FixedUpdate.
            // Keep the upper link above the worktop: the authored 120-degree shoulder limit
            // permits an elbow-down solution through the bench. Use the overhead branch.
            if(source.GetComponent<YaskawaSimulationRig>()==null)
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

        public void Reset() => RestorePose(home);
        public void RestorePose(float[] pose)
        {
            articulation.ResetTo(pose);
            System.Array.Clear(commandedVelocity, 0, commandedVelocity.Length);
            for (int i = 0; i < 6; i++) { angles[i] = pose[i]; Apply(i); }
        }
        private void Apply(int i) => joints[i].localRotation = rest[i] * Quaternion.AngleAxis(angles[i], axes[i]);
        public float[] Angles => (float[])angles.Clone();
        public Vector3[] JointPositions => System.Array.ConvertAll(articulation.joints, joint => joint.transform.position);
        public void SetAngles(float[] pose)
        { if(pose==null || pose.Length!=6)throw new System.ArgumentException("Six joint angles are required.");
          for(int i=0;i<6;i++)if(float.IsNaN(pose[i]) || float.IsInfinity(pose[i]) || pose[i]<lower[i]-.001f || pose[i]>upper[i]+.001f)
              throw new System.ArgumentException("Joint "+(i+1)+" target is outside the simulation limits.");
          for(int i=0;i<6;i++) { angles[i]=Mathf.Clamp(pose[i],lower[i],upper[i]); Apply(i); if (!planning)
                {
                    var joint = articulation.joints[i];
                    float dt = Time.fixedDeltaTime;
                    float error = angles[i] - joint.Target;
                    float wanted = Mathf.Clamp(error / dt, -SpeedLimit, SpeedLimit);
                    float braking = Mathf.Sqrt(2 * AccelerationLimit * Mathf.Abs(error));
                    wanted = Mathf.Clamp(wanted, -braking, braking);
                    commandedVelocity[i] = Mathf.MoveTowards(commandedVelocity[i], wanted, AccelerationLimit * dt);
                    float step = commandedVelocity[i] * dt;
                    if (Mathf.Abs(step) > Mathf.Abs(error) && Mathf.Sign(step) == Mathf.Sign(error)) step = error;
                    joint.SetTarget(joint.Target + step);
                } } }
        public float[] Plan(Vector3 position, Quaternion rotation, float[] seed = null, System.Action<float[]> validate = null)
        {
            planning = true;
            var original=Angles;
            float best=float.MaxValue, bestCost=float.MaxValue; float[] solution=null;
            float[] reference=seed ?? original;
            try {
            float yaw=Mathf.Atan2(position.x-joints[0].position.x,position.z-joints[0].position.z)*Mathf.Rad2Deg;
            for(int attempt=0;attempt<(cadRig?19:7);attempt++)
            {
                if(attempt==0)SetAngles(seed ?? original);
                else
                {
                    float elbow=attempt<=3?-100:100;
                    float shoulder=attempt%3==0?-55:attempt%3==1?0:55;
                    if(cadRig) {
                        elbow=(attempt%2==0?90:-90);
                        shoulder=((attempt-1)/2%3-1)*50;
                        SetPlanningSeed(new[]{yaw+((attempt-1)/6-1)*90,shoulder,elbow,0f,-shoulder-elbow,0f});
                    } else SetPlanningSeed(new[]{yaw,shoulder,elbow,0f,180f-shoulder-elbow,0f});
                }
                float error=Solve(position,rotation,cadRig?360:180);
                error+=Quaternion.Angle(planningGrip.rotation,rotation)*0.002f;
                // Equivalent wrist turns should remain closest to the previous waypoint.
                for(int j=0;j<6;j++) {
                    float equivalent=angles[j]+360f*Mathf.Round((reference[j]-angles[j])/360f);
                    if(equivalent>=lower[j] && equivalent<=upper[j]) { angles[j]=equivalent;Apply(j); }
                }
                float cost=0;
                for(int j=0;j<6;j++)cost+=Mathf.Pow(angles[j]-reference[j],2);
                bool continuous=true;
                if(seed!=null)for(int j=0;j<6;j++)if(Mathf.Abs(angles[j]-seed[j])>60f)continuous=false;
                if(error<=0.018f && continuous && cost<bestCost) {
                    float[] candidate=Angles;
                    try { validate?.Invoke(candidate); }
                    catch(System.InvalidOperationException) { continue; }
                    best=error;bestCost=cost;solution=candidate;
                    if(attempt==0)break;
                }
            }
            if(solution==null)throw new System.InvalidOperationException("No reachable gripper pose at "+position);
            if(seed!=null)
                for(int j=0;j<6;j++)if(Mathf.Abs(solution[j]-seed[j])>60f)
                    throw new System.InvalidOperationException("IK branch jump at joint "+(j+1)+"; motion rejected.");
            return solution;
            } finally { SetAngles(original);planning=false; }
        }
        private void SetPlanningSeed(float[] pose) {
            for(int i=0;i<6;i++)pose[i]=Mathf.Clamp(pose[i],lower[i],upper[i]);
            SetAngles(pose);
        }
        public void ValidateTrajectory(float[] from, float[] goal, System.Action<Collider, Vector3, Quaternion> check, Transform payload = null)
        {
            bool wasPlanning=planning;
            float[] original = Angles;
            if (ExtraSources != null)
                foreach (var finger in ExtraSources)
                    planningMap[finger].localPosition = finger.localPosition;
            planning = true;
            try
            {
                float max = 0;
                for (int i = 0; i < 6; i++) max = Mathf.Max(max, Mathf.Abs(goal[i] - from[i]));
                int samples = Mathf.Max(2, Mathf.CeilToInt(max / 2f));
                float[] pose = new float[6];
                for (int sample = 0; sample <= samples; sample++)
                {
                    for (int i = 0; i < 6; i++) pose[i] = Mathf.Lerp(from[i], goal[i], (float)sample / samples);
                    SetAngles(pose);
                    foreach (var collider in Root.GetComponentsInChildren<Collider>())
                    {
                        if (!collider.enabled || collider.isTrigger || !planningMap.TryGetValue(collider.transform, out var predicted)) continue;
                        check(collider, predicted.position, predicted.rotation);
                    }
                    if (ExtraColliders != null)
                        for (int i = 0; i < ExtraColliders.Length; i++)
                        {
                            var predicted = planningMap[ExtraSources[i]];
                            check(ExtraColliders[i], predicted.position, predicted.rotation);
                        }
                    if (payload != null)
                        foreach (var collider in payload.GetComponentsInChildren<Collider>())
                        {
                            if (!collider.enabled || collider.isTrigger) continue;
                            Vector3 local = Grip.InverseTransformPoint(collider.transform.position);
                            Quaternion localRotation = Quaternion.Inverse(Grip.rotation) * collider.transform.rotation;
                            check(collider, planningGrip.TransformPoint(local), planningGrip.rotation * localRotation);
                        }
                }
            }
            finally { SetAngles(original); planning = wasPlanning; }
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
                Vector3 p = planningGrip.position; Quaternion r = planningGrip.rotation;
                Vector3 delta = position - p;
                Vector3 turn = RotationVector(rotation * Quaternion.Inverse(r)) * OrientationWeight;
                if (delta.magnitude < 0.0006f && turn.magnitude < 0.002f) break;
                float[] error = { delta.x, delta.y, delta.z, turn.x, turn.y, turn.z };
                for (int j = 0; j < 6; j++)
                {
                    angles[j] += 0.25f; Apply(j);
                    Vector3 dp = (planningGrip.position - p) / (0.25f * Mathf.Deg2Rad);
                    Vector3 dr = RotationVector(planningGrip.rotation * Quaternion.Inverse(r)) * (OrientationWeight / (0.25f * Mathf.Deg2Rad));
                    angles[j] -= 0.25f; Apply(j);
                    jac[0,j]=dp.x; jac[1,j]=dp.y; jac[2,j]=dp.z;
                    jac[3,j]=dr.x; jac[4,j]=dr.y; jac[5,j]=dr.z;
                }
                for (int i = 0; i < 6; i++)
                {
                    for (int j = 0; j < 6; j++)
                    {
                        float v = i == j ? (cadRig?0.0003f:0.0012f) : 0;
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
            return Vector3.Distance(position, planningGrip.position);
        }
    }
}
