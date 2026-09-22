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
    no Client.Network / Game.Protocol / UltraSim.Network

Online
    Client Core + Client.Network + UltraSim.Network ClientOnly + Godot wrapper
    no Client.Local / local Game composition / Server Runtime

Complete
    union of both capability families + canonical Full UltraSim.Network
```

Opening or building the project without an explicit property defaults to `Complete` for development.
Canonical product builds must set `UltraSimEdition` exactly; the repository verifier publishes each
edition into an isolated directory and inspects its `.deps.json` and copied assemblies. Build-time
composition is the security/capability boundary. The matching export-preset feature is only a
startup consistency check.

All Network variants retain the same `UltraSim.Network.dll` identity. The export script therefore
selects the intended `UltraSimNetworkFlavor` explicitly, and the verifier inspects the actual DLL:
Online must contain Hosted Client and exclude Hosted Server, Complete must contain both, and Single
Player must contain no Network assembly. Assembly identity alone is never treated as proof of the
physical capability set.

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

Saving a live Render Distance change rebuilds native chunks and the bounded structural-residency
window, but deliberately retains the current-generation semantic cache. Public actors and objects
that were already applied therefore remain present without waiting for movement or a chunk crossing
to manufacture new publication work. The `--d0-1-smoke` gate exercises radii 1, 2, and 3 and requires
the controlled actor, cache identity, command sequence, and post-resize movement to remain valid.

F0.3a makes the **Multiplayer / Direct Connect** entry real in Online and Complete. Start the
external identity gateway with:

```bash
dotnet run --project Server/DirectConnect/UltraSim.Server.DirectConnect.csproj \
    -p:UltraSimNetworkFlavor=ServerOnly
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
The shipped different-feel canary makes that difference concrete: Third Person recommends an
8.5 m / 76-degree follow camera with middle-click Select and left-click Primary, while Orbit
recommends a 14 m / 70-degree orbit camera with left-click Select and middle-click Primary.
These remain local consented settings; neither profile supplies executable client behavior.

Godot lifecycle overrides remain in the canonical scene script, `ClientHost.cs`; partial remote
implementation files provide helpers but do not declare `_Process`/`_ExitTree`. This preserves
Godot's generated native dispatch while retaining compile-time product separation.

F1.0 freezes the pre-rework client ownership surface through the existing F0.7 Complete-product
journey. With `--f1-0-evidence=<path>`, that journey emits one bounded JSON snapshot covering Local,
cold Remote A, cold Remote B over the shared CAS, warm Remote A reconnect, settings/profile identity,
semantic residency, native renderer/actor/UI ownership, and all six clean retirement boundaries.
The canonical verifier owns the assertions and may retain the raw capture through
`ULTRASIM_F10_EVIDENCE_OUTPUT`. The accepted immutable summary lives under
`_Benchmarks/Baselines/client-f1.0-pre-rework-baseline-2026-09-19.*`. These are diagnostic
guardrails only; they do not introduce F1.1 content scopes or the later UI/rendering architecture.

Runtime-batched frozen/public `MultiMeshInstance3D` groups publish an exact combined transformed
custom AABB. Godot otherwise leaves these programmatic MultiMeshes with a zero native bound and may
frustum-cull a door, chest, telepad, gate, or sign from the chunk origin even while its authoritative
interaction and collision remain valid. The D0.3 native smoke crosses real render frames and requires
complete nonzero public render bounds before accepting the informational-object path.

Local saves live below `user://single-player/`. Controls are shown in the in-session HUD: F1/F2/F3
select movement profiles; F1 single-click selects, hold steers, and double-clicking ground creates a
destination; WASD moves in direct profiles; E interacts with the selected target; Space jumps in
direct profiles; F4 toggles the read-only Server Monitor; and the mouse wheel adjusts supported
camera profiles. The Central Island welcome sign exposes a local `Sign`
hover from resident presentation state; select it and press E to open its authoritative
informational view, then use the view's Close action to dismiss it.

Compiled UI Canvases are centered in the viewport. The Inventory v8 definition supplies its own
surface and authoritative Close action; `I` opens it when no UI is active and closes the existing
Inventory instead of stacking duplicate transparent views. Compiled definitions select a bounded
outside-pointer policy. The modeless default blocks world click-through over the visible UI but
allows outside gameplay input while the view remains open. Character Select explicitly blocks
gameplay; informational signs submit their ordinary authoritative Close action on an outside click.
Window dragging remains deferred. Tall
stock public objects use bounded full-height pointer volumes before terrain projection, so the
visible upper portions of doors and signs nominate those disclosed objects normally.

