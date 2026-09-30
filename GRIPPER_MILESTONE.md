# Deterministic gearbox gripper

The saved GearboxAssembly scene has RobotGripper on `Wrist/EndEffector/Gripper`. **Tools > Gearbox Demo > Setup Robot Gripper** configures an existing arm without rebuilding it. **Build Robot** also adds the gripper. Setup is repeatable and never rewrites gearbox prefab assets or FBX files.

## Select and test a part

1. Select the robot's Gripper object in the Hierarchy.
2. In RobotGripper, set **Selected Part** to Housing, OutputShaft, PinCarrier, Gear, SpurGear, Lid or Screw.
3. Click **Validate Selected Part**. Recognition works outside Play Mode, checks the actual scene wrapper and its configured GripPoint, and never moves anything.
4. In Play Mode, use **Open / Release**, **Close Fingers**, **Try Grab Selected Part**, and **Release / Validate Placement**. Inspector commands execute in the next normal runtime Update.
5. A grab requires the robot attachment point to be within 8 cm and 30 degrees of the selected part GripPoint by default. With Gizmos enabled, selecting the gripper displays the capture radius. Bring the robot near the individual part using an appropriate joint configuration. The current high safe poses do not themselves pick anything up.

**Try Grab Nearest Eligible Part** performs an explicit one-shot search ordered by GripPoint distance and then part ID. **Close Fingers** is visual only; it does not acquire a part. No automatic pickup loop or mechanical assembly order is implemented.

## API and physics

`RobotGripper.cs` provides `Open()`, `Close()`, `TryGrab()`, `Grab(IGrippable target)`, `Release()` and read-only `HeldObject`.

The gripper reads the existing part GripPoint and measures it relative to the wrapper root. After proximity and orientation checks, a small residual offset is closed by moving the part wrapper so both GripPoints coincide. The articulated robot is never teleported or rotated through its Transforms. Mesh pivots are not used as gripping locations.

A deterministic follower updates the held part from the actual solved robot attachment pose in FixedUpdate and LateUpdate. The part stays under its original parent to avoid mixed Rigidbody/ArticulationBody hierarchy and scale problems. The held Rigidbody is kinematic, with gravity, interpolation and contact detection disabled. Finger friction is not required. Held parts do not collide with the environment during transport; obstacle-aware manipulation remains a future milestone.

On release, the previous kinematic, gravity, interpolation, collision mode and contact-detection settings are restored. Matching AssemblyTargets validate type, position and orientation. Only valid placement installs the part; otherwise it becomes dynamic again. Reset, external installation, disabled parts and disabled grippers clear attachment ownership. GearboxPart.ResetPart also restores the original physics state.

Visual fingers open wide enough for the 24 cm housing/lid, and close according to the wrapper collider extent about the configured part GripPoint axis. Their colliders are disabled; the palm and arm retain fixed collision shapes. Capture tolerances, attachment point and part GripPoints remain Inspector-adjustable.

## Validation

**Tools > Gearbox Demo > Validate Gripper Prefabs** verifies explicit identity mapping, GrippableObject, configured GripPoint, AssemblyAnchor, Rigidbody and solid collider.

| Prefab | Recognized |
|---|---|
| Gearbox_Housing | Yes |
| Gearbox_OutputShaft | Yes |
| Gearbox_PinCarrier | Yes |
| Gearbox_Gear | Yes |
| Gearbox_SpurGear | Yes |
| Gearbox_Lid | Yes |
| Gearbox_Screw | Yes |

See `gripper-recognition-report.txt` and `gripper-playmode-report.txt`. The batch-only integration check stages a transient housing fixture near the socket, then verifies actual robot transport, release, reset and placement validation. It does not save staged parts/targets or add a pickup sequence. The saved scene stays unassembled.

To repeat checks, close Unity and launch `Unity.exe -batchmode -nographics -projectPath <Frontend absolute path> -executeMethod GearboxDemo.Editor.GearboxGripperChecks.RunBatch -logFile <log path>`. Omit `-quit`; the test exits after leaving Play Mode.

The prior Input Manager warning is resolved by Input System 1.20.0 with the new backend selected exclusively. Number-row shortcuts, numpad shortcuts and mouse pose buttons passed all 12 injected-device integration checks (`input-check-report.txt`). Restart an already-open editor after input-backend changes.
