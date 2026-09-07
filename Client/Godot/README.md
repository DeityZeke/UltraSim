# UltraSim client application project

This is the one maintained Godot application and the only production Godot script assembly. Its
`Adapter/`, `Shell/`, and conditionally compiled `Local/` sources replace the former `_Godot`,
`Bootstrap.Godot`, `Local.Godot`, and `Playable.Godot` projects. Keeping all native scripts in
`UltraSim.Client.Application.Godot` avoids duplicate Godot type registration during editor reload.
Only the scene-attached `ClientHost.cs` is imported as a Godot script. Internal adapter, shell,
local-play, and remote-play C# sources are `.gdignore`d from Godot's resource scanner while continuing to compile
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

Godot 4.7's .NET exporter also requires the adjacent
`UltraSim.Client.Application.Godot.sln`. It contains only this application project; the permanent
export gate rejects its absence or any extra solution project before producing the actual Online
Linux product.

Linux exports use:

```bash
Client/Godot/export-client.sh SinglePlayer
Client/Godot/export-client.sh Online
Client/Godot/export-client.sh Complete
```

The Online preset excludes bundled `Assets/**`. A0.8 launches that product from the primitive
bootstrap substrate and an empty local/default catalog; its playable WorldPack and typed Inventory
UI definition arrive only through the authenticated content requirement, persistent CAS, and
World/UI-owned validation paths. The actual exported executable passes the same external cold/warm
journey, and its exported managed closure is inspected for forbidden local authority and bundled
World/package artifacts.

For normal development, open this directory in Godot and run the Complete build. Choose **Single
Player**, enter a temporary guest display name, choose the X/Human Female or Y/Human Male body, and
select **Join** to enter the Concept Demo island. The accepted identity exists only for that session;
no account or saved character is created. Runtime diagnostics show the configured render radius and
the authored/resident/rendered chunk counts; intentional-void chunks have no mesh, so a larger
window can change the counts without adding visible geometry at the current location.

F0.3a makes the **Multiplayer / Direct Connect** entry real in Online and Complete. Start the
external identity gateway with:

```bash
dotnet run --project Server/DirectConnect/UltraSim.Server.DirectConnect.csproj
```

Then connect to `127.0.0.1:7777`. On first use, compare the client candidate fingerprint with the
gateway's printed fingerprint before choosing **Trust and Continue**. Reconnect uses the saved pin;
a changed deployment key is blocked and requires an explicit forget followed by a fresh trust
ceremony. The server's content endpoint defaults to `127.0.0.1:7778`; local HTTP/private-address
use requires the explicit development opt-in in the Direct Connect form. A0.8 then acquires or
reopens the authorized package, applies a fresh session proof, binds the WorldPack, and enters the
same remote guest/control/public-world/Chat/Inventory path. A warm cache transfers no package
payload but never reuses prior-session application knowledge.

After realm authentication, F0.3c displays the deployment's declarative stock client profile before
content acquisition. **Use Server Defaults** accepts its camera/grammar/bindings, **Keep My
Controls** retains the user's physical mappings, and **Customize for This Server** opens the normal
settings surface against that verified realm's override layer. Only the exact realm/revision/hash
decision is remembered in `user://settings/server-profiles.json`; recommendations never enter the
user settings document. An exact reconnect does not prompt, while a changed revision or hash does.
The server may be launched with `--client-profile=orbit|third-person|independent`, and the same
Online executable applies the corresponding stock camera/control implementation.

Godot lifecycle overrides remain in the canonical scene script, `ClientHost.cs`; partial remote
implementation files provide helpers but do not declare `_Process`/`_ExitTree`. This preserves
Godot's generated native dispatch while retaining compile-time product separation.

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
Chat sender label. F0.4 routes that controlled human and the nearby woodland animal through the
same renderer-neutral actor state used by Online, verifies Ctrl reveal over already-known eligible
actors, and anchors spatial speech to the exact presented public identity. Online has no bundled
X/Y scenes, so its same presenter deliberately realizes the current actors through the primitive
substrate rather than reintroducing hidden default content.

F1 Orbit starts in a close three-quarter composition: 45-degree elevation at 14 m rather than the
older 64-degree/28 m overhead survey view. Version-2 settings that merely captured the exact old
28 m Orbit default migrate to the new composition; distinct user-selected distances remain intact.
