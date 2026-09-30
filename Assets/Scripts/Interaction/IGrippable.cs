using UnityEngine;

namespace GearboxDemo
{
    public interface IGrippable
    {
        Transform GripPoint { get; }
        bool CanBeGrabbed();
        void OnGrabbed();
        void OnReleased();
    }
}
