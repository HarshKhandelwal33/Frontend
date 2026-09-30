using UnityEngine;
using UnityEngine.InputSystem;

namespace GearboxDemo
{
    // Keep this on Worker; replacing WorkerModel only requires assigning its Animator.
    [RequireComponent(typeof(CharacterController))]
    public sealed class WorkerMovement : MonoBehaviour
    {
        public Animator animator;
        [Tooltip("Optional arrow-key movement; 1–4 play point, pick-up, place and hand-over.")]
        public bool keyboardControl;
        [Min(0.1f)] public float speed = 1.2f;
        private CharacterController body;
        private Vector3 destination;
        private bool moving;
        private float verticalSpeed;

        private void Awake()
        {
            body = GetComponent<CharacterController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        public void MoveTo(Vector3 position) { destination = position; moving = true; }
        public void Stop() { moving = false; if (animator != null) animator.SetFloat("Speed", 0); }
        [ContextMenu("Point")] public void Point() => Action("Point");
        [ContextMenu("Pick Up")] public void PickUp() => Action("PickUp");
        [ContextMenu("Place")] public void Place() => Action("Place");
        [ContextMenu("Hand Over")] public void HandOver() => Action("HandOver");

        private void Action(string trigger)
        {
            if (animator == null || !animator.isActiveAndEnabled) return;
            Stop();
            foreach (string action in new[] { "Point", "PickUp", "Place", "HandOver" }) animator.ResetTrigger(action);
            animator.SetTrigger(trigger);
        }

        private void Update()
        {
            if (animator == null || !animator.isActiveAndEnabled) return;
            Vector3 direction = Vector3.zero;
            Keyboard keys = keyboardControl ? Keyboard.current : null;
            if (keys != null)
            {
                if (keys.digit1Key.wasPressedThisFrame) Point();
                if (keys.digit2Key.wasPressedThisFrame) PickUp();
                if (keys.digit3Key.wasPressedThisFrame) Place();
                if (keys.digit4Key.wasPressedThisFrame) HandOver();
                direction = new Vector3((keys.rightArrowKey.isPressed ? 1 : 0) - (keys.leftArrowKey.isPressed ? 1 : 0), 0,
                    (keys.upArrowKey.isPressed ? 1 : 0) - (keys.downArrowKey.isPressed ? 1 : 0));
                if (direction.sqrMagnitude > 0) moving = false;
            }
            if (moving)
            {
                direction = destination - transform.position;
                direction.y = 0;
                if (direction.magnitude < 0.08f) { moving = false; direction = Vector3.zero; }
            }
            bool acting = animator.GetCurrentAnimatorStateInfo(0).IsTag("Action") ||
                (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsTag("Action"));
            if (acting) direction = Vector3.zero;
            Vector3 velocity = direction.normalized * Mathf.Min(speed, direction.magnitude / Mathf.Max(Time.deltaTime, 0.001f));
            if (velocity.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(velocity), 240 * Time.deltaTime);
            verticalSpeed = body.isGrounded ? -2 : verticalSpeed + Physics.gravity.y * Time.deltaTime;
            body.Move((velocity + Vector3.up * verticalSpeed) * Time.deltaTime);
            Vector3 actual = body.velocity; actual.y = 0;
            animator.SetFloat("Speed", actual.magnitude, 0.1f, Time.deltaTime);
        }
    }
}
