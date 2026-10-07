using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GearboxDemo
{
    public sealed partial class GearboxGameSimulation
    {
        private sealed class SavedTransform
        {
            public Transform item, parent;
            public Vector3 position, scale;
            public Quaternion rotation;
        }
        private sealed class Checkpoint
        {
            public SavedTransform[] poses;
            public bool[] assembled, pickable;
            public GearboxPart[] occupants;
            public float[] angles;
            public int handovers;
        }
        private void SaveCheckpoint()
        {
            if (Payload != null) throw new InvalidOperationException("Cannot checkpoint a carried payload");
            var transforms = new HashSet<Transform>();
            var roots = new List<Transform> { worker, robot.Root, m.phaseOne.InternalAssemblyRoot, m.phaseTwoInsertion.finalGearboxRoot };
            roots.AddRange(m.allParts.Select(p => p.transform));
            foreach (var root in roots)
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) transforms.Add(t);
            checkpoints[Completed] = new Checkpoint {
                poses = transforms.Select(t => new SavedTransform { item=t, parent=t.parent,
                    position=t.localPosition, rotation=t.localRotation, scale=t.localScale }).ToArray(),
                assembled=m.allParts.Select(p=>p.assembled).ToArray(),
                pickable=m.allParts.Select(p=>p.canBePicked).ToArray(),
                occupants=m.allTargets.Select(t=>t.Occupant).ToArray(),
                angles=robot.Angles, handovers=HandoverCount
            };
        }
        public void RestoreCheckpoint(int completed)
        {
            if (EmergencyStopped || Fault != null) throw new InvalidOperationException("Reset required after stop or failure");
            if (!checkpoints.TryGetValue(completed, out var saved)) throw new InvalidOperationException("Checkpoint unavailable");
            StopAllCoroutines(); Running=false; Paused=false; singleStep=false; Time.timeScale=1;
            Payload=null; Holder=null; ActivePart=null;
            foreach (var target in m.allTargets) target.ClearOccupancy();
            // Parent first, then local pose: ordering in the hash set is immaterial.
            foreach (var p in saved.poses) p.item.SetParent(p.parent, false);
            foreach (var p in saved.poses) { p.item.localPosition=p.position; p.item.localRotation=p.rotation; p.item.localScale=p.scale; }
            robot.RestorePose(saved.angles);
            for (int i=0;i<m.allParts.Length;i++)
            {
                var part=m.allParts[i];part.assembled=saved.assembled[i];part.canBePicked=saved.pickable[i];
                var body=part.GetComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;body.detectCollisions=false;
                Sync(part.transform);
            }
            for (int i=0;i<m.allTargets.Length;i++) m.allTargets[i].RestoreOccupancy(saved.occupants[i]);
            RefreshHousingCollision();
            receivingRig.Rest(); Phase = CollaborativePhase.Idle; closeInteraction = false;
            HandoverCount=saved.handovers;MaxHandError=0;MaxRobotError=0;
            BuildTree();Tree.RestoreCompletedPrefix(completed);
            foreach(var key in checkpoints.Keys.Where(k=>k>completed).ToArray())checkpoints.Remove(key);
            Physics.SyncTransforms();ActionName="Ready to repeat operation "+(completed+1);
        }
    }
}
