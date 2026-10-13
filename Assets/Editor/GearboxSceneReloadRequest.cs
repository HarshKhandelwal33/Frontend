using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace GearboxDemo.Editor {
 [InitializeOnLoad]
 public static class GearboxSceneReloadRequest {
  const string Request=".simulation-check/reload-corrected-scene.request";
  const string Result=".simulation-check/reload-corrected-scene.result";
  static GearboxSceneReloadRequest(){EditorApplication.update+=Check;}
  static void Check(){
   if(Application.isBatchMode||!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
   if(EditorApplication.isPlayingOrWillChangePlaymode){EditorApplication.isPlaying=false;return;}

   try {
    EditorSceneManager.OpenScene("Assets/Scenes/GearboxTraining.unity",OpenSceneMode.Single);
    var manager=UnityEngine.Object.FindAnyObjectByType<GearboxAssemblyManager>();
    if(manager==null)throw new InvalidOperationException("Assembly manager missing after scene reload.");
    var gripper=manager.handover.robotGripper;
    if(Mathf.Abs(gripper.openHalfGap-.075f)>.001f)throw new InvalidOperationException("Scene does not contain the corrected gripper opening.");
    Selection.activeGameObject=manager.handover.robotArm.gameObject;
    SceneView.RepaintAll();
    File.WriteAllText(Result,"Reloaded corrected GearboxTraining scene at "+DateTime.UtcNow.ToString("O")+"; robot scale="+manager.handover.robotArm.transform.localScale+"; gripper half gap="+gripper.openHalfGap);
    File.Delete(Request);
   }catch(Exception e){File.WriteAllText(Result,"Reload failed: "+e);Debug.LogException(e);}
  }
 }
}

