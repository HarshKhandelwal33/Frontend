using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GearboxDemo.Editor
{
    [InitializeOnLoad]
    public static class GearboxRosChecks
    {
        private const string Key="GearboxRosChecks";
        private static double deadline;
        private static bool started;
        static GearboxRosChecks() {
            EditorApplication.update+=Check;
            EditorApplication.playModeStateChanged+=state=>{
                if(!SessionState.GetBool(Key,false))return;
                if(state==PlayModeStateChange.EnteredPlayMode)deadline=EditorApplication.timeSinceStartup+150;
                if(state==PlayModeStateChange.EnteredEditMode) {
                    SessionState.SetBool(Key,false);
                    EditorApplication.Exit(SessionState.GetInt(Key+"Exit",1));
                }
            };
        }
        public static void RunBatch() {
            try {
                YaskawaCadPreviewBuilder.Build();
                EditorSceneManager.OpenScene("Assets/Scenes/GearboxTraining.unity");
                SessionState.SetBool(Key,true);SessionState.SetInt(Key+"Exit",1);
                EditorApplication.isPlaying=true;
            } catch(Exception e) { Debug.LogException(e);EditorApplication.Exit(1); }
        }
        private static void Check() {
            if(!SessionState.GetBool(Key,false) || !EditorApplication.isPlaying || deadline==0)return;
            if(EditorApplication.timeSinceStartup>deadline) { Finish(1);return; }
            var client=UnityEngine.Object.FindFirstObjectByType<GearboxTrainingClient>();
            if(client==null) {
                var simulation=UnityEngine.Object.FindFirstObjectByType<GearboxGameSimulation>();
                if(simulation!=null && simulation.Ready) {
                    simulation.TrainingMode=true;
                    new GameObject("ROS test training client").AddComponent<GearboxTrainingClient>();
                }
                return;
            }
            if(!started && client!=null && client.IsConnected) {
                var bridge=new GameObject("ROS protocol test bridge").AddComponent<GearboxRosBridge>();
                bridge.BridgeUrl="ws://127.0.0.1:9091";
                started=true;
            }
            const string path=".simulation-check/ros-protocol.json";
            if(started && File.Exists(path)) {
                string report;
                try { report=File.ReadAllText(path); } catch(IOException) { return; }
                if(!report.TrimEnd().EndsWith("}"))return;
                Debug.Log("ROS protocol check: "+report);
                Finish(report.Contains("\"passed\": true")?0:1);
            }
        }
        private static void Finish(int code) {
            SessionState.SetInt(Key+"Exit",code);
            EditorApplication.ExitPlaymode();
        }
    }
}