I0.12 adds the first persistent world item to both local and remote consumers. An Iron Sword can be
selected and picked up with the ordinary Interact path, inspected in Inventory, equipped, used with
Primary, and dropped through the compiled Inventory view. Equipped sword presentation is attached
beneath the semantic `MainHandSocket`. That actor-relative socket samples the imported hand bone's
complete animated transform while orthonormalizing away the skeleton's `0.01` authoring scale. An
explicit grip correction gives the one-handed blade a stable diagonal resting pose while Idle/Walk
carry it with the wrist instead of leaving a world-vertical pendulum. A separate child pivot owns the bounded swing,
so retiring the cue cannot overwrite the resting mount. The semantic world item has a bounded
primitive presentation/pick volume; recipient-visible rolled facts arrive through the bounded cold
public metadata cache rather than hot movement rows. I0.13 formats that typed set through the same
engine-neutral catalog/profile used by Inventory. The shared sign/item tooltip has no sword-sized
minimum: it sizes to its visible title/rows, wraps at a bounded viewport-relative width, clamps to
the viewport, and only rebuilds its content tree when content or available layout width changes.
Godot auto-wrap labels carry an explicit maximum width; rows that already fit remain unwrapped, and
each cached Show reasserts the top-level panel's current bound minimum. This prevents a deferred
native wrap pass from leaving intermittent blank height after repeated sword hovers. An accepted
Primary result drives a short primitive cue on the controlled actor. It is feedback, not Combat
authority; it remains the fallback because the current bodies have no Attack clip.

Holding Ctrl also reveals the generic `Iron Sword` world label above an already-present ground item.
It never reveals rolled item properties; those remain available only through pointer inspection or
Inventory, and the reveal modifier performs no server query or AOI change.

F0.5 is closed. The application composes the passive `Client.StandardUi` Inventory artifact into
the same local/remote `UiClientRuntime` path used by reopened cached and content-backed exact
definitions. Online gains that passive artifact without gaining Game UI/Inventory authority, and
there is no second Godot UI implementation.

M0.0 is closed. In the Complete edition, the in-session menu can enable or stop remote hosting on
the already-running Concept Demo authority. The host remains on `Client.Local`; only remote guests
use Network. The current development control binds loopback endpoints and displays the exact
endpoint/fingerprint after activation. The permanent native gate launches Complete as Host Alice,
joins two separately running inspected exported Online clients, requires all three humans to use
the normal actor presenter, then observes both remote detaches and stops hosting without replacing
the local Game generation. Single Player and Online do not compile the Complete-only local-host
adapter.

C0.2 extends both local and Online Chat selectors with `Global`. The feed presents the same applied
spatial/Global/System occurrences in both compositions. Only spatial facts can create actor-anchored
overhead text; an explicit System fact also drives a top-centered banner for 4.5 seconds. That
timeout is Godot presentation state and does not remove the retained feed occurrence or create a
second protocol message.

D0.6 displays `Ping: Local` for the packet-free internal authority and sanitized smoothed peer RTT
for Online. F4 requests the normal Game-composed `game.server-monitor.gump/v2` read-only view. Local
receives its compiled definition over the direct semantic UI bridge; remote play acquires and
authorizes the exact passive `.usui` with the World/Inventory deployment content and receives the
existing compact HotOpen. Both render through the same `UiClientRuntime`/`GodotUiRenderer`. The
exported Online smoke now opens Inventory, then the content-backed Server Monitor, and requires its
native participant list before completing Chat and movement evidence.

D0.7a makes actor motion, equipment, and accepted actions one shared presentation path. The
presenter derives horizontal `Idle`/`Walk` from successive committed authoritative positions, while
Game-owned `Grounded`/`Rising`/`Falling` presentation state selects vertical motion and an exact
airborne-to-grounded transition produces the one-shot landing cue. Animation playback never becomes
Physics or Game truth. The local X/Y presentation uses reproducibly consolidated GLBs containing one body skeleton
plus exact `Idle`, `Walk`, and `Jump` actions. An explicit semantic `MainHandSocket` follows the
body's scale-free animated right-hand transform and applies an equipment grip correction, with a fixed
primitive anchor only for fallback actors. The public actor
metadata exposes only the equipped public prefab and latest accepted `Interact`/`Primary` sequence;
private sword rolls stay in Inventory/Inspection state. Late actor realization records the sequence
without replaying the old cue. Dropped swords return to ordinary batched world presentation, while
equipped swords remain attached, visible, and non-pickable.

The archived source has no Attack or Interact clips. Those actions therefore retain short bounded
primitive cues; a missing animation can never block accepted gameplay. D0.7b adds the runtime
content path without adding another presenter. Online excludes `Assets/**`, resolves logical X/Y
ActorPresentation definitions through an independently authorized presentation generation, pins
their verified CAS blobs, and lazily imports an UltraSim-controlled inert GLB scene. Passive and
native validation both require the exact skeleton/socket/Idle-Walk-Jump profile. The Realm
presentation plan Adds X/Y and an ordered package visibly Replaces X. It does not gate the
WorldPack/UI application proof. Equivalent loose files cannot bypass scope authorization; missing
or incompatible art falls through `world.local` then the humanoid primitive.

