# Voice training application

Open **Assets/Scenes/GearboxTraining.unity** in the root Unity project for readiness-gated voice training with a compact floating voice panel. See [TRAINING_SETUP.md](TRAINING_SETUP.md) for backend startup, Windows speech setup, Qwen configuration, controls, and verification. The new scene uses the prepared workcell from `thesis/`; both original scenes are preserved. The earlier demo and milestone notes below describe the simulation foundation.

# Current simulation

The scene now automatically runs a behaviour-tree-driven game simulation in Play Mode, starting with all twelve loose parts on the rack and empty fixtures. See [GAME_SIMULATION.md](GAME_SIMULATION.md) for controls, architecture, and verification. The earlier milestone notes below describe the original foundation.

# Gearbox assembly workcell

The deterministic gripper is implemented; see **GRIPPER_MILESTONE.md** for selection, recognition, capture tolerances and release behavior. **Tools > Gearbox Demo > Setup Robot Gripper** configures it without rebuilding the existing arm.

The six-DOF ArticulationBody robot milestone is implemented. See **ROBOT_MILESTONE.md** for measured dimensions, joint configuration, testing and controls. Use **Tools > Gearbox Demo > Build Robot** to regenerate the arm; enter Play Mode and use **1 Home, 2 TraySafe, 3 AssemblySafe, 4 HumanSafe**. The sections below describe the component-preparation foundation.

Separate Unity 6 project (6000.6.0f1), using the built-in render pipeline.

## Open and regenerate

1. In Unity Hub, add this `Frontend` folder as a project and open it with Unity 6000.6.0f1.
2. Wait for script compilation, then choose **Tools > Gearbox Demo > Build Demo Scene** outside Play Mode.
3. The builder opens and saves `Assets/Scenes/GearboxAssembly.unity`. This recreates the environment only.
4. Choose **Tools > Gearbox Demo > Setup Gearbox Parts** to generate/update the seven wrappers and populate the tray.
5. Choose **Tools > Gearbox Demo > Validate Gearbox Parts** to print the validation report.
6. Open the Game view or enter Play Mode. Parts settle under gravity and stay on the tray; no assembly runs automatically.

The delivered scene already contains the parts. Open it directly if you do not want to rebuild the environment. Setup operates on this active scene, saves it, and preserves existing start positions, pickup targets, assembly targets, grip points, anchors, Visual compensation, and existing physics tuning. New items receive defaults. Prefab changes are persistent asset edits; scene additions also use Undo.

Regenerating replaces the generated scene after a confirmation. Save any manual layout variations under another scene name first. Existing generated materials are reused, so material customization survives regeneration.

## Visual checks

- Grey floor and two supported tables; tabletop height is one metre.
- Parts table and raised-rim tray containing seven actual FBX models on the left.
- Workbench with green assembly mat on the right.
- Primitive articulated robot between the tables (the original orange floor marker is hidden after Build Robot).
- Blue human floor marker behind the assembly workbench, facing the assembly area.
- The remaining blue human marker has a facing indicator; a human avatar is not implemented yet.
- Main Camera frames the workcell from above the front; Directional Light illuminates it.
- Clearly named scene hierarchy under `Gearbox Assembly Workcell`; no pink or missing materials.
- Parts settle slightly under gravity in Play Mode. The robot moves only when commanded with keys/buttons; no automatic assembly sequence, human interaction, or AI is implemented.

## Assets and folders

`Assets/Models/Gearbox` contains the seven user-provided canonical FBX files. Their SHA-256 hashes were checked before and after implementation; all are unchanged. No importer settings were edited. Unity generated the new import `.meta` files normally.

The environment builder ensures folders for Scenes, Models/Gearbox, Prefabs/{Robot,Human,Gearbox,Workstation}, Scripts/{Core,Assembly,Robot,Human,Interaction,UI}, Editor, Materials, and ScriptableObjects. Only Assembly, Interaction, and Gearbox prefabs are implemented in this milestone.

The builder creates seven `Assets/Materials/GearboxDemo_*.mat` assets and the scene through Unity APIs. Unity generates `.meta` files, default project settings, and its package lock file. See `CREATED_FILES.txt` for the deliverable file inventory. Unity caches in Library, Temp, Logs, obj, and UserSettings are excluded from that inventory.

## Wrapper geometry and physics

Each `Gearbox_<Type>` prefab contains `Visual/<original FBX>`, `GripPoint`, and `AssemblyAnchor`. The prefab root has GearboxPart, GrippableObject, Rigidbody, and BoxCollider. Explicit editor definitions map each canonical filename to its enum; runtime components never parse filenames.

