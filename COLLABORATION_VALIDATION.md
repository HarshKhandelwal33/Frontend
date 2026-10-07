# Collaboration validation — 7 October 2026

Unity version: 6000.6.0f1. Project: repository root. Prepared scene: `Assets/Scenes/GearboxTraining.unity`.

`GearboxCollaborationChecks.RunStaticBatch` passed compilation and scene checks: six revolute articulations with valid limits, valid humanoid avatar, enabled IK pass, robot collision geometry, configured part colliders/grip points and saved Inspector settings. Existing obsolete Unity object-finding API warnings remain.

`GearboxCollaborationChecks.RunBatch` completed successfully. The fresh `collaboration-scenarios-report.txt` records:

- Emergency stop blocks Play/Step; explicit reset remains idle.
- Protective stop and cancellation latch.
- Invalid worker tracking causes a stop.
- An unreachable IK target is rejected without changing live articulation targets.
- Injected equipment obstruction causes a stop.
- A confirmed handover preserves the part world pose and changes ownership to the worker.
- Cancellation while holding retains the payload.
- Missing grasp confirmation times out while the robot retains ownership.
- Failed worker readiness/grasp does not release the part.

The runtime checker uses accelerated simulation time (3x); limits are expressed per simulated second. These are functional software checks, not safety qualification or measured physical stopping-performance tests. Equipment obstruction is a representative injected collider; every scene obstacle, payload, animation pose and body-region contact still needs visual inspection.

Diagnostic prepared-fixture runs verified housing seating, internal insertion and offset-grip lid placement after correcting fixture and collider geometry. Those fixtures bypass earlier operations and are not full-sequence evidence.

The final full run, recorded in `game-simulation-report.txt` and `collaboration-workflow.log`, passed all 13 operations from loose parts, all 10 handovers, 12 installed parts, 12 occupied targets and final alignment. Reset clears tree/ownership/occupancy; pause freezes the held pose; reset during carry restores the rack. The largest computed robot endpoint error was 0.003764 m. These are Unity transform measurements, not physical accuracy claims. Replay after reset and single-step stopping after one complete operation also passed.

Required manual review: animation quality and wrist/elbow posture at normal speed; worker reach at every station and replacement avatar; gripper/part contact surfaces; swept collision coverage for table, tray, fixtures, finger proxies and all carried internal parts; selected monitored-standstill staging; body proxy radii and real stopping-distance assumptions. The existing screw rotation remains an illustrative assembly action, not a screwdriver/contact-force simulation. Kinematic part attachment does not model payload loading, friction or grasp forces.

No new package or model asset was required. The earlier root assembly scene lacks the completed manager; use the prepared Training scene. Legacy handover/assembly controllers remain in the repository for compatibility but the active simulation disables their motion ownership and uses the existing behaviour tree.

See [COLLABORATIVE_SIMULATION.md](COLLABORATIVE_SIMULATION.md) for scene wiring, Inspector settings, standards status, limitations and manual test steps. No real-world certification or numerical safety compliance is claimed.

Worker-size update: the prepared Training scene now scales the worker root uniformly to 1.08. The CharacterController world height is 2.009 m (a collision proxy, not a measured anatomical height). Mesh, skeleton and collision controller scale together; body proxy radii were increased by the same ratio, and the minimum receiving stance moved to Z=1.18. `worker-scale-workflow-report.txt` verifies a complete first handover, retreat and worker placement, emergency-stop interlock and single-step completion at the new size. The earlier full 13-operation result predates this size update; the entire sequence at the larger size still needs review. Printed part dimensions were preserved because physical print measurements were unavailable.

Equipment proportions: saved Training scene now has 15% slimmer robot body geometry and 10% smaller table footprints. Articulation frames, gripper, table heights, worker and printed parts retain their dimensions. Workbench operator-side edge retained to support the mat. Geometric fit checked; runtime checks after this change remain pending because the Unity editor has this project open. Tools > Gearbox Demo > Compact Robot and Tables reapplies these proportions idempotently.
