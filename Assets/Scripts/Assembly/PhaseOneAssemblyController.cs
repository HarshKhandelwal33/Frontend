using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GearboxDemo
{
    [DisallowMultipleComponent]
    public sealed class PhaseOneAssemblyController : MonoBehaviour
    {
        public GearboxPart pinCarrier;
        public AssemblyTarget pinCarrierTarget;
        public OutputShaftAssemblyStep outputShaftStep;
        public PlanetGearAssemblyStep planetGearStep;
        public GearboxPart spurGear;
        public AssemblyTarget spurGearTarget;
        public Transform assembledGearbox;

        public bool IsRunning { get; private set; }
        public bool PhaseOneComplete { get; private set; }
        public Transform InternalAssemblyRoot => assembledGearbox;

        private void Update()
        {
            if (GetComponent<GearboxAssemblyManager>() != null) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.pKey.wasPressedThisFrame)
                StartPhaseOne();
        }

        public void StartPhaseOne()
        {
            if (!IsRunning && isActiveAndEnabled)
                StartCoroutine(PhaseOneRoutine());
        }

        public IEnumerator PositionPinCarrierAnimated()
        {
            var body = pinCarrier.GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            yield return planetGearStep.handover.WorkerReachToward(pinCarrier.gripPoint.position);
            yield return AssemblyMotion.Seat(pinCarrier.transform, pinCarrierTarget.transform, 1.5f,
                () => planetGearStep.handover.FollowWorkerHand(pinCarrier.gripPoint.position));
            PositionPinCarrier();
            planetGearStep.handover.ReleasePart(pinCarrier);
            yield return new WaitUntil(() => !planetGearStep.handover.IsBusy);
        }

        public void PositionPinCarrierForAssembly() => PositionPinCarrier();

        public void MarkPhaseOneComplete()
        {
            PhaseOneComplete = true;
            IsRunning = false;
            Debug.Log("PHASE ONE COMPLETE", this);
        }

        public void ResetOperationState()
        {
            StopAllCoroutines();
            IsRunning = false;
            PhaseOneComplete = false;
        }

        private IEnumerator PhaseOneRoutine()
        {
            if (!ReferencesAreValid()) yield break;
            IsRunning = true;
            PhaseOneComplete = false;

            yield return PositionPinCarrierAnimated();
            yield return planetGearStep.AssemblePart(spurGear, spurGearTarget);
            if (!planetGearStep.LastPartSucceeded) { StopPhaseOne("Central shaft failed."); yield break; }
            foreach (var gear in planetGearStep.planetGears)
            {
                yield return planetGearStep.AssemblePart(gear, gear.assemblyTarget);
                if (!planetGearStep.LastPartSucceeded) { StopPhaseOne("Planet installation failed."); yield break; }
            }
            outputShaftStep.RunOutputShaftAssembly();
            yield return new WaitUntil(() => !outputShaftStep.IsRunning);
            if (!outputShaftStep.outputShaft.assembled) { StopPhaseOne("Output carrier failed."); yield break; }

            PhaseOneComplete = true;
            IsRunning = false;
            Debug.Log("PHASE ONE COMPLETE", this);
        }

        private void PositionPinCarrier()
        {
            pinCarrier.transform.SetParent(assembledGearbox, true);
            pinCarrier.transform.SetPositionAndRotation(pinCarrierTarget.transform.position, pinCarrierTarget.transform.rotation);
            Rigidbody body = pinCarrier.GetComponent<Rigidbody>();
            body.position = pinCarrier.transform.position;
            body.rotation = pinCarrier.transform.rotation;
            body.isKinematic = true;
            body.useGravity = false;
            if (pinCarrierTarget.Occupant != pinCarrier)
            {
                pinCarrier.assembled = false;
                pinCarrier.canBePicked = true;
                if (!pinCarrierTarget.TryPlace(pinCarrier))
                {
                    pinCarrier.assembled = true;
                    pinCarrier.canBePicked = false;
                }
            }
            else
            {
                pinCarrier.assembled = true;
                pinCarrier.canBePicked = false;
            }
        }

        private bool HasUnassembledPlanetGear()
        {
            foreach (GearboxPart gear in planetGearStep.planetGears)
                if (gear == null || !gear.assembled) return true;
            return false;
        }

        private bool ReferencesAreValid()
        {
            bool valid = pinCarrier != null && pinCarrierTarget != null &&
                         outputShaftStep != null && planetGearStep != null &&
                         spurGear != null && spurGear.partType == GearboxPartType.SpurGear &&
                         spurGearTarget != null && spurGearTarget.acceptedPart == GearboxPartType.SpurGear &&
                         spurGear.assemblyTarget == spurGearTarget && assembledGearbox != null;
            if (!valid) Debug.LogError("Phase One assembly references are invalid.", this);
            return valid;
        }

        private void StopPhaseOne(string reason)
        {
            IsRunning = false;
            PhaseOneComplete = false;
            Debug.LogError("Phase One stopped: " + reason, this);
        }
    }
}
