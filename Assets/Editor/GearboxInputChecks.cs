using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace GearboxDemo.Editor
{
    // Explicit batch-only integration check: inject device events into the normal player loop.
    [InitializeOnLoad]
    public static class GearboxInputChecks
    {
        private const string Running = "Gearbox.InputChecks";
        private static Keyboard keyboard;
        private static Mouse mouse;
        private static RobotPoseController poses;
        private static int index;
        private static float started;
        private static bool active, observed;
        private static readonly Key[] Keys = { Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad1 };
        private static readonly string[] Names = { "TraySafe", "AssemblySafe", "HumanSafe", "Home" };

        static GearboxInputChecks()
        {
            EditorApplication.playModeStateChanged += State;
            EditorApplication.update += Tick;
        }
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch-only verification.");
            EditorSceneManager.OpenScene(GearboxDemoBuilder.ScenePath);
            File.WriteAllText("input-check-report.txt", "Input System integration checks\n");
            SessionState.SetBool(Running, true);
            EditorApplication.EnterPlaymode();
        }
        private static void State(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Running, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                // Batch mode has no focused Game view. This override is transient test state only.
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                keyboard = InputSystem.AddDevice<Keyboard>();
                mouse = InputSystem.AddDevice<Mouse>();
                poses = UnityEngine.Object.FindAnyObjectByType<RobotPoseController>();
                Time.timeScale = 6; active = true; index = 0; Send();
            }
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(Running, false);
                EditorApplication.Exit(SessionState.GetInt(Running + ".Exit", 1));
            }
        }
        private static void Send()
        {
            observed = false; started = Time.time;
            if (index < 8) InputSystem.QueueStateEvent(keyboard, new KeyboardState(Keys[index]));
            else
            {
                int button = (index + 1) % 4;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(100, Screen.height - (58 + button * 28)), buttons = 1 });
            }
        }
        private static void Tick()
        {
            if (!active || !EditorApplication.isPlaying) return;
            try
            {
                if (poses.Busy && !observed)
                {
                    observed = true;
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    InputSystem.QueueStateEvent(mouse, new MouseState());
                }
                if (Time.time - started > 50) throw new InvalidOperationException("Input did not complete: " + index + ", " + poses.Status);
                if (!observed || poses.Busy) return;
                if (poses.Status != Names[index % 4]) throw new InvalidOperationException("Incorrect pose from input " + index);
                File.AppendAllText("input-check-report.txt", "PASS " + (index < 8 ? Keys[index].ToString() : "Mouse button " + ((index + 1) % 4 + 1)) + " -> " + poses.Status + "\n");
                index++;
                if (index == 12) { File.AppendAllText("input-check-report.txt", "ALL 12 INPUT CHECKS PASSED\n"); Finish(0); }
                else Send();
            }
            catch (Exception exception) { File.AppendAllText("input-check-report.txt", "FAIL " + exception + "\n"); Debug.LogException(exception); Finish(1); }
        }
        private static void Finish(int code)
        {
            active = false;
            InputSystem.RemoveDevice(keyboard); InputSystem.RemoveDevice(mouse);
            Time.timeScale = 1;
            SessionState.SetInt(Running + ".Exit", code); EditorApplication.ExitPlaymode();
        }
    }
}
