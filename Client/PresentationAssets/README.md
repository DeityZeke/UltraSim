# Built-in presentation assets

This directory contains canonical build-time authoring inputs for Official presentation content.
It is not mounted into the maintained Godot project and no product exposes it as `res://Assets`.
F1.4's deterministic builder validates the exact GLBs and emits the separately removable
`Client/OfficialContent/ultrasim-official-actors-v1.uspkg` product artifact.

F0.2 recovers the historical Mixamo X Bot and Y Bot bodies as human-scale calibration content.
D0.7a replaces the separate model-only FBX imports with reproducibly consolidated GLBs. Each body
owns one skeleton and the finite `Idle`, `Walk`, and `Jump` clips which were authored against that
exact skeleton. This avoids Godot trying to associate an independently imported action skeleton
with another scene's rest pose—the path which previously produced distorted/misaligned bodies.
The authoring inputs and Blender build script live below `Client/PresentationSource/Avatars`.
The build strips the incidental Mixamo model take before export, and passive admission rejects both
unknown animation semantics and multiple names which normalize to the same semantic clip.

These files remain optional Official Local/default presentation source assets; they are not
primitive/bootstrap fallbacks. Single Player and Complete ship the exact verified package beside
the executable under `Content/Official`, not inside the executable/PCK. Local installs it through
`world.local` and the common CAS/activation/catalog path. The Concept Demo server consumes the same
exact archive to publish ordinary Realm-authorized actor packages. Online ships no Official
package, but can use an explicitly installed `world.local` package as fallback; Realm presentation
still wins and equivalent CAS bytes never grant Realm authorization or fresh application proof.

The initial presentation anchors are camera 1.6 m, nameplate 2.0 m, and effects 0.9 m; those are
presentation-definition evidence rather than Physics constants.
