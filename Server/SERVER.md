
The UltraSim "Server" is Engine Agnostic and should have clean boundaries
it can be started in a 'headless' mode via command prompt
for a GUI/UI use the "Console" or "Godot" versions - they should be WRAPPERS ONLY
that display output and limited input functionality

Current wrapper/runtime plan:

- `Server/Core` is the real reusable application runtime
- `Server/Hosting` owns shared host-loop plumbing
- `Server/_Headless` is the canonical dedicated-server shell
- `Server/_Console` and `Server/_Godot` are thin wrappers over the same control surface

See [SERVER_WRAPPER_IMPLEMENTATION_PLAN.md](/d:/MEGA/Godot%20Projects/UltraSim/Server/SERVER_WRAPPER_IMPLEMENTATION_PLAN.md) for the concrete implementation plan and [AGENTS.md](/d:/MEGA/Godot%20Projects/UltraSim/Server/AGENTS.md) for subsystem-specific engineering rules.
