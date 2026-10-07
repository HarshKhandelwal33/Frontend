using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.InputSystem;

namespace GearboxDemo
{
    /// <summary>Local desktop bridge and compact voice-first training HUD.</summary>
    public sealed partial class GearboxTrainingClient : MonoBehaviour
    {
        [Serializable] private class Operation { public string id, title; }
        [Serializable] private class Session {
            public string id, run_id, workflow_version, status;
            public int revision, completed;
            public Command pending;
            public Operation current_step;
        }
        [Serializable] private class Command {
            public string id, type, run_id, operation_id, supersedes;
            public int expected_revision, target_completed;
        }
        [Serializable] private class Commands { public Command[] commands; }
        [Serializable] private class Request { public string text, action, request_id; }
        [Serializable] private class Answer { public string text; public Session session; }
        [Serializable] private class ApiError { public string detail; }
        [Serializable] private class Result {
            public string command_id, run_id, error;
            public int expected_revision, completed;
            public bool success;
        }
        [Serializable] private class Mic { public bool enabled; }
        [Serializable] private class Spoken { public string text; public float confidence; }
        [Serializable] private class Voice { public bool enabled, listening, speaking; public string error; public Spoken[] utterances; }
        private static readonly string[] OperationIds = {"pin_carrier","input_shaft","planet_1","planet_2","planet_3","output_shaft","housing","insert_internal","lid","screw_1","screw_3","screw_2","screw_4"};
        public string BackendUrl = "http://127.0.0.1:8000";
        public bool IsConnected => connected;
        public bool IsThinking => thinking;
        public int AcknowledgedCompleted => session == null ? -1 : session.completed;
        public string BackendStatus => session == null ? "disconnected" : session.status;
        private GearboxGameSimulation game;
        private Session session;
        private Voice voice = new Voice();
        private string subtitle = "Start the Python backend, then connect to begin.", error = "", heard = "";
        private bool connected, thinking, muted, subtitles = true, micDesired;
        private float voiceLeaseAt;
        private Coroutine execution;
        private string executingId;
        private int generation;
        private readonly Dictionary<string, Result> results = new Dictionary<string, Result>();

        private IEnumerator Start()
        {
            game = FindFirstObjectByType<GearboxGameSimulation>();
            while (game == null || !game.Ready) yield return null;
            var configured = Environment.GetEnvironmentVariable("GEARBOX_BACKEND_URL");
            if (!string.IsNullOrWhiteSpace(configured)) BackendUrl = configured.TrimEnd('/');
            game.ResetGame();
            StartCoroutine(Connect());
            StartCoroutine(PollCommands());
            StartCoroutine(PollVoice());
        }

        private IEnumerator Http(string route, string body, Action<string> ok, Action<string> fail = null, int timeout = 8)
        {
            using (var request = new UnityWebRequest(BackendUrl + route, body == null ? "GET" : "POST"))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                if (body != null) {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                request.timeout = timeout;
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success) ok?.Invoke(request.downloadHandler.text);
                else {
                    string message = request.responseCode == 0 ? "Backend unavailable. Start backend and reconnect." : request.downloadHandler.text;
                    if(request.responseCode!=0) {
                        try { var apiError=JsonUtility.FromJson<ApiError>(message);if(!string.IsNullOrEmpty(apiError.detail))message=apiError.detail; }
                        catch(ArgumentException) { }
                    }
                    fail?.Invoke(message);
                }
            }
        }

        private IEnumerator Connect()
        {
            if (session != null) {
                yield return Http("/sessions/" + session.id, null, text => { session=JsonUtility.FromJson<Session>(text); connected=true; error=""; }, text=>error=text);
                yield break;
            }
            yield return Http("/sessions", "{}", text => {
                session=JsonUtility.FromJson<Session>(text);
                connected=session.workflow_version=="gearbox-v1";
                error=connected?"":"Workflow version mismatch";
                if(connected)subtitle="Connected. The tutor will explain each operation before you confirm readiness.";
            }, text=>error=text);
            if (connected) StartCoroutine(Ask("Explain the current assembly operation and what I should observe."));
        }

