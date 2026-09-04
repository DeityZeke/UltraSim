# UltraSim Concept Demo — Human-Facing Integration and Live Test Plan

**Status:** accepted cross-cutting demo/test target. This document does not replace the owning
subsystem contracts in `CLIENT.md`, `ASSET_STREAMING.md`, `TERRAIN.md`, `PHYSICS.md`, `CHAT.md`,
Network, Inventory, or UI. It defines the first coherent world and human test experience that earns
and exercises those capabilities together.

**Current baseline:** Client F0.0a, the permanent Network-free F0.0b local-session boundary, the
F0.1 persistent shell/settings/focus foundation, A0.0/A0.1 local content resolution, and the F0.2
Single Player Cabin/human-calibration vertical are closed. `Client/Godot` is the sole maintained
native client and compiles its local consumer into the Single Player and Complete editions. Terrain T1.0
supplies explicit base-surface presence/intentional-void semantics, and T1.1 supplies immutable
sparse terrain shells at free absolute elevations for the Sky Island and cave. Physics support,
grounding, and clearance are closed at P0.1c, and Physics P0.1d now supplies authoritative
jump/gravity/fall/landing/recovery. Concept Demo D0.0 now composes those accepted capabilities into
the permanent Single Player client as a floating Central Island, MountainShell cave, detached Sky
Island, and Game-owned telepad. The next local world checkpoint is render-distance/larger-residency
policy before adding the geographically separate Far Island and gate.

The demo has two purposes:

1. provide a small, understandable **super-pre-alpha concept demo** that a human can explore without
   needing an architecture briefing;
2. turn the same world into a controlled functional, multiplayer, AOI, streaming, content, UI, and
   stress workload with useful evidence when more players join.

The central rule is:

> **Demo content should explain itself while exercising real architecture.**
>
> A sign, island, cave, gate, fallback object, chat mode, inventory item, or diagnostic panel should
> exist because it demonstrates a real capability or failure mode, not because the demo needs random
> decoration.

---

# 1. Relationship to owning plans

This plan supplies concrete consumers. The owning subsystem still defines the contract.

```text
CLIENT.md
    executable composition
    local/remote semantic boundary
    persistent shell/settings/focus
    presentation window
    Direct Connect trust
    nameplates/chat presentation
    server-composed UI

ASSET_STREAMING.md
    passive content identity
    Default/Replace/Add
    content activation/application proof
    CAS/cache
    local/self-hosted/external byte sources
    fallback/contentless client

TERRAIN.md / Game World
    base terrain topology
    surface presence/absence
    additional sparse world-terrain surfaces
    cave/sky surface ownership
    structural residency

PHYSICS.md
    support/grounding
    vertical motion/gravity
    jump/fall/landing
    occupancy/relocation

CHAT.md
    Whisper/Say/Yell spatial facts
    first routed Global channel
    system broadcasts
    commands remain distinct

Inventory/UI
    player inventory
    cabin chest
    pickup/drop/equip
    minimal server-composed panels

Network/Replication
    public player state
    AOI/ChunkPublication
    retained facts/application proof
    Hosted external sessions
```

The demo may earn reopening a stopped seam. It may not quietly redefine that subsystem in this file.

---

# 2. Distribution and audience

Two concrete client distributions are used for the first external concept test.

## Online / MMO client

This is the ordinary tester build.

```text
Multiplayer / Direct Connect
Settings
Exit

NO local authoritative Game/server implementation
NO Single Player capability
```

It connects to the externally hosted Concept Demo server and contains only the client capabilities
allowed by the current Online composition/content plan.

## Complete client

A small number of trusted development friends may receive the Complete build:

```text
Single Player
Multiplayer
Settings
Exit
```

The Complete build includes local-authority capability because of executable composition, **not**
because the holder is a server administrator.

> **CLIENT EDITION != SERVER PERMISSION.**

Copying or receiving the Complete binary never grants mutable server/ECS/admin authority. Any later
privileged diagnostic/control capability is explicitly server-authorized by session/identity policy.

---

# 3. Round 1 versus Round 2

## Round 1 — concept demo

Round 1 proves the client/world/network/content/game seams without opening a Combat campaign.

Required experience includes:

- guest/demo Direct Connect entry with no account system;
- X/Y body selection plus bounded validated display name;
- large floating central terrain;
- true intentional void at floating-terrain edges;
- one upper floating terrain surface / Sky Island;
- one cave/lower terrain area on the opposite side;
- telepad access to the Sky Island for non-jump control profiles;
- real authoritative jump for the third-person profile once Physics earns it;
- a very distant island in the same world, reached through an authoritative gate;
- Cabin, door, chest, minimal player Inventory;
- one pickup/equip/drop tool or weapon and an authoritative swing/action with no damage;
- X/Y animation set sufficient for idle/movement/jump/swing/interaction presentation;
- signs that explain the demo and destinations;
- Fallback Lab explaining missing-content presentation;
- nameplates, Ctrl reveal, overhead speech, and chat feed;
- Local spatial chat plus one Global routed channel;
- system/test broadcasts shown both in chat and a transient top banner;
- functional render-distance setting;
- client ping display;
- read-only Server Monitor including sanitized player ping rows;
- opt-in client diagnostic capture;
- broad server-side test diagnostics and test markers.

