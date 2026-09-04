# UltraSim client application project

This is the one maintained Godot application project. The `UltraSimEdition` MSBuild property creates
three physically different dependency products from the same scene, settings, input surface, import
database, and presentation assets:

```text
SinglePlayer
    Bootstrap.Godot + Local.Godot
    no Client.Network / Game.Protocol / Network kernel

Online
    Bootstrap.Godot + Client.Network
    no Client.Local / local Game composition / Server Runtime

Complete
    union of both capability families
```

Opening or building the project without an explicit property defaults to `Complete` for development.
Canonical product builds must set `UltraSimEdition` exactly; the repository verifier publishes each
edition into an isolated directory and inspects its `.deps.json` and copied assemblies. Build-time
composition is the security/capability boundary. The matching export-preset feature is only a
startup consistency check.

Linux exports use:

```bash
Client/Godot/export-client.sh SinglePlayer
Client/Godot/export-client.sh Online
Client/Godot/export-client.sh Complete
```

The Online preset excludes bundled `Assets/**`. Actual passive-content acquisition remains owned by
the later asset-streaming checkpoints.
