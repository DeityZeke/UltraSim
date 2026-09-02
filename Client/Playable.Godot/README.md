# Launchable T0.10 client

This is the real OfflineDirect woods/cabin reference application. Its F0.0b
composition consumes the Network-free [`Client.Local`](../Local/README.md)
boundary: a private owner thread composes the authoritative runtime, Game world,
persistence, Chat, and UI, while Godot owns only input and presentation over
bounded Client-side semantic state.

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

The C0.1 Chat panel is always available at the lower left. Enter focuses the
bounded text field; choosing Whisper/Say/Yell and submitting uses packet-free
Game Chat semantics through the local retained-occurrence bridge and
`ClientChatState`. While the field has focus, gameplay movement
input is suppressed. `/help` and `/where` appear as private system rows. Spatial
speech also shows a short presentation-only overhead label when its public actor
anchor is currently resident; otherwise the feed remains authoritative for what
the client applied.

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

This application is intentionally OfflineDirect. Godot does not tick
`ServerRuntime`, access mutable ECS/Game state, scan the authority publication
store, or loop local Chat/UI through protocol or Network. It sends normal
control-session-fenced `GamePlayerCommand` intent to the authority owner thread;
public, Chat, and UI debt retires only after Client-side application proof.
Remote command transport remains a separate Online consumer path.