Round 1 does **not** require HP, damage, death, PvP, combat balance, accounts, saved characters, a
full social/chat suite, matchmaking, public server discovery, or production CCU claims.

## Round 2 — minimal combat

After Round 1 is stable, a separately reviewed second round may add:

```text
Health
Damage
validated hit/attack consequence
player-versus-player whacking with the equipped test weapon
death/respawn
minimal combat feedback
```

Round 1's attack/swing is intentionally useful before damage exists. Do not hide Round 2 Combat
semantics inside the animation or Inventory implementation.

---

# 4. Guest/demo admission — no Accounting requirement

The concept demo does not require account creation or saved character ownership.

Desired Online flow:

```text
launch Online client
    -> Multiplayer / Direct Connect
    -> trust/pin server according to CLIENT.md
    -> guest/demo entry

Name
    bounded validated display name

Body
    X / Human Female
    Y / Human Male

    -> Enter World
```

The client supplies intent only. The server validates supported body identity, name bounds/policy,
application-session lineage, and whether the participant is already entered.

The resulting participant/actor is session-scoped for Round 1. No password, account record,
character-list persistence, saved-character ownership, or account ecosystem is implied.

Existing accountless Admission behavior is implementation evidence. The demo may earn a narrower
remote guest-admission presentation/protocol seam without deleting the separate account-backed path.

---

# 5. Concept Demo world layout

The exact art layout remains an authoring choice, but the semantic areas are intentional.

```text
                                   SKY SIDE

                              [ Sky Island ]
                             upper terrain surface
                                  ^       ^
                             jump |       | telepad
                                  |       |

      [ Fallback Lab ] --- [ Welcome / Central Plaza ] --- [ Cabin ]
                                  |
                                  |
                        large central floating terrain
                                  |
                                  |
                         [ Cave side / entrance ]
                        lower floor + cave ceiling


               thousands of metres / nonresident world distance

                                  |
                               [ Gate ]
                                  |
                                  v
                            [ Far Island ]
```

The world should be large enough that the client cannot accidentally retain/present all authored
terrain as one permanent Cabin-sized set. World content may be much larger than the current
semantic/render presentation window.

The Far Island should be geographically distant in the same `SpatialSpaceId` unless a later World
requirement specifically earns another space. The gate proves long-distance authoritative relocation,
not a hidden map switch.

---

# 6. Floating terrain, intentional void, Sky Island, and cave

## 6.1 Main terrain itself is floating

The central playable terrain should have a real edge.

The correct semantic distinction is:

```text
surface present
    material + geometry/support

surface absent / intentional void
    no render surface
    no support/collision from that surface

structural data missing/not resident
    streaming/residency condition

outside authored/prepared world
    separate world-boundary condition
```

Do not represent a hole or island edge as material `0`, an invisible texture, an invisible colliding
plane, or another presentation trick.

Terrain T1.0 implements this base-terrain surface-presence/cutout contract in World/Terrain. The
actual floating-island content remains a Concept Demo integration task; the topology substrate is
no longer hypothetical.

## 6.2 Sky Island

The Sky Island exercises an **additional detached world-terrain shell** above the main base sheet:

```text
SkyIslandShell
====================== high

          air

BaseTerrain
====================== low
```

No blank numbered layer exists between them. T1.1 stores the Sky Island as sparse immutable
WorldPack shell fragments sharing one logical `WorldSurfaceId` where it crosses chunk boundaries.
It is not forced into ECS, a giant static/Multi, or a second dense world-sized heightfield.

## 6.3 Cave

The cave lives on the opposite side of the main island from the Sky Island and proves the shell
model rather than a numbered underground layer:

```text
MountainShell exterior/ceiling
===============================

            cave space

optional intermediate shell
        ===============

preserved BaseTerrain / lowest floor
===============================
```

An authored mountain may remain on BaseTerrain, or a future explicit rip may raise a
`MountainShell` while leaving the source BaseTerrain physically present. In the latter case that
preserved base naturally becomes potential cave floor once an opening makes the intervening space
reachable. Additional sparse shells supply any extra floors, ledges, bridges, or chambers.

The same MountainShell is exterior support from above and ceiling/clearance geometry from below;
it is not serialized as a permanent Floor or Ceiling layer. The first cave does not need to solve
every possible cliff/overhang/tunnel authoring problem. It must prove that renderer, support/
clearance queries, and client structural semantics select actual geometry at the body's Y.

## 6.4 Client void/residency implication

A presentation-window square near an island edge must not interpret intentional void or an unauthored
neighbor as a corrupted/missing streamed terrain chunk.

The client/world seam must eventually distinguish:

```text
authored structural data not resident yet
intentional resident void / absent surface
no authored WorldPack chunk / outside authored area
```

without weakening fail-closed behavior for genuinely missing required structural data.

---

# 7. Jump, fall, landing, telepad, and gate

## 7.1 Real jump

Third-person jump is authoritative physical movement, not a client animation plus teleport.

The demo provides the concrete consumer for Physics support/vertical work:

```text
grounded/support state
jump admission
vertical motion/velocity as required by Game
Gravity / falling
absence of support over void
landing on correct lower/upper surface
fall/recovery policy
```

Exact Physics checkpoint/API is owned by `PHYSICS.md` and must be reviewed before implementation.

