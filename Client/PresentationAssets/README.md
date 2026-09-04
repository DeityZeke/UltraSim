# Built-in presentation assets

This directory contains build-time client presentation assets shared by products that install the
local Cabin capability. The maintained `Client/Godot` project and legacy `Playable.Godot`
acceptance wrapper expose it as `res://Assets`; the Online export preset excludes `Assets/**` from
its product.

F0.2 recovers the historical Mixamo X Bot and Y Bot bodies as human-scale calibration content.
They remain bundled assets, are not A0 downloaded package payloads, and do not establish a runtime
model-import surface. Their initial presentation anchors are camera 1.6 m, nameplate 2.0 m, and
effects 0.9 m; those are presentation-definition evidence rather than Physics constants.
