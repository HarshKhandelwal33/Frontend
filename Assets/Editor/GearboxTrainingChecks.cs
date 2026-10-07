using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GearboxDemo.Editor
{
    [InitializeOnLoad]
    public static class GearboxTrainingChecks
    {
        private const string Key="GearboxTrainingChecks";
        private static int phase, next;
        private static double deadline;
        private static double nextDiagnostic;
        private static Vector3[][] positions=new Vector3[14][];
        private static Quaternion[][] rotations=new Quaternion[14][];
        private static bool[][] installed=new bool[14][];
        private static int[] handovers=new int[14];
        static GearboxTrainingChecks() {
            EditorApplication.update+=Tick;
            EditorApplication.playModeStateChanged+=state=>{
                if(!SessionState.GetBool(Key,false))return;
                File.AppendAllText("training-check-report.txt","Play mode: "+state+"\n");
                if(state==PlayModeStateChange.EnteredPlayMode)deadline=EditorApplication.timeSinceStartup+420;
                if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetInt(Key+"Exit",1));}
            };
        }
        public static void RunBatch() {
            File.WriteAllText("training-check-report.txt","Training checkpoints and voice HUD checks\n");
            SessionState.SetBool(Key,true);SessionState.SetInt(Key+"Exit",1);
            EditorSceneManager.OpenScene("Assets/Scenes/GearboxTraining.unity");
            EditorApplication.EnterPlaymode();deadline=EditorApplication.timeSinceStartup+420;
        }
        [MenuItem("Tools/Gearbox Demo/Open Voice Training")]
        public static void OpenTrainingPreview() {
            EditorSceneManager.OpenScene("Assets/Scenes/GearboxTraining.unity");
            var gameView=EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));
            gameView.Show();gameView.Focus();
            EditorApplication.EnterPlaymode();
        }
        private static void Save(GearboxGameSimulation g) {
            positions[g.Completed]=g.References.allParts.Select(p=>p.transform.position).ToArray();
            rotations[g.Completed]=g.References.allParts.Select(p=>p.transform.rotation).ToArray();
            installed[g.Completed]=g.References.allParts.Select(p=>p.assembled).ToArray();
            handovers[g.Completed]=g.HandoverCount;
        }
        private static void Check(GearboxGameSimulation g,int count) {
            if(g.Completed!=count||g.Payload!=null||g.Holder!=null||g.Running)throw new Exception("Checkpoint state mismatch "+count);
            for(int i=0;i<g.References.allParts.Length;i++) {
                var p=g.References.allParts[i];
                if(p.assembled!=installed[count][i] || Vector3.Distance(p.transform.position,positions[count][i])>.0001f || Quaternion.Angle(p.transform.rotation,rotations[count][i])>.05f)
                    throw new Exception("Checkpoint part mismatch "+count+" "+p.partId);
                if(p.assemblyTarget.occupied!=p.assembled)throw new Exception("Occupancy mismatch "+p.partId);
            }
            if(g.HandoverCount!=handovers[count])throw new Exception("Handover count mismatch");
        }
        private static void Tick() {
            if(!SessionState.GetBool(Key,false))return;
            var g=UnityEngine.Object.FindFirstObjectByType<GearboxGameSimulation>();
            if(EditorApplication.timeSinceStartup>nextDiagnostic) {
                nextDiagnostic=EditorApplication.timeSinceStartup+15;
                File.AppendAllText("training-check-report.txt","Check progress: playing="+EditorApplication.isPlaying+" phase="+phase+" ready="+(g!=null&&g.Ready)+" completed="+(g==null?-1:g.Completed)+"\n");
            }
            if(!EditorApplication.isPlaying)return;
            if(g==null||!g.Ready)return;
            try {
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Training check timeout");
                if(g.Fault!=null)throw new Exception(g.Fault);
                if(phase==0) {
                    g.TrainingMode=true;g.ResetGame();CheckInitial(g);Save(g);next=1;g.Step();Time.timeScale=8;phase=1;return;
                }
                if(phase==1) {
                    if(g.Running)return;
                    if(g.Completed!=next)throw new Exception("Single-step overshoot");
                    Save(g);
                    if(next<13){next++;g.Step();Time.timeScale=8;return;}
                    File.AppendAllText("training-check-report.txt","PASS all 13 readiness-gated operations; saved every boundary.\n");
                    for(int i=12;i>=0;i--){g.RestoreCheckpoint(i);Check(g,i);}
                    File.AppendAllText("training-check-report.txt","PASS backwards 13 to 0: world poses, occupancy, handovers, tree progress, ownership.\n");
                    g.Step();Time.timeScale=8;phase=2;return;
                }
                if(phase==2) {
                    if(g.Payload==null)return;
                    g.RestoreCheckpoint(0);Check(g,0);g.Step();Time.timeScale=8;phase=3;return;
                }
                if(phase==3) {
                    if(g.Running)return;
                    Check(g,1);g.Play();Time.timeScale=8;phase=4;return;
                }
                if(phase==4) {
                    if(g.Tree.Status!=BehaviourStatus.Success)return;
                    if(g.References.allParts.Any(p=>!p.assembled))throw new Exception("Replay incomplete");
                    g.RestoreCheckpoint(7);Check(g,7);g.Play();Time.timeScale=8;phase=5;return;
                }
                if(phase==5) {
                    if(g.Tree.Status!=BehaviourStatus.Success)return;
                    File.AppendAllText("training-check-report.txt","PASS cancel during carry; replay from zero and before internal insertion.\n");
                    g.ResetGame();g.TrainingMode=true;
                    new GameObject("Training UI preview").AddComponent<GearboxTrainingClient>();
                    phase=6;next=0;return;
                }
                if(phase==6 && ++next>30) {
                    ScreenCapture.CaptureScreenshot("training-ui-preview.png");phase=7;next=0;return;
                }
                if(phase==7 && ++next>10) {
                    File.AppendAllText("training-check-report.txt","PASS voice HUD instantiated; no runtime exceptions reported by check.\n");
                    SessionState.SetInt(Key+"Exit",0);EditorApplication.ExitPlaymode();
                }
            } catch(Exception e) {
                File.AppendAllText("training-check-report.txt","FAIL "+e+"\n");
                SessionState.SetInt(Key+"Exit",1);EditorApplication.ExitPlaymode();
            }
        }
        private static void CheckInitial(GearboxGameSimulation g) {
            if(g.Completed!=0||g.Running||g.References.allParts.Any(p=>p.assembled))throw new Exception("Training must start idle");
        }
    }
}