P0.1d closes the foundation: Space produces a focus-fenced Jump edge, Game admits only grounded
jumps, integrates vertical velocity/gravity with horizontal air control, lands against actual
Base/Top surfaces, stops at Bottom ceilings, and uses occupancy-aware Game recovery below the
admitted fall envelope. D0.0 now consumes that foundation through the actual island/cave traversal
fixture described below.

## 7.2 Telepad

The Sky Island also has a telepad so UO-like/control profiles without a jump grammar can reach the
same content.

```text
third-person profile
    jump/fall/land

UO-like profile
    use/walk onto telepad
    -> authoritative occupancy-aware relocation
```

This demonstrates that server/game content can support different stock client control grammars
without moving authority into the client.

## 7.2a D0.0 physical-world integration — CLOSED

D0.0 is the first real Concept Demo consumer checkpoint. It deliberately composes the accepted
World, Physics, local-application, and Godot seams rather than introducing another terrain or
movement mechanism:

```text
finite Central BaseTerrain sheet
    + intentional-void outer edge and east cutout

MountainShell
    + top support at 7 m
    + Bottom clearance face at 5 m
    + preserved BaseTerrain cave floor
    + sparse 3 m interior ledge
    + frozen structural perimeter with a real south opening

detached SkyIslandShell
    + top support at 3.5 m
    + Bottom face at 3 m
    + no BaseTerrain or synthetic intermediate layer beneath it
```

The normal Single Player entry selects this world through `Client.Local`. The F0.2 acceptance mode
in the same maintained application deliberately retains the byte-stable Cabin fixture, so D0.0
cannot silently weaken earlier lifecycle/content or edge-window evidence. The two fixtures also use
separate persistent-world identities and save roots.

The Sky telepad is one concrete public Game prefab. A client nominates only that admitted target;
Game validates identity and range, selects the authored destination, and invokes the existing
occupancy-aware relocation primitive. No teleport framework, client-provided coordinates, or new
Physics behavior was introduced.

Permanent D0.0 acceptance proves through the real Single Player client that:

- the mountain opening is traversable on preserved BaseTerrain beneath the shell Bottom;
- the central edge and Sky gap are actual intentional void;
- sparse mountain, cave-ledge, and Sky fragments enter bounded Client Core residency and Godot
  realization without ECS terrain entities;
- ThirdPersonDirect performs an authoritative jump with air control and lands on the Sky shell;
- OrbitCommand pointer destinations resolve against the detached Sky shell rather than requiring
  BaseTerrain, while ThirdPersonDirect/Independent mouse selection can nominate interaction targets;
- OrbitCommand still filters Jump and reaches the same shell through the authoritative telepad;
- walking off support produces normal fall/recovery to the safe occupied player start; and
- the historical Cabin WorldPack bytes/fingerprint remain unchanged.

Subsequent human-play hardening lowers the Orbit/ThirdPerson wheel-zoom floor from 7 m to 3 m and
makes complete collidable static bounds tops physical support. Follow-up playability hardening
separates the 600×600 mm body collision/clearance footprint from a 300×300 mm central support/feet
footprint. The authored rock, Cabin wall, shelf, broad log, or plank can therefore support ordinary
landing and traversal when the feet fit, without making the actor body narrower. Collidable frozen
geometry supports on top by default; the rare `NoTopSupport` override remains blocking while opting
out of standing/landing. Tightropes and ladders remain future explicit Game traversal semantics, not
special Physics prefab cases or reasons to shrink normal support to an implausible size.

This remains bounded Simple Physics; moving platforms, sliding/contact response, dynamic-projected
support, actor support, and special narrow traversal are not implied.

D0.0 does not implement render-distance policy, the Far Island/gate, terrain editing, or other
deferred Physics features.

## 7.2b D0.1 bounded structural residency + Render Distance — CLOSED

D0.1 removes the Cabin-era `StageAllChunks` assumption. At D0.1 the local Concept Demo WorldPack had
a deterministic 9×9 / 81-chunk authored structural extent, while the default client admitted only a
5×5 / 25-chunk semantic structural neighborhood and presents only a 3×3 / 9-chunk render window:

```text
authored structural source       81 chunks
    > default semantic residents 25 chunks
        > presented/rendered      9 chunks
```

`ClientStructuralResidency` is the engine-neutral bounded controller. It derives a clipped
one-chunk structural fringe around the requested presentation window, stages every entering chunk
before presentation admission, and retires no-longer-desired structural-only chunks only after the
presentation plan commits. A failed stage is rolled back without advancing desired membership.
Chunks retaining Game-owned dynamic-public rows remain alive until their separate public-interest
lineage retires; Render Distance does not change that entitlement.

The distinction from T1.0 remains explicit:

```text
resident authored chunk + absent cell = intentional void
authored chunk not resident            = client residency condition
no authored chunk                      = outside authored world
```

The F0.1 settings document now persists a bounded client-owned Render Distance of 1–3 chunks. Save
applies the value once rather than rebuilding on every slider sample. A live change reconstructs
the bounded semantic/presentation window without replacing the authoritative local Game session.
The permanent D0.1 Godot canary proves both the default 81/25/9 shape and an expanded 81/49/25
shape at radius 2 through the real Single Player consumer.

