# Built-in presentation assets

This directory contains build-time client presentation assets shared by executable roots that
install the local Cabin capability. `SinglePlayer.Godot`, `Complete.Godot`, and the legacy
`Playable.Godot` acceptance wrapper expose it as `res://Assets`; `Online.Godot` does not.

F0.2 recovers the historical Mixamo X Bot and Y Bot bodies as human-scale calibration content.
They remain bundled assets, are not A0 downloaded package payloads, and do not establish a runtime
model-import surface. Their initial presentation anchors are camera 1.6 m, nameplate 2.0 m, and
effects 0.9 m; those are presentation-definition evidence rather than Physics constants.
