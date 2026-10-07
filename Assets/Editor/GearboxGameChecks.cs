using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GearboxDemo.Editor
{
    [InitializeOnLoad]
    public static class GearboxGameChecks
    {
        private const string Key="GameAssemblyChecks";
        private static double deadline;
        private static int stage=-1;
        private static bool initialized;
        private static int handovers;
        private static int verificationPhase;
        private static double pauseStarted;
        private static Vector3 pausedPosition;
        private static Quaternion pausedRotation;
        static GearboxGameChecks()
        {
            EditorApplication.update+=Tick;
            EditorApplication.playModeStateChanged+=state=>
            {
                if(!SessionState.GetBool(Key,false))return;
                if(state==PlayModeStateChange.EnteredPlayMode)deadline=EditorApplication.timeSinceStartup+900;
                if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetInt(Key+"Exit",1));}
            };
        }
        private static string ReportPath => SessionState.GetBool(Key + "Equipment", false) ? "equipment-scale-workflow-report.txt" : SessionState.GetBool(Key + "Single", false) ? "worker-scale-workflow-report.txt" : SessionState.GetBool(Key + "Late", false) ? "collaboration-late-workflow-report.txt" : "game-simulation-report.txt";
        public static void RunLateBatch() { SessionState.SetInt(Key + "Prefix", 6); SessionState.SetBool(Key + "Late", true); RunBatch(); }
        public static void RunEquipmentScaleBatch() { GearboxCollaborationChecks.CompactEquipmentBatch(); GearboxCollaborationChecks.RunStaticBatch(); SessionState.SetBool(Key + "Equipment", true); SessionState.SetBool(Key + "Single", true); RunBatch(); }
        public static void RunWorkerScaleBatch() { GearboxCollaborationChecks.ResizeWorkerBatch(); SessionState.SetBool(Key + "Single", true); RunBatch(); }
        public static void RunFinalBatch() { GearboxCollaborationChecks.RunStaticBatch(); RunBatch(); }
        public static void RunLidBatch() { SessionState.SetInt(Key + "Prefix", 8); SessionState.SetBool(Key + "Late", true); RunBatch(); }
        public static void RunBatch()
        {
            EditorSceneManager.OpenScene(GearboxDemoBuilder.ScenePath);
            if (UnityEngine.Object.FindAnyObjectByType<GearboxAssemblyManager>() == null)
                EditorSceneManager.OpenScene("Assets/Scenes/GearboxTraining.unity");
            File.WriteAllText(ReportPath,"Behaviour tree / holder contact / reach / visibility / reset checks\n");
            SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
        }
        private static void Tick()
        {
            if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||deadline==0)return;
            var game=UnityEngine.Object.FindFirstObjectByType<GearboxGameSimulation>();
            if(game==null||!game.Ready)return;
            try
            {
                if(!initialized)
                {
                    initialized=true;
                    game.EmergencyStop();
                    game.Play(); game.Step();
                    if (!game.EmergencyStopped || game.Running || Time.timeScale != 0)
                        throw new Exception("Emergency stop did not latch against resume / step");
                    game.ResetGame();
                    if (SessionState.GetBool(Key + "Late", false))
                    {
                        foreach (var part in game.References.allParts.Where(p => p.partType != GearboxPartType.Housing && p.partType != GearboxPartType.Lid && p.partType != GearboxPartType.Screw))
                        {
                            part.transform.SetParent(game.References.phaseOne.InternalAssemblyRoot, true);
                            part.transform.SetPositionAndRotation(part.assemblyTarget.transform.position, part.assemblyTarget.transform.rotation);
                            if (!part.assemblyTarget.TryPlace(part)) throw new Exception("Late-workflow fixture failed: " + part.partId);
                        }
                        if (SessionState.GetInt(Key + "Prefix", 6) == 8)
                        {
                            var housing = game.References.phaseTwoInsertion.housing;
                            housing.transform.SetParent(game.References.phaseTwoInsertion.finalGearboxRoot, true);
                            housing.transform.SetPositionAndRotation(housing.assemblyTarget.transform.position, housing.assemblyTarget.transform.rotation);
                            if (!housing.assemblyTarget.TryPlace(housing)) throw new Exception("Housing fixture placement failed");
                            game.References.phaseOne.InternalAssemblyRoot.SetParent(game.References.phaseTwoInsertion.finalGearboxRoot, true);
                            game.References.phaseOne.InternalAssemblyRoot.position = game.FinalOrigin;
                        }
                        game.SendMessage("RefreshHousingCollision");
                        game.Tree.RestoreCompletedPrefix(SessionState.GetInt(Key + "Prefix", 6));
                        File.AppendAllText(ReportPath, "SETUP first " + SessionState.GetInt(Key + "Prefix", 6) + " operations bypassed with prepared fixture; this run tests only later operations and replay.\n");
                    }
                    if (SessionState.GetBool(Key + "Single", false)) { verificationPhase = 3; game.Step(); } else game.Play();
                    if (game.EmergencyStopped || !game.Running) throw new Exception("Reset did not clear emergency stop");
                    File.AppendAllText(ReportPath,"PASS emergency stop blocks resume and step until reset.\n");
                    if(!SessionState.GetBool(Key + "Late", false) && game.References.allParts.Any(p=>p.assembled))throw new Exception("Preassembled part at start");
                    Capture("game-start.png");
                    Time.timeScale=3;
                }
                if(game.Fault!=null)throw new Exception(game.Fault);
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Timed out: "+game.ActionName);
                if(game.Robot.JointPositions.Skip(2).Any(p=>p.y<1.025f))
                    throw new Exception("Robot link below worktop: "+game.ActionName);
                if(verificationPhase==1)
                {
                    if(game.Payload==null)return;
                    game.Pause();pausedPosition=game.Payload.position;pausedRotation=game.Payload.rotation;
                    pauseStarted=EditorApplication.timeSinceStartup;verificationPhase=2;return;
                }
                if(verificationPhase==2)
                {
                    if(EditorApplication.timeSinceStartup-pauseStarted<0.35)return;
                    if(Vector3.Distance(game.Payload.position,pausedPosition)>0.0001f||Quaternion.Angle(game.Payload.rotation,pausedRotation)>0.01f)
                        throw new Exception("Paused payload moved");
                    game.ResetGame();
                    if(game.Payload!=null||game.Completed!=0||game.References.allParts.Any(p=>p.assembled))throw new Exception("Reset while carrying failed");
                    File.AppendAllText(ReportPath,"PASS pause freezes held payload; reset during carry releases ownership and restores rack.\n");
                    game.Step();Time.timeScale=3;verificationPhase=3;return;
                }
                if(verificationPhase==3)
                {
                    if(game.Running)return;
                    if(game.Completed!=1||game.References.allParts.Count(p=>p.assembled)!=1)throw new Exception("Step did not stop after one operation");
                    File.AppendAllText(ReportPath,"PASS replay after reset; single-step stops after one complete operation.\n");
                    if (SessionState.GetBool(Key + "Single", false)) Capture(SessionState.GetBool(Key + "Equipment", false) ? "equipment-scale-complete.png" : "worker-scale-complete.png");
                    SessionState.SetInt(Key+"Exit",0);EditorApplication.ExitPlaymode();return;
                }
                if(game.Payload!=null)
                {
                    if(game.Holder==null||game.Payload.parent!=game.Holder)throw new Exception("Detached moving payload");
                    var bounds=game.Payload.GetComponentsInChildren<Renderer>().Select(r=>r.bounds).ToArray();
                    if(bounds.Length>0&&bounds.Min(b=>b.min.y)<0.995f)throw new Exception("Payload below tabletop: "+game.ActionName);
                }
                if(game.HandoverCount!=handovers)
                {
                    handovers=game.HandoverCount;
                    var p=game.ActivePart;
                    float gap=Vector3.Distance(game.Holder.position,game.WorkerGraspPosition(p));
                    if(gap>0.025f)throw new Exception("Handover contact gap: "+gap);
                    File.AppendAllText(ReportPath,"PASS handover "+handovers+" above table / contact gap "+gap.ToString("F4")+" m\n");
                    Capture("game-handover.png");
                }
                if(stage!=game.Completed)
                {
                    stage=game.Completed;
                    File.AppendAllText(ReportPath,"Tree progress "+stage+"/13 | "+game.ActionName+"\n");
                    Capture("game-stage-"+stage.ToString("00")+".png");
                }
                if(game.Tree.Status!=BehaviourStatus.Success)return;
                if(game.References.allParts.Any(p=>!p.assembled)||game.References.allTargets.Any(t=>!t.occupied))throw new Exception("Final assembly incomplete");
                foreach(var p in game.References.allParts)
                {
                    bool internalPart=p.transform.IsChildOf(game.References.phaseOne.InternalAssemblyRoot);
                    Vector3 expected=p.assemblyTarget.transform.position+(internalPart?game.FinalOrigin-game.FirstOrigin:Vector3.zero);
                    if(Vector3.Distance(p.transform.position,expected)>0.003f)throw new Exception("Final alignment: "+p.partId);
                }
                File.AppendAllText(ReportPath,(SessionState.GetBool(Key + "Late", false) ? "PASS later operations only (prepared fixture); " : "PASS all 13 operations; ") + "12 installed parts; 12 occupied targets; final alignment.\nMaximum robot endpoint error: "+game.MaxRobotError+"; hand endpoint error: "+game.MaxHandError+"\n");
                Capture("game-complete.png");
                game.ResetGame();
                if(game.Completed!=0||game.Payload!=null||game.References.allParts.Any(p=>p.assembled)||game.References.allTargets.Any(t=>t.occupied))throw new Exception("Reset failed");
                File.AppendAllText(ReportPath,"PASS reset clears tree, ownership, installed flags and target occupancy.\n");
                verificationPhase=1;game.Play();Time.timeScale=3;
            }
            catch(Exception e)
            {
                Capture("game-failure.png");
                File.AppendAllText(ReportPath,"FAIL "+e+"\n");
                SessionState.SetInt(Key+"Exit",1);EditorApplication.ExitPlaymode();
            }
        }
        private static void Capture(string path)
        {
            if (SessionState.GetBool(Key + "Late", false)) path = "late-" + path;
            var c=Camera.main;var rt=new RenderTexture(1440,900,24);var old=RenderTexture.active;
            c.targetTexture=rt;c.Render();RenderTexture.active=rt;
            var texture=new Texture2D(1440,900,TextureFormat.RGB24,false);
            texture.ReadPixels(new Rect(0,0,1440,900),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());
            c.targetTexture=null;RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
