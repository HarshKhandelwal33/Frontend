using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GearboxDemo.Editor
{
    [InitializeOnLoad]
    public static class GearboxHandoverChecks
    {
        private static bool active;
        private static double deadline;
        static GearboxHandoverChecks() { EditorApplication.update+=Tick; }
        [MenuItem("Tools/Gearbox Demo/Verify First Handover")]
        public static void Request() {
            Directory.CreateDirectory(".simulation-check");
            File.WriteAllText(".simulation-check/check-handover","");
        }
        private static void Tick() {
            if(!EditorApplication.isPlaying)return;
            var game=UnityEngine.Object.FindFirstObjectByType<GearboxGameSimulation>();
            if(game==null || !game.Ready)return;
            const string report=".simulation-check/handover-report.txt";
            if(!active && File.Exists(".simulation-check/check-handover")) {
                File.Delete(".simulation-check/check-handover");
                game.TrainingMode=true;game.ResetGame();game.Step();
                deadline=EditorApplication.timeSinceStartup+240;active=true;
                File.WriteAllText(report,"Testing first handover at normal simulation speed.\n");
            }
            if(!active)return;
            if(game.Fault!=null || EditorApplication.timeSinceStartup>deadline) {
                File.AppendAllText(report,"FAIL "+(game.Fault??"Timed out")+"\n");active=false;return;
            }
            if(game.Completed==1 && !game.Running) {
                File.AppendAllText(report,"PASS first operation completed; handovers="+game.HandoverCount+", maximum tool error="+game.MaxRobotError+" m.\n");
                game.ResetGame();active=false;
                Debug.Log("First handover regression passed; simulation reset.");
            }
        }
    }
}