Diagnostics expose configured radius, authored/current/peak semantic residents,
current/peak rendered chunks, semantic and presentation enters/exits, resets, and renderer queue
state. The complete local structural source remains preinstalled in D0.1; CAS/remote acquisition
is still deferred. At the D0.1 boundary the Far Island/gate remained D0.2, so its long-distance
reset could next exercise real retirement and re-admission rather than teleporting between
permanently resident regions.

## 7.3 D0.2 sparse geography + Far Island gate reset — CLOSED

D0.2 expands the same canonical WorldPack to two independently authored 9×9 clusters in one
`SpatialSpaceId`: Central at chunk X `-4..4` and Far Island at chunk X `124..132`. The resulting
162-chunk source contains no synthesized chunks across the roughly 2.048 km gap. That gap is
outside authored world, not resident intentional void and not authored-but-currently-nonresident
content.

`ClientStructuralResidency` now consumes the pack's sorted authored key set rather than requiring
one complete bounding rectangle. A presentation center is admitted only where its complete render
square is authored; the bounded structural fringe stages only authored keys. At a clipped edge the
last valid center remains stable. A position near another authored cluster selects that cluster by
actual key distance—no gap chunks or fake blank layers are manufactured.

The two public Far Gate instances are ordinary dynamic-public Game entities. Interaction chooses
the nearest admitted gate from trusted authoritative position, and Game maps its exact authored
position to the reciprocal server-owned destination before using the existing occupancy-aware
relocation primitive. The client supplies neither destination nor trusted target identity.

A disjoint long-distance move performs an explicit bounded transition:

```text
retire old presentation window
    -> drain bounded native retirement work
    -> retire old desired structural neighborhood
    -> admit the destination structural neighborhood
    -> build the new presentation window
```

The old and new structural sets therefore never need to coexist merely because the relocation is
distant. Dynamic-public knowledge remains independently owned. Local public deliveries for chunks
that terminally leave interest are invalidated before a same-key re-entry can occur, matching the
remote adapter's leave fencing: an old delayed proof cannot advance a new subscription lifetime.
This invalidation is abandonment, not application proof.

Permanent Core tests use two sparse clusters to prove central → empty reset → far admission and
edge clamping. The packet-free Game regression proves both gates and the unauthored gap. The native
Single Player D0.2 canary travels central → Far Island → central through ordinary select/Interact,
and proves two semantic/presentation resets with the bounded radius-1 shape:

```text
authored structural source       162 chunks
    > active semantic residents   25 chunks
        > presented/rendered       9 chunks
```

The accepted headless journey completed with exact reciprocal positions, one visible local gate,
no stale Cabin presentation on Far Island, no stale Far presentation after return, zero rejected
world applications, and fresh public snapshots after re-entry.

The intended effect is now real:

Expected effect:

```text
Game validates gate interaction
    -> authoritative occupancy-aware relocation
    -> old AOI/public relationships retire
    -> old nameplates/overhead anchors retire
    -> client semantic/presentation window resets/rebuilds
    -> far structural/public neighborhood becomes current
```

A return gate provides the reverse route.

The gate is explicitly a stronger streaming/AOI test than the nearby Sky Island.

---

# 8. Signs and self-explaining world UI

Signs are real interactable world objects.

## Hover

Mouse/pointer hover may highlight a sign or display a short local tooltip such as `Sign` using already
known presentation state.

Hover is client presentation. It does not query the server for a hidden description.

## Click / interact

Click/Interact sends normal Game intent. The server validates the interaction/range and opens a
small server-composed informational UI.

Example sign copy may explain:

```text
SKY ISLAND
Third-person controls can jump to this island.
Other control profiles can use the telepad.

FAR ISLAND
This gate relocates you to a distant section of the same world.
Terrain and nearby-player residency should change.

CAVE
This area demonstrates multiple world-terrain surfaces at the same horizontal coordinates.
```

The sign UI is content/data. Godot remains the renderer.

---

# 9. Fallback Lab

Place four obvious demonstration objects on labeled pedestals near the welcome area.

```text
1. CORRECT
   model present
   texture present

2. MODEL MISSING
   model missing
   texture/content identity otherwise valid
   -> category primitive/model fallback

3. TEXTURE MISSING
   model present
   texture missing
   -> real model + diagnostic texture/material fallback

4. MODEL + TEXTURE MISSING
   model missing
   texture missing
   -> primitive + diagnostic texture/material fallback
```

Each pedestal has a sign explaining that the result is deliberate diagnostic behavior.

This area exists so testers understand why other unfinished content may use primitives or ugly
textures, and so fallback behavior becomes observable acceptance instead of a hidden code path.

Texture-debug mode may use the intentionally obnoxious checker/rainbow material; neutral/debug-off
fallback may use a simple grey/default presentation. Fallback never changes authoritative collision,
item identity, interaction, or Game truth.

---

# 10. Human actor presentation and minimal animations

X/Y bodies are the first real human-scale actor definitions.

Desired available clips:

```text
Idle
Walk
Run
Sprint
Jump
Attack
Crouch
Interact
```

Clip availability does not automatically mean Game semantics exist.

Round 1 should wire, where their authoritative meanings are implemented:

```text
Idle
Walk / Run
Sprint
Jump
Attack/swing
Interact
```

