using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GearboxDemo
{
    /// <summary>Opt-in ROS 2 rosbridge transport for the simulation, never a physical driver.</summary>
    public sealed class GearboxRosBridge : MonoBehaviour
    {
        [Serializable] private class Stamp { public int sec; public uint nanosec; }
        [Serializable] private class Header { public Stamp stamp; public string frame_id="simulation_base"; }
        [Serializable] private class JointState { public Header header; public string[] name; public double[] position; public double[] velocity=new double[0], effort=new double[0]; }
        [Serializable] private class StringMessage { public string data; }
        [Serializable] private class Envelope { public string op, topic, type; public StringMessage msg; }
        [Serializable] private class JointEnvelope { public string op="publish", topic="/gearbox/simulation/joint_states"; public JointState msg; }
        [Serializable] private class Control { public string request_id, action; }
        [Serializable] private class State { public int completed; public bool running, paused; public string fault, backend_status; }
        [Serializable] private class Ack { public string request_id, action, status, error; }

        private readonly ConcurrentQueue<string> incoming=new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> outgoing=new ConcurrentQueue<string>();
        private readonly Queue<string> recent=new Queue<string>();
        private readonly HashSet<string> handled=new HashSet<string>();
        private readonly CancellationTokenSource lifetime=new CancellationTokenSource();
        private GearboxGameSimulation game;
        private GearboxTrainingClient client;
        private ClientWebSocket socket;
        private Task connection;
        private volatile bool online;
        private float nextPublish;
        private string url;
        public string BridgeUrl="ws://127.0.0.1:9090";
        public bool Connected => online;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install() {
            if(!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEARBOX_ROSBRIDGE_URL")))
                new GameObject("ROS 2 simulation bridge").AddComponent<GearboxRosBridge>();
        }
        private void Start() {
            url=Environment.GetEnvironmentVariable("GEARBOX_ROSBRIDGE_URL");
            if(string.IsNullOrWhiteSpace(url))url=BridgeUrl;
            if(!Uri.TryCreate(url,UriKind.Absolute,out var uri) || (uri.Scheme!="ws" && uri.Scheme!="wss")) {
                Debug.LogError("GEARBOX_ROSBRIDGE_URL must be a ws:// or wss:// URL.");enabled=false;return;
            }
            connection=ConnectLoop(uri,lifetime.Token);
        }
        private async Task ConnectLoop(Uri uri,CancellationToken token) {
            while(!token.IsCancellationRequested) {
                using(var ws=new ClientWebSocket()) {
                    socket=ws;
                    try {
                        await ws.ConnectAsync(uri,token);
                        await Send(ws,"{\"op\":\"advertise\",\"topic\":\"/gearbox/simulation/joint_states\",\"type\":\"sensor_msgs/msg/JointState\"}",token);
                        await Send(ws,"{\"op\":\"advertise\",\"topic\":\"/gearbox/simulation/state\",\"type\":\"std_msgs/msg/String\"}",token);
                        await Send(ws,"{\"op\":\"advertise\",\"topic\":\"/gearbox/simulation/control_ack\",\"type\":\"std_msgs/msg/String\"}",token);
                        await Send(ws,"{\"op\":\"subscribe\",\"topic\":\"/gearbox/simulation/control\",\"type\":\"std_msgs/msg/String\",\"queue_length\":1}",token);
                        online=true;
                        using(var linked=CancellationTokenSource.CreateLinkedTokenSource(token)) {
                            var reader=Read(ws,linked.Token);
                            var writer=Write(ws,linked.Token);
                            await Task.WhenAny(reader,writer);
                            linked.Cancel();ws.Abort();
                            try { await Task.WhenAll(reader,writer); } catch(OperationCanceledException) {} catch(WebSocketException) {}
                        }
                    } catch(OperationCanceledException) {} catch(WebSocketException) {} catch(System.Net.Http.HttpRequestException) {}
                    finally { online=false;socket=null;while(outgoing.TryDequeue(out _)){} while(incoming.TryDequeue(out _)){} }
                }
                try { await Task.Delay(2000,token); } catch(OperationCanceledException) { break; }
            }
        }
        private static Task Send(ClientWebSocket ws,string text,CancellationToken token) {
            var bytes=Encoding.UTF8.GetBytes(text);
            return ws.SendAsync(new ArraySegment<byte>(bytes),WebSocketMessageType.Text,true,token);
        }
        private async Task Write(ClientWebSocket ws,CancellationToken token) {
            while(!token.IsCancellationRequested) {
                if(outgoing.TryDequeue(out var text))await Send(ws,text,token);
                else await Task.Delay(10,token);
            }
        }
        private async Task Read(ClientWebSocket ws,CancellationToken token) {
            var buffer=new byte[4096];var message=new System.IO.MemoryStream();
            using(message)while(!token.IsCancellationRequested) {
                var result=await ws.ReceiveAsync(new ArraySegment<byte>(buffer),token);
                if(result.MessageType==WebSocketMessageType.Close)return;
                if(result.MessageType!=WebSocketMessageType.Text || message.Length+result.Count>65536)throw new WebSocketException("Invalid ROS message");
                message.Write(buffer,0,result.Count);
                if(result.EndOfMessage) {
                    if(incoming.Count>=32)throw new WebSocketException("ROS command queue full");
                    incoming.Enqueue(Encoding.UTF8.GetString(message.ToArray()));message.SetLength(0);
                }
            }
        }
        private void Queue(string text) { if(online && outgoing.Count<64)outgoing.Enqueue(text); }
        private void PublishString(string topic,string data) => Queue(JsonUtility.ToJson(new Envelope{op="publish",topic=topic,msg=new StringMessage{data=data}}));
        private void Update() {
            if(game==null)game=FindFirstObjectByType<GearboxGameSimulation>();
            if(client==null)client=FindFirstObjectByType<GearboxTrainingClient>();
            if(game==null || !game.Ready || client==null)return;
            while(incoming.TryDequeue(out var json)) {
                Control command=null;
                try {
                    var envelope=JsonUtility.FromJson<Envelope>(json);
                    if(envelope.op!="publish" || envelope.topic!="/gearbox/simulation/control")continue;
                    command=JsonUtility.FromJson<Control>(envelope.msg.data);
                    if(string.IsNullOrWhiteSpace(command.request_id) || command.request_id.Length>100)throw new ArgumentException("A unique request_id is required");
                    if(handled.Contains(command.request_id)) {
                        PublishString("/gearbox/simulation/control_ack",JsonUtility.ToJson(new Ack{request_id=command.request_id,action=command.action,status="duplicate"}));continue;
                    }
                    string transcript;
                    switch(command.action) {
                        case "ready": transcript="I'm ready";break;
                        case "pause":case "resume":case "reset":transcript=command.action;break;
                        default:throw new ArgumentException("Allowed actions: ready, pause, resume, reset");
                    }
                    if(!client.IsConnected)throw new InvalidOperationException("Training backend is disconnected");
                    handled.Add(command.request_id);recent.Enqueue(command.request_id);
                    if(recent.Count>256)handled.Remove(recent.Dequeue());
                    client.SubmitTranscript(transcript);
                    PublishString("/gearbox/simulation/control_ack",JsonUtility.ToJson(new Ack{request_id=command.request_id,action=command.action,status="dispatched"}));
                } catch(Exception e) {
                    PublishString("/gearbox/simulation/control_ack",JsonUtility.ToJson(new Ack{request_id=command?.request_id,action=command?.action,status="rejected",error=e.Message}));
                }
            }
            if(!online || Time.realtimeSinceStartup<nextPublish)return;
            nextPublish=Time.realtimeSinceStartup+0.1f;
            var joints=game.References.handover.robotArm.joints;
            var names=new string[6];var positions=new double[6];
            for(int i=0;i<6;i++){names[i]="simulation_joint_"+(i+1);positions[i]=joints[i].Angle*Math.PI/180.0;}
            long milliseconds=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Queue(JsonUtility.ToJson(new JointEnvelope{msg=new JointState{header=new Header{stamp=new Stamp{sec=(int)(milliseconds/1000),nanosec=(uint)(milliseconds%1000)*1000000}},name=names,position=positions}}));
            PublishString("/gearbox/simulation/state",JsonUtility.ToJson(new State{completed=game.Completed,running=game.Running,paused=game.Paused,fault=game.Fault,backend_status=client.BackendStatus}));
        }
        private void OnDestroy() { lifetime.Cancel();socket?.Abort(); }
    }
}