`GodotActorPresenter.RefreshResolvedVisuals()` replaces only the visual child after a resource is
realized. The actor root, generational public identity, controlled role, nameplate, overhead Chat,
action fence, and equipped Main Hand survive. Import failure has a finite diagnostic and leaves the
humanoid primitive. The actual cold/warm Online export canary contains no body GLB. Required
World/UI acquisition proves the application independently; Realm art can arrive later from CAS
and upgrade an existing primitive visual without replacing its actor root or semantic identity.

F0.7 adds a permanent Complete-client mixed-session lifecycle canary. One native process enters
two independent local sessions, remote deployment A, distinct remote deployment B, deployment A
again, and a final local session. Each lifetime is populated with an open compiled UI view, actors,
nameplates, overhead Chat, rendered chunks, focused Chat input, and—remotely—leased actor content
before retirement. Every menu boundary must return to the exact persistent root shape, clear the
active realm profile and transient transport/content consumers, and restore global input/camera
settings, including camera distance/FOV and every physical binding. The deployments retain
distinct realm-bound catalog identities over the same content
payload contract. Realm A's exact durable profile decision is reused on its same-process reconnect
while a fresh application-session epoch is still required.

F0.8 exports, inspects, and executes all three physical Godot products. The actual Single Player
export has no Network assembly, the actual Contentless Online export has ClientOnly Network and no
local authority/playable content/body GLBs, and the actual Complete export has Full Network plus
both capability families. That exported Complete binary hosts two separately running exported
Online binaries while retaining its own packet-free local participant. The same Online executable
also applies the materially different Third Person and Orbit profiles above over one unchanged
deployment/realm/content identity; the changed profile needs fresh consent but no content refetch.

The Fallback Lab sits south-west of the welcome area. Its four pedestals show, left to right, a
complete model/material, a neutral category primitive for a missing model, the real model with a
magenta/yellow diagnostic material for a missing texture, and both fallbacks together. The nearby
sign explains the cases through the same authoritative informational-UI path. These visuals never
change the immutable objects' collision or support geometry. `--d0-4-smoke` permanently verifies
the three fallback classes, model/material independence, native bounds, and sign UI.

F1.3 makes the primitive substrate permanent and bounded. Engine-neutral
`ClientPrimitivePresentation` descriptors supply stock actor/item/public volumes and exact
disclosed frozen-static/Multi-fragment bounds; known public pointer nominations use the same
stock bounds. `GodotPrimitiveSubstrate` owns the actor primitives, neutral and high-contrast
missing-texture materials, and one generic missing icon. `--f1-3-primitive-smoke` deliberately
suppresses optional local actor/static/public art while retaining the normal semantic world and
required activation. It covers interaction, sword pickup/equip/drop/re-pickup, Inventory, Server
Monitor, Chat, nameplates/overhead speech, action cues, and clean session retirement. This is
an acceptance composition. F1.4 now removes Official X/Y art from every PCK and consumes the exact
external `Content/Official/ultrasim-official-actors-v1.uspkg` archive through `world.local`, the CAS,
an immutable presentation generation, and `GodotContentActorVisualResolver`. Single Player and
Complete export that package beside the executable; Online remains contentless. The unchanged Local
binary accepts `--official-content-absent`/`--official-content-root=...`; the maintained
`--f1-4-official-smoke=installed|absent` proves both native realization paths and clean retirement.

`--d0-5-smoke` crosses the real Single Player button and Admission form. It proves rejected input
does not start authority, then enters with a trimmed Unicode name and the Y body and checks the
authoritative guest identity, selected public character prefab, controlled nameplate, and spatial
Chat sender label. It also drives the native Chat input lifecycle: Enter submits exactly once,
Escape releases Chat without opening the shell menu, click-away preserves the draft, and gameplay
input resumes after focus is released. Focused Chat owns those events even though shared shell focus
policy has already disabled gameplay input. F0.4 routes that controlled human and the nearby woodland animal through the
same renderer-neutral actor state used by Online, verifies Ctrl reveal over already-known eligible
actors, and anchors spatial speech to the exact presented public identity. Online has no bundled
X/Y scenes, so its same presenter deliberately realizes the current actors through the primitive
substrate rather than reintroducing hidden default content.

The controlled nameplate is visible for 2.5 seconds after its control lifetime begins, then hides
until Ctrl reveal. Ctrl remains purely local presentation over already-known eligible actors.

The controlled actor is a special presentation consumer of the fresh owner-private pose already
used to position the player-relative world. Its independently delivered public row remains useful
to other observers but may trail that owner pose. The shared Local/Online presenter therefore pins
the controlled root to the owner pose and uses public rows for every other actor; combining a fresh
world origin with a stale controlled public row would otherwise make the avatar sawtooth backward
and forward as the two valid streams converge.

F1 Orbit starts in a close three-quarter composition: 45-degree elevation at 14 m rather than the
older 64-degree/28 m overhead survey view. Version-2 settings that merely captured the exact old
28 m Orbit default migrate to the new composition; distinct user-selected distances remain intact.