Crouch may exist as an asset/clip before it becomes an input action. It must not visually reduce the
body while authoritative Physics still treats the actor as the unchanged full-height body and then
claim crouch gameplay is implemented.

Animation is presentation derived from authoritative/public motion/action facts where appropriate.
Do not make the loaded animation state gameplay truth.

---

# 11. Cabin, player Inventory, pickup/equip/drop, and swing

Retain the Cabin, door, and chest as understandable interaction anchors.

Round 1 Inventory vertical:

```text
world weapon/tool
    -> pickup
    -> player Inventory
    -> minimal server-composed Inventory UI
    -> equip
    -> equipped presentation
    -> Primary / accepted swing
    -> attack animation
    -> drop
    -> world item again
```

The Cabin chest opens a minimal container UI and allows movement between player Inventory and chest
subject to existing authoritative Inventory rules.

A training dummy may exist as a swing target without taking damage.

Round 1 swing does not imply hitpoints, damage, Combat authority, death, or PvP. Those belong to
Round 2.

---

# 12. Render-distance setting

The real client Settings must expose a functional **Render Distance / presentation radius** control.

The setting changes client presentation demand. It does not automatically change server dynamic AOI,
server disclosure, or Game authority.

Conceptual application:

```text
user changes render distance
    -> validate supported/bounded radius
    -> prepare semantic/presentation capacity
    -> transactionally retire/rebuild desired presentation window as required
    -> Godot renderer converges
```

Do not mutate/reconstruct the window every slider-motion sample if a release/apply action is more
appropriate.

The exact min/max values are not frozen before client/render evidence. Every selectable value must
fit the configured semantic/presentation capacity and be diagnosable.

Diagnostics should expose at least:

```text
configured render radius
current semantic resident chunks
current presentation/render chunks
peak resident/render chunks
window resets/enters/exits
```

---

# 13. Nameplates, overhead speech, chat feed, and ping

Round 1 requires:

- authoritative disclosed player names;
- ordinary nameplates;
- Ctrl reveal over already-known resident eligible actors;
- overhead local speech;
- bounded chat feed;
- client ping/connection indicator.

A local host using the in-process application bridge should display `Local` rather than inventing a
fake Network RTT.

Far-gate movement is a permanent presentation test: a player who leaves another player's AOI must
lose the old nameplate/overhead anchor without stale ghosts.

---

# 14. Local spatial chat and first Global routed channel

Round 1 opens exactly one routed chat consumer without turning Chat into a full social suite.

## Local

Local spatial speech retains the accepted authoritative modes:

```text
Whisper
Talk / Say
Shout / Yell
```

The chat input has a small radio/selector controlling the **default local speech mode**. The selected
mode is intent; the server still owns the actual hearing radius and recipients.

Per-message aliases may override the default for one submission, conceptually:

```text
/w message       Whisper this message
/s message       Say/Talk this message
/y message       Yell/Shout this message
/g message       Global this message
```

Exact aliases are client/Game input policy and may be adjusted. They are not trusted radii. The
client never supplies `radius=...` or a recipient list.

## Global

`Global` is the first non-spatial routed channel. Its audience is the server's currently admitted
eligible global-chat recipients, independent from world hearing distance/AOI.

Global remains an occurrence/fact with bounded fanout/debt/application-proof behavior. It does not
create Party/Guild/DM/moderation/history infrastructure.

## Commands remain separate

Recognized command input remains command intent and never falls through to speech. `/help`, `/where`,
and future operator/admin commands keep their own authority path.

---

# 15. System broadcasts and test-marker announcements

The live test system deliberately uses every test marker start/completion to exercise system
broadcast presentation.

A marker transition creates one authoritative system-broadcast **occurrence**, conceptually:

```text
TEST START: Plaza Crowd — 50 players gather near the cabin.

TEST COMPLETE: Plaza Crowd.
```

The exact internal type belongs to Game/Chat/application-event ownership. Required semantics are:

```text
server/operator creates fact
    -> bounded reliable Event/application delivery
    -> Client Core applies once / proves application
    -> chat feed retains the occurrence
    -> banner presentation briefly displays the same applied fact
```

The top-screen banner is a transient presentation of an already-applied system fact. If the banner is
not visible because of timing/focus/presentation conditions, the chat-feed occurrence remains.

Do not implement the banner as a second independently authoritative message and do not let a
presentation timeout erase the chat fact.

Test marker activation/completion should also be written into the client/server diagnostic timelines
so performance changes can be correlated to the live activity.

Marker control initially belongs to a trusted server console/test operator path. Do not manufacture a
large player RBAC framework merely to start a concept-test phase.

---

# 16. Read-only Server Monitor

Round 1 may expose a read-only server-composed **Server Monitor** to ordinary demo users.

It is intentionally observational and sanitized.

Suggested tabs:

```text
Overview
ECS
World
Network
Players
```

Candidate fields:

```text
Overview
    authority frequency
    current/peak players
    entity/archetype counts
    coarse process memory/timing

ECS
    archetype/entity/system counts
    useful bounded/high-water metrics

World
    public/spatial chunks
    AOI/subscriber counts
    publication debt/recovery counters

Network
    aggregate send/receive rate
    queue/high-water/retransmit/application-proof metrics

Players
    display name
    current sanitized ping/RTT
```

