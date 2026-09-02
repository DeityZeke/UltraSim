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

The authority thread owns `ServerRuntime`, Admission, mutable ECS/Game state,
Inventory UI policy, Chat routing, persistence, the local public recipient, and
all Game mutation. It advances at authoritative cadence independently of Godot.
Presentation stalls therefore accumulate only bounded domain debt; they do not
pause the world or grant the presentation thread authority access.

The bridge is deliberately concrete rather than a transport framework:

```text
GamePlayerCommand              bounded FIFO intent
owner/private presentation     one coalesced immutable view
public ChunkPublication        retained ticket until Client apply proof
Chat                           retained occurrence until Client apply proof
server-composed UI             retained operation until Client apply/reject proof
```

Rejected public-world and Chat applications retain the same semantic debt for
retry. A rejected UI operation instead retires that operation and
desynchronizes its view, matching the remote UI contract; later operations do
not continue against unknown client UI state.

Public disclosure uses `GameChunkInterestProfile.Default` and the accepted
`OfflineDirectChunkCadence.EvidenceProfile`. It consumes packet-free Game
replication semantics directly; it does not scan/copy the complete publication
store on every render frame. Structural client residency remains independent
from dynamic-public interest, so leaving public interest retires only public
knowledge from a resident Client chunk.

The local path does not serialize Game protocol messages, create a loopback
connection, or reference `Client.Network`, Game Protocol, Remote Replication, or
`Kernels.Network`. Hosting in a Complete composition adds independent remote
sessions around the same authority; the host player stays on this local path.

`LocalPlayableSession` is currently the concrete Cabin/reference composition,
not the final F0.2 content package or a universal local-server API. F0.1 supplies
the persistent shell; F0.2 will bind this accepted boundary into its Single Player
session lifecycle and built-in Test World composition without changing ownership.
