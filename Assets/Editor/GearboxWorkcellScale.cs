using System;
using System.IO;
using System.Text;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace GearboxDemo.Editor {
 public static class GearboxWorkcellScale {
  [MenuItem("Tools/Gearbox Demo/Correct Entire Workcell Scale")]
  public static void Apply() {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play Mode before editing scale.");
   EditorSceneManager.OpenScene("Assets/Scenes/GearboxTraining.unity");
   var m=UnityEngine.Object.FindAnyObjectByType<GearboxAssemblyManager>();
   var arm=m.handover.robotArm.transform;
   arm.localScale=Vector3.one*.65f;
   var pos=arm.position;pos.y=1.0f;arm.position=pos;
   var g=m.handover.robotGripper;
   g.transform.localScale=Vector3.one/.65f;
   g.openHalfGap=.075f;g.closedHalfGap=.008f;g.captureDistance=.025f;
   foreach(var finger in new[]{g.leftFinger,g.rightFinger}) {
    finger.localScale=new Vector3(.008f,.06f,.016f);
    var fp=finger.localPosition;fp.x=finger==g.leftFinger?-.075f:.075f;
    fp.y=g.attachmentPoint.localPosition.y-.025f;finger.localPosition=fp;
   }
   var palm=g.transform.Find("GripperPalm");
   palm.localScale=new Vector3(.11f,.035f,.065f);
   var pp=palm.localPosition;pp.y=g.attachmentPoint.localPosition.y-.07f;palm.localPosition=pp;
   var game=m.GetComponent<GearboxGameSimulation>();
   game.lidGraspHalfGap=Mathf.Min(game.lidGraspHalfGap,.025f);
   var ordered=m.allParts.OrderBy(p=>p.partType).ThenBy(p=>p.partId).ToArray();
   for(int i=0;i<ordered.Length;i++){var location=ordered[i].transform.position;location.x=-.48f+i%3*.14f;location.z=-.14f+i/3*.17f;ordered[i].transform.position=location;}
   var tray=GameObject.Find("Tray Base").transform;
   tray.localScale=new Vector3(.72f,.05f,.72f);
   var trayLocation=tray.parent.position;trayLocation.x=-.45f;trayLocation.z=.10f;tray.parent.position=trayLocation;
   // Reposition after moving the parent tray, preserving supported part heights.
   for(int i=0;i<ordered.Length;i++){var location=ordered[i].transform.position;location.x=-.48f+i%3*.14f;location.z=-.14f+i/3*.17f;ordered[i].transform.position=location;}
   var station=m.phaseOne.InternalAssemblyRoot.parent;
   var first=new Vector3(.45f*(.65f/.85f),1.105f,.15f+(.50f-.15f)*(.65f/.85f));
   var final=new Vector3(.50f,1.105f,first.z);
   m.phaseOne.InternalAssemblyRoot.position=first;
   m.phaseTwoInsertion.internalAssemblyTarget.position=final;
   foreach(var part in m.allParts) {
    bool internalPart=part.partType!=GearboxPartType.Housing && part.partType!=GearboxPartType.Lid && part.partType!=GearboxPartType.Screw;
    var target=part.assemblyTarget.transform;
    target.position=(internalPart?first:final)-target.rotation*part.transform.Find("Visual").localPosition;
   }
   m.phaseTwoInsertion.worker.position=new Vector3(first.x+.25f,0,.90f);
   var mat=GameObject.Find("Assembly Mat").transform;
   mat.localScale=new Vector3(.65f,.03f,.45f);
   mat.position=new Vector3((first.x+final.x)*.5f,mat.position.y,first.z);
   foreach(var fixtureName in new[]{"Reference Fixture One","Reference Fixture Two"}) {
    var fixture=station.Find(fixtureName);if(fixture==null)continue;fixture.position=fixtureName=="Reference Fixture One"?first:final;
    foreach(Transform support in fixture) {
     var sp=support.localPosition;sp.x=Mathf.Sign(sp.x)*.05f;sp.z=Mathf.Sign(sp.z)*.05f;support.localPosition=sp;
     var ss=support.localScale;ss.x=.018f;ss.z=.018f;support.localScale=ss;
    }
   }
   foreach(var part in m.allParts){PrefabUtility.RecordPrefabInstancePropertyModifications(part.transform);PrefabUtility.RecordPrefabInstancePropertyModifications(part.assemblyTarget.transform);}
   PrefabUtility.RecordPrefabInstancePropertyModifications(m.phaseTwoInsertion.worker);
   Physics.SyncTransforms();
   var report=new StringBuilder("Whole workcell proportions in Unity metres; estimated miniature gearbox dimensions.\n");
   report.AppendLine("Robot uniform scale 0.65; base height 1.0 m; gripper world scale 1; jaw centre opening 150 mm; fingers 60 x 8 x 16 mm; palm 110 x 35 x 65 mm.");
   foreach(var part in m.allParts)report.AppendLine(part.partId+" "+GearboxModelAudit.Measure(part.transform).size.ToString("F4"));
   report.AppendLine("Worker visual bounds: "+(m.phaseTwoInsertion.worker.GetComponent<CharacterController>().height*m.phaseTwoInsertion.worker.lossyScale.y).ToString("F3"));
   EditorUtility.SetDirty(g);EditorUtility.SetDirty(game);
   EditorSceneManager.MarkSceneDirty(m.gameObject.scene);EditorSceneManager.SaveScene(m.gameObject.scene);
   File.WriteAllText("workcell-scale-report.txt",report.ToString());
   var camera=Camera.main;camera.transform.position=new Vector3(2.4f,2.6f,-3.2f);camera.transform.LookAt(new Vector3(0,1.05f,.2f));camera.fieldOfView=42;
   var rt=new RenderTexture(1600,1000,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
   var im=new Texture2D(1600,1000,TextureFormat.RGB24,false);im.ReadPixels(new Rect(0,0,1600,1000),0,0);im.Apply();File.WriteAllBytes("workcell-scale-preview.png",im.EncodeToPNG());
   camera.targetTexture=null;RenderTexture.active=null;UnityEngine.Object.DestroyImmediate(im);UnityEngine.Object.DestroyImmediate(rt);
   EditorSceneManager.MarkSceneDirty(m.gameObject.scene);EditorSceneManager.SaveScene(m.gameObject.scene);
  }
  public static void ApplyBatch(){Apply();EditorApplication.Exit(0);}
 }
}






