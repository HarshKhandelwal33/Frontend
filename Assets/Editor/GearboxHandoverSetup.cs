using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GearboxDemo.Editor
{
    public static class GearboxHandoverSetup
    {
        [MenuItem("Tools/Gearbox Demo/Setup Human-Robot Handover")]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before setting up handover.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != GearboxDemoBuilder.ScenePath)
                throw new InvalidOperationException("Open GearboxAssembly first.");

            Transform workcell = scene.GetRootGameObjects().Single(g => g.name == "Gearbox Assembly Workcell").transform;
            RobotArmController arm = workcell.GetComponentInChildren<RobotArmController>(true);
            RobotGripper gripper = workcell.GetComponentInChildren<RobotGripper>(true);
            Transform worker = workcell.Find("Worker");
            Animator animator = worker != null ? worker.GetComponentInChildren<Animator>(true) : null;
            if (arm == null || gripper == null || animator == null || !animator.isHuman)
                throw new InvalidOperationException("Existing robot, gripper and Humanoid Worker are required.");

            Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (rightHand == null) throw new InvalidOperationException("Worker Humanoid has no RightHand bone.");
            Transform workerHold = EnsureChild(rightHand, "WorkerRightHandHoldPoint");
            workerHold.localPosition = new Vector3(0.06f, 0, 0);
            workerHold.localRotation = Quaternion.identity;

            Transform endEffectorParent = arm.transform.Find("Base/Joint1/Joint2/Joint3/Joint4/Joint5/Joint6/Wrist/EndEffector");
            if (endEffectorParent == null) throw new InvalidOperationException("Existing robot EndEffector was not found.");
            Transform robotEndEffector = EnsureChild(endEffectorParent, "RobotEndEffector");
            robotEndEffector.localPosition = Vector3.zero;
            robotEndEffector.localRotation = Quaternion.identity;

            Transform robotHold = EnsureChild(gripper.transform, "RobotHoldPoint");
            robotHold.SetPositionAndRotation(arm.gripPoint.position, arm.gripPoint.rotation);
            gripper.attachmentPoint = robotHold;
            arm.gripPoint = robotHold;
            EditorUtility.SetDirty(gripper);
            EditorUtility.SetDirty(arm);

            Transform handoverPoint = EnsureChild(workcell, "RobotWorkerHandoverPoint");
            handoverPoint.position = rightHand.position + worker.forward * 0.42f + Vector3.up * 0.06f;
            handoverPoint.rotation = workerHold.rotation;

            HumanRobotHandover handover = handoverPoint.GetComponent<HumanRobotHandover>();
            if (handover == null) handover = Undo.AddComponent<HumanRobotHandover>(handoverPoint.gameObject);
            handover.robotWorkerHandoverPoint = handoverPoint;
            handover.robotEndEffector = robotEndEffector;
            handover.robotHoldPoint = robotHold;
            handover.workerRightHandHoldPoint = workerHold;
            handover.workerAnimator = animator;
            handover.robotArm = arm;
            handover.robotGripper = gripper;
            handover.handoverJointDegrees = CalculateHandoverPose(arm, handoverPoint.position);
            EditorUtility.SetDirty(handover);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Could not save handover setup.");
            Debug.Log("Human-robot handover prepared. No gearbox parts or assembly targets were recreated.");
        }

        private static float[] CalculateHandoverPose(RobotArmController arm, Vector3 target)
        {
            Transform baseTransform = arm.transform.Find("Base");
            Vector3 shoulder = arm.joints[0].transform.position;
            float upper = Vector3.Distance(arm.joints[1].transform.position, arm.joints[2].transform.position);
            float forearm = Vector3.Distance(arm.joints[2].transform.position, arm.joints[3].transform.position);
            Vector3 delta = target - baseTransform.position;
            float radius = new Vector2(delta.x, delta.z).magnitude;
            float vertical = target.y - shoulder.y + 0.16f;
            float cosine = Mathf.Clamp((radius * radius + vertical * vertical - upper * upper - forearm * forearm) /
                                       (2f * upper * forearm), -1f, 1f);
            float elbow = Mathf.Acos(cosine);
            float shoulderAngle = Mathf.Atan2(radius, vertical) -
                                  Mathf.Atan2(forearm * Mathf.Sin(elbow), upper + forearm * Mathf.Cos(elbow));
            float yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            float shoulderDegrees = shoulderAngle * Mathf.Rad2Deg;
            float elbowDegrees = elbow * Mathf.Rad2Deg;
            return new[] { yaw, shoulderDegrees, elbowDegrees, 0f, 180f - shoulderDegrees - elbowDegrees, 0f };
        }

        private static Transform EnsureChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null) return child;
            var obj = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(obj, "Create " + name);
            obj.transform.SetParent(parent, false);
            return obj.transform;
        }

        public static void RunBatch()
        {
            EditorSceneManager.OpenScene(GearboxDemoBuilder.ScenePath);
            Setup();
        }
    }
}
