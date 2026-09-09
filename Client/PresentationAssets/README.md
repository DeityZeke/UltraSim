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

These remain optional bundled local/default presentation assets. They are not primitive/bootstrap
fallbacks, are not A0 downloaded package payloads, and do not establish runtime model/animation
artifact realization. Online therefore continues to render the same semantic actors through the
humanoid primitive. Its required content/application proof is never satisfied by these files.

The initial presentation anchors are camera 1.6 m, nameplate 2.0 m, and effects 0.9 m; those are
presentation-definition evidence rather than Physics constants.
