# Reference-based gearbox assembly

Open `Assets/Scenes/GearboxAssembly.unity`, enter Play Mode, and click **AUTO PLAY** (or press Space). **NEXT STEP** runs one operation; **PAUSE** freezes the motion; **RESET** restores the initial state. Select **Assembly detail** for a smooth close-up or **Workcell view** for the whole cell.

The highlighted route in Gearbox1/Gearbox2 is interpreted as:

1. Locate the pin carrier in the first fixture.
2. Mount the central spur/input shaft.
3. Mount the three planet gears individually.
4. Mount the output shaft/carrier, completing phase one.
5. Insert the internal assembly into the housing at the second fixture.
6. Install the lid.
7. Tighten the four screws in a cross pattern.

Phase one uses worker mounting with robot part supply. Phase two uses the robot for insertion and lid placement. Final screwing remains a worker task, following the diagram's 1.1 skill assignment even though it falls within phase two.

Assembly targets undo the tray Visual recentering to recover the FBX files' shared assembly coordinates. Repeated planets are rotated 120 degrees and screws 90 degrees about that shared axis. Both fixtures provide clearance underneath the projecting shaft. CAD files and import settings are unchanged.

Animation changes include quintic easing, vertical lift before lateral travel, alignment dwell, slower axial seating, hand tracking during worker placement, progressive screw rotation, metal materials, soft shadows, and camera transitions. The original robot articulation system remains in use.

This is a deterministic assembly visualization, not a validated force/contact simulation. Existing simplified colliders are bypassed during controlled insertion. Robot pickup and final approach still use authored poses and interpolated part transfers rather than a collision-planned Cartesian IK trajectory. No torque, gear-meshing or manufacturing-tolerance validation is implied.

To reapply the layout and appearance, use **Tools > Gearbox Demo > Apply Reference Assembly and Presentation** outside Play Mode. This deliberately replaces the assembly target coordinates, fixture geometry, timing and material assignments; use it only when those should be restored to the reference setup.

Verification entry point: `GearboxDemo.Editor.GearboxSequenceChecks.RunBatch` (Unity batch mode, without `-quit`). It compiles, applies the reference setup, runs the complete sequence, verifies all twelve installed parts and occupied targets, then checks reset. Results are written to `reference-sequence-report.txt`; the completed assembly is rendered to `reference-assembly-preview.png`.
