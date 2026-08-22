# Launchable T0.10 client

This is the real OfflineDirect woods/cabin application composition root. It
keeps the reusable Godot adapter narrow while composing the local authoritative
runtime, Game world, persistence, Client Core profiles/cache/window, and native
renderer in one launchable project.

From the repository root:

```bash
godot --path Client/Playable.Godot --editor
```

or launch the project directly with a Godot 4.7 .NET build. The scene includes
an on-screen control reference. Saves live under `user://t0-10-saves`.

This application is intentionally OfflineDirect. It must not adapt the legacy
network `InputVelocity` path; remote control-session binding and command
transport remain N1 work.
