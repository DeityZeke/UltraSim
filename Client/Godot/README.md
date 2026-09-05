# UltraSim client application project

This is the one maintained Godot application and the only production Godot script assembly. Its
`Adapter/`, `Shell/`, and conditionally compiled `Local/` sources replace the former `_Godot`,
`Bootstrap.Godot`, `Local.Godot`, and `Playable.Godot` projects. Keeping all native scripts in
`UltraSim.Client.Application.Godot` avoids duplicate Godot type registration during editor reload.
Only the scene-attached `ClientHost.cs` is imported as a Godot script. Internal adapter, shell, and
local-play C# sources are `.gdignore`d from Godot's resource scanner while continuing to compile
into that same assembly; this prevents inherited native base types from being registered again
during editor hot reload. The build also keeps only the active Debug/Release copy of the sole script
assembly in the live development tree.

Godot's C# source generator still emits managed script metadata for native-derived types compiled
inside that assembly. Each such type therefore stays in a source file with the matching class name;
in particular, `ClientRoot` lives in `Shell/ClientRoot.cs`. A mismatched historical filename left
the abstract root without its own generated script path, so editor Play could try to register that
same inherited type twice even though only one DLL existed.

The `UltraSimEdition` MSBuild property creates three physically different dependency products from
the same scene, settings, input surface, import database, and presentation assets:

```text
SinglePlayer
    Client Core + Client.Local + built-in local content + Godot wrapper
    no Client.Network / Game.Protocol / Network kernel

Online
    Client Core + Client.Network + Godot wrapper
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

For normal development, open this directory in Godot and run the Complete build. Choose **Single
Player**, enter a temporary guest display name, choose the X/Human Female or Y/Human Male body, and
select **Join** to enter the Concept Demo island. The accepted identity exists only for that session;
no account or saved character is created. Runtime diagnostics show the configured render radius and
the authored/resident/rendered chunk counts; intentional-void chunks have no mesh, so a larger
window can change the counts without adding visible geometry at the current location.

Runtime-batched frozen/public `MultiMeshInstance3D` groups publish an exact combined transformed
custom AABB. Godot otherwise leaves these programmatic MultiMeshes with a zero native bound and may
frustum-cull a door, chest, telepad, gate, or sign from the chunk origin even while its authoritative
interaction and collision remain valid. The D0.3 native smoke crosses real render frames and requires
complete nonzero public render bounds before accepting the informational-object path.

Local saves live below `user://single-player/`. Controls are shown in the in-session HUD: F1/F2/F3
select movement profiles; F1 single-click selects, hold steers, and double-clicking ground creates a
destination; WASD moves in direct profiles; E interacts with the selected target; Space jumps in
direct profiles; and the mouse wheel adjusts supported camera profiles. The Central Island welcome sign exposes a local `Sign`
hover from resident presentation state; select it and press E to open its authoritative
informational view, then use the view's Close action to dismiss it.

The Fallback Lab sits south-west of the welcome area. Its four pedestals show, left to right, a
complete model/material, a neutral category primitive for a missing model, the real model with a
magenta/yellow diagnostic material for a missing texture, and both fallbacks together. The nearby
sign explains the cases through the same authoritative informational-UI path. These visuals never
change the immutable objects' collision or support geometry. `--d0-4-smoke` permanently verifies
the three fallback classes, model/material independence, native bounds, and sign UI.

`--d0-5-smoke` crosses the real Single Player button and Admission form. It proves rejected input
does not start authority, then enters with a trimmed Unicode name and the Y body and checks the
authoritative guest identity, selected public character prefab, controlled nameplate, and spatial
Chat sender label.

F1 Orbit starts in a close three-quarter composition: 45-degree elevation at 14 m rather than the
older 64-degree/28 m overhead survey view. Version-2 settings that merely captured the exact old
28 m Orbit default migrate to the new composition; distinct user-selected distances remain intact.
