using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace GearboxDemo.Editor {
 public static class GearboxRobotColours {
  const string Folder="Assets/Models/YaskawaRobot/";
  static Material Finish(string name,Color colour,float gloss){
   string path=Folder+name+".mat";
   var material=AssetDatabase.LoadAssetAtPath<Material>(path);
   if(material==null){material=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(material,path);}
   material.color=colour;material.SetFloat("_Metallic",.12f);material.SetFloat("_Glossiness",gloss);
   EditorUtility.SetDirty(material);return material;
  }
  public static void ApplyTo(Transform arm){
   var shell=Finish("RobotWarmGrey",new Color(.76f,.77f,.74f),.32f);
   var charcoal=Finish("RobotCharcoal",new Color(.105f,.13f,.15f),.24f);
   var teal=Finish("RobotMutedTeal",new Color(.20f,.44f,.44f),.3f);
   foreach(var renderer in arm.GetComponentsInChildren<MeshRenderer>(true)){
    Material colour=null;
    switch(renderer.name){
     case "base CAD geometry": case "LeftFinger": case "RightFinger": case "baxis CAD geometry": case "GripperPalm":colour=charcoal;break;
     case "saxis CAD geometry": case "taxis CAD geometry":colour=teal;break;
     case "laxis CAD geometry": case "uaxis CAD geometry": case "raxis CAD geometry":colour=shell;break;
    }
    if(colour==null)continue;
    Undo.RecordObject(renderer,"Warm grey charcoal muted teal finish");renderer.sharedMaterial=colour;
    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);EditorUtility.SetDirty(renderer);
   }
  }
  [MenuItem("Tools/Gearbox Demo/Warm Grey Charcoal Teal Robot")]
  public static void Apply(){
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new System.InvalidOperationException("Exit Play Mode before changing the robot finish.");
   var scene=EditorSceneManager.OpenScene("Assets/Scenes/GearboxTraining.unity");
   var manager=Object.FindAnyObjectByType<GearboxAssemblyManager>();ApplyTo(manager.handover.robotArm.transform);
   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   var camera=Camera.main;var rt=new RenderTexture(1600,1000,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
   var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();File.WriteAllBytes("robot-colours-preview.png",image.EncodeToPNG());
   camera.targetTexture=null;RenderTexture.active=null;Object.DestroyImmediate(image);Object.DestroyImmediate(rt);
   File.WriteAllText("robot-colours-report.txt","Warm light grey links, charcoal base and gripper, muted teal shoulder and wrist accents. Material-only change; transforms, meshes, collision geometry and motion settings preserved.");
  }
  public static void ApplyBatch(){Apply();EditorApplication.Exit(0);}
 }
}

