using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GearboxDemo
{
    [DisallowMultipleComponent]
    public sealed class GearboxAssemblyManager : MonoBehaviour
    {
        public PhaseOneAssemblyController phaseOne;
        public OutputShaftAssemblyStep outputShaftStep;
        public PlanetGearAssemblyStep planetGearStep;
        public PhaseTwoInsertionController phaseTwoInsertion;
        public LidInstallationController lidInstallation;
        public ScrewInstallationController screwInstallation;
        public HumanRobotHandover handover;
        public RobotPoseController robotPoses;
        public GearboxPart[] allParts;
        public AssemblyTarget[] allTargets;
        [Min(0f)] public float autoPlayStepDelay = 0.35f;

        public int StepIndex { get; private set; }
        public string CurrentPhase { get; private set; } = "READY";
        public string CurrentActor { get; private set; } = "SYSTEM";
        public string CurrentAction { get; private set; } = "PRESS START";
        public string CurrentPart { get; private set; } = "—";
        public bool IsPaused { get; private set; }
        public bool AutoPlayEnabled { get; private set; }
        public bool IsExecuting { get; private set; }

        private const int TotalSteps = 12;
        private Transform worker;
        private Transform initialWorkerParent;
        private Vector3 initialWorkerPosition;
        private Quaternion initialWorkerRotation;
        private Transform initialInternalParent;
        private Vector3 initialInternalLocalPosition;
        private Quaternion initialInternalLocalRotation;
        private Vector3 initialInternalLocalScale;
        private Transform[] initialPartParents;
        private Vector3[] initialPartPositions;
        private Quaternion[] initialPartRotations;
        private Vector3[] initialPartScales;
        private bool failed;
        private string actionBeforePause;

        private void Awake()
        {
            worker = phaseTwoInsertion.worker;
            initialWorkerParent = worker.parent;
            initialWorkerPosition = worker.localPosition;
            initialWorkerRotation = worker.localRotation;
            Transform internalRoot = phaseOne.InternalAssemblyRoot;
            initialInternalParent = internalRoot.parent;
            initialInternalLocalPosition = internalRoot.localPosition;
            initialInternalLocalRotation = internalRoot.localRotation;
            initialInternalLocalScale = internalRoot.localScale;
            initialPartParents = new Transform[allParts.Length];
            initialPartPositions = new Vector3[allParts.Length];
            initialPartRotations = new Quaternion[allParts.Length];
            initialPartScales = new Vector3[allParts.Length];
            for (int i = 0; i < allParts.Length; i++)
            {
                initialPartParents[i] = allParts[i].transform.parent;
                initialPartPositions[i] = allParts[i].transform.position;
                initialPartRotations[i] = allParts[i].transform.rotation;
                initialPartScales[i] = allParts[i].transform.localScale;
            }
        }

        private void Update()
        {
            var keys = Keyboard.current;
            if (keys != null && keys.spaceKey.wasPressedThisFrame)
            { if (IsExecuting && !IsPaused) PauseSimulation(); else StartAutoPlay(); }
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            Vector2 point = mouse.position.ReadValue(); point.y = Screen.height - point.y;
            float x = Mathf.Max(12f, Screen.width - 342f);
            if (new Rect(x + 14, 162, 94, 30).Contains(point)) StartSimulation();
            else if (new Rect(x + 118, 162, 94, 30).Contains(point)) PauseSimulation();
            else if (new Rect(x + 222, 162, 94, 30).Contains(point)) NextStep();
            else if (new Rect(x + 14, 202, 145, 30).Contains(point)) ResetSimulation();
            else if (new Rect(x + 171, 202, 145, 30).Contains(point)) StartAutoPlay();
        }

        public void StartSimulation()
        {
            ResumeClock();
            AutoPlayEnabled = false;
            BeginExecutionIfIdle();
        }

        public void PauseSimulation()
        {
            if (!IsPaused) actionBeforePause = CurrentAction;
            IsPaused = true;
            Time.timeScale = 0f;
            CurrentAction = "PAUSED";
        }

        public void NextStep()
        {
            ResumeClock();
            AutoPlayEnabled = false;
            BeginExecutionIfIdle();
        }

        public void StartAutoPlay()
        {
            ResumeClock();
            AutoPlayEnabled = true;
            BeginExecutionIfIdle();
        }

        public void ResetSimulation()
        {
            Time.timeScale = 1f;
            StopAllCoroutines();
            IsExecuting = false;
            IsPaused = false;
            AutoPlayEnabled = false;
            failed = false;

            screwInstallation.ResetOperationState();
            lidInstallation.ResetOperationState();
            phaseTwoInsertion.ResetOperationState();
            phaseOne.ResetOperationState();
            planetGearStep.ResetOperationState();
            outputShaftStep.ResetOperationState();
            handover.ResetHandoverState();

            foreach (AssemblyTarget target in allTargets)
                if (target != null) target.ClearOccupancy();
            foreach (GearboxPart part in allParts)
            {
                if (part == null) continue;
                part.ResetPart();
                part.assembled = false;
            }

            Transform internalRoot = phaseOne.InternalAssemblyRoot;
            internalRoot.SetParent(initialInternalParent, false);
            internalRoot.localPosition = initialInternalLocalPosition;
            internalRoot.localRotation = initialInternalLocalRotation;
            internalRoot.localScale = initialInternalLocalScale;
            for (int i = 0; i < allParts.Length; i++)
            {
                GearboxPart part = allParts[i];
                part.transform.SetParent(initialPartParents[i], true);
                part.transform.SetPositionAndRotation(initialPartPositions[i], initialPartRotations[i]);
                part.transform.localScale = initialPartScales[i];
                Rigidbody body = part.GetComponent<Rigidbody>();
                body.position = body.transform.position;
                body.rotation = body.transform.rotation;
            }
            Physics.SyncTransforms();
            worker.SetParent(initialWorkerParent, false);
            worker.localPosition = initialWorkerPosition;
            worker.localRotation = initialWorkerRotation;
            robotPoses.ResetToHome();

            StepIndex = 0;
            CurrentPhase = "READY";
            CurrentActor = "SYSTEM";
            CurrentAction = "PRESS START";
            CurrentPart = "—";
        }

        private void BeginExecutionIfIdle()
        {
            if (!IsExecuting && StepIndex < TotalSteps)
                StartCoroutine(RunSteps());
        }

        private IEnumerator RunSteps()
        {
            IsExecuting = true;
            failed = false;
            do
            {
                yield return ExecuteStep(StepIndex);
                if (failed) break;
                StepIndex++;
                if (StepIndex >= TotalSteps)
                {
                    CurrentPhase = "COMPLETE";
                    CurrentActor = "SYSTEM";
                    CurrentAction = "GEARBOX ASSEMBLY COMPLETE";
                    CurrentPart = "Final Gearbox";
                    break;
                }
                if (!AutoPlayEnabled) break;
                if (autoPlayStepDelay > 0f) yield return new WaitForSeconds(autoPlayStepDelay);
            }
            while (AutoPlayEnabled && StepIndex < TotalSteps);
            IsExecuting = false;
        }

        private IEnumerator ExecuteStep(int index)
        {
            switch (index)
            {
                case 0:
                    SetStatus("PHASE ONE", "WORKER", "POSITIONING", "Pin Carrier");
                    yield return phaseOne.PositionPinCarrierAnimated();
                    if (!phaseOne.pinCarrier.assembled) Fail("Pin Carrier setup failed.");
                    break;
                case 1:
                    SetStatus("PHASE ONE", "ROBOT / WORKER", "PICK, HANDOVER, MOUNT", "Central spur shaft");
                    yield return planetGearStep.AssemblePart(phaseOne.spurGear, phaseOne.spurGearTarget);
                    if (!planetGearStep.LastPartSucceeded) Fail("Central spur shaft installation failed.");
                    break;
                case 2:
                case 3:
                case 4:
                    int gearIndex = index - 2;
                    GearboxPart gear = planetGearStep.planetGears[gearIndex];
                    SetStatus("PHASE ONE", "ROBOT / WORKER", "PICK, HANDOVER, MOUNT", "Planetary Gear " + (gearIndex + 1));
                    yield return planetGearStep.AssemblePart(gear, planetGearStep.planetGearTargets[gearIndex]);
                    if (!planetGearStep.LastPartSucceeded || !gear.assembled) Fail(gear.partId + " installation failed.");
                    break;
                case 5:
                    SetStatus("PHASE ONE", "ROBOT / WORKER", "MOUNT OUTPUT CARRIER", "Output shaft");
                    outputShaftStep.RunOutputShaftAssembly();
                    yield return new WaitUntil(() => !outputShaftStep.IsRunning);
                    if (!outputShaftStep.outputShaft.assembled)
                        Fail("Spur Gear installation failed.");
                    else
                        phaseOne.MarkPhaseOneComplete();
                    break;
                case 6:
                    SetStatus("PHASE TWO", "WORKER", "MOVING TO OBSERVATION POSITION", "Internal Assembly");
                    phaseTwoInsertion.StartPhaseTwoInsertion();
                    yield return null;
                    SetStatus("PHASE TWO", "ROBOT", "PICK, MOVE, INSERT", "Internal Assembly");
                    yield return new WaitUntil(() => !phaseTwoInsertion.IsRunning);
                    if (!phaseTwoInsertion.InternalAssemblyInserted) Fail("Internal assembly insertion failed.");
                    break;
                case 7:
                    SetStatus("PHASE TWO", "ROBOT", "PICK, ALIGN, LOWER, INSTALL", "Lid");
                    lidInstallation.InstallLid();
                    yield return new WaitUntil(() => !lidInstallation.IsRunning);
                    if (!lidInstallation.LidInstalled) Fail("Lid installation failed.");
                    break;
                case 8:
                case 9:
                case 10:
                case 11:
                    int screwIndex = new[] { 0, 2, 1, 3 }[index - 8];
                    GearboxPart screw = screwInstallation.screws[screwIndex];
                    SetStatus("PHASE TWO", "WORKER / ROBOT ASSIST", "ALIGN, THREAD, TIGHTEN", "Screw " + (screwIndex + 1));
                    yield return screwInstallation.InstallScrew(screw, screwInstallation.screwTargets[screwIndex]);
                    if (!screwInstallation.LastScrewSucceeded || !screw.assembled)
                        Fail(screw.partId + " installation failed.");
                    else if (screwIndex == 3)
                        screwInstallation.MarkGearboxAssemblyComplete();
                    break;
            }
        }

        private void SetStatus(string phase, string actor, string action, string part)
        {
            CurrentPhase = phase;
            CurrentActor = actor;
            CurrentAction = action;
            CurrentPart = part;
        }

        private void Fail(string reason)
        {
            failed = true;
            AutoPlayEnabled = false;
            CurrentAction = "STOPPED: " + reason;
            Debug.LogError("Assembly Manager: " + reason, this);
        }

        private void ResumeClock()
        {
            if (IsPaused && !string.IsNullOrEmpty(actionBeforePause)) CurrentAction = actionBeforePause;
            IsPaused = false;
            if (Time.timeScale == 0f) Time.timeScale = 1f;
        }

        private void OnGUI()
        {
            float width = 330f;
            float x = Mathf.Max(12f, Screen.width - width - 12f);
            GUI.Box(new Rect(x, 12f, width, 242f), "GEARBOX ASSEMBLY");
            GUI.Label(new Rect(x + 14f, 40f, width - 28f, 22f), "Current Phase: " + CurrentPhase);
            GUI.Label(new Rect(x + 14f, 63f, width - 28f, 22f), "Current Actor: " + CurrentActor);
            GUI.Label(new Rect(x + 14f, 86f, width - 28f, 22f), "Current Action: " + CurrentAction);
            GUI.Label(new Rect(x + 14f, 109f, width - 28f, 22f), "Current Part: " + CurrentPart);
            GUI.Label(new Rect(x + 14f, 132f, width - 28f, 22f), "Step: " + StepIndex + " / " + TotalSteps);

            float buttonWidth = 94f;
            GUI.Box(new Rect(x + 14f, 162f, buttonWidth, 30f), "START", GUI.skin.button);
            GUI.Box(new Rect(x + 118f, 162f, buttonWidth, 30f), "PAUSE", GUI.skin.button);
            GUI.Box(new Rect(x + 222f, 162f, buttonWidth, 30f), "NEXT STEP", GUI.skin.button);
            GUI.Box(new Rect(x + 14f, 202f, 145f, 30f), "RESET", GUI.skin.button);
            GUI.Box(new Rect(x + 171f, 202f, 145f, 30f), "AUTO PLAY", GUI.skin.button);
        }

        private void OnDisable()
        {
            if (Time.timeScale == 0f) Time.timeScale = 1f;
        }
    }
}
