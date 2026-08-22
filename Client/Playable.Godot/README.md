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

- 1/F1 `OrbitCommand`: overhead mouse-command camera; click selects, hold steers,
  double-click sets Destination, WASD does not move, and the character faces
  the accepted steering/destination direction.
- 2/F2 `ThirdPersonDirect`: close third-person camera with camera-relative WASD;
  the character faces the accepted movement vector.
- 3/F3 `IndependentMoveLook`: fixed isometric-style camera, world-axis WASD
  (`W` is always north), and authoritative mouse-facing independent of movement.

Use the on-screen profile buttons if the embedded Godot editor consumes function
keys. Middle-mouse drag and wheel zoom affect Orbit/ThirdPerson but are locked in
IndependentMoveLook. E uses the selected cabin door/chest, F5 saves, and F9
restores.

The player starts just south of a recognizable 8 m x 6 m openable cabin. The
thin wall segments, trees, shelf, closed door, and chest use authoritative
world bounds: ordinary Direct movement stops against them, opening the door
allows passage through its two-metre gap, and a blocked straight-line
Destination stops because this T0.10 client has no pathfinder yet. Godot physics
is not the authority and there is not yet wall sliding or automatic route-around.
The placeholder grounded meshes use outward Godot triangle winding and normals;
their interior/back faces are not the intended visible shell. The asymmetric
player wedge is built through the same Godot-native conversion helper and has
its own closed-prism winding/normal acceptance.

At the finite authored-world edge, the complete 3x3 structural window clamps to
the final available chunks while the player-relative camera origin continues to
follow the actor. Structural-window center and viewer origin are deliberately
separate; there is no implicit camera breakaway mode yet.

This application is intentionally OfflineDirect. It must not adapt the legacy
network `InputVelocity` path; remote control-session binding and command
transport remain N1 work.
