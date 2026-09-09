# Built-in presentation assets

This directory contains build-time client presentation assets shared by products that install the
local playable capability. The maintained `Client/Godot` project exposes it as `res://Assets`; the
Online export preset excludes `Assets/**` from its product.

F0.2 recovers the historical Mixamo X Bot and Y Bot bodies as human-scale calibration content.
D0.7a replaces the separate model-only FBX imports with reproducibly consolidated GLBs. Each body
owns one skeleton and the finite `Idle`, `Walk`, and `Jump` clips which were authored against that
exact skeleton. This avoids Godot trying to associate an independently imported action skeleton
with another scene's rest pose—the path which previously produced distorted/misaligned bodies.
The authoring inputs and Blender build script live below `Client/PresentationSource/Avatars`.
The build strips the incidental Mixamo model take before export, and passive admission rejects both
unknown animation semantics and multiple names which normalize to the same semantic clip.

These files remain optional bundled local/default presentation assets for products with local play;
they are not primitive/bootstrap fallbacks. D0.7b also embeds the exact consolidated authoring bytes
in the Concept Demo server build and publishes them as ordinary verified actor-content packages.
The exported Online product still excludes `Assets/**`: it obtains X/Y from the authenticated
activation/CAS and realizes them lazily. Merely finding equivalent preinstalled bytes never
satisfies realm authorization or fresh application proof.

The initial presentation anchors are camera 1.6 m, nameplate 2.0 m, and effects 0.9 m; those are
presentation-definition evidence rather than Physics constants.
