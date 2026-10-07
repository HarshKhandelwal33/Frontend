using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GearboxDemo.Editor
{
    [InitializeOnLoad]
    public static class GearboxIntegrationChecks
    {
        private const string Key="GearboxIntegrationChecks";
        private static int phase;
        private static double deadline;
        private static GearboxTrainingClient client;
        static GearboxIntegrationChecks() {
            EditorApplication.update+=Tick;
            EditorApplication.playModeStateChanged+=state=>{
                if(!SessionState.GetBool(Key,false))return;
                if(state==PlayModeStateChange.EnteredPlayMode)deadline=EditorApplication.timeSinceStartup+180;
                if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetInt(Key+"Exit",1));}
            };
        }
        public static void RunBatch() {
            File.WriteAllText("training-integration-report.txt","Live Python/Unity integration; backend must be running.\n");
            SessionState.SetBool(Key,true);SessionState.SetInt(Key+"Exit",1);
            EditorSceneManager.OpenScene("Assets/Scenes/GearboxTraining.unity");EditorApplication.EnterPlaymode();
        }
        private static void Tick() {
            if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||deadline==0)return;
            var g=UnityEngine.Object.FindFirstObjectByType<GearboxGameSimulation>();
            if(g==null||!g.Ready)return;
            try {
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Integration timed out at phase "+phase);
                if(phase==0) {
                    g.TrainingMode=true;g.ResetGame();
                    client=new GameObject("Live integration client").AddComponent<GearboxTrainingClient>();phase=1;return;
                }
                if(phase==1) {
                    if(!client.IsConnected||client.IsThinking)return;
                    File.AppendAllText("training-integration-report.txt","PASS Unity connected and created a backend session.\n");
                    client.SubmitTranscript("I am ready");phase=2;return;
                }
                if(phase==2) {
                    Time.timeScale=5;
                    if(client.AcknowledgedCompleted!=1||g.Running)return;
                    if(g.Completed!=1||g.References.allParts.Count(p=>p.assembled)!=1)throw new Exception("Forward result differs from backend");
                    File.AppendAllText("training-integration-report.txt","PASS spoken readiness transcript -> backend command -> Unity operation -> acknowledged completion.\n");
                    client.SubmitTranscript("go back");phase=3;return;
                }
                if(phase==3) {
                    if(client.AcknowledgedCompleted!=0)return;
                    if(g.Completed!=0||g.References.allParts.Any(p=>p.assembled)||g.Payload!=null)throw new Exception("Restore result differs from backend");
                    File.AppendAllText("training-integration-report.txt","PASS go back -> actual world restoration -> backend acknowledgement.\n");
                    client.SubmitTranscript("stop");phase=4;return;
                }
                if(phase==4) {
                    if(client.BackendStatus!="stopped")return;
                    if(!g.EmergencyStopped)throw new Exception("Local stop failed");
                    client.SubmitTranscript("start again");phase=5;return;
                }
                if(phase==5) {
                    if(client.BackendStatus!="ready"||g.EmergencyStopped)return;
                    File.AppendAllText("training-integration-report.txt","PASS stop and reset synchronize Unity and Python.\n");
                    SessionState.SetInt(Key+"Exit",0);EditorApplication.ExitPlaymode();
                }
            } catch(Exception e) {
                File.AppendAllText("training-integration-report.txt","FAIL "+e+"\n");
                SessionState.SetInt(Key+"Exit",1);EditorApplication.ExitPlaymode();
            }
        }
    }
}
