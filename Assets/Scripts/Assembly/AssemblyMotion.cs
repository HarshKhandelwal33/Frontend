using System;
using System.Collections;
using UnityEngine;

namespace GearboxDemo
{
    /// <summary>Deterministic presentation motion, not a contact/force simulation.</summary>
    public static class AssemblyMotion
    {
        public static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        public static IEnumerator Move(Transform item, Vector3 position, Quaternion rotation,
            float duration, Action follow = null)
        {
            Vector3 from = item.position;
            Quaternion orientation = item.rotation;
            float elapsed = 0;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Ease(elapsed / Mathf.Max(0.01f, duration));
                item.SetPositionAndRotation(Vector3.Lerp(from, position, t), Quaternion.Slerp(orientation, rotation, t));
                foreach (Rigidbody body in item.GetComponentsInChildren<Rigidbody>())
                {
                    body.position = body.transform.position;
                    body.rotation = body.transform.rotation;
                }
                follow?.Invoke();
                yield return null;
            }
            item.SetPositionAndRotation(position, rotation);
            follow?.Invoke();
        }

        public static IEnumerator Seat(Transform item, Transform target, float duration, Action follow = null)
        {
            // Lift before lateral transport, align above the bore, then insert axially.
            Vector3 approach = target.position + target.up * 0.14f;
            Vector3 lift = item.position + Vector3.up * Mathf.Max(0.08f, approach.y - item.position.y);
            yield return Move(item, lift, item.rotation, duration * 0.3f, follow);
            yield return Move(item, approach, target.rotation, duration * 0.65f, follow);
            yield return new WaitForSeconds(0.2f);
            yield return Move(item, target.position, target.rotation, duration * 0.75f, follow);
        }
    }
}
