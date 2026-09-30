# Industrial worker

The Worker in Assets/Scenes/GearboxAssembly.unity uses a 1.86 m skinned adult humanoid with a textured face, individual fingers, orange safety shirt, work overalls with reflective bands, black shoes, and a hard hat. The model is Assets/Models/Human/IndustrialWorker.fbx; source/license information is beside it. No additional runtime packages were installed.

The existing Worker prefab, Humanoid Animator Controller, six animations and WorkerMovement controls remain. Enable Keyboard Control for arrow-key movement and keys 1–4 for Point, PickUp, Place and HandOver. MoveTo(Vector3) and Stop() provide straight-line movement with collision handling. These are basic authored gestures, not motion capture or automatic object manipulation. HumanRobotHandover retains ownership of part transfers and temporarily suspends the Animator for target-driven reaches.

To replace the character again, retain the Worker root and its CharacterController. Assign the new Humanoid Animator to WorkerMovement and HumanRobotHandover, use Worker.controller, and place WorkerRightHandHoldPoint beneath the new right hand. Adjust the capsule to fit. Tools > Gearbox Demo > Setup Worker Animations rebuilds the clips from the current prefab's humanoid proportions.

Verified in Unity 6000.6.0f1: Humanoid validity, six animation states and returns to Idle, movement/destination stop, handover references, target-driven reach and Animator reset. Visual previews and the playback log are in Logs/worker-realistic-*.
