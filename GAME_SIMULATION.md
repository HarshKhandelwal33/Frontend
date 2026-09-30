# Gearbox game simulation

Open `Assets/Scenes/GearboxAssembly.unity` and enter Play Mode. The simulation initializes an empty fixture, puts all twelve loose components on the rack, and starts the assembly automatically. Stop and re-enter Play Mode after updating scripts in an already-open editor.

Controls: **Play / Resume**, **Pause**, **Step** (one complete operation), **Reset**, **Camera**, and **0.5x / 1x / 2x**. Space pauses/resumes, R resets, and C cycles workcell/action/assembly cameras. Reset leaves the cell ready; Play restarts it.

## Behaviour tree

`AssemblyBehaviourTree.cs` supplies real ticked Sequence, Condition and coroutine Action nodes with Ready/Running/Success/Failure states. Nested coroutine errors and operation timeouts become Failure; a failed operation stops the sequence. The HUD displays progress and each operation's state.

The root sequence runs 13 operations:

1. Worker mounts the pin carrier after robot pickup and handover.
2. Worker mounts the central spur/input shaft.
3. Worker mounts each of the three planets.
4. Worker mounts the output shaft/carrier.
5. Robot picks the housing from the rack and seats it in the second fixture.
6. Robot grips and inserts the complete internal assembly into the housing.
7. Robot picks and seats the lid.
8. Worker installs the four screws in a cross pattern, with robot supply.

Each worker operation has an availability/occupancy condition followed by robot pickup, contact-checked handover, and worker placement. Robot operations use availability checks, pickup and placement. Internal insertion checks the completed internal parts and housing before starting.

## Motion and ownership

`GearboxGameSimulation` becomes the sole runtime owner when this prepared scene loads. It disables the earlier assembly scripts and their competing controls. It preserves the editor scene and original articulation rig, and builds a mesh-only copy for deterministic game animation.

`GameRobotIK` solves six-joint gripper poses within the authored joint limits using damped least squares and multiple starting configurations. Joint interpolation uses smooth acceleration/deceleration. Transfers use raised clearance poses and a waypoint in front of the robot base.

The robot reaches the actual part grip before attaching it. A transported part is parented to the robot grip or the worker's palm throughout the carry. There is no independent part-to-gripper flight. The worker's palm offset is calibrated in world metres to handle the imported humanoid's bone scale. Ownership transfers without changing world pose after contact is checked above the table. The gripper opens visibly before retreating. Parts are detached only when seated at a validated assembly target.

Loose components are deliberately kinematic on the rack: no physics settling, sliding or launching. Fixture supports meet the pin carrier underside, leaving clearance for the projecting input shaft. The original CAD files are unchanged.

The active game uses kinematic animation and IK, not force/torque or collision-planning validation. Endpoint reach and carried-part tabletop clearance are checked; this does not establish full robot-link collision avoidance for arbitrary scene edits.

## Verification

Run Unity with `-batchmode -executeMethod GearboxDemo.Editor.GearboxGameChecks.RunBatch` against this project or an isolated copy; do not pass `-quit`. The runner checks empty initial assembly, all 13 operations, ten handovers, payload ownership and tabletop clearance, final twelve-part alignment/occupancy, pause while holding a part, reset while carrying, replay, and single-step stopping.

Results: `game-simulation-report.txt`. Preview renders: `game-handover.png` and `game-complete.png`. Automated renders show the camera scene; the live behaviour-tree HUD is drawn separately by IMGUI in Game view.

The older milestone and reference-assembly documents describe earlier controllers; this runtime supersedes their playback controls.
