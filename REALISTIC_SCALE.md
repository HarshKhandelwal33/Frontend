# Compact robot workcell

GearboxTraining uses a 1.80 m worker and an estimated 120 mm housing. The CAD-derived arm is now uniformly scaled to 65%, about 24% smaller than the preceding 85% setting. Gripper world size stays independent of the arm, preserving its 150 mm opening and 60 mm precision fingers.

The compact parts grid has 140 mm column spacing and 170 mm row spacing. Fixture locations, handover height and robot lift/approach clearances are adapted to the shorter reach. Model dimensions are demonstration estimates, not a physical robot calibration.

Use Tools > Gearbox Demo > Correct Entire Workcell Scale to reapply outside Play Mode. The video proportion command also finishes with this compact configuration.

Verification used the actual GearboxTraining scene. The first six mounting handovers were exercised during full-sequence checks. After resolving the far-side approach limit, a separate run began with the internal mechanism prepared and passed housing placement, internal insertion, lid placement, four screw handovers and installations, final alignment, reset, pause while carrying, replay and single-step completion. Maximum endpoint error in that later-stage run was 1.33 mm. It was not one uninterrupted final run of all thirteen steps. See smaller-arm-workflow-report.txt for the passing later-stage result; smaller-arm-handovers-report.txt records the earlier six handovers and the approach failure that prompted the final route adjustment.
