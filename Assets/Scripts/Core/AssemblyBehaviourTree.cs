using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GearboxDemo
{
    public enum BehaviourStatus { Ready, Running, Success, Failure }

    public abstract class AssemblyNode
    {
        public readonly string Name;
        public BehaviourStatus Status { get; protected set; }
        protected AssemblyNode(string name) { Name = name; }
        public abstract BehaviourStatus Tick();
    }

    public sealed class AssemblySequence : AssemblyNode
    {
        public readonly List<AssemblyNode> Children = new List<AssemblyNode>();
        public int Index { get; private set; }
        public AssemblySequence(string name, params AssemblyNode[] nodes) : base(name) { Children.AddRange(nodes); }
        public override BehaviourStatus Tick()
        {
            if (Status == BehaviourStatus.Failure || Status == BehaviourStatus.Success) return Status;
            Status = BehaviourStatus.Running;
            if (Index >= Children.Count) return Status = BehaviourStatus.Success;
            var result = Children[Index].Tick();
            if (result == BehaviourStatus.Failure) return Status = result;
            if (result == BehaviourStatus.Success) Index++;
            return Status;
        }
    }

    public sealed class AssemblyCondition : AssemblyNode
    {
        private readonly Func<bool> check;
        public AssemblyCondition(string name, Func<bool> check) : base(name) { this.check = check; }
        public override BehaviourStatus Tick() => Status = check() ? BehaviourStatus.Success : BehaviourStatus.Failure;
    }

    public sealed class AssemblyAction : AssemblyNode
    {
        private readonly MonoBehaviour host;
        private readonly Func<IEnumerator> action;
        private readonly Action<string> report;
        public AssemblyAction(string name, MonoBehaviour host, Func<IEnumerator> action, Action<string> report) : base(name)
        { this.host = host; this.action = action; this.report = report; }
        public override BehaviourStatus Tick()
        {
            if (Status == BehaviourStatus.Ready)
            { Status = BehaviourStatus.Running; report(Name); host.StartCoroutine(Run()); }
            return Status;
        }
        private IEnumerator Run()
        {
            // Flatten nested routines so exceptions produce Failure rather than leaving a node stuck Running.
            var stack = new Stack<IEnumerator>();
            try { stack.Push(action()); } catch (Exception e) { Fail(e); }
            float deadline = Time.time + 90;
            while (stack.Count > 0 && Status == BehaviourStatus.Running)
            {
                object yielded = null;
                try
                {
                    if (Time.time > deadline) throw new TimeoutException(Name);
                    if (!stack.Peek().MoveNext()) { stack.Pop(); continue; }
                    yielded = stack.Peek().Current;
                    if (yielded is IEnumerator nested) { stack.Push(nested); continue; }
                }
                catch (Exception e) { Fail(e); }
                yield return yielded;
            }
            if (Status == BehaviourStatus.Running) Status = BehaviourStatus.Success;
        }
        private void Fail(Exception e)
        { Status = BehaviourStatus.Failure; report("Stopped: " + e.Message); Debug.LogError(Name + ": " + e); }
    }
}