Do **not** expose IP addresses, transport endpoints, cryptographic identity/key material, passwords,
account identifiers, machine identifiers, or mutable administrative controls.

Update cadence should be intentionally cold, such as approximately once per second, rather than
making the diagnostic UI itself a hot workload.

The monitor should consume existing/coherently sampled diagnostic snapshots where possible instead
of creating a second metrics implementation.

---

# 17. Diagnostic capture — local-first and inspectable

The concept demo should make voluntary diagnostics useful without creating opaque phone-home
telemetry.

## 17.1 Client diagnostic capture

Client Settings includes an option equivalent to:

```text
[ ] Record local diagnostic report

Creates a readable diagnostic report on this computer.
The report is never uploaded automatically.

[What is recorded]
[Preview Report]
[Open Report Folder]
```

Default is **off**.

When enabled, the client records bounded/coarsely sampled application, connection, world, content,
and presentation statistics to a local human-readable text report.

The user can inspect the exact report before voluntarily sending it to the developers/test operator.
Round 1 does not need an in-client `Upload`/`Send Diagnostics` feature.

## 17.2 Privacy contract

Normal client diagnostic reports must explicitly state what they do and do not contain.

```text
CLIENT DIAGNOSTIC CAPTURE IS OPT-IN AND OFF BY DEFAULT.

CLIENT REPORTS ARE CREATED LOCALLY AND ARE NEVER AUTOMATICALLY UPLOADED.

THE USER CAN INSPECT THE EXACT REPORT BEFORE SHARING IT.

NORMAL DIAGNOSTICS DO NOT RECORD:
    IP ADDRESSES
    MAC ADDRESSES / HARDWARE SERIAL NUMBERS
    PERSISTENT MACHINE IDENTIFIERS
    OS USERNAME / HOME-DIRECTORY PATHS
    CHAT MESSAGE CONTENT
    COMMAND CONTENT
    KEYSTROKES
    CLIPBOARD CONTENT
    PASSWORDS / TOKENS / CRYPTOGRAPHIC KEYS
    EXACT MOVEMENT/POSITION HISTORY
    PACKET PAYLOAD DUMPS
```

Useful non-secret environment/performance information may include:

```text
build/commit/client edition
OS/version
CPU model
GPU model
RAM capacity
renderer/backend
resolution
render-distance setting
coarse timings/high-water counters
```

No hardware serial/persistent fingerprint is generated from those values.

Exception/error recording sanitizes absolute machine paths. Normal diagnostic reports do not blindly
dump invasive crash dumps, packet captures, process dumps, or full raw logs. Those are a separate,
explicit developer diagnostic class.

## 17.3 Diagnostic session correlation

Each application session may use one random opaque `DiagnosticSessionId`, preferably 128-bit or
similarly collision-resistant.

It is diagnostic identity only and is not:

```text
ConnectionId
ParticipantId
CharacterId
AccountId
IP/endpoint
machine/user identity
```

Client and server diagnostic reports may contain the same opaque session ID. A tester can therefore
voluntarily send a client report and let the developer correlate it with the matching server-side
session without sharing an IP address or machine identity.

The diagnostic ID grants no authority and must not become a durable gameplay identity.

## 17.4 Client report content

Candidate summary/timeline fields:

```text
privacy header
build/commit/edition
DiagnosticSessionId
session duration
verified server realm short identity
platform/renderer/hardware class

connection
    RTT p50/p95/max
    reconnects
    bytes sent/received
    relevant queue/recovery counters exposed by client

presentation
    FPS/frame-time percentiles
    long-frame counts
    render radius
    current/peak semantic chunks
    current/peak render chunks
    presentation enters/exits/resets

content
    cache hits/misses
    bytes downloaded
    fallback model/texture activations
    content-source/failure counts without credential-bearing URLs

application
    applied/rejected semantic counts where useful
    UI/Inventory/chat occurrence counters without chat text

coarse area timeline
    Welcome Plaza
    Cabin
    Sky Island
    Cave
    Far Island

system test-marker start/completion occurrences
```

Do not record coordinate-by-coordinate travel merely to reproduce where a player walked.

A later machine-readable `.json` twin may be generated from the **same exact diagnostic model** for
large-report aggregation, but it must not hide additional fields absent from the human-readable
privacy contract.

---

# 18. Server-side live-test diagnostics

The server records broad operational evidence during concept-test runs independently from client
report opt-in because it owns its own runtime health.

This is not intended to be an always-on invasive observability platform.

Coarsely sample/aggregate useful data such as:

```text
build/commit
uptime
current/peak players
process CPU/memory where available

authority
    tick mean/p50/p95/p99/max
    deadline/late-step counts

ECS/runtime
    entities/archetypes/system counts
    useful high-water/capacity metrics

World/replication
    spatial/public chunks
    AOI/subscriber counts
    publication deliveries/debt/history misses/snapshot recovery

Network
    aggregate send/receive
    connection count
    RTT distribution
    retransmit/loss/replay/queue/high-water counters as already exposed
    application-proof debt/recovery

Chat
    accepted local/global/system occurrence counts
    mode counts
    rejected/rate-limited counts
    NO message text

Inventory/UI
    accepted/rejected action counts
    open-view/cache counts where useful

Content hosting
    requests/bytes/failures when self-hosting
```