        public void SubmitTranscript(string text)
        {
            heard=text;
            string normalized=Regex.Replace(text.ToLowerInvariant(), @"[^\w\s]", "").Trim();
            normalized=Regex.Replace(normalized,@"\s+"," ");
            // These controls act locally even when networking or inference is delayed.
            if (normalized=="pause") { game.Pause();return; }
            if (normalized=="resume") { Resume();return; }
            if (normalized=="stop") { Stop();return; }
            if (normalized=="go back" || normalized=="previous step" || normalized=="go back to previous step" || normalized=="go back to the previous step") { SendAction("previous");return; }
            if (normalized=="reset" || normalized=="start again") { SendAction("reset");return; }
            if(normalized=="im ready" || normalized=="i am ready" || normalized=="next step") { Ready();return; }
            if(normalized=="repeat" || normalized=="repeat the explanation") { Repeat();return; }
            if (!thinking) StartCoroutine(Ask(text));
        }

        private void Ready()
        {
            if(game.Paused && game.Running) { Resume();return; }
            if(!thinking) SendAction("ready");
        }
        private void Resume() { if(game.Running && game.Paused && !game.EmergencyStopped) game.Step(); }
        private void Stop() {
            generation++;thinking=false;
            game.EmergencyStop();
            StartCoroutine(Http("/voice/silence","{}",null));
            SendAction("stop");
        }
        private void Repeat() {
            if(!muted && !voice.speaking && !string.IsNullOrEmpty(subtitle))
                StartCoroutine(Http("/voice/speak",JsonUtility.ToJson(new Request{text=subtitle}),null,text=>error=text));
        }
        private void SendAction(string action) {
            if(!connected || session==null) { error="Connect to the backend first.";return; }
            if(action=="previous" || action=="reset") {
                generation++;thinking=false;
                // Freeze immediately while awaiting a validated restoration command.
                game.Pause();
            }
            StartCoroutine(ActionRequest(action));
        }
        private IEnumerator ActionRequest(string action) {
            string body=JsonUtility.ToJson(new Request{action=action,request_id=Guid.NewGuid().ToString()});
            yield return Http("/sessions/"+session.id+"/actions",body,text=>{
                var answer=JsonUtility.FromJson<Answer>(text);
                if(answer.session.revision>=session.revision)session=answer.session;
                error="";
            },text=>error=text);
        }

        private IEnumerator Ask(string text)
        {
            if(!connected || session==null || thinking) yield break;
            thinking=true;
            int token=++generation;
            // Suppress listening while the tutor thinks as well as while it speaks.
            yield return Http("/voice/microphone",JsonUtility.ToJson(new Mic{enabled=false}),null);
            yield return Http("/sessions/"+session.id+"/messages",JsonUtility.ToJson(new Request{text=text,request_id=Guid.NewGuid().ToString()}),json=>{
                if(token!=generation)return;
                var answer=JsonUtility.FromJson<Answer>(json);
                if(answer.session.revision>=session.revision)session=answer.session;
                subtitle=answer.text;error="";
            },message=>{if(token==generation)error=message;},95);
            if(token!=generation)yield break;
            thinking=false;
            if(string.IsNullOrEmpty(error) && !muted)
                yield return Http("/voice/speak",JsonUtility.ToJson(new Request{text=subtitle}),null,message=>error=message);
            if(micDesired)yield return Http("/voice/microphone",JsonUtility.ToJson(new Mic{enabled=true}),null,message=>error=message);
        }

