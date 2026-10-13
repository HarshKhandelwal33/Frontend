using UnityEditor;
using UnityEngine;

namespace GearboxDemo.Editor
{
    public static class GearboxRosTools
    {
        [MenuItem("Tools/Gearbox Demo/ROS 2/Connect Simulation Bridge")]
        public static void Connect() {
            if(!EditorApplication.isPlaying) { Debug.LogWarning("Enter Play Mode first, then connect the ROS 2 simulation bridge.");return; }
            var bridge=Object.FindFirstObjectByType<GearboxRosBridge>();
            if(bridge==null)bridge=new GameObject("ROS 2 simulation bridge").AddComponent<GearboxRosBridge>();
            Selection.activeGameObject=bridge.gameObject;
        }
    }
}