The exported concept-test diagnostic report must not include IP addresses or cryptographic/session
secrets. Lower-level server security logs may have separate operational/security requirements, but
they are not silently copied into this tester-facing performance artifact.

Detailed per-session diagnostic rows use only the opaque DiagnosticSessionId plus sanitized metrics
such as RTT. Do not use the client report mechanism as a reason to persist endpoint history.

Normal periodic diagnostic sampling should be cold (for example a multi-second cadence) with
cumulative histograms/counters. Do not write every authority tick or packet to disk by default.

Exceptional bounded markers may record events such as:

```text
slow-tick threshold crossed
queue/high-water threshold crossed
connection lost/recovered
content/hash failure
publication history miss/recovery
client semantic rejection
```

without dumping payloads.

---

# 19. Test markers

A trusted operator can mark named phases in the live test. Marker identifiers/labels become part of
server diagnostics and, through the system-broadcast occurrence, client diagnostics/chat.

Example sequence:

```text
TEST START  baseline-free-roam
TEST COMPLETE baseline-free-roam

TEST START  fallback-lab
TEST COMPLETE fallback-lab

TEST START  render-distance-cycle
TEST COMPLETE render-distance-cycle

TEST START  plaza-crowd-50
TEST COMPLETE plaza-crowd-50

TEST START  local-chat-fanout
TEST COMPLETE local-chat-fanout

TEST START  global-chat
TEST COMPLETE global-chat

TEST START  sky-jump-telepad
TEST COMPLETE sky-jump-telepad

TEST START  cave-shell
TEST COMPLETE cave-shell

TEST START  far-gate-aoi
TEST COMPLETE far-gate-aoi

TEST START  inventory-swing
TEST COMPLETE inventory-swing
```

A marker is diagnostics/control state; it does not become ECS world truth merely because the clients
see an announcement.

Markers allow later correlation such as:

```text
server tick p99 rose
    -> plaza-crowd-50 active

bandwidth/residency spike
    -> far-gate-aoi active

Event/application-proof pressure
    -> global-chat active
```

---

# 20. Functional and stress populations

External turnout is evidence, not a production CCU promise.

Use these expectations as test posture rather than guarantees:

```text
8–16 players
    functional multiplayer acceptance target

20–50 players
    strong concept-demo / natural concurrency evidence

50–100 players
    serious real-client/network/AOI stress evidence

100–200 players
    deliberate stress/failure-characterization territory
    NOT a declared production-supported CCU
```

Test both density and distribution.

## Crowd

Gather everyone near the Welcome Plaza/Cabin.

Exercises:

- dense player public state;
- nameplates/Ctrl reveal;
- local speech fanout;
- global chat;
- server-monitor Players tab;
- render/presentation pressure.

## Distributed

Split players between Central, Sky, Cave, and Far islands.

Exercises:

- AOI benefit;
- player retirement/re-entry;
- independent local speech groups;
- Global across distance;
- structural/presentation residency.

## Churn

Repeatedly:

- cross chunks;
- change render distance;
- use telepad/gate;
- jump/fall/land;
- open/close Cabin and UI;
- pickup/equip/drop/swing;
- submit local/global chat.

Exercises lifecycle/recovery behavior that static benchmarks may not expose.

A failure at a specific turnout is useful evidence when diagnostic markers/reports identify why.
Do not hide or relabel a stress failure as functional acceptance.

---

# 21. Concept Demo Round 1 acceptance journey

A normal tester should be able to experience the demo without developer instruction:

```text
launch Online/MMO client
    -> Direct Connect
    -> trust fingerprint if first connection
    -> choose X/Y + name
    -> enter Welcome Plaza

read Welcome sign
    -> inspect Fallback Lab
    -> understand correct/model-missing/texture-missing/both-missing states

open Settings
    -> change Render Distance
    -> observe world presentation change

see player nameplates
    -> hold Ctrl for all known eligible plates
    -> use local chat / overhead speech
    -> use Global chat

visit Cabin
    -> door
    -> chest UI
    -> player Inventory
    -> pickup/equip/swing/drop test tool

visit Sky side
    -> jump to Sky Island when using supported third-person profile
       OR
    -> use telepad

visit Cave side
    -> enter lower/overhead terrain surfaces

use Far Gate
    -> central AOI/presentation retires
    -> Far Island loads/becomes current
    -> return through gate

open Server Monitor
    -> inspect sanitized live counters and player pings

optionally enable Diagnostic Capture
    -> preview local report
    -> voluntarily share report after the test if desired
```

The world should make obvious which unfinished visuals are intentional fallbacks rather than asking
testers to infer that from development state.

---

# 22. Sequencing relative to current roadmaps

Do not bypass the accepted F0.0b boundary by implementing demo spectacle directly in the reference
executable or against mutable authority.

Preferred dependency order remains approximately:

