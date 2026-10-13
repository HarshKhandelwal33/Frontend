using System;
using UnityEditor;
using UnityEngine;

namespace GearboxDemo.Editor
{
    public static class YaskawaCadPreviewBuilder
    {
        [MenuItem("Tools/Gearbox Demo/Yaskawa/Create CAD Preview Prefab")]
        public static void Build() {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play Mode before building the CAD preview.");
            AssetDatabase.Refresh();
            var root=new GameObject("Yaskawa CAD reference - uncalibrated");
            try {
                foreach(var name in new[]{"base","saxis","laxis","uaxis","raxis","baxis","taxis"}) {
                    var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/YaskawaRobot/"+name+".obj");
                    if(model==null)throw new InvalidOperationException("Missing converted mesh: "+name);
                    var link=(GameObject)PrefabUtility.InstantiatePrefab(model,root.transform);
                    link.name=name;link.transform.localPosition=Vector3.zero;link.transform.localRotation=Quaternion.identity;link.transform.localScale=Vector3.one;
                }
                const string path="Assets/Models/YaskawaRobot/YaskawaCadReference.prefab";
                PrefabUtility.SaveAsPrefabAsset(root,path);
                Selection.activeObject=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Debug.Log("Yaskawa CAD reference prefab created. Joint calibration and controller limits are required before replacing the workflow rig.");
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
