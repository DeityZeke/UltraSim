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

Profiles are intentionally distinct:

- F1 `OrbitCommand`: overhead mouse-command camera; click selects, hold steers,
  double-click sets Destination, and WASD does not move.
- F2 `ThirdPersonDirect`: close third-person camera with camera-relative WASD.
- F3 `IndependentMoveLook`: wider move/look proof camera with WASD movement and
  an independent pointer look nomination.

Middle-mouse drag rotates the current camera, the wheel zooms, E uses the
selected cabin door/chest, F5 saves, and F9 restores.

This application is intentionally OfflineDirect. It must not adapt the legacy
network `InputVelocity` path; remote control-session binding and command
transport remain N1 work.
