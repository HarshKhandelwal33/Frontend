# Articulation robot milestone

Open `Assets/Scenes/GearboxAssembly.unity` in Unity 6000.6.0f1. The robot is already built. To regenerate it, choose **Tools > Gearbox Demo > Build Robot** while outside Play Mode. The builder reads the current tray, workstation and gearbox bounds; replaces only RobotArm; resets its four generated pose assets to measured defaults; and saves the active scene. Save manual robot/pose edits separately before rebuilding. The existing gearbox FBXs, prefab assets, GripPoints, scene parts and targets are not recreated or edited.

The current scene calls its workstation `Assembly Area` and `Workbench`. The builder accepts those existing objects as well as `AssemblyStation`, so no workstation rename is required. The former orange robot position marker is hidden once the arm exists. Rebuilding the environment removes the arm; run Setup Gearbox Parts and then Build Robot to restore the complete scene.

## Controls

Enter Play Mode and focus the Game view. Number row and numeric keypad keys work; equivalent onscreen buttons are also provided.

| Key | Named pose | Purpose |
|---|---|---|
| 1 | Home | Low folded configuration |
| 2 | TraySafe | Hover above the parts tray |
| 3 | AssemblySafe | Hover above the assembly mat |
| 4 | HumanSafe | Folded parking configuration turned away from the operator side |

Each transition retracts to Home joint angles at the current base yaw, turns while folded, then extends to the requested pose. The last request received during movement is queued. A joint tracking timeout cancels remaining stages and reports an error. There are no generic Pickup poses and no part-specific manipulation yet. Safe-pose names describe these tested simulation configurations; changing the environment or tuning poses requires clearance verification again.

## Dimensions from the real scene

The tray base measures 1.4 × 1.0 m; the workbench top measures 2.2 × 1.2 m. The largest wrapped gearbox components are 0.24 m across. The base fits in the gap between the tables at world (-0.2, 0, 0.3). The farthest actual GripPoint/assembly target is 2.092 m horizontally from it. The compact revision is sized for these working references rather than unused outer tray corners.

- Shoulder height: 1.230 m, above the work surfaces.
- Main links: 1.153 m and 1.064 m.
- Main-link reach: 2.217 m, including a 6% margin over the farthest working reference.
- Wrist/tool extension: approximately 0.300 m; link thickness: 0.120 m.
- Tested TraySafe/AssemblySafe GripPoint height: approximately 1.554 m, above the parts and workstation.

This reach covers the existing separated work areas. It does not certify a collision-free future grasp path for every part or constitute a mechanically exact commercial robot model. The four pose assets are joint configurations, with approximate safe hover poses derived in the editor from measured geometry; there is no runtime IK.

## Implementation

`RobotArm/Base` is the immovable articulation root. Six serial revolute bodies (`Joint1` through `Joint6`) use yaw, shoulder pitch, elbow pitch, wrist roll, wrist pitch and tool roll axes. Each joint owns a primitive link/housing; Joint6 owns `Wrist/EndEffector/Gripper/GripPoint`, `LeftFinger`, and `RightFinger`. The gripper now supports visual finger opening/closing and deterministic attachment; see GRIPPER_MILESTONE.md.

Articulation drives are angular position drives with joint limits, finite torque limits, damping, gravity and explicit solver settings. Runtime motion changes only `ArticulationDrive.target`, using synchronized quintic interpolation at up to 35 degrees/second commanded speed. Reduced joint coordinates are initialized once in Awake to the authored Home pose before simulation. Runtime scripts never rotate child Transforms.

Robot-to-robot collider pairs are ignored because the simplified primitive joint housings overlap. Environment and gearbox collisions remain active. Detailed self-collision geometry remains a later refinement.

Runtime files:

- `RobotJointController.cs`: articulation reference, measured angle, drive target and limits.
- `RobotArmController.cs`: startup coordinates and smooth drive motion.
- `RobotPose.cs`: six-angle ScriptableObject configuration.
- `RobotPoseController.cs`: safe staged transitions and request queue.
- `RobotDebugController.cs`: Game-view keys, buttons and motion status.

Editor files: `GearboxRobotBuilder.cs` and `GearboxRobotChecks.cs`. Four pose assets are saved under `Assets/ScriptableObjects/Robot{Home,TraySafe,AssemblySafe,HumanSafe}.asset`; robot materials are in Assets/Materials. Full created/modified file inventory is in `ROBOT_FILES.txt`.

## Verification

Unity compilation passed. The batch-only Play Mode suite rebuilt twice and verified one robot with seven ArticulationBodies, tested all 12 directed pose pairs, observed motion in all six joints, checked base immobility, checked finite/bounded joint velocities, and checked moving colliders against the workcell and parts. Every transition passed; largest observed settled angle error was below one degree. The test calls the same pose request API as the keys/buttons; keyboard focus should be checked interactively in Unity.

See `robot-build-report.txt`, `robot-playmode-report.txt`, and `robot-workcell-preview.png`. Original gearbox FBX and prefab hashes were verified unchanged.

To repeat automated checks, close the editor for this project and launch Unity with `-batchmode -nographics -projectPath <Frontend absolute path> -executeMethod GearboxDemo.Editor.GearboxRobotChecks.RunBatch -logFile <log path>`. Do not add `-quit`: the suite exits after testing and restoring Edit Mode. Tests use accelerated simulation time, without changing fixed physics timestep or the saved scene.

Inspect the six joints, drive limits, base stability and clearance while pressing 1–4. No automatic pickup sequence, ROS, AI, or human interaction has been added. Manual gripper attachment is documented in GRIPPER_MILESTONE.md.

Reference: [Unity articulation overview](https://unity.com/blog/industry/use-articulation-bodies-to-easily-prototype-industrial-designs-with-realistic-motion-and).

## Compact visual revision

Main links are 14% shorter and link diameter is 23% smaller. A lower, deeply folded Home pose reduces GripPoint height from 3.23 m to 1.95 m. Pearl-grey rounded shells, graphite joint drums, teal seals and slim link inserts replace the orange block geometry. All four joint pose assets are recalculated; controls and articulation drive behavior are retained. Existing parts, target positions and gearbox prefabs stay unchanged.


The rendered Home configuration has an overall maximum height of 2.287 m. The current visual preview is robot-detail-preview.png. The compact revision passed the same 12 directed pose-transition checks without compilation warnings or errors.


## Input backend

The project uses Input System 1.20.0 with Active Input Handling set to Input System Package (New) only. RobotDebugController reads Keyboard.current in Update for number-row and numpad keys. Mouse.current handles the visible pose-panel hit regions; IMGUI is used only to draw the panel. No legacy Input Manager keyboard or pointer handling is required. Unity may require a restart after the backend setting changes.



