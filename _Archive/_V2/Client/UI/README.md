# UltraSim Client UI Runtime

This project holds engine-agnostic client UI state and intent.

It sits between the UI kernel contracts and concrete renderers:

- `Kernels/UI/` defines schema, model, action, tooltip, and cache contracts.
- `Client/UI/` owns open panel instances, retained compatibility contexts,
  local model/repeater state, action validation, and state-binding resolver
  registration.
- `Client/_Godot/Scripts/UI/` renders validated client UI instances into Godot
  `Control` trees.

Do not put Godot nodes, server admission flow policy, account validation, or
gameplay-specific panel definitions here. Those belong in renderer bridges,
server script packages, or gameplay/server glue.
