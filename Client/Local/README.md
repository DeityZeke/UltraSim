# Local client application bridge

`UltraSim.Client.Local` is the Network-free, packet-free application boundary
between a local authoritative Game/runtime and Client-side semantic state. It is
an optional client capability: Single Player and Complete include it; Online
does not.

The accepted Cabin composition uses one private authority owner thread. Godot
and other presentation consumers can only:

- enqueue bounded, control-session-fenced `GamePlayerCommand` intent;
- enqueue bounded local administrative or Chat intent;
- consume immutable/coalesced owner presentation facts;
- apply bounded public-world, Chat, and UI semantic debt to the same Client
  state used by remote consumers; and
- return explicit applied/rejected proof so authority can retire or retry debt
  according to that domain's existing semantics.

W5R gives the Concept Demo a distinct canonical structural source: immutable declared
Packs `(0,0)` and `(1,0)` plus a complete manifest, bound and validated for each
Game generation. The immutable Local authority view carries either the historical
Cabin `PreparedWorld` or the Concept Demo `PreparedComposition`, never both. The
client cache and native renderer consume that canonical composition without a
Pack-aware presentation branch. Ordinary Single Player does not construct the
historical combined Concept Demo Pack; Complete hosting now grants the two independently
declared Packs to Remote clients after manifest proof. A pre-W5R Concept Demo `worldview/v1` restore
fails closed explicitly rather than dropping possible structural overlay state.

The authority thread owns `ServerRuntime`, Admission, mutable ECS/Game state,
Inventory and informational-sign UI policy, Chat routing, persistence, the local public recipient,
and all Game mutation. It advances at authoritative cadence independently of Godot.
Presentation stalls therefore accumulate only bounded domain debt; they do not
pause the world or grant the presentation thread authority access.

Local session construction accepts one untrusted `GameGuestAdmissionRequest`. The owner thread
submits it to authoritative Admission before populating the world. Admission validates and retains
the canonical session-scoped `GameGuestIdentity`; the immutable authority view then exposes that
accepted result to the local Client. Body selection chooses a concrete compiled Game/public actor
prefab, and Chat derives its sender label from the retained Admission session. The local bridge does
not turn the typed client value into truth, create an Accounting record, or persist guest identity.

The bridge is deliberately concrete rather than a transport framework:

```text
GamePlayerCommand              bounded FIFO intent
owner/private presentation     one coalesced immutable view
public ChunkPublication        retained ticket until Client apply proof
Chat                           retained occurrence until Client apply proof
server-composed UI             retained operation until Client apply/reject proof
```

Rejected public-world and Chat applications retain the same semantic debt for retry. A chunk that
terminally leaves the current public-interest set is different: retained local world deliveries for
that key are explicitly invalidated, not applied, so an old proof cannot advance a later same-key
subscription after distant leave/re-entry. A rejected UI operation instead retires that operation
and desynchronizes its view, matching the remote UI contract; later operations do not continue
against unknown client UI state.

UI operation completion debt belongs to the retained local channel, not to its disposable
`LocalQueuedUiClient` action-sink bridge. Detaching and reattaching that bridge to the same
`UiClientRuntime` cannot replay an already-applied renderer/model operation while proof publication
is backpressured. Attaching a different runtime to the live channel is rejected: a different client
projection requires retirement of the containing local session and its authority-side views.
Bridge pumping is non-reentrant, so a renderer callback cannot consume/reject the operation whose
renderer application is still in progress. Disposal commits only after action-sink detachment is
admitted; a callback-time disposal rejection therefore leaves both the bridge and session
attachment live instead of stranding a disposed action sink in the client runtime.

Public disclosure uses `GameChunkInterestProfile.Default` and the accepted
`OfflineDirectChunkCadence.EvidenceProfile`. It consumes packet-free Game
replication semantics directly; it does not scan/copy the complete publication
store on every render frame. Structural client residency remains independent
from dynamic-public interest, so leaving public interest retires only public
knowledge from a resident Client chunk.

