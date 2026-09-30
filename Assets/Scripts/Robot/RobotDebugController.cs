using UnityEngine;
using UnityEngine.InputSystem;

namespace GearboxDemo
{
    [RequireComponent(typeof(RobotPoseController))]
    public sealed class RobotDebugController : MonoBehaviour
    {
        private static Rect PoseButtonRect(int index) => new Rect(22, 46 + index * 28, 215, 24);

        private void Update()
        {
            var manager = FindFirstObjectByType<GearboxAssemblyManager>();
            if (manager != null && manager.IsExecuting) return;
            Keyboard keyboard = Keyboard.current;
            var poses = GetComponent<RobotPoseController>();
            if (keyboard != null)
            {
                if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) poses.RequestPose(poses.home);
                else if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) poses.RequestPose(poses.traySafe);
                else if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) poses.RequestPose(poses.assemblySafe);
                else if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame) poses.RequestPose(poses.humanSafe);
            }

            // Input System does not generate IMGUI input; handle panel clicks explicitly.
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            Vector2 point = mouse.position.ReadValue();
            point.y = Screen.height - point.y;
            RobotPose[] named = { poses.home, poses.traySafe, poses.assemblySafe, poses.humanSafe };
            for (int i = 0; i < named.Length; i++)
                if (PoseButtonRect(i).Contains(point)) { poses.RequestPose(named[i]); break; }
        }

        private void OnGUI()
        {
            var poses = GetComponent<RobotPoseController>();
            GUI.Box(new Rect(12, 12, 235, 180), GUIContent.none);
            GUI.Label(new Rect(22, 20, 215, 24), "Robot: " + poses.Status);
            GUI.Box(PoseButtonRect(0), "1 — Home", GUI.skin.button);
            GUI.Box(PoseButtonRect(1), "2 — TraySafe", GUI.skin.button);
            GUI.Box(PoseButtonRect(2), "3 — AssemblySafe", GUI.skin.button);
            GUI.Box(PoseButtonRect(3), "4 — HumanSafe", GUI.skin.button);
            GUI.Label(new Rect(22, 158, 215, 24), "Focus Game view to use number keys.");
        }
    }
}
