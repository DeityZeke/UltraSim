# Client executable composition foundation

F0.0a uses this project for the minimal shared Godot bootstrap/menu. It contains no Local Play or
Remote Play implementation. Each executable root supplies one fixed compile-time capability
manifest and references only the implementation family it installs:

```text
SinglePlayer.Godot
    Bootstrap.Godot + Core/Godot adapter
    Client.Local + transitive Game/packet-free replication/Server Runtime
    no Client.Network, Game.Protocol, or Kernels.Network

Online.Godot
    Bootstrap.Godot + Core/Godot adapter
    Client.Network + Game.Protocol + Kernels.Network
    no Game composition, packet-free authority replication, or Server Runtime

Complete.Godot
    union of the Local Play and Remote Play capability families
```

The shared bootstrap renders exactly the supplied capability catalog. Missing capabilities
therefore have no menu entry, while the repository verifier independently inspects the resolved
project graph, generated `.deps.json`, and copied managed outputs. The entries remain placeholders
until their later roadmap checkpoints. F0.0b's authority thread/local semantic bridge now lives in
`Client.Local`; the shared bootstrap does not depend on it, and F0.2 is where the Single Player menu
entry becomes the real Cabin/Test World launch path.
