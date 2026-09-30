using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GearboxDemo.Editor
{
    public static class WorkerAnimationSetup
    {
        private const string Folder = "Assets/Prefabs/Human";
        private static readonly string[] Actions = { "Point", "PickUp", "Place", "HandOver" };
        private static HumanPose restPose;

        [MenuItem("Tools/Gearbox Demo/Setup Worker Animations")]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != GearboxDemoBuilder.ScenePath)
                throw new InvalidOperationException("Open GearboxAssembly first.");
            string path = Folder + "/Worker.controller";
            var reference = PrefabUtility.LoadPrefabContents(Folder + "/Worker.prefab");
            try
            {
                var rig = reference.GetComponentInChildren<Animator>();
                using (var handler = new HumanPoseHandler(rig.avatar, rig.transform)) handler.GetHumanPose(ref restPose);
            }
            finally { PrefabUtility.UnloadPrefabContents(reference); }
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            var machine = controller.layers[0].stateMachine;
            foreach (var state in machine.states) machine.RemoveState(state.state);
            foreach (var transition in machine.anyStateTransitions) machine.RemoveAnyStateTransition(transition);
            controller.parameters = Array.Empty<AnimatorControllerParameter>();
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            var idle = machine.AddState("Idle"); idle.motion = Clip("Idle", 2, true);
            var walk = machine.AddState("Walk"); walk.motion = Clip("Walk", 1, true);
            machine.defaultState = idle;
            var start = idle.AddTransition(walk); start.duration = 0.15f; start.AddCondition(AnimatorConditionMode.Greater, 0.05f, "Speed");
            var stop = walk.AddTransition(idle); stop.duration = 0.15f; stop.AddCondition(AnimatorConditionMode.Less, 0.05f, "Speed");
            foreach (string action in Actions)
            {
                controller.AddParameter(action, AnimatorControllerParameterType.Trigger);
                var state = machine.AddState(action); state.tag = "Action"; state.motion = Clip(action, 2, false);
                var enter = machine.AddAnyStateTransition(state); enter.duration = 0.12f; enter.canTransitionToSelf = false;
                enter.AddCondition(AnimatorConditionMode.If, 0, action);
                var leave = state.AddTransition(idle); leave.hasExitTime = true; leave.exitTime = 1; leave.duration = 0.15f;
            }
            var prefab = PrefabUtility.LoadPrefabContents(Folder + "/Worker.prefab");
            try { Configure(prefab, controller); PrefabUtility.SaveAsPrefabAsset(prefab, Folder + "/Worker.prefab"); }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            var worker = GameObject.Find("Worker");
            if (worker == null) { GearboxWorkerBuilder.Build(); worker = GameObject.Find("Worker"); }
            Configure(worker, controller);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(worker.scene);
            EditorSceneManager.SaveScene(worker.scene);
            Debug.Log("Worker ready: Humanoid, six retargetable animation clips, controller and movement; existing model and placement retained.");
        }

        private static void Configure(GameObject worker, AnimatorController controller)
        {
            var animator = worker.GetComponentInChildren<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman || !animator.avatar.isValid)
                throw new InvalidOperationException("Worker requires a valid Humanoid Avatar.");
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var body = worker.GetComponent<CharacterController>();
            if (body == null) body = worker.AddComponent<CharacterController>();
            body.height = 1.86f; body.radius = 0.25f; body.center = Vector3.up * 0.93f;
            body.skinWidth = 0.015f; body.stepOffset = 0.2f; body.minMoveDistance = 0;
            var movement = worker.GetComponent<WorkerMovement>();
            if (movement == null) movement = worker.AddComponent<WorkerMovement>();
            movement.animator = animator;
            EditorUtility.SetDirty(animator); EditorUtility.SetDirty(body); EditorUtility.SetDirty(movement);
            if (PrefabUtility.IsPartOfPrefabInstance(worker))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
                PrefabUtility.RecordPrefabInstancePropertyModifications(body);
                PrefabUtility.RecordPrefabInstancePropertyModifications(movement);
            }
        }

        // Human muscle curves work with any replacement Humanoid model, independent of bone names.
        private static AnimationClip Clip(string name, float duration, bool loop)
        {
            string path = Folder + "/" + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { clip = new AnimationClip { name = name, frameRate = 30 }; AssetDatabase.CreateAsset(clip, path); }
            clip.ClearCurves();
            void Curve(string muscle, params float[] values)
            {
                if (!HumanTrait.MuscleName.Contains(muscle)) throw new InvalidOperationException("Unknown muscle " + muscle);
                var keys = values.Select((v, i) => new Keyframe(duration * i / (values.Length - 1), v)).ToArray();
                var curve = new AnimationCurve(keys);
                for (int i = 0; i < keys.Length; i++) curve.SmoothTangents(i, 0);
                string property = muscle;
                var parts = muscle.Split(' ');
                if (parts.Length >= 3 && new[] { "Thumb", "Index", "Middle", "Ring", "Little" }.Contains(parts[1]))
                    property = parts[0] + "Hand." + parts[1] + (parts[2] == "Spread" ? " " : ".") + string.Join(" ", parts.Skip(2));
                clip.SetCurve("", typeof(Animator), property, curve);
            }
            for (int i = 0; i < HumanTrait.MuscleCount; i++) Curve(HumanTrait.MuscleName[i], restPose.muscles[i], restPose.muscles[i]);
            // Muscle-only clips otherwise put the body center at ground level.
            void Constant(string property, float value) => clip.SetCurve("", typeof(Animator), property, AnimationCurve.Constant(0, duration, value));
            Constant("RootT.x", restPose.bodyPosition.x); Constant("RootT.y", restPose.bodyPosition.y); Constant("RootT.z", restPose.bodyPosition.z);
            Constant("RootQ.x", restPose.bodyRotation.x); Constant("RootQ.y", restPose.bodyRotation.y);
            Constant("RootQ.z", restPose.bodyRotation.z); Constant("RootQ.w", restPose.bodyRotation.w);
            foreach (string side in new[] { "Left", "Right" })
            {
                Curve(side + " Arm Down-Up", -0.85f, -0.85f);
                Curve(side + " Forearm Stretch", 0.8f, 0.8f);
            }
            Curve("Chest Front-Back", 0, 0.025f, 0);
            if (name == "Walk")
            {
                foreach (string side in new[] { "Left", "Right" })
                {
                    float sign = side == "Left" ? 1 : -1;
                    Curve(side + " Upper Leg Front-Back", 0.32f * sign, 0, -0.32f * sign, 0, 0.32f * sign);
                    Curve(side + " Lower Leg Stretch", 0.9f, side == "Left" ? 0.15f : 0.9f, 0.9f, side == "Left" ? 0.9f : 0.15f, 0.9f);
                    Curve(side + " Arm Front-Back", -0.22f * sign, 0, 0.22f * sign, 0, -0.22f * sign);
                }
            }
            if (name == "Point" || name == "HandOver")
            {
                float reachHeight = name == "Point" ? -0.25f : -0.5f;
                Curve("Right Arm Down-Up", -0.85f, reachHeight, reachHeight, -0.85f);
                Curve("Right Arm Front-Back", 0, -0.75f, -0.75f, 0);
                Curve("Right Forearm Stretch", 0.8f, name == "Point" ? 0.95f : 0.75f, 0.95f, 0.8f);
            }
            if (name == "Point")
                foreach (string finger in new[] { "Index", "Middle", "Ring", "Little" })
                    for (int joint = 1; joint <= 3; joint++)
                        Curve("Right " + finger + " " + joint + " Stretched", 0, finger == "Index" ? 1 : -0.7f, finger == "Index" ? 1 : -0.7f, 0);
            if (name == "PickUp" || name == "Place")
            {
                Curve("Spine Front-Back", 0, -0.25f, -0.25f, 0);
                Curve("Chest Front-Back", 0, -0.15f, -0.15f, 0);
                foreach (string side in new[] { "Left", "Right" })
                {
                    Curve(side + " Arm Front-Back", 0, -0.7f, -0.7f, 0);
                    Curve(side + " Arm Down-Up", -0.85f, -0.65f, -0.65f, -0.85f);
                    Curve(side + " Forearm Stretch", 0.8f, name == "PickUp" ? 0.9f : 0.6f, name == "PickUp" ? 0.6f : 0.9f, 0.8f);
                }
            }
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop; settings.loopBlend = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        public static void RunBatch()
        {
            EditorSceneManager.OpenScene(GearboxDemoBuilder.ScenePath);
            Setup();
            GearboxWorkerBuilder.Validate();
        }
    }
}
