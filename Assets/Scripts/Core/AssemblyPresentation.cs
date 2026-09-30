using UnityEngine;
using UnityEngine.InputSystem;

namespace GearboxDemo
{
    [RequireComponent(typeof(GearboxAssemblyManager))]
    public sealed class AssemblyPresentation : MonoBehaviour
    {
        private GearboxAssemblyManager manager;
        private Camera view;
        private Vector3 overview;
        private Quaternion overviewRotation;
        private float overviewFov;
        private int mode;

        private void Start()
        {
            manager = GetComponent<GearboxAssemblyManager>(); view = Camera.main;
            if (view == null) return;
            overview = view.transform.position; overviewRotation = view.transform.rotation; overviewFov = view.fieldOfView;
            view.nearClipPlane = 0.015f;
        }

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            Vector2 point = mouse.position.ReadValue(); point.y = Screen.height - point.y;
            float y = Screen.height - 86f;
            if (new Rect(24, y + 32, 118, 28).Contains(point)) mode = 0;
            if (new Rect(152, y + 32, 118, 28).Contains(point)) mode = 1;
        }

        private void LateUpdate()
        {
            if (view == null || Time.timeScale == 0) return;
            Vector3 focus = manager.StepIndex < 6 ? manager.phaseOne.InternalAssemblyRoot.position : manager.phaseTwoInsertion.internalAssemblyTarget.position;
            focus += Vector3.up * 0.085f;
            Vector3 position = mode == 0 ? overview : focus + new Vector3(-0.52f, 0.48f, -0.65f);
            Quaternion rotation = mode == 0 ? overviewRotation : Quaternion.LookRotation(focus - position);
            float blend = 1f - Mathf.Exp(-3f * Time.deltaTime);
            view.transform.SetPositionAndRotation(Vector3.Lerp(view.transform.position, position, blend), Quaternion.Slerp(view.transform.rotation, rotation, blend));
            view.fieldOfView = Mathf.Lerp(view.fieldOfView, mode == 0 ? overviewFov : 38f, blend);
        }

        private void OnGUI()
        {
            if (manager == null) return;
            float y = Screen.height - 86f;
            GUI.Box(new Rect(12, y, 520, 74), "REFERENCE ASSEMBLY  |  " + (manager.StepIndex < 6 ? "01  WORKER + ROBOT ASSIST" : "02  HOUSING + CLOSURE"));
            if (GUI.Button(new Rect(24, y + 32, 118, 28), "Workcell view")) mode = 0;
            if (GUI.Button(new Rect(152, y + 32, 118, 28), "Assembly detail")) mode = 1;
            GUI.Label(new Rect(282, y + 36, 240, 24), "Fixture supported • CAD parts");
        }
    }
}
