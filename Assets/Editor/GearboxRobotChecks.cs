using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GearboxDemo.Editor
{
    [InitializeOnLoad]
    public static class GearboxRobotChecks
    {
        private const string Key = "GearboxRobotChecks.Running";
        private static RobotArmController arm;
        private static RobotPoseController poses;
        private static List<RobotPose> sequence;
        private static int index;
        private static float started;
        private static Vector3 basePosition;
        private static float[] min, max;
        private static Collider[] moving, environment;
        private static bool active;
        static GearboxRobotChecks() { EditorApplication.playModeStateChanged += State; EditorApplication.update += Tick; }
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch-only test entry point.");
            EditorSceneManager.OpenScene(GearboxDemoBuilder.ScenePath);
            GearboxRobotBuilder.Build();
            GearboxRobotBuilder.Build();
            Require(UnityEngine.Object.FindObjectsByType<RobotArmController>().Length == 1, "Repeated builder duplicates robot");
            Require(UnityEngine.Object.FindObjectsByType<ArticulationBody>().Length == 7, "Expected base plus six articulation bodies");
            Require(UnityEngine.Object.FindObjectsByType<GearboxPart>().Length == 7, "Gearbox parts changed");
            File.WriteAllText("robot-playmode-report.txt", "PASS repeatable robot build; seven articulation bodies; existing seven parts retained\n");
            SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode();
        }
        private static void State(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                arm = UnityEngine.Object.FindAnyObjectByType<RobotArmController>();
                poses = arm.GetComponent<RobotPoseController>();
                var named = new[] { poses.home, poses.traySafe, poses.assemblySafe, poses.humanSafe };
                sequence = new List<RobotPose>();
                for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) if (i != j) { sequence.Add(named[i]); sequence.Add(named[j]); }
                min = Enumerable.Repeat(float.PositiveInfinity, 6).ToArray(); max = Enumerable.Repeat(float.NegativeInfinity, 6).ToArray();
                basePosition = arm.transform.Find("Base").position;
                moving = arm.GetComponentsInChildren<Collider>().Where(c => c.GetComponentInParent<ArticulationBody>() != arm.transform.Find("Base").GetComponent<ArticulationBody>()).ToArray();
                environment = UnityEngine.Object.FindObjectsByType<Collider>().Where(c => !c.transform.IsChildOf(arm.transform)).ToArray();
                active = true; index = -1; started = Time.time; Time.timeScale = 6;
            }
            if (state == PlayModeStateChange.EnteredEditMode)
            { SessionState.SetBool(Key, false); EditorApplication.Exit(SessionState.GetInt(Key + ".Result", 1)); }
        }
        private static void Tick()
        {
            if (!active || !EditorApplication.isPlaying) return;
            try
            {
                Require(Vector3.Distance(basePosition, arm.transform.Find("Base").position) < 0.001f, "Base moved");
                for (int i = 0; i < 6; i++)
                {
                    var joint = arm.joints[i]; float angle = joint.Angle;
                    Require(joint.Body.dofCount == 1 && !float.IsNaN(angle) && !float.IsInfinity(angle), "Invalid joint/DOF " + i);
                    Require(Mathf.Abs(joint.Body.jointVelocity[0]) < 3f, "Joint explosion " + i + " angle=" + angle + " velocity=" + joint.Body.jointVelocity[0]);
                    min[i] = Mathf.Min(min[i], angle); max[i] = Mathf.Max(max[i], angle);
                }
                foreach (Collider a in moving) foreach (Collider b in environment)
                    if (b.enabled && !b.isTrigger && a.bounds.Intersects(b.bounds))
                        Require(!Physics.ComputePenetration(a, a.transform.position, a.transform.rotation, b, b.transform.position, b.transform.rotation, out _, out float depth) || depth < 0.003f,
                            "Robot collision: " + a.name + "/" + b.name);
                Require(Time.time - started < 50, "Pose transition timeout: " + poses.Status);
                if (index == -1 && Time.time - started < 2) return;
                if (poses.Busy || arm.IsMoving) return;
                Require(arm.MaxError < 2, "Joint did not settle: " + arm.MaxError);
                if (index >= 0)
                {
                    Require(poses.Status == sequence[index].poseName, "Pose failure: " + poses.Status);
                    File.AppendAllText("robot-playmode-report.txt", $"PASS {index}: {poses.Status}; max angle error {arm.MaxError:F3} deg; grip {arm.gripPoint.position:F3}\n");
                }
                index++;
                if (index >= sequence.Count)
                {
                    for (int i = 0; i < 6; i++) Require(max[i] - min[i] > 5, "Joint " + (i + 1) + " did not move");
                    File.AppendAllText("robot-playmode-report.txt", "PASS all six joints moved; all 12 directed pose pairs; no workcell/part penetration or unstable velocities; immovable base\nALL CHECKS PASSED\n");
                    Finish(0); return;
                }
                poses.RequestPose(sequence[index]); started = Time.time;
            }
            catch (Exception e) { File.AppendAllText("robot-playmode-report.txt", "FAIL " + e + "\n"); Debug.LogException(e); Finish(1); }
        }
        private static void Finish(int code) { active = false; Time.timeScale = 1; SessionState.SetInt(Key + ".Result", code); EditorApplication.ExitPlaymode(); }
        private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    }
}