        private IEnumerator PollCommands()
        {
            while(true) {
                if(connected && session!=null) {
                    yield return Http("/sessions/"+session.id+"/commands",null,json=>{
                        var commands=JsonUtility.FromJson<Commands>(json).commands;
                        if(commands==null || commands.Length==0)return;
                        var c=commands[0];
                        if(c.id==executingId)return;
                        if(execution!=null)StopCoroutine(execution);
                        executingId=c.id;execution=StartCoroutine(Execute(c));
                    },text=>{error=text;connected=false;if(game.Running)game.Pause();});
                }
                yield return new WaitForSecondsRealtime(0.3f);
            }
        }

        private IEnumerator Execute(Command c)
        {
            Result result;
            if(!results.TryGetValue(c.id,out result)) {
                string failure=null;
                try {
                    if(c.run_id!=session.run_id || c.expected_revision!=session.revision) throw new InvalidOperationException("Stale command");
                    switch(c.type) {
                        case "execute_operation":
                            if(c.target_completed!=game.Completed+1 || game.Completed>=13 || c.operation_id!=OperationIds[game.Completed])
                                throw new InvalidOperationException("Unity/backend operation mismatch");
                            game.Step();break;
                        case "restore_checkpoint": game.RestoreCheckpoint(c.target_completed);break;
                        case "reset": game.ResetGame();break;
                        case "stop": game.EmergencyStop();break;
                        case "pause": game.Pause();break;
                        case "resume": Resume();break;
                        default: throw new InvalidOperationException("Unknown command");
                    }
                } catch(Exception e) {failure=e.Message;}
                if(c.type=="execute_operation" && failure==null) {
                    while(game.Running && game.Fault==null)yield return null;
                    failure=game.Fault;
                }
                result=new Result{command_id=c.id,run_id=c.run_id,expected_revision=c.expected_revision,
                    success=failure==null,completed=game.Completed,error=failure??""};
                results[c.id]=result;
            }
            bool accepted=false;
            while(!accepted && executingId==c.id) {
                yield return Http("/sessions/"+session.id+"/results",JsonUtility.ToJson(result),json=>{
                    if(executingId!=c.id)return;
                    session=JsonUtility.FromJson<Session>(json);accepted=true;error="";
                },message=>{error=message;});
                if(!accepted)yield return new WaitForSecondsRealtime(1);
            }
            if(executingId==c.id){executingId=null;execution=null;}
            if(accepted && result.success && c.type!="stop" && c.type!="pause" && c.type!="resume") {
                if(game.Completed<13)StartCoroutine(Ask("Explain the current assembly operation and what I should observe."));
                else { subtitle="Gearbox assembly complete. You can go back to review the last operation.";Repeat(); }
            }
        }

        private IEnumerator PollVoice()
        {
            while(true) {
                if(connected)yield return Http("/voice/status",null,json=>{
                    voice=JsonUtility.FromJson<Voice>(json);
                    if(!string.IsNullOrEmpty(voice.error))error=voice.error;
                    if(!thinking && voice.utterances!=null)
                        foreach(var utterance in voice.utterances)SubmitTranscript(utterance.text);
                });
                // Heartbeat keeps microphone opt-in alive only while Unity is running.
                if(connected && micDesired && !thinking && Time.realtimeSinceStartup-voiceLeaseAt>8) {
                    voiceLeaseAt=Time.realtimeSinceStartup;
                    yield return Http("/voice/microphone",JsonUtility.ToJson(new Mic{enabled=true}),null);
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        private void Update() {
            if(game==null)return;
            var keys=Keyboard.current;
            if(keys!=null && keys.escapeKey.wasPressedThisFrame)Stop();
        }

        private void OnDestroy() {
            // Best effort; server-side microphone lease also expires if Unity exits abruptly.
            if(micDesired) {
                var request=new UnityWebRequest(BackendUrl+"/voice/microphone","POST");
                request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes("{\"enabled\":false}"));
                request.downloadHandler=new DownloadHandlerBuffer();request.SetRequestHeader("Content-Type","application/json");
                request.SendWebRequest().completed+=_=>request.Dispose();
            }
        }
    }
}