The delivery/proof cursor is paired with one current-generation semantic application cache. A live
client Render Distance change must retain that cache and rebuild only its residency/presentation
policy; replacing the cache while retaining these cursors would suppress unchanged public snapshots
and make actors disappear until later publication work. Authority-generation replacement remains an
explicit fresh-cache boundary.

The local path does not serialize Game protocol messages, create a loopback
connection, or reference `Client.Network`, Game Protocol, Remote Replication, or
`Kernels.Network`. Hosting in a Complete composition adds independent remote
sessions around the same authority; the host player stays on this local path.

M0.0 adds that hosting without putting Network into this assembly. `LocalPlayableSession` accepts
an optional server-extension factory, constructs it on the private authority owner thread, and
offers bounded administrative enable/disable requests plus immutable status and public-presentation
snapshots. The Complete-only `Server.ConceptDemo.LocalHost` implementation uses that seam to borrow
the existing runtime, scene, Admission, Chat, local participant/control session, and WorldPack.
Stopping the extension leaves all of those local owners alive. Single Player has no implementation
of the extension interface, so its physical Network-free closure is unchanged.

`LocalPlayableSession` remains a concrete playable-world authority composition rather than a
universal local-server API. `CreateCabin` preserves the historical F0.2 fixture;
`CreateConceptDemo` composes D0.0's finite Central Island, mountain/cave, Sky Island, and public
telepad plus D0.2's geographically separate Far Island and reciprocal public gates over the same
bounded bridge. Its 162 authored chunks are two 9×9 clusters with no authored gap chunks. The
fixtures have separate persistent-world identities and save roots. The permanent Single Player and
Complete shells select the Concept Demo explicitly; acceptance can still select Cabin without
changing this ownership boundary.

D0.3 adds an authoritative welcome sign as a second concrete consumer of that generic local UI
path. Hover is derived only from resident/presented public kind. Interact nominates the finite local
selection; Game resolves the trusted sign identity, validates current kind and range, and supplies
the immutable title/body. The sign's compiled Close action crosses the same retained semantic
operation and explicit apply/reject proof boundary as other local UI.

F0.5 lets composition supply the exact passive stock Inventory definition when attaching the
client runtime. The definition is cached before use and the local Game authority emits the ordinary
`HotOpen`; without that exact stock capability it retains the accepted cold-open path. Dynamic
model values/actions remain Game-owned, and every new local session owns a fresh bounded UI cache.

I0.12 reuses the same boundary for an exact public-item nomination, owner-private Inventory details,
Drop, and the admitted Primary result. The local command may carry a disclosed
`WorldPublicEntityId`; the authority resolves and revalidates its current kind/range before moving
the same persistent sword entity into the player's backpack. No ECS handle, rolled stat, or drop
position crosses from Godot into authority. I0.13 replaces the local formatted detail string with
the same immutable typed Inspection Property set consumed remotely. Local still remains packet-free:
it publishes those facts directly into Client Core under the same identity/revision semantics.

C0.2 likewise keeps the embedded host packet-free. Local Global submissions enter the same
Game-owned routed audience used by remote participants, and the registered local recipient receives
Global/System facts through the same bounded Client Core occurrence/application-proof seam. No
loopback packet or local-only Chat rule is introduced.

D0.6 adds one more packet-free consumer on the same authority owner thread. The local session records
authoritative tick durations into a fixed bounded window, composes a sanitized one-participant
Server Monitor snapshot, and opens the ordinary cold compiled UI definition. When Complete hosting
is active, the optional extension supplies the shared authority's one-Local-plus-remote participant
snapshot without replacing the local world or clock. Refresh is cold (approximately one second),
and only the typed local UI-queue backpressure condition is deferred; unrelated authority failures
are not swallowed. Local HUD latency remains the literal label `Local`, never a loopback RTT.

D0.7a adds no packets to this path. The immutable authority view exposes only the currently equipped
Main Hand public prefab and finite Game-owned vertical phase. An accepted Interact/Primary command
is observed after Game acceptance and published as a bounded public actor cue; rejected actions
never advance it. Without hosting, the local client applies that state directly under the same
generational cold-metadata contract. With Complete hosting, the optional extension records the
local host in the same shared actor read model consumed by remote participants. The local control
path remains packet-free in both cases.