The imported housing and lid are 0.6 metres across. All seven models use a uniform **0.4 presentation scale**, preserving relative proportions. This is a tray-layout choice, not a verified engineering scale. The Visual wrapper rotates -90 degrees about X (original Z becomes up), recentres X/Z, and places the geometry bottom at root Y=0. The nested FBX transform and source file remain intact. All imported models have offset assembly-space pivots, including lateral offsets on the gear and screw. Raw measurements are in `gearbox-model-audit.txt`.

| Component | Wrapper bounds X/Y/Z (m) | Collider | Mass (kg) |
|---|---|---|---|
| Housing | 0.2400 / 0.1200 / 0.2400 | Box | 1.50 |
| Output shaft | 0.1847 / 0.1600 / 0.1680 | Box | 0.60 |
| Pin carrier | 0.1727 / 0.0624 / 0.1560 | Box | 0.40 |
| Gear | 0.0789 / 0.0400 / 0.0800 | Box | 0.20 |
| Spur gear | 0.0800 / 0.1520 / 0.0789 | Box | 0.25 |
| Lid | 0.2400 / 0.0400 / 0.2400 | Box | 0.50 |
| Screw | 0.0400 / 0.0800 / 0.0400 | Box | 0.05 |

These conservative bounds colliders prioritize tray stability; they fill holes and do not support mechanically accurate insertion/contact. Each body has gravity enabled, is non-kinematic, and uses continuous dynamic collision detection, interpolation, linear damping 0.15 and angular damping 0.6. Default starts are 6 mm above the tray surface, with room between parts. No physics constraints freeze them.

## Adjustable references and future integration

- Fine-tune `GripPoint` and `AssemblyAnchor` in Prefab Mode or as scene overrides. GripPoint defaults just above the geometry, with its local up axis pointing down. AssemblyAnchor starts at the bottom centre.
- Start markers live in `PartsTray/<Type>Start`, with the associated prefab beneath each one.
- Pickup references live in `RobotTargets/PickupTargets/<Type>Pickup`. They are initialized from GripPoint once, then preserved as independently editable approach references. If you later move a part or grip, adjust its pickup reference too.
- `Assembly Area/AssemblyTargets/<Type>Target` contains AssemblyTarget components and cyan Scene-view gizmos. These are separated provisional staging poses on the mat, **not a verified assembled gearbox arrangement or order**. Move/rotate them after mechanical review.
- `AssemblyTarget.TryPlace(part)` checks type, occupancy, anchor distance, and anchor rotation. It optionally aligns the anchor, marks the part installed, and raises `PartInstalled`. Nothing invokes it automatically in normal Play Mode. A future assembly manager can subscribe to this event.
- `IGrippable` exposes GripPoint, CanBeGrabbed, OnGrabbed, and OnReleased. Grabbing temporarily disables physics motion; releasing restores the previous physics state unless installed. Parenting belongs to the future gripper controller.
- GearboxPart captures its initial parent, pose, scale, pickup flags, and physics state in Awake. `ResetPart()` restores them, clears grip state, and releases target occupancy. Call `CaptureInitialState()` after future runtime spawning/positioning if the intended start differs from Awake. No Reset UI is added yet.

## Verification performed

Unity 6000.6.0f1 compiled the scripts and generated the scene/prefabs successfully. Validation reported zero errors. Repeated setup retained object counts, prefab GUIDs, and grip positions. An explicit batch-only Play Mode check simulated eight seconds: all seven real parts settled on the tray with no horizontal drift, falling, or startup collisions. Grab/release, wrong type/distance/orientation rejection, snapping, installed notifications, occupancy, uninstall, and reset (including while held) passed for every part.

Reports: `gearbox-validation-report.txt`, `gearbox-playmode-report.txt`, `gearbox-setup-report.txt`. Rendered views: `gearbox-tray-preview.png` and `gearbox-workcell-preview.png`. Full milestone change inventory: `MILESTONE2_FILES.txt`.

To repeat the automated Play Mode check, close the Unity editor for this project and run Unity with `-batchmode -nographics -projectPath <Frontend absolute path> -executeMethod GearboxDemo.Editor.GearboxPartsChecks.RunBatch -logFile <log path>`. Do not supply `-quit`; the check exits Unity itself after leaving Play Mode. The check explicitly exercises placement/reset on transient Play Mode objects; the saved scene remains unassembled.

In Unity, inspect the tray close up, toggle Gizmos to see target references, review grip accessibility and the provisional orientations, and enter Play Mode to confirm settling on your machine. The overview camera intentionally frames the whole workcell; use Scene view Frame Selected on PartsTray for detailed inspection.

Input: Unity Input System 1.20.0 (new backend only). If migrating an already-open editor, restart Unity after package import. The 1–4 shortcuts, numpad equivalents and mouse pose buttons remain available.



