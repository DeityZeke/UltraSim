# Persistent Godot client shell

F0.1 uses this project for the persistent shared Godot `ClientRoot`. It contains no Local Play or
Remote Play implementation. Each executable root supplies one fixed compile-time capability
manifest and references only the implementation family it installs:

```text
SinglePlayer.Godot
    Bootstrap.Godot + Core/Godot adapter
    Client.Local.Godot + Client.Local + built-in Cabin content
    transitive Game/packet-free replication/Server Runtime
    no Client.Network, Game.Protocol, or Kernels.Network

Online.Godot
    Bootstrap.Godot + Core/Godot adapter
    Client.Network + Game.Protocol + Kernels.Network
    no Game composition, packet-free authority replication, or Server Runtime

Complete.Godot
    union of the Local Play and Remote Play capability families
```

The persistent root owns Controller and ContentCatalog roots, the World presentation tree, and
World/HUD/Menu/Debug UI layers. It renders main, settings, loading, error, and in-session option
surfaces over the engine-neutral lifecycle/focus state in `Client.Core`. Human-readable versioned
settings live at `user://settings/client.json`; invalid documents fail closed to built-in defaults.
Settings content is vertically scrollable at the minimum client viewport. Shell button transitions
are deferred so rebuilding a surface never frees the Godot control that is still emitting its input
signal.

The shared shell renders exactly the supplied capability catalog. Missing capabilities therefore
have no menu entry, while the repository verifier independently inspects the resolved project graph,
generated `.deps.json`, and copied managed outputs. F0.0b's authority thread/local semantic bridge
lives in `Client.Local`; the shared bootstrap does not depend on it. F0.2's separately composed
`Client.Local.Godot` capability binds that session into the persistent World/HUD roots for Single
Player and Complete while Online remains physically free of the local authority/Test World family.