```text
F0.0a  executable composition foundation                 CLOSED
F0.0b  permanent Network-free local-session extraction   CLOSED
F0.1   persistent shell/settings/focus                     CLOSED

A0.0   content contracts/resolution                        CLOSED
A0.1   local Cabin Default/Replace/Add oracle                       CLOSED
F0.2   real Single Player Cabin + human calibration                 CLOSED

then earn/integrate Concept Demo Round 1 consumers:
    terrain T1.0 surface presence/void                         CLOSED
    terrain T1.1 sparse terrain shells                         CLOSED
    Physics P0.1c support / grounding / clearance              CLOSED
    Physics P0.1d vertical motion / jump / fall / landing      CLOSED
    D0.0 physical central/Sky Island/cave + telepad integration CLOSED
    D0.1 Render Distance + bounded structural residency          CLOSED
    F0.0c one maintained Godot project / three physical products CLOSED
    D0.2 sparse geography + Far Island + gate reset              CLOSED
    signs/Fallback Lab
    guest/demo admission presentation
    animation/pickup/inventory swing
    Global/system Chat
    diagnostics/Server Monitor/test markers

F0.3 remote client / Direct Connect controlled actor
A0.2+ remote cache/source/content activation

then external Concept Demo multiplayer/stress run
```

Exact subsystem checkpoint labels should be chosen in the owning plan when the consumer reaches that
boundary. Do not use this cross-cutting document to pre-authorize a giant terrain/physics/chat/client
commit.

---

# 23. Permanent demo principles

```text
THE DEMO USES REAL GAME/CLIENT/NETWORK SEMANTICS, NOT DEMO-ONLY PARALLEL PATHS.

THE WORLD EXPLAINS ITS INTENTIONAL FALLBACKS AND TEST FEATURES TO THE USER.

COMPLETE CLIENT CAPABILITY DOES NOT GRANT SERVER ADMINISTRATION.

GUEST DEMO ENTRY DOES NOT REQUIRE OR SECRETLY CREATE A FULL ACCOUNT SYSTEM.

RENDER DISTANCE IS CLIENT PRESENTATION POLICY; IT DOES NOT SILENTLY EXPAND SERVER AOI.

INTENTIONAL TERRAIN VOID != MISSING STREAMED CONTENT.

SKY/CAVE SURFACES ARE WORLD TERRAIN DATA, NOT FORCED ECS ENTITIES BY DEFAULT.

BASE TERRAIN IS THE DENSE DEFAULT SHEET, NOT THE UNIVERSAL LOWEST SURFACE.

SKY, CAVE, UPPER, AND LOWER DO NOT IMPLY NUMBERED RUNTIME TERRAIN LAYERS.

SPACE BETWEEN TERRAIN SHELLS IS ORDINARY WORLD SPACE.

JUMP IS AUTHORITATIVE GAME/PHYSICS MOVEMENT, NOT A CLIENT TELEPORT ANIMATION.

TELEPADS/GATES USE AUTHORITATIVE RELOCATION AND OCCUPANCY RULES.

LOCAL CHAT IS SPATIAL. GLOBAL CHAT IS ROUTED. COMMANDS ARE NOT CHAT FACTS.

SYSTEM BROADCASTS ARE APPLICATION FACTS; BANNERS AND CHAT ARE TWO PRESENTATIONS OF THE SAME FACT.

TEST MARKERS ARE BOTH DIAGNOSTIC CORRELATION AND SYSTEM-BROADCAST ACCEPTANCE.

CLIENT DIAGNOSTIC CAPTURE IS OPT-IN, LOCAL, INSPECTABLE, AND NEVER AUTO-UPLOADED.

SERVER CONCEPT-TEST DIAGNOSTICS ARE SANITIZED OPERATIONAL EVIDENCE, NOT PACKET/PAYLOAD SURVEILLANCE.

NO DIAGNOSTIC REPORT NEEDS IP ADDRESSES, CHAT CONTENT, KEYSTROKES, CREDENTIALS, OR EXACT MOVEMENT HISTORY.

REAL-USER STRESS RESULTS ARE EVIDENCE, NOT AUTOMATIC PRODUCTION CCU CLAIMS.

ROUND 1 SWING/ATTACK PRESENTATION DOES NOT SILENTLY BECOME ROUND 2 COMBAT AUTHORITY.
```

---

# 24. Definition of Round 1 done

Round 1 is ready to hand to external concept-demo testers when:

1. the real Online client can Direct Connect and enter through the accepted guest/demo admission path;
2. the real Complete client can run the same world locally/remotely without changing host authority semantics;
3. X/Y bodies, names, nameplates, overhead chat, feed, and basic animations work;
4. the floating central world, Sky Island, cave, and Far Island use correct World/Terrain semantics;
5. third-person jump and alternate telepad access both reach the Sky Island through authoritative rules;
6. Far Gate correctly retires/rebuilds AOI/client presentation without stale actors/anchors;
7. Render Distance is a real bounded Settings control;
8. signs and Fallback Lab explain the world and verify missing-content presentation;
9. Cabin door/chest/player Inventory/pickup/equip/swing/drop work without requiring Combat damage;
10. Local Whisper/Talk/Shout plus routed Global work with retained occurrence/application-proof semantics;
11. system test broadcasts appear in chat and transient banner presentation;
12. read-only Server Monitor exposes useful sanitized live information, including player pings but no IP addresses;
13. client diagnostic recording is opt-in, local, human-readable, previewable, and non-uploading;
14. server diagnostics record bounded broad operational evidence and marker timelines;
15. the permanent functional multiplayer gate is proven at a modest controlled population before larger stress runs;
16. external larger-population tests are labeled and retained as stress/failure-characterization evidence rather than production capacity promises.

Round 2 combat remains separately authorized after this baseline is useful and stable.
