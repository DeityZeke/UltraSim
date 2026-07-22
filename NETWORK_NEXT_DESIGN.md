# UltraSim Network Next: Rebuild Specification, Feature Inventory, and Validation Plan

Status: proposed implementation specification  
Scope: network kernel, wire protocol, transports, sessions, replication, AOI, client projection, engine-agnostic server/client runtimes, wrapper/entry-point topologies, application protocols, tests, benchmarks, tooling, and migration  
Primary objective: preserve the useful behavior of the current stack while rebuilding for correct delivery semantics, zero steady-state allocation, bounded memory, minimum p95/p99 tick cost, and minimum useful bytes per connection

## 1. Purpose

This document defines how UltraSim's next networking stack should be built from scratch. The current implementation is the functional base and comparison target; its public APIs and internal data structures are not compatibility requirements. Correct behavior, useful feature coverage, operational simplicity, protocol safety, and measured performance are the compatibility requirements.

The desired outcome is unusually aggressive:

- a nominal `16 ms` server tick, approximately `62.5 Hz`;
- a normal network-stage p95 budget at or below `4 ms`;
- a severe-load p99/worst-case design target at or below `8 ms`, without allowing an unbounded backlog to hide the cost;
- approximately RunUO-like bandwidth efficiency where the workload permits it;
- zero managed allocation in steady-state packet I/O, publication, scheduling, assembly, fanout, decode, and ordinary client apply paths after warmup;
- no routine garbage collections during a representative steady-state soak;
- bounded queues, pools, replay windows, baselines, UI transfers, subscriptions, and session state;
- deliberate degradation of distant/cosmetic traffic before owner-critical or control traffic;
- correctness and security that do not depend on a reliable LAN, localhost benchmark, or friendly client.

The motivating operational reference is the reported RunUO figure of roughly `4-5 KB/s` per connected player. That number should be treated as a directional historical target, not a verified wire-level specification: it is unclear whether the original statement meant decimal or binary kilobytes, one direction or both, and application payload or full wire traffic. Even so, it is a useful forcing function. At `62.5 Hz`, `4-5 KiB/s` is only about `65-82 bytes` per connection per simulation tick on average. A 3D game with prediction, AOI, fanout, terrain, UI, and richer state cannot hit that in every combat case, but it should avoid spending hundreds of bytes per tick on unchanged or irrelevant state.

The current rewrite has already demonstrated that a generational improvement is possible. The owner-reported change from its predecessor was approximately:

- general capacity from roughly `1,500-2,000` CCU to `2,500-3,000` CCU;
- p95 ping from more than `80 ms` to approximately `20 ms` in the compared workload;
- dense capacity from roughly `100` to roughly `300` CCU;
- lower packet volume;
- about a `30%` broad improvement, with some isolated paths approaching `10x`.

Those figures are valuable acceptance context but must not be silently mixed with differently shaped repository benchmarks. Next must retain named workload profiles and machine-readable artifacts so claims remain reproducible.

Tests and benchmarks are part of the architecture. Every implementation phase must add or port tests, run a semantically equivalent Legacy/Current/Next comparison where one exists, and record allocation, GC, bytes, loss/recovery, and tail latency. The expected direction in this document is intentionally strong, but it is not sacred. If tests, packet traces, disassembly, hardware counters, impairment runs, or production-shaped replays show that a proposed technique loses, change the technique and update this document.

Documentation is evidence of intent, not proof of implementation. When sources disagree, use this order:

1. executable code and focused tests;
2. fresh build, benchmark, packet-trace, and launch-smoke results;
3. current subsystem README status sections;
4. active implementation plans;
5. historical baselines and old design documents;
6. recollected results that no longer have an artifact.

## 2. Generation Names and Comparison Policy

The repository contains three useful network generations or reference bands:

| Name in this document | Location or intended location | Role |
| --- | --- | --- |
| Legacy | `_OLD/UltraSim/Network`, `_OLD/Docs`, and older client/server runtime paths | Historical behavior, implementation lessons, and archived benchmark baselines |
| Current | `Kernels/Network` plus `Server/Network`, `Client/Network`, current tests, and current benchmark tools | Functional base, current correctness baseline, and primary performance comparison |
| Next | An isolated `Kernels/Network.Next` project and matching bridge projects until cutover | Implementation specified here |

Legacy and Current should remain runnable while Next develops. Do not optimize or repair a baseline in place without recording the change and producing a new baseline ID. Where old code cannot coexist in one process, keep the existing separate-process comparison approach and join JSON reports by a stable scenario ID and semantic trace hash.

Comparison priorities are:

1. Next versus Current on the same semantics and workload.
2. Next versus Legacy for historical context.
3. Current versus Legacy to detect harness or machine drift.
4. Next versus Next-previous-commit to catch incremental regression.

A faster result is invalid if it achieves speed by omitting required delivery, client apply, session cleanup, encryption, AOI transitions, or expected recipients. Every performance report must state its semantic profile, including which delivery guarantees and transforms were enabled.

Deliberate Current non-parity cases that Next must fix include:

- real channel delivery semantics instead of metadata-only UDP channel declarations;
- authenticated encryption and replay protection for credentials and gameplay sessions;
- atomic, non-interleaved baselines with exact acknowledgement rules;
- acknowledgement-based state knowledge instead of send-based assumptions;
- per-connection ordered event sequences without invisible scoped gaps;
- lifecycle tombstones for despawn, AOI leave, and removal;
- lossless cursor advancement when an output queue is full or fanout is partial;
- ownership-safe pooled buffers without copyable disposable handles;
- bounded unknown-endpoint/session/UI/chat state;
- wire-byte budgeting rather than payload-only budgeting;
- cadence that defers a one-shot state change instead of losing it;
- canonical chunk identity independent of a recipient's current distance ring;
- strict parsers that reject trailing, malformed, oversized, and non-canonical data.

Tests must name and lock each intentional non-parity decision before that Next subsystem is considered complete.

## 3. Non-Negotiable Targets

### 3.1 Correctness targets

- Every channel's declared delivery behavior is implemented end to end and tested under loss, duplication, reordering, delay, and wrap-around.
- A connection cannot become ready until protocol, schema, content requirements, security state, and admission state agree.
- No background network worker reads mutable live ECS storage.
- Publication either records a change in owned network state or reports a bounded-capacity failure; it never silently loses a change.
- Latest state remains pending until it is admitted to a valid send or safely superseded by a newer version.
- Ordered events are sequenced in the recipient-visible domain; filtered-out events cannot create an unexplained gap.
- Baselines stage on the client and commit atomically only after complete fragment, item-count, uniqueness, generation, and checksum validation.
- Only a baseline ID and epoch actually issued to that connection can be acknowledged.
- Incremental state is accepted only against the exact committed baseline generation or an explicitly compatible state epoch.
- Spawn, despawn, AOI enter, AOI leave, teleport, and chunk transition semantics are explicit and ordered.
- Queue admission, transform, socket submission, acknowledgement, and application are distinct states.
- Cursor advancement happens only at the commit point defined for that lane.
- Sequence and connection epoch wrap cannot make old traffic valid in a new session.
- Every pooled buffer and table slot has exactly one valid ownership state at a time.
- Disconnect, timeout, malformed traffic, and socket failure release all owned resources.
- Hard capacity limits have deterministic failure/degradation behavior and metrics.
- Multiple world, server, and loopback instances remain isolated.
- Negative world coordinates use floor division consistently.
- Origin shifting never changes canonical world position or network identity.

Every target requires direct unit tests, randomized/model-based tests where appropriate, deterministic link-simulator tests, and at least one regression test named for the failure it prevents.

### 3.2 Allocation and memory targets

- After pool initialization and warmup, a steady-state server network tick allocates exactly `0` managed bytes within configured capacity.
- Steady-state receive, authenticate, decode, dispatch, publish, schedule, assemble, packetize, encrypt, queue, socket completion, and client apply paths allocate exactly `0` managed bytes.
- Connection establishment, AOI window resize, content catalog changes, and administrative diagnostics may allocate only in explicitly cold paths and must be separately measured.
- No per-packet `Task`, delegate closure, exception, `byte[]`, `List`, dictionary entry, LINQ iterator, boxed interface call, or timestamp object is created in hot paths.
- Buffer pools, connection tables, replay windows, state tables, fragment tables, subscriptions, and work rings have configured maxima and high-water metrics.
- Capacity overflow is explicit: reject, coalesce, defer within a bound, snapshot-recover, drop a permitted lane, or disconnect. It never falls back to unbounded allocation.
- The default ten-minute steady-state soak should report zero Gen0/Gen1/Gen2 collections caused by the network runtime after warmup.
- Long soaks must show flat retained managed memory, native memory, pool occupancy, session count, endpoint count, and replay retention once workload stabilizes.

Allocation tests should measure both the combined network tick and each major substage. A zero-allocation claim is not accepted if allocation was moved into a background socket task or client process.

### 3.3 Latency and throughput targets

- Nominal server tick remains `16 ms` unless a workload-specific profile deliberately selects another rate.
- Simulation tick and replication send cadence are independent. A `62.5 Hz` simulation does not require every observer to receive every category every tick.
- Normal network p95 target is `<= 4 ms` for protected profiles.
- Severe-load p99 target is `<= 8 ms` where the configured load is inside the accepted operating envelope.
- Tick maximum, queue age, missed deadlines, and recovery events are reported; averages cannot hide stalls.
- Owner-critical input/correction and control traffic remain serviceable under bulk pressure.
- Next must meet or beat Current on protected semantic-equivalence profiles before cutover.
- Initial per-connection bandwidth bands to test, not assume, are:
  - idle/light world activity: `4-6 KiB/s`;
  - active traversal: `6-10 KiB/s`;
  - dense combat: `10-20 KiB/s` with explicit caps and degradation;
  - baseline/content bulk: separately budgeted and rate-limited so it cannot stall realtime traffic.
- Reports separate useful application bytes, protocol bytes, UDP payload bytes, and estimated IP/UDP/Ethernet wire bytes.

### 3.4 Security targets

- Production sessions authenticate every post-handshake datagram.
- Credentials are never sent as plaintext application payload over unauthenticated UDP.
- Handshake cookies and anti-amplification rules prevent an unverified endpoint from consuming a full connection slot or causing reflected traffic.
- Connection tokens are unpredictable and bound to a security epoch.
- Packet numbers and receive windows provide replay protection.
- Every field, count, string, collection, fragment, and decompressed output has a validated upper bound before allocation or mutation.
- Rate and strike limits exist per endpoint, connection, packet family, and expensive action class.
- Parser failure is data, not an uncaught exception path.
- Cryptographic choice and implementation require focused tests, interop vectors, key rotation tests, and throughput benchmarks on deployment hardware.

### 3.5 Concurrency and determinism targets

- One assembly shard owns a connection at a time.
- One transport shard owns a socket association's mutable protocol state at a time.
- Ownership migration is explicit, rare, epoch-stamped, and tested.
- Shard-local hot state avoids shared atomics and false sharing wherever possible.
- Cross-thread handoff uses bounded SPSC/MPSC rings with explicit backpressure.
- No socket worker mutates gameplay state directly; decoded commands enter bounded queues and apply at the server's normal mutation barrier.
- Given the same accepted input trace, interest trace, time source, and configuration, protocol decisions are replayable even if physical socket completion timing differs.

## 4. Current Network Feature Inventory

This section describes verified Current functionality. It distinguishes a working seam from the stronger semantics its name may imply.

### 4.1 Kernel boundary and layering

Current does well:

- the audited source surface is substantial: `137` kernel C# source files / approximately `7,864` lines, plus `63` C# bridge files / approximately `5,814` lines across `Server/Network` and `Client/Network` (excluding generated `obj` files);
- `Kernels/Network` is engine-agnostic and has no ECS, Godot, gameplay, server-host, or client-host dependency.
- Server ECS/network integration lives in `Server/Network`.
- Client projection and movement integration lives in `Client/Network`.
- The kernel is split into `Core`, `Serialization`, `Protocol`, `Transport`, `Session`, `Subscriptions`, `Streams`, `Publish`, `Scheduler`, `Assembly`, `Transform`, and metrics/profile records.
- The design documents explicitly preserve immutable outbound data, traffic-class isolation, a transform seam, build-once fanout, and chunk-backed world-space AOI.

This boundary is one of Current's strongest features and should be retained. Next should make the boundaries stricter by exposing generated descriptors and owned snapshots rather than general-purpose object graphs.

Tests:

- project-reference tests should prove the kernel cannot reference ECS, server, client, or engine assemblies;
- architecture tests should reject forbidden namespaces;
- loopback tests should run without Godot or a socket.

### 4.2 Core identities and traffic classification

Current features:

- compact `ConnectionId` and `SessionId` value types;
- planes: `Control`, `Input`, and `State`;
- packet classes: `Control`, `Realtime`, `ReliableEvent`, and `Bulk`;
- service classes `A-D`;
- channels: `ReliableOrdered`, `ReliableUnordered`, `UnreliableSequenced`, and `Unreliable`;
- packet metadata registry mapping packet IDs to channel/class policy.

What is good:

- planes, packet classes, and channels describe different concerns instead of collapsing everything into one priority integer;
- bulk is structurally distinct from realtime;
- the model is rich enough to express owner-critical, nearby state, distant state, reliable events, and content transfer.

Current limits:

- service classes are mostly descriptive and not consistently used by runtime scheduling;
- channel behavior is not implemented by the UDP transport; it is metadata;
- identities do not carry a session/security epoch sufficient to reject delayed traffic after reuse;
- sequence domains use widths and comparison rules that are not consistently wrap-safe.

Next retains the conceptual classifications, eliminates unused duplicate knobs, and defines one exact mapping from message family to lane, delivery, service, expiry, and overflow policy. Tests must exhaustively validate that table.

### 4.3 Serialization, packet contracts, and envelope

Current features:

- span-based `BitWriter` and `BitReader`;
- static-abstract typed `IPacket<TSelf>` packet structs;
- frozen O(1)-style packet registry and metadata registry;
- compact packet envelope containing packet ID, flags, and payload length;
- transform flags for compression and encryption;
- typed control, movement, state, event, baseline, delta, chat, UI, credential, and presentation packets;
- optional realtime state batching.

Current packet families include:

- handshake hello/result and rejection reasons;
- ping/pong;
- client/server disconnect notices;
- movement input and authoritative movement state;
- realtime state frame and batch;
- reliable event frame and highest-contiguous acknowledgement;
- baseline begin/frame/complete/ack;
- latest-state delta frame;
- chat submit/message;
- UI panel open, model update, document chunk, and action request;
- login credential material;
- entity presentation events.

What is good:

- typed packets and span readers are a sound low-allocation foundation;
- static metadata supports startup validation and fast dispatch;
- the five-byte logical envelope is small;
- packet families already prove a broad vertical slice.

Current limits:

- `PacketCodec.EncodePayload` rents/allocates an initial array and may `Array.Resize`, creating two arrays for an underestimated packet;
- overflow is communicated through exception text instead of `TryMeasure/TryWrite` results;
- several variable-sized packets own `byte[]` or `string` data;
- parsers can accept trailing data and some malformed bundles can throw out of the hot dispatch path;
- counts, nesting, UI documents, and decompressed size do not share a single generated bound table;
- packet ID/key collision protection is incomplete; some higher-layer hashed keys can collide silently;
- a transform flag does not prove the transform happened;
- protocol/schema compatibility is hand-maintained rather than generated from the actual wire schema.

Next uses generated exact sizing and strict canonical decode. Every codec requires truncation, trailing-byte, maximum-bound, random-byte, and round-trip tests.

### 4.4 Transports

Current features:

- in-memory loopback transport using the same datagram contracts;
- UDP socket transport;
- same-connection envelope bundling to reduce UDP send calls and wire datagram count;
- synchronous and experimental asynchronous receive/flush paths;
- socket buffer and realtime pacing options;
- transport poll and flush profile snapshots;
- pooled inbound/outbound payload buffers in several paths.

What is good:

- loopback and real UDP use a common kernel seam;
- bundling produced a real reduction between logical fanout datagrams and physical UDP datagrams;
- transport profiling separates receive, bundle, and send costs;
- transport can be exercised in separate server/client processes.

Current limits:

- UDP does not implement reliability, ordering, duplicate suppression, sequenced delivery, fragmentation, packet acknowledgement, RTT/RTO, congestion response, or a complete pacing policy;
- compression, segmentation, and encryption are interfaces/flags, not production implementations;
- the credential packet can traverse plaintext UDP;
- unknown endpoints can receive connection IDs before a validated handshake and endpoint/session dictionaries are insufficiently bounded and cleaned;
- the default maximum datagram near `2048` bytes can exceed conservative Internet path MTU;
- there is no robust fragmentation/reassembly protocol;
- polling relies on `Socket.Available`, yield/sleep behavior, and hot-loop timing calls;
- asynchronous flush uses `Task.Run`-style work rather than a persistent completion-driven I/O engine;
- failed flush/reset paths can clear queued datagrams without releasing their payload ownership;
- output and input datagrams are copyable readonly `IDisposable` structs, making double-release possible;
- per-recipient datagram/refcount work remains expensive;
- queues, endpoint maps, and pending bundles need comprehensive hard limits and eviction behavior.

Next treats the current transport as a useful socket/bundling prototype, not a completed UDP protocol.

### 4.5 Session and control plane

Current features:

- handshake version/feature validation;
- connection and session registries;
- lifecycle states and snapshots/events;
- admission and duplicate-session policy;
- auth and keepalive timeout sweep;
- ping/pong and explicit disconnect notices;
- client/server control-plane host that works over loopback and UDP;
- server binding, unbinding, kick, and silent-client cleanup in the main movement host;
- accounting/admission and selected-character integration above the kernel.

What is good:

- session state is network-native and bridge code owns gameplay identity;
- lifecycle events and metrics exist early;
- explicit disconnect shortens cleanup compared with timeout-only behavior;
- live Godot/headless smoke hooks validate connect, reconnect, ping, movement, chat, and cleanup.

Current limits:

- timeout can leave a disconnected session in manager maps until a host-specific drain performs cleanup;
- generic use of the session manager can therefore retain stale state;
- some hosts sweep more than once per inbound batch;
- endpoint validation, anti-amplification, cryptographic key establishment, endpoint migration, and replay protection do not exist;
- session/connection/sequence wrap is not protected by a sufficiently strong epoch;
- per-IP and pre-auth resource budgets are incomplete;
- protocol negotiation advertises transforms that are not implemented.

Next must make cleanup intrinsic to the session state machine and prove it under timeout, malformed input, duplicate login, reconnect, endpoint migration, and abrupt socket failure tests.

### 4.6 Latest-state streams

Current features:

- `LatestStateStream` coalesces by logical item key and scope;
- stable logical item identity can be preserved across AOI scopes;
- cached ordered snapshots avoid sorting on every drain;
- `Realtime` and `Bulk` publication;
- snapshot copy, per-connection sent tracking, delta planning, and delta building;
- slot-indexed client observer storage in the widened harness.

What is good and somewhat unique:

- latest mutable state is structurally separated from ordered facts;
- coalescing happens before recipient fanout;
- stable item identity across a changing scope was a measured structural win;
- the stream can serve shared fanout rather than serializing the same value per recipient.

Current limits:

- entries are class/dictionary based and snapshots still require global ordered views;
- scheduling commonly scans a large shared snapshot for each connection and filters by scope;
- `ConnectionLatestStateSentTracker` records sent state before acknowledgement, so packet loss can make the server believe a client knows data it never received;
- stale baseline acknowledgements can seed incorrect sent state;
- removing a latest-state entry emits no tombstone;
- publishing an entity in a new chunk/ring does not inherently remove its old scoped entry, allowing ghosts;
- one ECS dirty drain is destructive, so multiple consumers or cadence-gated publications can miss data unless an intermediate network-owned version table captures it;
- a one-shot change published on an off-cadence ring can be permanently missed;
- recipient phasing can similarly skip a version forever if pending state is not tracked per recipient/cohort.

Next retains latest-wins semantics but replaces global snapshot scanning with per-scope changed queues, dense network item slots, item versions, tombstones, and recipient-known acknowledgement state.

### 4.7 Ordered-event streams and recovery

Current features:

- bounded ordered event ring;
- highest-contiguous client acknowledgement;
- client out-of-order buffer;
- server acknowledgement tracker;
- per-connection replay buffer;
- retransmit policy, planner, and builder;
- recovery planner capable of selecting retransmit or full baseline;
- production `ServerNetworkSessionRuntime` filters scoped events and renumbers them contiguously per connection.

What is good:

- events that must retain order are not forced through latest-state semantics;
- replay is bounded;
- recovery versus retransmit is an explicit policy decision;
- the real runtime corrects the classic global-sequence/scoped-visibility gap by giving each connection a contiguous visible sequence.

Current limits:

- the widened harness can bypass the real per-connection sequencing path and enable `AllowSequenceGaps`, so benchmark success does not prove production reliable-event semantics;
- retransmit entries do not consistently update their last-sent deadline, which can make them eligible every tick after the first timeout;
- acknowledgements can advance beyond data actually issued unless strictly checked;
- delivery still rides best-effort UDP;
- event retention and payload ownership are layered above rather than integrated with a real reliable lane;
- global event sequence and recipient sequence identity are not clearly separated in all APIs.

Next assigns reliable lane sequence only after interest filtering, retains an optional global event ID for diagnostics/deduplication, and integrates acknowledgement/retransmit with the transport's real reliable window.

### 4.8 Baseline and delta recovery

Current features:

- begin/frame/complete/ack packet family;
- state baseline descriptors;
- filtered snapshot builder;
- client observer store reset/apply;
- ordered-event floor reset after baseline completion;
- incremental delta packets after baseline acknowledgement;
- generic runtime composing baseline, delta, event replay, and recovery.

What is good:

- baseline is recognized as a first-class recovery operation;
- the current generic runtime gives the feature an integration point rather than leaving isolated helpers;
- recovery has bounded policy seams.

Current limits:

- the client clears/applies live state during receipt rather than building a complete staging image;
- there is no atomic swap after full validation;
- item count, uniqueness, fragment set, and checksum validation are insufficient;
- a server can begin a fresh baseline every tick until ack, interleaving generations;
- baseline acknowledgement itself is on best-effort UDP;
- stale/unissued acknowledgements can be accepted;
- incremental delta acceptance uses a permissive baseline comparison instead of exact generation matching;
- marking latest state known on send creates a stale-ack/loss race;
- a snapshot is emitted as many independent frames without a complete reliable fragmented-message contract.

Next permits one active baseline generation per connection/scope, uses reliable isolated fragments, stages and checks the snapshot, commits atomically, and queues or versions post-cutoff deltas until exact commit.

### 4.9 AOI and subscriptions

Current features:

- world-space positions and chunk coordinates;
- player-centered AOI rings specified in meters;
- chunk overlap used as an indexing/maintenance method;
- each overlapped chunk assigned to its nearest ring;
- bidirectional connection-to-subscription and scope-to-connection registry;
- registry-owned reconciliation;
- server bridge classification for latest state and ordered events over ring scopes;
- configurable ring radii and cadence in the widened harness;
- currently demonstrated direction of immediate every tick, near every two ticks, far every three ticks.

What is good and distinctive:

- gameplay distance is defined in world units; chunks are an indexing tool rather than the meaning of distance;
- reverse subscriptions make shared spatial fanout possible;
- cadence and ring radius produced material capacity and memory improvement;
- the policy aligns with a future client render-chunk window.

Current limits:

- `ConnectionSubscriptionRegistry` uses `List`/`HashSet`, global locking, and costly reconciliation/removal patterns;
- the current key can combine ring index with chunk coordinate, incorrectly mixing stable spatial identity with recipient-relative priority;
- one chunk must be allowed to be immediate for one client, near for another, and far for a third;
- current production movement world setup can be effectively global/single-scope while richer multi-ring behavior lives mainly in primitives and the widened harness;
- entity/chunk leave and tombstone behavior is incomplete;
- cross-chunk movement can leave old latest-state entries;
- current integer chunk coordinates should expand for enormous worlds;
- vertical policy, multiple interest anchors, hysteresis, prefetch, and teleport behavior are not complete;
- cadence and global sequence phasing can starve one-shot updates.

Next makes canonical `ChunkKey` stable and stores ring, cadence, phase, priority, known revision, and pending version in per-connection interest state.

### 4.10 Scheduler, assembly, and shared fanout

Current features:

- per-packet-class item and payload-byte budgets;
- connection schedule state;
- typed scheduled work;
- latest state, reliable event, and bulk lanes;
- transform-aware assembly;
- shared published frames;
- subscription fanout dispatcher;
- optional realtime batching and recipient/connection phasing;
- build-once payload reuse across a subscriber set.

What Current does especially well:

- build-once fanout is the correct response to dense AOI cohorts;
- immutable published payloads make later parallelization possible;
- bulk has a separate budget from combat/movement;
- scheduling is explicit and benchmarked rather than embedded in a monolithic send loop.

Current limits:

- scheduler drains latest state first and can `break` when that lane exceeds budget, causing head-of-line blocking for other eligible lanes;
- budgets count body bytes rather than final datagram/wire bytes;
- schedule cursors can advance before fanout/output admission succeeds;
- partial output-array admission can lose work and leak retained frames;
- no-subscriber, abandoned-transform, and transform-failure outputs can leak pooled payloads;
- `ImmutablePayloadBuffer` begins with no creator ownership, producing ambiguous retain/release rules;
- per-recipient output objects and atomic refcounts consume a large part of the fanout win;
- global-sequence modulo cadence can permanently starve a recipient;
- batching can bypass connection phasing;
- service classes `A-D` do not consistently drive selection;
- profiling timestamps and delegate-heavy paths add measurable hot-loop overhead.

Next schedules independent lane cursors, reserves exact wire cost before commit, commits only after bounded queue admission, and sends cohort descriptors rather than manufacturing one managed datagram object per recipient.

### 4.11 Server-side built-on features

Current server bridges and fixtures include:

- server status, metrics, system runtime, player session, account summary, and application event publication;
- ECS world/runtime snapshot publication;
- dirty component rules classified as realtime latest state, bulk latest state, reliable ordered event, or bulk event;
- chunk-AOI scope classification and ring cadence policy;
- movement input validation and burst coalescing;
- authoritative ECS movement application;
- owner correction deadband and maximum-silent-tick policy;
- observer movement/presentation publication;
- generic session runtime composing baseline, deltas, reliable events, replay, retransmit, and recovery;
- chat service, channels, local scopes, rate/length options, commands, and metrics;
- UI service and chunked cold-open documents;
- admission/login/character selection and credential flow;
- script command and panel-provider binding;
- entity inspection service;
- headless, console, and Godot server host integration.

Current limits and Next requirements:

- ECS dirty extraction must become a multi-consumer or immutable network-owned capture so cadence cannot destroy evidence;
- JSON snapshot/status payloads are cold/operator traffic and should not contaminate hot gameplay codecs;
- movement input needs expiry and redundancy so a lost stop packet cannot leave motion active forever;
- owner correction suppression must not suppress input acknowledgement, velocity-mode changes, teleports, or required reconciliation anchors;
- chat maps/rate state need bounded cleanup;
- UI transfer IDs, chunk counts, bytes, pending transfers, and deadlines need hard caps;
- UI/schema/content bytes should use pooled streaming views rather than repeated array copies;
- hashed item/scope IDs need collision-safe generation or verified registration;
- production must run the same multi-ring/cadence/session semantics used by the accepted benchmark profile.

### 4.12 Client-side built-on features

Current client bridges and fixtures include:

- input sequencing and movement send throttling;
- immediate start, stop, direction-change, and edge sends plus an unchanged-input heartbeat;
- prediction storage and acknowledgement;
- authoritative correction magnitude metrics;
- interpolation buffers for observer state;
- latest-state single and batch apply;
- baseline and delta apply;
- ordered-event contiguous apply and bounded out-of-order buffer;
- reliable event and baseline acknowledgements;
- chat event routing;
- UI open/model/chunk routing and reassembly;
- a shared movement/control session host;
- Godot presentation hooks for movement, observer state, chat, UI, ping, disconnect, reconnect, and cleanup.

What is good:

- engine-neutral prediction/reconciliation/interpolation remains outside Godot;
- one shared poll path handles control and gameplay;
- client decode/apply is included in widened performance measurement;
- slot-indexed observer storage proved cheaper than scoped dictionaries.

Current limits and Next requirements:

- prediction is still a narrow movement slice, not a complete rollback/reconciliation system;
- latest-state lifecycle lacks authoritative despawn/AOI leave tombstones;
- `AllowSequenceGaps` is a harness workaround, not a production semantic;
- baseline apply must become transactional;
- UI reassembly must be bounded by sender, transfer, bytes, chunks, and time;
- client storage should be dense/generational and pre-sized, with explicit overflow behavior;
- remote interpolation needs snapshot age, jitter, loss, teleport, and dormancy policies;
- floating-origin/render-window integration is still planned.

### 4.13 Current tests and validation fixtures

Fresh Release runs during this audit passed:

- Network kernel tests: `85` tests;
- Client tests: `61` tests;
- Server contract tests: `111` tests;
- combined relevant baseline: `257` passing tests.

The coverage is valuable: it includes codecs, lifecycle, loopback and UDP movement, subscriptions, stream retention, baseline/delta seams, event acknowledgements, recovery helpers, fanout, AOI cadence, UI chunking, admission, chat, persistence integration, timeout, disconnect, multi-client UDP, and client apply.

The important caveat is that many tests prove seams in isolation. They do not prove a reliable encrypted UDP protocol under impairment, atomic baseline commit, bounded hostile input, zero allocations, or production-equivalent widened harness semantics. Next must preserve useful tests, rename tests that encode corrected behavior, and add semantic trace comparison.

### 4.14 Current benchmark and operational tooling

Current tools include:

- `_Benchmarks/UltraSim.Network.Benchmarks` microbenchmark runner;
- `_Benchmarks/UltraSim.Network.BotHarness` real UDP client/server load harness;
- a sharded wrapper launching separate server and client processes;
- staged CCU ramps;
- dense and spread geometry;
- active-movement ratios and phased input/ping;
- optional observer latest state, visual state, bulk state, and ordered events;
- configurable AOI radii and ring cadence;
- owner correction pressure/jitter;
- logical versus wire datagram counts;
- server pump/publish/dispatch/flush substages;
- client poll/decode/apply substages;
- p50-style averages, p95, p99, max, allocation, and GC telemetry;
- report archives under `_Benchmarks/results` and historical `_tmp`/`_OLD` paths.

The current toolchain has unusually good instincts: real sockets, separate processes, sharding, client consumption, staged load, and tail metrics. It must be repaired and made trustworthy before it becomes a Next gate.

Fresh audit result:

- the Network, Server, and Client libraries build;
- the microbenchmark project currently fails to compile because of API/type drift and a missing scripting reference;
- the bot harness currently fails to compile because of a missing scripting reference and an `ulong` to `uint` mismatch;
- historical JSON remains useful context but is not a fresh executable baseline until these build failures are fixed.

Current microbenchmark methodology also needs correction:

- setup and teardown work is included inside timed/allocation regions;
- iteration count is too small for stable tails;
- forced full collections contaminate reported collection counts;
- several fanout results are not disposed, invalidating allocation/ownership conclusions;
- no canonical output verifies semantic equivalence;
- the report marks some non-comparable results without making them impossible to compare accidentally.

Current harness methodology also needs correction:

- localhost does not model WAN delay, jitter, loss, reordering, MTU, NAT, or encryption cost;
- the widened path can bypass the real reliable-event sequence/runtime path;
- earlier persistent staged runs retained stale sessions, including a run whose server-ready count grew from `3072` to `5835` to `8276`;
- first-N active clients and regular spawn grids can bias AOI geometry;
- some scratch arrays are much larger than a production capacity plan;
- output ownership/leaks can inflate allocations;
- no expected-delivery oracle proves every required semantic;
- CPU affinity, warmup, repeat count, confidence interval, and machine-noise policy are incomplete.

### 4.15 What Current does best and what is genuinely distinctive

Current's best decisions should be preserved:

1. **Engine-independent kernel.** It is possible to test the protocol and replication fabric without Godot or ECS.
2. **Latest state versus ordered events.** This is the correct conceptual split for MMO replication.
3. **Planes, packet classes, and bulk isolation.** The design can express importance, delivery, and workload shape independently.
4. **Build-once shared fanout.** Dense local crowds do not require identical serialization per recipient.
5. **Immutable handoff and transform seam.** The architecture is prepared for worker ownership and encryption/compression even though the implementations are incomplete.
6. **World-space AOI with chunks as an index.** Gameplay distance is not accidentally defined by chunk count.
7. **Stable item identity across scopes.** Moving between near/far buckets does not have to create a new logical object.
8. **Real socket and client-apply benchmarks.** The measurement culture goes beyond isolated serializer throughput.
9. **RunUO-like surface compatibility above an ECS core.** Chat, commands, UI, admission, presentation, and script-host features already sit above neutral packet/stream seams.
10. **Empirical cadence tuning.** Immediate/near/far scheduling has already demonstrated meaningful nonlinear capacity gains.

The rewrite should not discard these wins while fixing their implementation gaps.

## 5. Next Architecture Overview

### 5.1 Runtime planes and lanes

Next retains three external planes:

- `Control`: handshake, auth, session, chat, UI actions, admin, graceful close;
- `Input`: time-sensitive client intent and redundant recent input;
- `State`: owner correction, observer state, lifecycle, events, snapshots, and world/content streaming.

The send runtime uses independent lanes:

| Lane | Default delivery | Ordering domain | Typical content | Overload behavior |
| --- | --- | --- | --- | --- |
| Control | reliable ordered | per connection | handshake-after-key, admission, disconnect, critical UI control | bounded queue; disconnect on persistent overflow |
| Input | unreliable sequenced with redundancy | per input stream | movement/aim/action intent | replace old; expire by tick/time |
| OwnerState | unreliable sequenced plus receive feedback | per owner stream | correction, input ack, combat anchor | coalesce; guaranteed minimum service; periodic keyframe |
| RealtimeState | unreliable sequenced | per scope/item version | observer transforms and hot state | coalesce; reduce cadence; drop stale |
| ReliableEvent | reliable ordered | per connection/subchannel | spawn/despawn, chat, discrete gameplay facts | retransmit; snapshot-recover when semantics allow |
| Bulk | reliable segmented, separately paced | per transfer | baseline, terrain, UI schema, content | pause/defer; never block realtime/control |
| UnreliableFx | unreliable | none or short sequence | disposable cosmetic effects | first to drop |

Service class is a scheduling property derived from lane plus recipient-relative interest, not a second independent protocol guarantee. Start with `Critical`, `Immediate`, `Near`, `Far`, and `Background`. Keep `A-D` only as a compatibility mapping until removed.

Tests must verify lane isolation: saturating `Bulk` or `UnreliableFx` cannot delay a small Control/Input/OwnerState packet beyond its configured deadline.

### 5.2 Four-stage data flow

The architecture is a publish-once, route-once, budget-before-build pipeline:

1. `Capture/Publish`
   - runs at the simulation boundary;
   - converts ECS/runtime changes to stable network-owned values;
   - updates latest-state tables or appends true events;
   - never scans recipients and never performs socket work.
2. `Route/Schedule`
   - maintains incremental subscriptions;
   - converts canonical scope changes into recipient/cohort work;
   - applies service class, cadence, deadline, congestion, and wire-byte budgets;
   - reserves output capacity before selecting work.
3. `Assemble/Packetize`
   - builds a payload once per compatible scope/transform cohort;
   - creates connection-relative headers, IDs, ACKs, and position origins;
   - fragments only where the message contract allows it;
   - encrypts/authenticates into pre-owned packet buffers.
4. `Send/Complete`
   - submits bounded batches to the socket;
   - processes completions and ACK feedback;
   - releases buffers and advances delivery state at the correct commit point.

The simulation thread performs only the bounded capture portion. Route, assemble, and send can operate on network-owned immutable/versioned data without live ECS reads.

### 5.3 Kernel and bridge boundaries

The kernel owns:

- protocol identities, lanes, and generated codec descriptors;
- connection protocol state;
- secure datagram framing and reliability;
- transport I/O abstractions;
- latest-state/event/snapshot storage contracts;
- subscription indexes and neutral interest metadata;
- scheduling, packet assembly, pacing, retransmit, and buffer pools;
- low-cardinality metrics and trace hooks.

The kernel does not own:

- ECS component definitions or live storage;
- gameplay relevance rules such as party, stealth, ownership, or combat forcing;
- account/character authorization policy;
- chat permission/content policy;
- UI document meaning or rendering;
- terrain file format or asset trust policy;
- Godot nodes, physics bodies, scenes, or resources;
- persistence coordination.

Server and client bridges provide generated, typed adapters. `Server` and `Client` are themselves portable application runtimes, not names for Godot implementations. Integration tests must prove that no bridge leaks engine types into the kernel, no background worker holds a live ECS ref/span past the capture boundary, and no wrapper-specific dependency enters `Server/Core`, `Server/Hosting`, `Server/Network`, `Client/Core`, or `Client/Network`.

### 5.4 Engine-agnostic runtimes, wrappers, and deployment topologies

The actual server is the engine-agnostic composition of:

- `Server/Core`: application lifecycle, commands, status, metrics, and common runtime state;
- `Server/Hosting`: fixed-step host loop, lifecycle orchestration, injected time/log/config/discovery/storage services, and shared control surface;
- `Server/Network`: server bridge over the Network kernel;
- ECS, gameplay, scripting, persistence, admission, accounting, inventory, UI-schema, and other server services;
- no dependency on Godot, console I/O, CLI parsing, windowing, rendering, or a particular process entry point.

The actual client is likewise the engine-agnostic composition of:

- `Client/Core`: projected runtime state, input sequencing, prediction, reconciliation, interpolation, content/cache state, and app/session models;
- `Client/Network`: client bridge over the Network kernel;
- neutral UI/content/runtime services where applicable;
- no dependency on Godot nodes, resources, rendering, audio, input devices, windowing, or a particular process entry point.

The folders prefixed with `_` are wrappers/composition roots. They start, supervise, observe, and stop the same underlying runtime; they do not define alternative simulation or protocol implementations.

#### Server wrapper roles

`Server/_Console` is the RunUO-style interactive command-line server shell:

- constructs the normal engine-agnostic server runtime;
- presents logs and status as terminal output;
- converts console input into the shared typed command/control surface;
- owns CLI parsing and console process behavior;
- does not mutate ECS, session, or network internals directly.

`Server/_Headless` is the silent/noninteractive server shell:

- constructs exactly the same server runtime and host loop;
- supports an ordinary dedicated server process;
- supports internal automation and test servers;
- supports the embedded or background local server used for single-player;
- owns process/embedded lifecycle plumbing, but no gameplay or protocol policy;
- can expose health/readiness diagnostics without requiring an interactive console or engine.

`Server/_Godot` is a Godot-based operator terminal and visual control surface:

- constructs or attaches to the same engine-agnostic server runtime;
- displays logs, status, settings, metrics, session lists, inspection views, and other operator data;
- converts buttons, forms, and terminal-style input into the same shared commands used by `_Console`;
- may use Godot APIs for presentation and window lifecycle only;
- does not become the simulation owner merely because Godot drives its outer frame callbacks;
- does not contain special movement, ECS, admission, persistence, AOI, replication, or transport implementations.

The wrapper names do not imply different server semantics. `_Console` is not “the console implementation of gameplay,” `_Headless` is not a reduced server, and `_Godot` is not a Godot-authoritative server. They are different fronts over one server.

#### Client wrapper roles

`Client/_Godot` is the first graphical client wrapper:

- constructs the normal engine-agnostic client runtime and session driver;
- translates device input into neutral input samples;
- maps projected entities/chunks/UI state to Godot presentation;
- owns scenes, nodes, resources, camera, audio, rendering, and window behavior;
- cannot place protocol, prediction authority, replication storage, or gameplay rules in Godot-only code.

Headless bots, test clients, future alternative engines, and tooling clients should use `Client/Core` and `Client/Network` directly or through another thin wrapper. Godot is the first host, not the client architecture.

#### Supported deployment topologies

The same runtime contracts support:

1. **Dedicated headless server**
   - `_Headless` owns a server process;
   - remote clients connect normally.
2. **RunUO-style console server**
   - `_Console` owns the server process and interactive operator shell;
   - remote clients connect normally.
3. **Godot operator server**
   - `_Godot` owns or attaches to the server runtime and presents a graphical terminal/control panel;
   - remote clients connect normally.
4. **Single-player/local world**
   - the client starts or attaches to a silent `_Headless` server in the background;
   - the server still owns authority, ECS, gameplay, persistence, admission, and replication;
   - the normal client session driver automatically connects to the advertised localhost endpoint;
   - startup waits for explicit server readiness rather than sleeping for an assumed duration;
   - shutdown ownership and save completion are explicit;
   - startup failure remains visible and diagnosable even though the server is normally silent.
5. **Test/benchmark composition**
   - server and client runtimes can run in one process or separate processes;
   - the chosen transport is declared in the profile;
   - semantic shortcuts are forbidden in parity measurements.

Single-player must remain a real network instance. The default parity path uses the production protocol over localhost, including handshake, security, admission, session state, input, authority, replication, AOI, baseline, persistence binding, disconnect, and cleanup. The client must not directly call server gameplay objects, read the authoritative ECS, share entity storage, bypass packet codecs, or substitute local mutation for replication.

The in-memory loopback transport remains valuable for deterministic unit/integration tests and microbenchmarks. It may also be offered as an explicitly selected embedded optimization only if it preserves the exact message, lane, queue, ownership, session, serialization, and apply-barrier semantics and passes the same trace suite. It must not become an unmeasured single-player-only fast path. Production-equivalence and wrapper-parity tests should use real localhost UDP.

#### Shared lifecycle and control contract

Every wrapper uses the same lifecycle and control APIs:

- construct/configure;
- initialize/load;
- start/listen and publish readiness;
- fixed-step pump/tick through `Server/Hosting` or the client host;
- status, metrics, logs, settings, commands, save, kick, inspection, and shutdown;
- graceful drain with a deadline;
- deterministic disposal and failure reporting.

The common host owns tick accumulation and catch-up policy. A Godot frame, console read, or client render frame may request/pump host work but cannot redefine simulation order or network delivery semantics. Wrapper-specific event loops must not add an extra authoritative tick path.

Tests:

- architecture/reference tests reject `Godot.*`, console I/O, CLI, rendering, and wrapper references below their wrapper projects;
- the same deterministic server trace through `_Headless`, `_Console`, and `_Godot` adapters produces the same world and network semantic hashes;
- lifecycle transition, command, save, settings, kick, inspection, and shutdown behavior is wrapper-equivalent;
- a local single-player launch starts `_Headless`, observes readiness, connects through the normal localhost session, enters the world, saves, disconnects, and shuts down cleanly;
- a local client cannot enter the world when handshake, admission, or baseline validation fails;
- local startup cancellation, bind failure, server crash, client exit, reconnect, and save-on-stop have explicit ownership and cleanup assertions;
- real localhost UDP and in-memory loopback execute the same semantic trace where their declared delivery profile is equivalent;
- no wrapper can mutate ECS/network state except through the shared typed control or runtime input surfaces.

Benchmarks:

- engine-agnostic server runtime alone versus `_Headless`, `_Console` idle, and `_Godot` idle wrapper overhead;
- dedicated separate-process versus embedded/background single-player CPU, memory, allocation, and tail latency;
- localhost UDP versus in-memory loopback with semantic parity clearly labeled;
- Godot operator telemetry disabled, sampled, and active under gameplay load;
- client render cost reported separately from `Client/Core`/`Client/Network` decode and apply.

### 5.5 Stable identity domains

Do not conflate these IDs:

- `ConnectionId`: process-local slot plus generation;
- `SessionId`: logical login/session lifetime;
- `ConnectionToken`: unpredictable wire routing token;
- `SecurityEpoch`: key/replay epoch;
- `NetworkEntityId`: connection- or shard-scoped identity for a projected entity;
- `ReplicationItemId`: stable identity of one replicated state family/item;
- `ChunkKey`: canonical world chunk coordinate/layer identity;
- `TransferId`: bounded bulk/UI/content transfer lifetime;
- ECS runtime entity handle;
- persistent/account/character/content identity.

For hot tables, IDs should be dense generational indices. On the wire, use the smallest session-local ID that satisfies reuse safety. A mapping is preferable to exposing an ECS index as a protocol contract. Tests must delay and replay traffic across disconnect/reconnect and prove that old IDs cannot mutate new objects occupying the same slots.

### 5.6 Time, ticks, sequence arithmetic, and epochs

Next defines four distinct clocks:

- simulation tick: authoritative gameplay progression;
- network schedule tick: replication cadence and deadlines;
- monotonic high-resolution time: RTT, RTO, pacing, timeout, and profiling;
- wall clock: logs, certificates, and operator-facing timestamps only.

Hot protocol code must not call `DateTime.UtcNow`. Each pump receives one sampled monotonic timestamp and one tick value. Tests inject a manual clock.

All sequence comparisons use generated modular arithmetic helpers with a documented half-range rule. Reliable packet numbers should be at least 32 bits; connection/security epochs prevent old wrap cycles from crossing a new session. Long soaks and accelerated wrap tests must cover:

- packet number wrap;
- lane message sequence wrap;
- input sequence wrap;
- baseline ID wrap;
- transfer ID wrap;
- tick wrap if a narrow wire tick is used;
- delayed packets from a previous epoch;
- acknowledgement ranges spanning the wrap boundary.

### 5.7 Capacity planning and sealing

Every server profile supplies a `NetworkCapacityPlan` before start. It includes:

- maximum verified and unverified endpoints;
- active connections and sessions;
- per-shard connection counts;
- inbound/outbound packet slots;
- payload slabs by size class;
- maximum subscribed scopes per connection;
- reverse subscribers per scope and total subscription edges;
- latest-state items, scopes, changed entries, and tombstones;
- ordered events and retained bytes;
- reliable send-window packets/bytes;
- fragment sets and reassembly bytes;
- active baselines and bulk transfers;
- UI/chat/admission pending state;
- command queue entries;
- trace and metrics buffers.

`Seal()` or host start allocates/pins/native-allocates these structures and warms generated codecs. Runtime overflow follows configured lane policy and increments a counter. Tests should deliberately exceed every capacity one at a time and assert exact behavior, cleanup, and lack of fallback allocation.

## 6. Transport and Wire Protocol

### 6.1 Transport strategy

The primary direction is a purpose-built secure UDP protocol because UltraSim needs:

- unreliable sequenced input/state without TCP head-of-line blocking;
- reliable ordered control/events;
- reliable but independently paced bulk transfer;
- shared datagram packing across lanes;
- direct control over cadence, expiry, congestion, and acknowledgement feedback.

This is not permission to improvise a half-reliable UDP layer. The protocol is a first-class product with a state machine, deterministic simulator, fuzzing, and security review.

Keep alternative transports as experiments behind the same message/lane contract:

- loopback for deterministic tests;
- TCP/TLS fallback for restrictive networks if measurement justifies it;
- QUIC as a separately benchmarked prototype, not an assumed replacement. Runtime/API maturity, datagram availability, stream head-of-line behavior, allocation, and CPU cost must be verified on the deployment runtime before selection.

Transport choice cannot change gameplay APIs or message semantics. A fallback that cannot provide an unreliable lane must explicitly map it to the least harmful behavior and report the mode.

### 6.2 Conservative datagram size and packetization

Start with a conservative maximum UDP payload near `1200 bytes`, configurable only downward/upward after path-MTU evidence. Never emit a `2048`-byte UDP datagram simply because a local socket accepts it.

One physical datagram may contain:

- fixed authenticated connection header;
- packet number and security epoch;
- ACK summary/ranges;
- lane sections;
- one or more small messages;
- message fragments only for contracts that permit fragmentation;
- authentication tag.

The packetizer uses exact measured sizes and stops before the configured MTU. A message too large for an unfragmented lane is rejected or redirected to Bulk; it is never silently IP-fragmented.

Tests should cover every size from `0` through MTU boundaries, exact-fit packing, authentication tag overhead, varint growth points, maximum ACK ranges, mixed-lane packing, and messages one byte too large.

### 6.3 Datagram header

The exact bit layout should be generated and benchmarked, but the semantic fields are:

- protocol marker/version bits sufficient to reject random traffic cheaply;
- connection token or compact post-handshake connection route;
- security epoch/key phase;
- monotonically increasing packet number;
- authenticated flags;
- optional largest-received packet plus ACK mask/ranges;
- payload section count or a canonical parse-to-end format;
- optional path challenge/response.

Do not repeat fields that the connection context makes redundant. Do authenticate every routing, acknowledgement, lane, fragment, and length field that affects state.

### 6.4 Handshake and anti-amplification

Recommended handshake:

1. Client sends a small hello containing protocol version, nonce, capabilities, and optional resumption token.
2. Server returns a stateless cookie/challenge bounded by amplification rules; it does not allocate a full connection.
3. Client echoes proof from the same reachable endpoint.
4. Server allocates a provisional connection and completes an authenticated key agreement.
5. Protocol/schema/content compatibility and admission proceed inside the authenticated control lane.
6. The connection becomes `Ready` only after all required gates succeed.

Requirements:

- unverified endpoint table is fixed-size/evicting and cheap;
- bytes sent to an unverified endpoint do not exceed a conservative multiple of bytes received;
- cookies expire and bind endpoint/address-family plus server secret epoch;
- repeated invalid proof is rate-limited without log spam;
- session resumption cannot bypass current account/admission checks;
- endpoint migration requires an authenticated path challenge before the new path becomes primary.

Tests include spoofed source, cookie replay, expired cookie, NAT port change, IPv4/IPv6, amplification accounting, duplicate hello, reordered handshake messages, slowloris timing, and table saturation.

### 6.5 Encryption, authentication, and replay protection

Production data packets use an AEAD construction with per-direction keys and unique nonces derived from security epoch plus packet number. Candidate algorithms should use platform-accelerated span APIs, initially AES-GCM and/or ChaCha20-Poly1305 depending on deployment CPU support. Selection is benchmark-driven and must not weaken nonce uniqueness.

Requirements:

- authenticated header fields are associated data;
- receive windows reject duplicate/too-old packet numbers before expensive decode where safe;
- key rotation occurs before nonce/packet-number exhaustion;
- old key phases have a short bounded overlap;
- authentication failure does not reveal parser detail or mutate connection state;
- credentials and admission data exist only inside the protected channel;
- secrets are cleared on slot release where practical;
- development plaintext mode is explicit, impossible to advertise as secure, and excluded from production acceptance.

Tests use known cryptographic vectors, tampered header/body/tag cases, nonce uniqueness assertions, key rotation, reordered phases, replay windows, and sustained encryption throughput/allocation benchmarks.

### 6.6 Receive feedback, RTT, and RTO

Every authenticated datagram participates in receive feedback even when its messages are unreliable. Packet ACKs provide:

- RTT samples;
- loss/reordering estimates;
- congestion feedback;
- acknowledgement-based state knowledge;
- reliable packet retirement.

Maintain smoothed RTT, RTT variance, minimum RTT, bounded RTO, loss estimate, and latest receive/send time per connection. Use one timer wheel or per-shard deadline heap/ring, not one managed timer per packet.

ACK rules:

- acknowledge received packets, not merely parsed application messages;
- bound ACK range count and encoded bytes;
- ignore impossible ACKs above issued packet number;
- tolerate reordered ACKs;
- never let ACK processing free a buffer twice;
- update a retransmission's next deadline and retry/backoff state when it is sent again.

Tests should use deterministic loss patterns and property tests against a simple reference model.

### 6.7 Reliability and ordering

Reliability is implemented per lane/subchannel so one missing bulk fragment cannot block control or realtime state.

Reliable messages carry:

- lane/subchannel;
- connection-visible message sequence;
- message or fragment identity;
- expiry/recovery policy;
- payload reference owned by the reliable window.

The send window owns bytes until acknowledged, expired into an allowed snapshot recovery, or connection teardown. Retransmission packetizes the same immutable message into a new datagram packet number and updates its last-sent deadline.

Receiver behavior:

- duplicate messages are discarded;
- reliable ordered messages buffer only within a bounded gap window;
- reliable unordered messages apply once in any order;
- a gap beyond capacity triggers protocol recovery or disconnect according to lane;
- no hidden `AllowSequenceGaps` mode exists in production.

Test matrices must cover 0-30% loss, duplicate bursts, arbitrary reorder, delay spikes, ACK loss, packet-number wrap, message-sequence wrap, and reliable-window overflow.

### 6.8 Congestion control, pacing, and fairness

Next needs an explicit rate controller even on a dedicated MMO server. The first implementation should be deliberately simple and testable:

- per-connection pacing bucket based on estimated delivery rate/RTT/loss;
- server-wide and per-shard egress caps;
- reserved minimum tokens for Control/Input/OwnerState;
- separate Bulk ceiling;
- loss/ECN response where available;
- burst cap so a delayed tick does not dump multiple ticks of data at once;
- fair scheduling across connections with starvation aging.

Do not commit to a sophisticated congestion algorithm by name before measurement. Prototype a conservative controller and compare it with fixed-rate/token-bucket and any candidate modern controller using the link simulator and real WAN tests.

Tests must prove that one lossy/slow client cannot grow memory or consume the assembly budget of healthy clients.

### 6.9 Segmentation and reassembly

Fragment only reliable baseline/bulk messages. Prefer application-level chunking with independently verifiable units over generic fragmentation.

Each fragment set has:

- connection and transfer ownership;
- message type and generation;
- total byte/item count with hard maximum;
- fragment count with hard maximum;
- fragment index and offset;
- whole-message checksum/hash where appropriate;
- deadline and received bitmap;
- exact reserved reassembly capacity.

Reject overlapping, inconsistent, duplicate-with-different-data, out-of-range, or over-limit fragments. Reassembly state is fixed-capacity and evicts/expires by policy. Tests fuzz fragment order, duplication, omission, overlap, truncation, checksum failure, timeout, and memory exhaustion.

### 6.10 Socket I/O engine

Replace polling/yield/`Task.Run` flush with persistent completion-driven workers:

- one or more long-lived socket shards;
- pre-created `SocketAsyncEventArgs` or equivalent reusable operations;
- pinned or native slab buffers sized during sealing;
- batched receive/send completion processing;
- bounded SPSC rings between protocol/assembly and socket owner;
- no task per send, flush, connection, or tick;
- no `Socket.Available` spin as the primary readiness mechanism;
- optional platform adapters for `recvmmsg`/`sendmmsg` only after portable correctness exists and benchmarks prove value.

The server tick samples completion queues; it does not wait for a socket flush. Backpressure is observed before assembly. Tests inject short sends, would-block, socket close, cancellation, shard shutdown, and queue saturation.

### 6.11 Buffer ownership model

Do not expose copyable `IDisposable` packet structs. Use an ownership state machine centered on generational pool handles:

- producer acquires a slot in `ProducerOwned` state;
- successful queue admission transfers it to `QueueOwned`;
- socket submission transfers it to `SocketOwned`;
- completion or abort returns it to `Free` and increments generation;
- shared immutable message bodies are owned by a cohort/reliable-window record, not by N copied datagram wrappers;
- debug builds validate every transition and poison returned memory;
- release builds retain cheap generation/state assertions behind a switch.

A synchronous `ref struct` writer lease may provide temporary access while building, but it cannot cross an async boundary. Queues carry only a compact `BufferToken`. The creator starts with one real owner; no-subscriber and transform-failure paths release it explicitly.

Required tests:

- creator abandon;
- zero/one/many subscriber fanout;
- partial queue admission;
- transform failure;
- socket submission failure;
- sync and async shutdown;
- copied/stale token use;
- double release;
- generation wrap stress;
- leak check after randomized fault injection.

## 7. Protocol and Codec Design

### 7.1 Generated schemas

Use source generation for production packet/message metadata:

- stable packet/message ID;
- lane and delivery policy;
- minimum/maximum/exact measured size;
- field order, widths, quantization, and bounds;
- version/feature gates;
- encode/decode functions;
- strict validation;
- debug name and metric index;
- protocol schema hash;
- fuzz seed corpus and documentation tables.

Reflection may verify generator output in tests and tooling, but not dispatch live packets.

Generated IDs must be collision-checked at build time. Dynamic script/plugin messages must register through a namespaced manifest with explicit collision failure; hashed identity alone is not enough.

### 7.2 Codec API direction

An illustrative API shape:

```csharp
public interface IMessageCodec<T> where T : struct
{
    static abstract MessageDescriptor Descriptor { get; }
    static abstract int Measure(in T value);
    static abstract bool TryWrite(in T value, ref PacketWriter writer);
    static abstract bool TryRead(ref PacketReader reader, out T value);
}
```

The generator may inline concrete calls so no interface dispatch occurs. Variable-length messages use exact `Measure` followed by one acquire/write, or write into a fixed MTU packet builder with bounded rollback. No `Array.Resize`, exception-driven retry, or speculative heap array is allowed.

### 7.3 Strict canonical parsing

Decode succeeds only if:

- the entire declared message was consumed;
- every enum/flag combination is allowed;
- lengths/counts are within generated bounds;
- strings use valid agreed encoding and normalization policy;
- varints use canonical shortest encoding;
- floats/quantized values reject invalid reserved representations;
- nested payload depth/count remains bounded;
- packet/message type is valid in the current session state and lane.

Unknown extension messages may be skipped only when the enclosing schema declares forward-compatible length framing. Unknown critical control messages reject the connection cleanly.

Fuzzing must target the unauthenticated handshake parser and every authenticated message decoder. Parser functions return structured failure codes and never log raw credentials.

### 7.4 Compact encoding priorities

Optimize in this order:

1. do not send irrelevant entities/scopes;
2. do not send unchanged state;
3. reduce cadence according to perceptual/gameplay need;
4. build/fan out one shared payload per compatible cohort;
5. encode homogeneous batches with shared headers;
6. use change masks and session-local IDs;
7. quantize positions, rotations, velocities, and common scalar ranges;
8. use canonical varints/delta coding for sparse IDs/counts;
9. compress only sufficiently large payloads where measured savings exceed CPU/latency.

Bit-packing tiny fields cannot compensate for a wrong AOI or redundant per-recipient rebuild.

### 7.5 Position and transform encoding

Canonical server position should not use a single enormous float vector. Recommended representation:

- signed 64-bit world chunk coordinate per spatial axis;
- bounded local coordinate within the chunk, preferably fixed-point or a well-defined quantized integer;
- optional vertical band/layer where needed;
- velocity/orientation in schema-specific quantized ranges.

Network messages use a per-connection origin:

- small signed chunk delta from the connection's current origin chunk;
- quantized local position;
- compact change/teleport flags;
- fallback absolute chunk form when outside delta range.

Hot observer batches share origin, scope, tick, baseline generation, and schema ID once. Tests cover chunk edges, negative coordinates, maximum representable deltas, fallback, quantization error, teleport, and origin changes.

### 7.6 Compression

Compression is disabled for tiny realtime/control messages. Candidate use cases:

- terrain/static chunk blobs;
- large UI schemas;
- content manifests/patches;
- baseline fragments with enough redundancy;
- repeated structured bulk batches.

Compression writes into a pre-reserved output slot and declares maximum decompressed bytes. Use per-family dictionaries only if versioning/cache complexity produces a measured win. Never allocate based on an attacker-controlled decompressed size.

Benchmarks compare no compression, fast block compression, and stronger bulk compression on real payload corpora, recording encode/decode CPU, bytes, p95, and pool pressure.

## 8. Session Runtime and Application Admission

### 8.1 State machine

Recommended states:

- `Free`;
- `CookieValidated`;
- `KeyExchange`;
- `ProtocolValidated`;
- `Authenticating`;
- `CharacterSelection`;
- `WorldSync`;
- `Ready`;
- `Draining`;
- `Closed`.

Every state has:

- allowed inbound/outbound message IDs;
- timeout;
- byte/message rate budget;
- owned resources;
- exact entry/exit actions;
- cleanup behavior.

The state machine owns removal from every map/table. Host event draining is observability, not required cleanup. Tests assert table counts immediately after every close path.

### 8.2 Admission and credentials

Keep account/admission policy above the kernel. The kernel supplies an authenticated control stream and opaque credential bytes with strict length/lifetime rules. Prefer challenge/response or token-based flows that avoid retaining reusable secrets. Clear credential material after use.

Admission tests include invalid/expired material, duplicate account policy, reconnect, character ownership, ready-before-auth rejection, rate limiting, and server shutdown during auth.

### 8.3 Keepalive, idle, and loss

Keepalive should be adaptive and inexpensive:

- ordinary traffic proves liveness;
- ping/path probes occur only after quiet intervals or for metrics;
- timeout uses monotonic time and connection state;
- graceful close is reliable within a short drain window;
- crash/loss cleanup is intrinsic and bounded;
- reconnect always establishes a fresh connection/security epoch.

### 8.4 Session table layout

Use struct-of-arrays or hot/cold split storage per shard:

- hot: state, token, packet numbers, ACK window, RTT/RTO, pacing, lane queue indices, last activity;
- warm: subscriptions, cursors, baseline/reliable window indices;
- cold: endpoint diagnostics, account/session opaque keys, disconnect reason, detailed sampled metrics.

Avoid a large reference-rich `Connection` object touched by every packet. Benchmark hot table iteration and false-sharing behavior.

## 9. Replication State Model

### 9.1 Capture contract from ECS/runtime

The ECS/network bridge publishes immutable typed changes at a known barrier. It must not give the network a live component reference.

Recommended generated capture APIs:

```csharp
publisher.PublishLatest(itemId, canonicalScope, version, in value);
publisher.PublishTombstone(itemId, canonicalScope, version, reason);
publisher.PublishEvent(canonicalScope, eventType, in value);
publisher.PublishOwner(ownerId, itemId, version, in value);
```

For maximum performance, generated component extractors write directly into network-owned fixed slots or a preallocated staging ring. A single destructive ECS dirty drain cannot be the only source when multiple policies/consumers exist. ECS Next should provide independent change cursors; until then, the server bridge captures once into a network-owned version table and all network cadences consume from it.

Tests compare captured values and lifecycle facts with a reference world model, including partial buffer drains, multiple consumers, entity destruction, component removal, and rapid cross-chunk movement.

### 9.2 Dense latest-state table

Replace `(item, scope)` entry classes and global sorted snapshots with:

- dense `ReplicationItemSlot` IDs with generation;
- SoA arrays for item ID, schema, canonical scope, current version, payload location, lifecycle state, and dirty epoch;
- per-scope intrusive/dense item lists;
- per-scope changed-slot rings or generation-stamped bitsets;
- tombstone retention with explicit acknowledgement/expiry policy;
- immutable/pinned payload storage or typed inline fixed layouts for common state;
- cached encoded bodies keyed by item version and encoding cohort where reuse wins.

Publication updates the slot and appends a change marker once per dirty epoch. Repeated writes coalesce. Scope movement atomically records leave/tombstone for the old scope and enter/current version for the new scope.

Tests must prove no duplicate/missing changed slot, no old-scope ghost, and zero allocation for repeated updates inside capacity.

### 9.3 Recipient-known state

Do not assume that `sent` means `known`. Per connection/cohort track:

- committed baseline generation;
- acknowledged item/version knowledge where delta encoding depends on it;
- pending newest item version;
- last admitted/sent version for duplicate suppression only;
- last acknowledged transport packet that carried a version;
- tombstone/lifecycle knowledge;
- forced keyframe deadline.

Transport ACK processing promotes item versions to known through a bounded sent-packet receipt map. If maintaining detailed ACK knowledge costs more than it saves for a hot schema, encode independent absolute compact samples instead of dependency deltas. This is a schema-level benchmark decision.

Tests drop arbitrary state datagrams and prove eventual convergence without waiting for a full reconnect.

### 9.4 Latest-state packet semantics

Each latest update declares enough information to reject stale/incompatible state:

- network entity/item ID;
- item version or modular state sequence;
- schema/field group;
- exact baseline generation if baseline-dependent;
- base version if a delta depends on a prior item version;
- change mask and encoded values;
- lifecycle flags where appropriate.

Client apply rules:

- ignore older/duplicate versions;
- apply a dependency delta only if exact base version is known;
- request/trigger repair for a missing base rather than guessing;
- treat compact absolute samples as self-contained;
- process tombstone/leave before slot reuse;
- validate schema and bounds before mutation.

### 9.5 Pending state, cadence, and phasing

Cadence selects when pending work becomes eligible; it does not decide whether a change exists.

For every interested connection or cached cohort:

- a new item version marks pending immediately;
- off-cadence items remain pending;
- when cadence opens and budget admits the item, the newest version is sent;
- if not admitted, it remains pending and gains age/starvation priority;
- a newer version replaces the pending older version;
- spawn, enter, leave, despawn, teleport, owner-critical correction, and required combat changes bypass normal cadence;
- phase selection uses connection/item stable hashes, not a global event sequence that can starve a fixed recipient.

This directly fixes the Current one-shot ring-cadence and recipient-phasing loss bugs. Tests publish exactly one change on every off-phase combination and assert eventual bounded delivery.

### 9.6 Ordered events

Ordered events are used only when intermediate facts matter. Publication stores a canonical event with scope and payload once. On routing to a connection:

- visibility/permission filtering occurs first;
- a contiguous per-connection lane sequence is assigned second;
- the reliable window retains the shared body plus compact recipient sequence metadata;
- ACK/retransmit uses the transport reliability engine;
- falling behind triggers a semantically defined snapshot recovery only for recoverable families.

Examples:

- recoverable by baseline: entity lifecycle/state facts already represented in a snapshot;
- not silently recoverable: chat delivery, commerce result, privileged admin action, or other one-time facts unless the application provides an idempotent ledger/query.

Tests compare each connection's received event list to a visibility-filtered reference list under impairment.

### 9.7 Atomic baseline protocol

One active baseline exists per connection/scope family. It contains:

- baseline ID and state/security epoch;
- scope identity and schema/content generation;
- snapshot cutoff epoch/tick;
- item count and uncompressed byte count;
- fragment count;
- checksum/hash;
- ordered-event sequence floor;
- expiry/retry policy.

Server flow:

1. Acquire or build a snapshot from network-owned state.
2. Freeze its descriptor and cutoff.
3. Queue reliable bulk fragments without starting another generation.
4. Continue capturing newer state; retain/coalesce it as post-cutoff pending state.
5. Validate an exact issued baseline acknowledgement.
6. Mark the baseline committed and release snapshot ownership.
7. Emit pending newer deltas/events.

Client flow:

1. Reserve a bounded staging image after validating descriptor limits.
2. Accept fragments for only the active generation.
3. Validate duplicate consistency, item uniqueness, count, schema, and checksum.
4. Build a complete candidate projection without mutating live state.
5. Atomically swap/commit the candidate at a client update barrier.
6. Reset event/state floors exactly once.
7. Send a reliable acknowledgement.
8. Apply buffered post-cutoff data in order.

Timeout may retransmit missing fragments, restart with a new generation after explicitly cancelling the old one, or disconnect. It never interleaves two live baseline generations.

Tests fault every step and assert either old state remains intact or the new snapshot commits completely.

### 9.8 Snapshot caches and dense mode

Snapshots should be built from replicated state, not live ECS. Reuse is permitted when recipients share:

- canonical scope set;
- visibility/security policy;
- schema/content generation;
- quantization/origin cohort where relevant;
- snapshot cutoff.

For extreme density, far observers may receive periodic scope snapshots plus sparse forced lifecycle/critical deltas instead of every entity update. This is an explicit mode with staleness and byte limits, not accidental backlog.

Cache ownership, invalidation, maximum count/bytes, and reuse hit rate require tests and metrics.

### 9.9 Lifecycle and dormancy

Network entity lifecycle states:

- unknown;
- spawning/baseline pending;
- live;
- dormant but retained;
- leaving AOI;
- despawned/tombstoned;
- slot reusable only after the generation/ack safety condition.

AOI leave differs from world destruction. The client may retain a dormant cache entry for quick re-entry but must remove presentation/physics according to policy. A world destroy invalidates the network entity generation and cannot be undone by a delayed state packet.

Tests include leave/re-enter, destroy while invisible, destroy during baseline, rapid chunk crossing, teleport across the whole window, packet reordering around lifecycle events, and slot reuse.

## 10. Interest Management, Chunks, and Sliding Render Window

### 10.1 Separate stable identity from recipient-relative policy

The canonical publication key is a `ChunkKey`:

- signed 64-bit chunk coordinates;
- optional vertical band/layer/world/shard identity;
- no ring index;
- stable across every observer.

Per-connection `InterestEdge` stores:

- canonical `ChunkKey`;
- priority ring/class;
- cadence and stable phase;
- hysteresis state;
- known chunk manifest/state revision;
- pending item/scope versions;
- enter/leave generation;
- last service tick and starvation/debt;
- reasons/anchors contributing interest.

The same chunk can therefore be Immediate for client A, Near for B, Far for C, and not subscribed for D. Publish the chunk/item change once; schedule it differently.

### 10.2 Initial ring policy

The starting gameplay-shaped policy is:

- Immediate: approximately `7.5-8 m`, full network cadence;
- Near: next configured radius, every `2` network schedule ticks by default;
- Far: outer configured radius, every `3` network schedule ticks by default;
- optional prefetch halo: background chunk manifest/content only.

The existing benchmark profile `7.5,23.5,39.5 m` with cadence `1,2,3` is the initial comparison point, not a permanent world-design law. Combat relevance, ownership, projectile threat, target lock, party, sound, and scripted force-interest can raise priority. Hysteresis avoids boundary flapping.

The ring-0 reduction from roughly `16 m` to roughly `8 m` should remain a named A/B benchmark. In a planar uniform distribution, halving radius reduces enclosed area by roughly `75%`; moving the old outer area to half/third cadence compounds the fanout reduction. Real populations are clustered, so measure rather than extrapolate.

### 10.3 Incremental subscription indexes

Use shard-owned dense tables:

- connection-to-interest edge ranges;
- chunk-to-subscriber edge ranges;
- free-list/generation for edges;
- cached cohorts by compatible scheduling/encoding policy where profitable;
- incremental row/column updates when the center moves one chunk;
- full bounded rebuild for teleport.

Avoid global locks, per-update `HashSet`, and rebuilding every subscribed chunk from scratch. Determine sorted/stable ordering once or use dense slot order where wire determinism does not require sort.

Tests compare incremental movement with a simple full-recompute model across random paths, negative coordinates, ring boundaries, teleports, multiple anchors, and churn.

### 10.4 Multiple interest layers and anchors

Terrain/render, dynamic entities, physics, audio, VFX, chat, and prefetch may share a spatial index but have different policies. Do not force one radius/cadence table onto every family.

Support one player anchor first, then allow a bounded set:

- controlled player;
- camera/spectator/drone;
- party/owned object;
- admin inspection target;
- scripted temporary observation.

The union keeps the highest priority and a reason mask. Tests remove one of overlapping reasons and ensure interest remains until the final reason disappears.

### 10.5 Canonical coordinates and floating origin

Use three coordinate spaces:

1. **Canonical world:** signed 64-bit chunk coordinate plus bounded local fixed/quantized coordinate. This is authoritative for server storage, AOI, persistence mapping, and chunk identity.
2. **Network relative:** chunk delta from the connection's current origin plus quantized local coordinate. This is compact and immune to enormous absolute float magnitude.
3. **Client render/physics:** small floating-point coordinates relative to a floating origin. The local player/camera presentation can remain exactly at or near `(0,0,0)` while the canonical position changes.

The server may use local floating-point physics islands with their own 64-bit canonical origins. A rebase changes the island/render transform, not canonical entity positions one by one.

Tests assert that repeated origin shifts do not alter pairwise distances, canonical positions, AOI membership, collision results within tolerance, or network round-trips.

### 10.6 Fixed toroidal render window

The client render window is a fixed preallocated 2D/3D slot grid. Each `RenderChunkSlot` stores:

- local render-slot coordinate;
- bound canonical `ChunkKey`;
- binding generation;
- current origin-relative transform;
- terrain/static manifest revision and hash;
- dynamic entity container/index;
- load/ready/failure state;
- pooled render, physics, audio, and resource handles;
- async request generation/cancellation token.

When the player crosses a chunk boundary:

- the window origin changes;
- only the exiting edge row/column is unbound;
- those same slots are rebound to entering chunks ahead;
- pooled resources are reset/reused;
- stale asynchronous results are rejected by binding generation;
- a teleport outside incremental range rebinds all slots in bounded batches.

The data structure should be toroidal so normal movement changes indices/origins rather than moving every chunk container. Presentation keeps the player at zero by shifting the window/root transform.

Required tests:

- movement in all directions and diagonals;
- repeated wrap around the toroidal grid;
- negative chunk coordinates;
- exactly one row/column recycled per unit move;
- no duplicate or missing canonical binding;
- stale async terrain result after slot reuse;
- rapid direction reversal;
- teleport/full rebind;
- memory and node/object counts remain constant after warmup;
- no visible/world-coordinate discontinuity at origin shift.

### 10.7 Terrain, statics, and manifests

Keep three related but separate chunk concepts:

- spatial chunk: dynamic membership/AOI/fanout index;
- authoritative terrain/static chunk: versioned world content, collision, LOS, nav, overlays;
- client render chunk: pooled presentation container bound to a canonical chunk.

Frozen map statics are chunk content records, not ECS/network entities by default. Promote doors, containers, destructibles, harvestables, and other stateful objects to entities.

Networking sends:

- world/chunk manifest revisions and hashes;
- missing/outdated chunk requests;
- reliable bulk chunk payloads or deltas;
- static overlay changes;
- dynamic entity lifecycle/state separately.

Client cache and content resolver own disk files and engine resources. The network owns only bounded transfer, integrity metadata, and delivery. Tests cover cache hit, bad hash, revision race, partial transfer, cancellation, and server identity namespace.

### 10.8 Cross-chunk dynamic entities

When an entity moves between chunks:

- canonical item/entity identity remains stable;
- old chunk membership is removed;
- new chunk membership is added;
- subscribers leaving visibility receive a reliable leave/tombstone as policy requires;
- subscribers entering visibility receive spawn/current state or inclusion in a pending baseline;
- subscribers interested in both chunks receive no duplicate spawn/despawn and continue with one item version stream;
- transitions are forced immediately regardless of normal ring cadence;
- fast movement crossing several chunks in one tick processes start/end plus necessary visibility facts without publishing every intermediate chunk unless gameplay requires it.

Property tests compare recipients against a brute-force visibility oracle.

## 11. Scheduling, Assembly, Fanout, and Backpressure

### 11.1 Connection scheduler

Each connection has independent lane cursors and a wire-byte token budget. A scheduling pass:

1. reads already-known output ring capacity;
2. reserves minimum Control/Input/OwnerState service;
3. scores eligible pending work by deadline, service class, age, relevance, and estimated wire cost;
4. selects across lanes without `break`-based head-of-line blocking;
5. reserves output slots/bytes;
6. emits bounded work descriptors;
7. commits selection only after assembly/queue admission succeeds.

An oversized item in one lane cannot prevent a smaller critical item in another lane. Work that cannot fit remains pending or moves to a permitted bulk/fragment path.

Tests exercise every combination of empty/full lane, one-byte-over budget, output ring saturation, partial cohort admission, starvation aging, and disconnect during scheduling.

### 11.2 Wire-cost accounting

Budget estimates include:

- message body;
- lane/message headers;
- per-datagram connection/ACK header share;
- fragment metadata;
- encryption tag;
- expected UDP/IP overhead for reporting;
- retransmit cost tracked separately from new useful bytes.

The packetizer returns actual wire payload cost; estimate error is a metric. Persistent underestimation reduces future admission or fails a test. Bandwidth reports distinguish logical useful bytes from redundancy, ACKs, retransmits, and headers.

### 11.3 Cohort-based shared fanout

Build once for recipients sharing:

- canonical scope/item versions;
- visibility result;
- schema/feature version;
- quantization/origin compatibility;
- baseline/state dependency;
- security/compression policy where body reuse permits it.

Do not require ciphertext reuse: AEAD nonce and connection header are per connection. Reuse the encoded plaintext body or structured item batch, then packetize/encrypt into connection-owned datagram buffers. For very large cohorts, vectorize/copy from the same body and group recipients by transport shard.

The handoff should look like one `FanoutCohort` with a recipient span/range and one body handle, not N managed `OutboundDatagram` instances. Tests cover mixed feature versions, origins, permissions, no subscribers, one subscriber, thousands of subscribers, partial shard failure, and body lifetime.

### 11.4 Overload and degradation

Default degradation order:

1. coalesce superseded latest state;
2. remove disposable VFX;
3. reduce Far cadence and batch more aggressively;
4. skip Far ticks within maximum staleness;
5. reduce Near cadence;
6. switch lower-priority scopes to periodic snapshot mode;
7. pause Bulk;
8. preserve Control/Input/OwnerState and required reliable lifecycle;
9. disconnect a connection whose reliable/control backlog cannot recover within hard limits.

Never silently drop a non-recoverable reliable event. Every action increments a reason counter and exposes oldest pending age.

### 11.5 Work and memory locality

- shard connections by stable hash/slot;
- keep hot connection arrays contiguous;
- keep per-lane queues separate;
- group scope changes by spatial shard;
- use generation-stamped arrays/bitsets instead of clearing large tables each tick;
- use SPSC rings for fixed producer/consumer paths;
- use per-worker scratch and reductions;
- pad shard counters/queues to avoid false sharing where counters prove it matters;
- prefetch/vectorize only after disassembly/hardware-counter evidence.

Parallelism is not automatically faster. Start single-threaded with the final ownership boundaries, then add coarse shards. At each worker count, compare p95/p99/max, CPU total, allocations, cache misses, and synchronization. Two workers were historically a better knee than four in an older replication build; tests may reveal a different point on current hardware.

### 11.6 Transform pipeline

The explicit transform seam becomes an implemented ordered pipeline:

1. message encode/batch;
2. optional compression for eligible payloads;
3. application segmentation/fragment metadata;
4. datagram packetization/ACK insertion;
5. authenticated encryption;
6. socket queue admission.

Each stage writes from an owned input view to an already reserved output slot or returns a structured failure without cursor advancement. Transform choice becomes part of the cohort/cache key where it changes reusable bytes.

Tests inject failure/capacity at each transform boundary and assert ownership, flags, and cursor state.

## 12. Client Runtime

### 12.1 Input send and expiry

Retain Current's useful immediate start/change/stop/edge behavior and held-input heartbeat, but define the wire contract around expiring intent:

- each input has sequence, client tick/time, duration/expiry, and action flags;
- packets include the newest input plus a small configurable redundancy window of recent inputs;
- server accepts only a bounded sequence/time window;
- duplicate inputs are idempotent;
- absence beyond expiry returns continuous movement intent to neutral unless a gameplay system explicitly holds it;
- stop/edge changes send immediately;
- rate and magnitude validation happen before enqueue;
- input acknowledgements ride every eligible owner correction even when position correction is within deadband.

This prevents a lost stop packet from producing permanent movement. Tests drop every individual packet in a start/hold/stop sequence and assert eventual correct intent.

### 12.2 Prediction and reconciliation

Keep prediction/reconciliation engine-neutral. Required state:

- bounded input history keyed by sequence;
- predicted owner snapshots sufficient for replay;
- last authoritative input acknowledgement;
- authoritative owner state and state tick;
- reconciliation threshold by field/system;
- teleport/hard-snap marker;
- correction statistics and cause.

Apply flow:

1. validate state sequence/session/entity generation;
2. remove acknowledged input history;
3. compare the authoritative anchor with the saved prediction at that input;
4. if within tolerance, retain smooth prediction while still accepting non-position authoritative fields;
5. otherwise rewind the client projected state, reapply remaining inputs, and expose a presentation smoothing offset;
6. never mutate server authority or bypass the client ECS barrier.

Deadband may suppress redundant position bytes, but not input ACK, velocity mode, grounded state, combat state, teleport, or other independently changed fields. Tests model latency/loss/jitter and compare the final client state with an authoritative reference.

### 12.3 Observer interpolation

Use dense generational observer slots with fixed-capacity sample rings. Policy includes:

- interpolation delay derived from observed jitter within bounds;
- sample tick/time and state sequence;
- limited extrapolation duration;
- teleport/snap threshold;
- dormancy and stale timeout;
- out-of-order/duplicate rejection;
- per-schema interpolation rules;
- presentation-only smoothing separate from canonical local state.

Tests cover sparse near/far cadence, burst delivery, loss, reorder, origin shift, dormancy, teleport, slot reuse, and steady-state allocation.

### 12.4 Client state apply barrier

Network workers decode into bounded client command/state rings. The client runtime applies them at a defined barrier:

- lifecycle before dependent state;
- baseline commit as one transaction;
- state versions in canonical order where required;
- UI/chat application through their own services;
- presentation notification after data is consistent.

No socket callback creates/destroys Godot nodes or mutates ECS storage directly. Tests assert thread ownership and deterministic apply order.

### 12.5 Render-window integration

The client network bridge owns the mapping from canonical `ChunkKey`/`NetworkEntityId` to client runtime slots. The Godot bridge owns render/physics resources. On window shift:

- network subscriptions/origin change in canonical coordinates;
- existing network entities retain identity;
- render slots rebind by generation;
- local render transforms are recomputed relative to the new origin in batch;
- stale loads and stale state for recycled slots are rejected;
- the player presentation remains at zero while camera/world roots shift.

Benchmark origin shift separately from network receive so an expensive engine-side rebase is not misattributed to protocol decode.

## 13. Server Bridges and Application Protocols

### 13.1 ECS bridge

The Server/Network bridge should be generated from replication descriptors attached to components/field groups:

- network item family and schema;
- canonical key construction;
- scope classifier;
- owner/observer visibility;
- lane/delivery;
- quantization and change thresholds;
- forced-change rules;
- baseline inclusion;
- persistence/content generation compatibility;
- maximum encoded size.

Generated capture avoids delegates, reflection, boxing, JSON, and repeated metadata lookup. Rare dynamic script-defined schemas may use a slower cold path but must register a bounded generated-equivalent descriptor before world start/content generation publication.

The bridge consumes ECS Next independent change cursors and lifecycle tombstones when available. Until then it owns one non-destructive captured version table. Tests compare Current and Next publication for the same scripted ECS trace.

### 13.2 Movement and authority

Preserve:

- input validation;
- burst coalescing to newest valid intent;
- authoritative ECS apply;
- owner correction;
- observer movement publication;
- correction deadband/max-silent policy;
- entity/network identity mapping.

Improve:

- expiring intent and redundant input history;
- explicit server tick/input ACK in owner state;
- schema-defined quantized compact transform;
- force correction for mode changes/teleport/large divergence;
- separate owner and observer encodings;
- no global observer scope in production acceptance profiles;
- no allocation/delegate call per entity/input.

Tests include speed hacks, NaN/infinity, invalid action flags, stale/future sequence, packet duplication, stop loss, teleport, map collision correction, and owner entity despawn.

### 13.3 Chat and commands

Preserve local/system/channel chat, command prefix routing, access metadata, and server-authored chat events. Next adds:

- authenticated session-only submission;
- generated UTF-8 byte and Unicode scalar limits;
- normalization/control-character policy;
- token-bucket rate limits per account/session/channel;
- bounded recent-state table with cleanup on disconnect;
- server-side recipient resolution through interest/group indexes;
- reliable delivery where gameplay requires it;
- optional dense-recipient/rate caps with explicit semantics and metrics;
- message/event IDs for idempotent client display if retransmitted;
- no string allocation in the kernel parser; decode to a bounded UTF-8 view and allocate only in the application/UI layer if needed.

Tests cover malformed UTF-8, limit boundaries, spam, permission changes, disconnect cleanup, local visibility, duplicate retransmit, and command/chat ambiguity.

### 13.4 Server-driven UI

Preserve the RunUO-style server-driven UI shape:

- panel open with prefab/schema identity;
- hot cached schema path;
- cold chunked document path;
- model update/full and scoped patches;
- validated action requests;
- panel instance/supersession sequencing;
- context/radial and later inventory/admin surfaces.

Next treats UI documents as bounded bulk transfers:

- maximum open panels per connection;
- maximum active transfers, document bytes, chunk count, chunk bytes, and model bytes;
- transfer deadline and cancellation;
- generational transfer ID;
- exact schema/content/server identity and hash;
- reliable isolated bulk lane;
- pooled byte views and streaming validation;
- action rate/permission/instance checks;
- no UI bulk traffic in realtime/control reserved bytes.

Tests attack every limit, reorder/duplicate chunks, supersede while loading, disconnect mid-transfer, stale action, cache mismatch, and malicious nesting.

### 13.5 Admission/accounting/character flow

Keep policy in `Server/Admission` and `Server/Accounting`; network provides secure messages and lifecycle. Preserve login panel, credential submit, account resolution, character selection, selected-character session, spawn/bind, and clean unbind.

Identity mapping must remain explicit:

```text
account/character durable ID
        -> authenticated session binding
        -> ECS runtime entity
        -> NetworkEntityId per connection/world epoch
        -> client projected entity slot
```

Tests prove no raw ECS index, connection slot, or client-supplied entity ID can cross an authorization boundary.

### 13.6 Presentation and content

Entity presentation messages send stable logical content IDs and revision/hue/material data, not Godot resource paths. Client content packs and cache layers resolve them.

World/content transfer protocol should support:

- server identity and manifest generation;
- required/optional pack advertisement;
- file/chunk size and hash;
- missing-range request;
- resume/cancel;
- separate download URL metadata when policy allows;
- strict executable-content/trust policy above the kernel;
- Bulk lane or out-of-band HTTP comparison.

Large content should usually use cacheable hash-addressed transfer outside the latency-sensitive socket if that is faster and operationally simpler. Benchmark and test both paths; do not force all assets through gameplay UDP.

### 13.7 Operator and inspection traffic

Status, metrics, system snapshots, and entity inspection are valuable Current features but are cold/privileged traffic. Next requirements:

- separate admin authorization and rate limit;
- sampled/batched snapshots;
- schema-generated binary fields or a cold JSON path isolated from gameplay workers;
- bounded query/result sizes;
- cancellation/deadline;
- audit event;
- no unbounded per-connection metric labels.

Tests run admin inspection under gameplay load and assert protected-lane latency/allocation remains within gate.

### 13.8 Prefab and archetype replication contract

The RunUO-style prefab authoring model remains above the network:

- a broad script/base class describes the wide ECS archetype and its replication field groups;
- a concrete spawnable prefab selects that archetype and fills component values;
- spawning produces a normal ECS entity, not a parallel network object or long-lived OOP instance;
- generated archetype/component replication descriptors determine which state enters owner, observer, event, and bulk families;
- the initial spawn/baseline contains the concrete prefab's resulting values, not serialized C# class state;
- many concrete prefabs sharing a wide archetype can share codec, batch, baseline-layout, and client apply paths;
- the server-only canonical prefab key remains available for persistence/admin/script provenance, while the client receives only presentation/content IDs and gameplay state it is authorized to know;
- a prefab/content revision that affects the client's schema or presentation is versioned explicitly; an implementation type name is never the wire contract.

This preserves the desired “entities/components underneath, simple item/mobile scripts on the surface” experience while keeping fanout data-oriented. Tests spawn multiple inherited prefab definitions into the same archetype, compare their initial and incremental replication, verify unauthorized server-only fields never appear, and benchmark mixed concrete prefabs through one shared generated batch codec.

### 13.9 Persistence, restart, and identity rebinding

Runtime network identities are not persisted. Persistence stores durable world/character/content identity and component references according to the ECS/server persistence design. On load:

- the persistence layer maps old saved entity indices/keys to newly allocated ECS entities during the transactional restore;
- server bridges rebuild runtime entity-to-network projection maps from the committed world;
- existing network sessions, if a live reload is supported, receive a new world/state epoch and atomic resynchronization baseline;
- a full process restart normally establishes fresh connections, security epochs, and `NetworkEntityId` mappings;
- delayed packets from the pre-load/pre-restart epoch cannot resolve against the new world;
- durable character/account/inventory references are resolved before a session is rebound to its new runtime entity.

This keeps the efficient temporary old-to-new ID remap inside load and avoids turning `NetworkEntityId` into another persistence layer. Tests save/load worlds with cross-entity references, reconnect characters, inject stale pre-load traffic, and verify every client projection is rebuilt from fresh IDs.

## 14. API Direction

These sketches communicate ownership and flow. Exact names are not compatibility requirements.

### 14.1 Host lifecycle

```csharp
public interface INetworkRuntime : IDisposable
{
    void Start(in NetworkStartOptions options);
    NetworkPumpResult PumpInbound(long nowTimestamp, ulong simulationTick);
    NetworkScheduleResult Schedule(long nowTimestamp, ulong simulationTick);
    NetworkPumpResult PumpCompletions(long nowTimestamp);
    void Stop(NetworkStopMode mode, long deadlineTimestamp);
}
```

No API promises that `Schedule` flushes the socket synchronously. Tests validate state transitions and shutdown ownership.

### 14.2 Portable server/client host and wrapper control surfaces

The Network kernel does not own server/client process topology, but Next integrations require one portable host contract that every wrapper composes. An illustrative server shape is:

```csharp
public interface IServerApplicationHost : IDisposable
{
    ServerLifecycleState State { get; }
    ServerReadinessSnapshot Readiness { get; }
    IServerControlSurface Control { get; }

    void Initialize(in ServerStartOptions options);
    void Start();
    ServerHostPumpResult Pump(long nowTimestamp);
    void Stop(ServerStopMode mode, long deadlineTimestamp);
}

public interface IServerControlSurface
{
    ServerCommandResult Enqueue(in ServerControlCommand command);
    ServerStatusSnapshot GetStatus();
    ServerMetricsSnapshot GetMetrics();
    int DrainEvents(Span<ServerApplicationEvent> destination);
}
```

`_Console`, `_Headless`, and server `_Godot` construct the same `IServerApplicationHost`. Console text and Godot button actions become the same typed `ServerControlCommand`. Reads use immutable/bounded snapshots and events. None of these APIs returns live ECS, session-table, or transport internals.

The client uses an equivalent engine-neutral `IClientApplicationHost` for session lifecycle, neutral input samples, projected-state apply, content/UI state, and status. Client `_Godot` supplies device input and consumes presentation views; it does not own a second protocol or prediction implementation.

Local single-player startup belongs in wrapper/hosting composition, not the Network kernel. Its conceptual contract returns explicit readiness and lifetime ownership:

```csharp
public interface ILocalServerLauncher
{
    LocalServerLaunchResult Start(in LocalServerStartOptions options);
}

public interface ILocalServerHandle : IDisposable
{
    LocalServerState State { get; }
    NetworkEndpoint ReadyEndpoint { get; }
    LocalServerFailure Failure { get; }
    void Stop(ServerStopMode mode, long deadlineTimestamp);
}
```

The client waits for `ReadyEndpoint`, then passes it to the normal `Client/Network` session driver. Whether the handle owns an in-process runtime or child process is a wrapper policy recorded in configuration/metrics; it does not change the network or ECS contract.

Tests use fake clocks/process adapters for lifecycle failures and real localhost integration for production-equivalence. API tests prove every wrapper reaches the same control surface and cannot obtain a direct server-world mutation reference.

### 14.3 Transport I/O

```csharp
public interface IDatagramIo : IDisposable
{
    int DrainReceived(Span<ReceivedPacketSlot> destination);
    int Submit(ReadOnlySpan<SendPacketSlot> source);
    int DrainCompletions(Span<SendCompletion> destination);
    DatagramIoSnapshot Snapshot();
}
```

Slots contain generational `BufferToken`s and compact metadata. Submission either transfers ownership for an exact prefix or reports no transfer. Tests cover partial-prefix semantics.

### 14.4 Publication

```csharp
public readonly ref struct NetworkPublisher
{
    public PublishResult Latest<T>(
        ReplicationItemId item,
        ChunkKey scope,
        ulong version,
        in T value) where T : unmanaged;

    public PublishResult Tombstone(
        ReplicationItemId item,
        ChunkKey previousScope,
        ulong version,
        LifecycleReason reason);

    public PublishResult Event<T>(
        ChunkKey scope,
        EventTypeId type,
        in T value) where T : unmanaged;
}
```

Generated specialized methods may replace generic dispatch on hot paths. Publication returns capacity/coalescing status; callers/tests cannot assume success silently.

### 14.5 Interest updates

```csharp
public readonly record struct InterestAnchor(
    InterestAnchorId Id,
    CanonicalPosition Position,
    InterestPolicyId Policy,
    InterestReasonMask Reasons);

public interface IInterestRuntime
{
    InterestUpdateResult SetAnchors(
        ConnectionId connection,
        ReadOnlySpan<InterestAnchor> anchors,
        ulong tick);
}
```

Higher-layer visibility filters can append/remove forced interest without teaching the kernel party/stealth rules.

### 14.6 Scheduling

```csharp
public readonly record struct ConnectionBudget(
    int WireBytes,
    int MessageItems,
    int DatagramSlots,
    long DeadlineTimestamp);

public interface IConnectionScheduler
{
    ScheduleResult TrySchedule(
        ConnectionId connection,
        in ConnectionBudget budget,
        Span<ScheduledBatch> output);
}
```

`ScheduledBatch` is a reservation-backed descriptor. `Commit` follows successful assembly/queue transfer; `Cancel` leaves pending versions/cursors intact.

### 14.7 Baseline and recovery

```csharp
public interface IBaselineRuntime
{
    BaselineStartResult TryStart(ConnectionId connection, ScopeSetId scopes);
    BaselineAckResult Acknowledge(ConnectionId connection, BaselineId id, Hash128 hash);
    RecoveryDecision Evaluate(ConnectionId connection, long nowTimestamp);
}
```

The API refuses a second active baseline unless the first is explicitly cancelled. Tests assert stale/unissued ACK rejection.

### 14.8 Client apply

```csharp
public interface IClientNetworkApplySink
{
    ApplyResult Spawn(in SpawnView value);
    ApplyResult Latest(in LatestStateView value);
    ApplyResult Event(in ReliableEventView value);
    ApplyResult Leave(in LeaveView value);
    ApplyResult Despawn(in DespawnView value);
    ApplyResult CommitBaseline(in BaselineImageView value);
}
```

Views are consume-window-only. Retention requires an explicit copy into client-owned preallocated storage. Tests poison views after the barrier.

### 14.9 Metrics and tracing

```csharp
public interface INetworkMetricsSink
{
    void Add(NetworkCounterId id, long value);
    void Observe(NetworkHistogramId id, long value);
}
```

Generated numeric IDs avoid string/hash work. Detailed per-connection traces use a bounded sampled ring activated by operator policy.

## 15. Observability

### 15.1 Always-on low-cost metrics

Per server/shard:

- active/unverified/ready/draining connections;
- received/sent/auth-failed/dropped datagrams;
- application/protocol/UDP/estimated-wire bytes;
- packets/messages by lane;
- ACK and retransmit bytes;
- RTT/loss/reorder summaries;
- queue depth/high-water and oldest age by lane;
- pool occupancy/high-water/failure by size class;
- publish/schedule/assemble/encrypt/send-completion time;
- useful item/event counts;
- coalesced/deferred/dropped/snapshot-recovered counts;
- subscription edges, changes, active chunks, density;
- baseline/fragment/transfer count and bytes;
- disconnect reason counts;
- allocation and GC deltas in benchmark/dev profiles.

### 15.2 Sampled detailed metrics

Per sampled/top-N connection:

- send rate by lane;
- pending age/debt;
- RTT/RTO/loss and pacing rate;
- subscribed scopes by class;
- baseline/recovery state;
- reliable window occupancy;
- recent protocol strikes;
- client apply/interpolation age where reported.

Do not export raw user IDs as unbounded metric labels. Use logs/traces with access control for individual diagnosis.

### 15.3 Stage timing policy

Profiling every packet with `Stopwatch.GetTimestamp` can become a hot cost. Sample one timestamp per batch/stage and support three modes:

- production core: counters plus coarse sampled stage histograms;
- diagnostic: detailed substage timing for selected shards/connections;
- benchmark: full timing and allocation instrumentation.

Benchmark the instrumentation itself and report whether it is enabled.

### 15.4 Trace recorder/replayer

A bounded binary trace records:

- configuration/schema hashes;
- accepted decoded inputs/control events;
- canonical publication and lifecycle events;
- interest anchor changes;
- manual clock advances;
- impairment decisions;
- expected connection-visible outputs or their semantic hashes.

It must support replay against Legacy, Current, and Next adapters where semantics overlap. Sensitive payloads are redacted or replaced with hashes. Tests verify trace determinism and versioning.

## 16. Proposed Tooling

### 16.1 Protocol/source generator

Outputs:

- message IDs and collision diagnostics;
- codecs and exact size functions;
- lane/security/state-machine tables;
- schema/content hash;
- docs tables;
- max-size/capacity contributions;
- Wireshark/dissector metadata if practical;
- fuzz corpus seeds;
- Current-to-Next trace adapters for retained messages.

Generator tests compare emitted codecs with a reflection/reference codec and snapshot the manifest.

### 16.2 Capacity and bandwidth planner

Inputs:

- CCU;
- tick/send rates;
- active movement ratio;
- AOI density distribution;
- rings/cadence;
- state schema sizes/change rates;
- event rates;
- baseline/chunk/content sizes;
- loss/retransmit assumptions;
- socket/pool/shard configuration.

Outputs:

- expected useful/protocol/wire bytes per connection and total;
- packet/datagram rate;
- connection/scope/subscription/state/replay memory;
- pool sizes and high-water safety margin;
- estimated CPU work units;
- warnings when the target cannot fit the `4 ms`/bandwidth envelope.

Compare planner estimates with every benchmark report and track estimation error.

### 16.3 Packet inspector and protocol dissector

Provide a development-only inspector that can decode authorized captured traffic using supplied session keys, show lane/message/size/version fields, and compute byte contribution. It must never log credentials by default. A Wireshark dissector or exported schema is useful for independent wire validation.

### 16.4 Deterministic link simulator

The simulator operates between packetization and receive:

- fixed/random delay;
- jitter distributions;
- loss and burst loss;
- duplication;
- reorder;
- bandwidth cap and queue discipline;
- MTU/drop-too-large;
- path change;
- corruption/auth failure;
- clock advance.

Every reliability, baseline, lifecycle, input, and session test should use it rather than bespoke packet shuffling.

### 16.5 Semantic oracle

Given canonical publications, interest traces, lane policy, and impairments, the oracle computes:

- required eventually delivered events;
- latest expected state by deadline/staleness class;
- allowed drops/coalesces;
- lifecycle visibility;
- baseline convergence;
- byte/message accounting.

Harness runs fail on semantic mismatch even when p95 is excellent.

### 16.6 Benchmark comparison reporter

Join Legacy/Current/Next JSON by:

- stable scenario ID;
- semantic/profile hash;
- protocol/security options;
- machine/runtime/build metadata;
- code revision;
- trace seed.

Report absolute and percentage deltas for:

- p50/p95/p99/max stage and end-to-end latency;
- useful/app/protocol/wire bytes;
- datagrams and retransmits;
- allocation/GC/retained memory;
- CPU time and cycles where available;
- semantic result hash;
- connection/session cleanup;
- pool/queue high-water.

Refuse a headline comparison when semantic/profile hashes differ.

### 16.7 Pool/ownership verifier

Development builds should expose:

- live tokens grouped by owner state and acquisition stack/site ID;
- generation mismatch/double release;
- oldest outstanding token;
- per-path acquire/release balance;
- poison validation;
- shutdown assertion that all non-reserved slots returned.

Run randomized failure-injection tests through this verifier.

### 16.8 Geometry and AOI visualizer

Render or dump:

- canonical chunks and connection anchors;
- Immediate/Near/Far/prefetch edges;
- subscriber density;
- pending versions and service age;
- render-window slot-to-world binding;
- origin shifts;
- cross-chunk entity transitions.

The tool should replay benchmark geometry seeds and export machine-readable distribution stats so a surprising density result can be reproduced.

## 17. Test Strategy

### 17.1 Test layers

1. **Unit tests** for sequence arithmetic, codecs, tables, pools, budgets, transforms, and policies.
2. **Property/model tests** comparing optimized structures with simple reference models.
3. **Fuzz tests** for handshake, authenticated decode, fragments, state machines, and generators.
4. **Deterministic impairment tests** for delivery, convergence, recovery, and timeout.
5. **Integration tests** for loopback and real UDP client/server paths.
6. **Cross-generation semantic tests** using the same trace and output oracle.
7. **Allocation tests** after warmup for every hot stage and vertical slice.
8. **Microbenchmarks** for isolated algorithms after correctness.
9. **Load/ramp tests** with separate processes and client apply.
10. **Soak/chaos tests** for leaks, cleanup, wrap, loss, and overload.
11. **Launch smokes** for headless/console/Godot hosts.
12. **Security tests** for spoofing, replay, amplification, malformed input, and rate limits.

Tests land in the same phase as the feature. A phase is not done because an API compiles.

### 17.2 Current test baseline and immediate gaps

Preserve the `257` freshly passing Network/Client/Server tests as a starting behavioral inventory. Before Next Phase 1, add Current regression tests that expose known faults where feasible:

- channel metadata does not imply UDP reliability;
- stale/unissued baseline ACK;
- two baseline generations interleaving;
- partial output admission cursor loss;
- no-subscriber fanout ownership leak;
- copy/double-dispose datagram hazard;
- old chunk ghost after movement;
- one-shot off-cadence loss;
- recipient phasing starvation;
- latest sent-but-lost state knowledge;
- event ACK beyond issued sequence;
- retransmit deadline update;
- unknown endpoint table growth;
- UI transfer limits;
- session manager cleanup without host drain.

These can be expected-failing Current characterizations while Next implements the corrected behavior. Do not “fix” the baseline solely to make comparison convenient.

### 17.3 Codec tests

For every generated message:

- minimum, maximum, and typical round trip;
- exact measured size equals bytes written;
- one-byte truncation at every boundary;
- trailing byte rejection;
- invalid enum/flags;
- max-minus-one/max/max-plus-one lengths;
- canonical varint enforcement;
- random valid values;
- random bytes/fuzz corpus;
- cross-version compatibility matrix;
- no allocation after warmup;
- endian/reference vectors;
- sensitive-field redaction.

### 17.4 Transport/protocol tests

- cookie/handshake state model;
- AEAD vectors, tamper, replay, and key rotation;
- packet ACK model/property tests;
- RTT/RTO calculations with manual clock;
- reliable ordered/unordered delivery under impairment;
- unreliable sequenced newest-only behavior;
- input redundancy/expiry;
- fragmentation/reassembly bounds;
- MTU exact-fit and oversize;
- congestion/pacing fairness;
- endpoint migration/path validation;
- shutdown and socket error ownership;
- connection/sequence/epoch wrap;
- pre-auth flood capacity and anti-amplification.

### 17.5 Replication tests

- repeated latest writes coalesce;
- changed-scope rings contain every required slot exactly once;
- pending version survives cadence and budget denial;
- latest version supersedes older pending version;
- transport ACK promotes exact known version;
- lost state converges;
- dependency delta rejects missing base;
- tombstone/despawn defeats delayed state;
- filtered event sequence remains contiguous per connection;
- event retransmit does not duplicate apply;
- retention overflow chooses correct recovery/disconnect;
- baseline stages and commits atomically;
- stale/incorrect baseline ACK rejects;
- post-cutoff delta/event order;
- snapshot cache isolation across visibility/schema/origin cohorts;
- dense snapshot mode staleness bound;
- scope movement has no ghost/duplicate.

### 17.6 AOI/render-window tests

- floor division and local coordinate normalization for negative values;
- ring overlap/hysteresis versus brute-force geometry;
- incremental subscription update versus full recompute;
- all eight planar movement directions;
- multiple anchors and reason-mask removal;
- forced interest/priority override;
- ring cadence `1,2,3` delivery deadline;
- one-shot updates on every phase;
- cross-chunk fast movement/teleport;
- toroidal slot mapping and stable memory;
- stale asynchronous result rejection;
- canonical/network/render coordinate round trip;
- origin shift invariants and player-at-zero presentation;
- terrain manifest/cache/revision transitions.

### 17.7 Application and host tests

- engine-agnostic server/client project-reference and forbidden-namespace checks;
- identical server lifecycle/control/world/network semantics through `_Console`, `_Headless`, and server `_Godot` wrappers;
- `_Console` command input and `_Godot` buttons route through the same typed control surface;
- single-player `_Headless` readiness, localhost auto-connect, ordinary admission/world entry, save, disconnect, and shutdown;
- proof that single-player cannot bypass serialization, session state, authority, ECS isolation, or client apply barriers;
- admission/account/character authorization and identity mapping;
- chat limits, permission, locality, cleanup, and deduplication;
- UI transfer bounds, cache hot/cold path, supersession, and action validation;
- movement exploit validation, stop loss, correction, and despawn;
- operator/inspection isolation from gameplay lanes;
- server shutdown graceful drain and deadline abort;
- client disconnect/reconnect with stale packet injection;
- Godot render-window and presentation smoke;
- headless multi-session cleanup;
- content/chunk transfer cancellation and bad hash;
- server persistence/load rebind to fresh runtime/network IDs.

### 17.8 Test-driven course correction

For each structural choice, record:

- hypothesis;
- protected correctness tests;
- representative benchmark scenarios;
- expected improvement and possible regression;
- observed result with artifact;
- keep/revise/reject decision.

Examples that must remain empirical:

- custom UDP versus a QUIC/fallback prototype;
- native slabs versus pinned managed arrays;
- dense arrays versus hash tables for a particular scope shape;
- ACK-detailed state deltas versus compact absolute samples;
- batch size and cohort cache threshold;
- connection/transport shard count;
- ring radius/cadence;
- snapshot mode threshold;
- compression algorithm/dictionary;
- `sendmmsg/recvmmsg` adapter;
- SIMD/unsafe packet copy.

Tests-as-we-go may point in a different direction. The protected semantics remain; the implementation is allowed to change.

## 18. Benchmark Strategy

### 18.1 Benchmark rules

- Fix the current benchmark and bot-harness builds before recording a new baseline.
- Separate setup, connection establishment, pool growth, and warmup from steady-state measurement.
- Preserve a separately named cold-start/join-storm scenario instead of hiding cold cost.
- Run Release without debugger and record runtime, OS, CPU, core topology, frequency policy, memory, NIC, build revision, config, and instrumentation mode.
- Pin or record process affinity and isolate competing work where practical.
- Use multiple independent process runs, not only iterations inside one process.
- Report median and confidence/noise information alongside p95/p99/max.
- Record semantic/profile hash and require the oracle to pass.
- Dispose/drain every output and assert pools are balanced after each iteration.
- Do not force GC inside a timed iteration. Report naturally occurring collections.
- Measure allocation separately for server, each client shard, and every network stage.
- Record useful bytes, protocol bytes, UDP bytes, estimated wire bytes, and retransmit bytes.
- Include encryption/authentication in production-equivalence profiles.
- Include client decode/apply and session cleanup in end-to-end profiles.
- Run dense, spread, mixed, clustered-multi-hotspot, traversal, and teleport geometry.
- Use randomized but recorded active-client and geometry seeds.
- Separate simulation tick rate from input/send cadence.
- Never compare a harness path using `AllowSequenceGaps` with a real reliable runtime as if equivalent.

### 18.2 Current tracked microbenchmark snapshot

The tracked `_Benchmarks/results/network-benchmarks.json` was produced on .NET `8.0.25`, Release, `16` logical processors, timestamp `2026-04-28T01:51:33Z`. Selected historical results:

| Scenario | Mean ms | Mean allocated bytes | Interpretation |
| --- | ---: | ---: | --- |
| Shared fanout, 64 frames x 128 subscribers | 2.440 | 1,222,872 | Faster and about one-third the allocation of naive in this scale |
| Naive fanout, 64 x 128 | 3.511 | 3,757,736 | Comparison baseline |
| Shared fanout, 256 x 512 | 25.073 | 20,913,152 | Large allocation win, no mean-time win in this run |
| Naive fanout, 256 x 512 | 24.875 | 61,723,600 | Demonstrates why both CPU and allocation must be reported |
| Schedule and fanout, 64 x 128 | 0.576 | 1,233,792 | Useful seam signal, still alloc-heavy |
| Schedule and fanout, 256 x 512 | 6.534 | 20,958,280 | Scaling reference |
| Server runtime publish/fanout, 2048 subscribers | 2.989 | 4,063,640 | Narrow operator/status-style slice |
| ECS dirty publish, 1024 entities | 1.014 | 488,136 | Capture-only reference |
| ECS dirty publish/fanout, 1024 x 128 | 4.464 | 3,298,608 | Bridge plus fanout |
| AOI-classified 1024 RT + 256 event + 128 bulk, 128 conns | 5.837 | 11,392,776 | Multi-family synthetic path |
| Client movement loopback, 1024 inputs | 1.636 | 1,108,416 | Client bridge reference |
| Server movement loopback, 1024 inputs | 3.105 | 3,531,336 | Paired server reference |
| UDP movement round trip, 1024 inputs | 15.059 | 4,388,648 | Real socket aggregate scenario |
| UDP mini ramp, 32 clients x 240 ticks | 118.577 | 9,926,848 | Aggregate run, not per-tick p95 |
| AOI update, 512 conns x 120 ticks | 55.278 | 2,832,672 | Subscription maintenance reference |
| AOI update, 2048 conns x 120 ticks | 240.560 | 11,005,448 | Subscription scaling reference |

These numbers are historical only until the runner builds and the methodology is corrected. Every entry is marked `Comparable: false`, uses only five measured iterations after one warmup, and its forced-GC counts are not a natural runtime collection signal.

### 18.3 Current tracked sharded snapshots

The April 28 sharded artifacts provide stronger real-socket context.

Dense, four observer families, 60-second stages:

| Target CCU | Server tick p95 ms | Server alloc | Client alloc sum | Notes |
| ---: | ---: | ---: | ---: | --- |
| 256 | 2.350 | 0.564 GB | 1.720 GB | four families, client apply |
| 300 | 2.913 | 0.739 GB | 2.277 GB | inside 4 ms p95 |
| 350 | 4.018 | 0.911 GB | 2.869 GB | approximately the dense 4 ms edge |

Dense, single observer family, 60-second stages:

| Target CCU | Server tick p95 ms | Server alloc | Client alloc sum |
| ---: | ---: | ---: | ---: |
| 256 | 1.085 | 0.191 GB | 0.649 GB |
| 300 | 1.339 | 0.244 GB | 1.073 GB |
| 350 | 1.664 | 0.292 GB | 1.328 GB |

Spread/single-family staged profile:

| Target CCU | Server tick p95 ms | Server alloc | Client alloc sum | Ping p95 max ms |
| ---: | ---: | ---: | ---: | ---: |
| 1024 | 3.048 | 0.712 GB | 0.532 GB | 16.471 |
| 1280 | 3.956 | 0.934 GB | 0.566 GB | 23.485 |
| 1536 | 4.747 | 1.181 GB | 0.749 GB | 32.797 |
| 2048 | 6.224 | 1.653 GB | 0.920 GB | 32.382 |
| 2560 | 8.049 | 2.125 GB | 1.263 GB | 32.616 |
| 3072 | 9.883 | 2.624 GB | 1.803 GB | 31.412 |

The 120-second single-family `3072` soak reported:

- server tick avg `8.007 ms`, p95 `11.331 ms`, p99 `14.116 ms`, max `116.175 ms`;
- server allocation `6.184 GB`, about `51.5 MB/s`;
- server collections `127/35/6` for Gen0/1/2;
- client allocation sum `5.161 GB`, about `43 MB/s`;
- client collections `109/44/13`;
- ping p95 max `33.067 ms`;
- ready/active clients exactly `3072` in this isolated run.

This is strong evidence that Current's next generational target is allocation and ownership, not merely another cadence tweak.

Historical A/B observations to preserve as named experiments:

- exact frame buffer sizing at `3072` reduced server allocation from about `2.589 GB` to `1.556 GB` in compared short runs, roughly `40%`, while p95 remained about `9.72 ms`;
- at dense `300`, the same change barely moved allocation and p95 worsened from `2.635` to `2.930 ms` in single runs;
- an async-flush `1024` run reported p95 `3.339 ms` versus sync `6.646 ms`, but this is not conclusive without repeated controlled A/B;
- realtime batching did not produce a stable default-worthy win;
- the persistent `3072` stability run is invalid for capacity conclusions because ready-session counts grew across stages.

### 18.4 Historical Legacy baselines

Historical old-system documents record useful evolution, not directly comparable Current results:

- pre-AOI island slice knees around `56-80` CCU depending on activity, with failure around `64-96`;
- later AOI/UDP/selector work moved mixed and hotspot envelopes substantially;
- the March 23 old baseline reports `1024-2560` realistic-profile stages passing a `50 ms` ping p95 SLO, with tick averages from `4.42` to `14.35 ms`;
- at `2048`, that baseline reported about `9.59 ms` average tick and `47.75 ms` ping p95;
- priority LOD, dense cadence, observer cadence `3`, and two build workers were the accepted shape;
- four workers, dense recipient caps at the tested density, and spatial-first lookup combined with cadence were measured regressions.

Keep these as Legacy artifacts. Do not claim Next beats them unless the same traffic, client apply, security, tick rate, hardware, and success semantics are reproduced.

### 18.5 Protected benchmark profiles

Create stable versioned profiles:

1. `control_idle`
   - ready sessions, keepalive/ping, no movement;
   - measures per-connection floor and timeout cleanup.
2. `movement_owner`
   - configured active ratios `0.35`, `0.40`, and `1.0`;
   - input redundancy, owner ACK/correction, client prediction apply.
3. `spread_single_family`
   - observer transform only;
   - broad CCU scaling and core fanout cost.
4. `spread_full_mix`
   - movement, owner, observer transform, visual, bulk, ordered event, light chat/UI;
   - rings `7.5,23.5,39.5`, cadence `1,2,3`.
5. `dense_300_full_mix`
   - ring-0/near duel/crowd geometry;
   - direct successor to the current dense target.
6. `multi_hotspot_3k`
   - several dense clusters plus spread population;
   - prevents one regular grid from defining the architecture.
7. `traversal_window`
   - all players cross chunk boundaries and shift render windows;
   - lifecycle, subscription, manifest, and origin-shift pressure.
8. `teleport_storm`
   - bounded hostile/recovery case;
   - full interest rebuild and baseline cancellation.
9. `join_baseline_storm`
   - cold joins while established players remain active;
   - baseline/bulk isolation.
10. `chat_ui_burst`
    - reliable small events plus bounded UI cold opens;
    - control/bulk separation.
11. `lossy_wan`
    - full mix under several RTT/loss/jitter/reorder profiles;
    - correctness plus useful-byte/retransmit cost.
12. `slow_client`
    - one or more bandwidth-limited/high-loss recipients among healthy clients;
    - bounded memory and fairness.
13. `local_singleplayer`
    - client wrapper starts or attaches to the silent `_Headless` server;
    - explicit readiness followed by normal localhost handshake/admission/world sync;
    - real localhost UDP for the production-equivalence run;
    - separate-process and embedded/background variants;
    - verifies save, disconnect, server shutdown, semantic trace, total CPU, memory, allocation, and wrapper overhead.

Each profile has a canonical seed/trace, expected semantic output, accepted staleness by lane, and capacity plan.

### 18.6 Impairment matrix

At minimum run:

| Profile | RTT | Jitter | Loss | Reorder/duplicate | Bandwidth |
| --- | ---: | ---: | ---: | --- | --- |
| LAN | 1 ms | 0.2 ms | 0% | none | high |
| Good regional | 20 ms | 2 ms | 0.1% | light | high |
| Typical WAN | 60 ms | 8 ms | 1% | 0.2% reorder | moderate |
| Poor mobile | 120 ms | 25 ms | 3% | bursts/duplicates | constrained |
| Recovery stress | 200 ms | 50 ms | 10% | heavy reorder | constrained |

These are test profiles, not universal player-network claims. Use production telemetry later to replace weighting while keeping pathological tests.

### 18.7 Microbenchmark matrix

Add isolated, setup-free benchmarks for:

- generated encode/decode by message and size;
- datagram pack/unpack and mixed-lane packing;
- AEAD encrypt/decrypt by size and algorithm;
- ACK range insert/encode/process;
- reliable window enqueue/ack/retransmit;
- timer wheel/deadline processing;
- pool acquire/transfer/release;
- latest-state publish/coalesce/change-ring drain;
- per-scope pending selection;
- acknowledgement-to-known-state promotion;
- event route/per-connection sequence assignment;
- baseline snapshot build/reuse/fragment/verify/commit;
- subscription unit move/teleport/full recompute;
- cohort formation and shared-body fanout;
- wire-budget scheduler under mixed lane pressure;
- position quantization/origin encoding;
- render-window toroidal shift;
- client dense apply/interpolation;
- compression on captured real payload corpora;
- metrics disabled/core/detailed overhead.

Every benchmark has a correctness checksum and zero-allocation assertion where applicable.

### 18.8 Soak and chaos matrix

- ten minutes: zero-GC steady-state protected profile;
- two hours: mixed workload, periodic joins/leaves, AOI traversal;
- twelve to twenty-four hours: session/endpoint/pool/replay/transfer retention;
- accelerated sequence/epoch wrap;
- random socket errors and worker shutdown/restart where supported;
- slow clients and bulk transfer cancellation;
- malformed authenticated traffic from sampled connections;
- pre-auth flood within lab safety controls;
- repeated connect/disconnect/reconnect from the same endpoints;
- content/UI/baseline interruptions;
- server graceful and deadline-abort shutdown.

Gates include flat retained memory after warmup, zero ownership imbalance, bounded queue ages, exact session counts, semantic oracle pass, and no unexplained p99 drift.

### 18.9 Performance acceptance policy

Initial policy, revisable with evidence:

- Next must not regress Current by more than `5%` in a protected semantically equivalent scenario unless it is paying for newly required security/correctness; such overhead must be isolated and then recovered elsewhere before cutover.
- Next must materially reduce server and client allocation in every protected profile and reach zero steady-state network allocation within configured capacity.
- Next must beat Current in dense full-mix and spread full-mix p95/p99 or provide a documented architecture revision before proceeding.
- Next must reduce useful+protocol bytes per active connection for equivalent state, excluding deliberate encryption tag overhead which is reported separately.
- All correctness, security, cleanup, and ownership gates are pass/fail; speed cannot waive them.
- One surprising run does not approve/reject an optimization. Require repeated results and inspect distribution/max.

## 19. Proposed Repository Shape

```text
Kernels/Network.Next/
  AGENTS.md
  NETWORK_NEXT_DESIGN.md
  Core/
    Ids/
    Time/
    Capacity/
    Collections/
  Generated/
  Protocol/
    Schema/
    Codec/
    Security/
    Handshake/
    Reliability/
    Congestion/
    Fragmentation/
  Transport/
    Loopback/
    Sockets/
    Pools/
    Queues/
  Session/
  Replication/
    Latest/
    Events/
    Baseline/
    Lifecycle/
  Interest/
    Spatial/
    Subscriptions/
    Cohorts/
  Scheduling/
  Assembly/
  Metrics/
  Tracing/

Server/
  Core/                       # Engine-agnostic application runtime
  Hosting/                    # Shared fixed-step host and control surface
  Network.Next/               # Engine-agnostic bridge over Network Next
    EcsCapture/
    Movement/
    Sessions/
    Admission/
    Chat/
    Ui/
    Content/
    Inspection/
  _Console/                   # RunUO-style command-line wrapper
  _Headless/                  # Silent dedicated/internal/embedded wrapper
  _Godot/                     # Godot operator terminal/control wrapper

Client/
  Core/                       # Engine-agnostic projected client runtime
    WorldStreaming/
      CanonicalCoordinates/
      RenderWindow/
      ChunkCache/
  Network.Next/               # Engine-agnostic bridge over Network Next
    Session/
    Input/
    Prediction/
    Observer/
    Baseline/
    Interest/
    Chat/
    Ui/
    Content/
  _Godot/                     # Rendering/input/audio/UI wrapper

_Tests/UltraSim.Network.Next.Tests/
_Tests/UltraSim.Network.Next.Fuzz/
_Tests/UltraSim.Network.Next.Integration/
_Benchmarks/UltraSim.Network.Next.Benchmarks/
_Benchmarks/UltraSim.Network.Next.Harness/
_Tools/UltraSim.Network.ProtocolGenerator/
_Tools/UltraSim.Network.Trace/
_Tools/UltraSim.Network.PacketInspector/
_Tools/UltraSim.Network.CapacityPlanner/
_Tools/UltraSim.Network.AoiVisualizer/
```

Do not physically duplicate shared `Server/Core`, `Server/Hosting`, `Client/Core`, prediction, content, or UI code merely to match this diagram. The key rules are:

- Next remains isolated enough for Current and Next to run against the same traces until cutover;
- `_Console`, `_Headless`, and `_Godot` reference the shared runtime/bridge composition rather than receiving Next-specific gameplay implementations;
- the embedded single-player composition starts the same `_Headless` runtime and connects the same `Client/Network` session over localhost;
- wrappers contain presentation, process, and entry-point concerns only.

## 20. Implementation Phases and Gates

### Phase 0: freeze evidence and repair comparison tools

Deliver:

- repair Current microbenchmark and bot-harness builds without changing measured semantics;
- capture new Current Release artifacts for protected profiles;
- label Legacy artifacts and owner-reported operational figures separately;
- add semantic/profile hashes and environment metadata;
- fix disposal/setup/GC methodology in the microbench runner;
- make sharded stages use fresh processes and exact ready-session assertions;
- add deterministic trace/oracle scaffold;
- add Next projects and dependency architecture tests;
- inventory Current packet/feature/test parity in generated form.

Tests:

- Current `257` relevant tests remain green;
- benchmark output ownership balances;
- a repeated no-op run has stable session/pool counts;
- trace record/replay produces identical semantic hash;
- reporter rejects mismatched profiles.

Benchmarks:

- reproduce current spread and dense reference runs;
- record at least three independent runs at selected CCU points;
- establish measurement noise and instrumentation overhead.

Gate:

- no Next performance claim until Phase 0 tools are trustworthy.

### Phase 1: capacity, memory, ownership, and generated codec foundation

Deliver:

- capacity plan/sealing;
- generational dense IDs and fixed tables;
- slab/buffer pool and ownership state machine;
- bounded SPSC/MPSC rings;
- packet reader/writer;
- protocol generator and manifest/hash;
- exact-size strict codecs for handshake, ping, disconnect, and movement;
- loopback byte transport.

Tests:

- pool fault/random ownership suite;
- all capacity overflow policies;
- codec matrix/fuzz/trailing rejection;
- multi-instance isolation;
- zero allocation for pool/codec/loopback hot paths;
- shutdown returns all resources.

Benchmarks:

- Current versus Next codecs;
- pool/ring throughput and tails;
- managed pinned versus native slabs if both are candidates;
- no security yet, explicitly labeled.

Gate:

- zero steady-state allocation and exact ownership before real sockets.

### Phase 2: secure UDP connection protocol

Deliver:

- stateless cookie/anti-amplification;
- key agreement and AEAD;
- connection token/security epoch;
- packet numbers/replay window;
- ACK ranges and RTT/RTO;
- lane message framing;
- persistent socket I/O workers;
- conservative MTU and packetizer;
- loopback/link-simulator and real UDP implementations.

Tests:

- handshake state model and hostile pre-auth suite;
- crypto vectors/tamper/replay/key rotation;
- socket failure and cleanup;
- packet ACK/property/wrap;
- exact MTU boundaries;
- zero allocation after established-session warmup.

Benchmarks:

- handshake rate/cost and pre-auth saturation;
- plaintext development versus production AEAD isolated overhead;
- Current UDP movement versus Next secure UDP movement;
- socket sync/current async versus persistent completion engine.

Gate:

- no credential/admission port until authenticated session and cleanup tests pass.

### Phase 3: lanes, reliability, pacing, and session lifecycle

Deliver:

- reliable ordered Control;
- unreliable sequenced Input and OwnerState;
- reliable Event and isolated Bulk windows;
- retransmit timer wheel;
- congestion/pacing/fairness controller;
- complete session state machine;
- keepalive, graceful close, timeout, endpoint migration;
- per-state rate/strike limits.

Tests:

- deterministic impairment matrix;
- reliable delivery oracle;
- slow client isolation;
- lane HOL isolation;
- input expiry/redundancy;
- ACK-above-issued rejection;
- retransmit deadline/backoff;
- session cleanup without host event drain;
- epoch/reconnect stale traffic.

Benchmarks:

- reliable event throughput/loss cost;
- control latency under saturated bulk;
- pacing CPU and fairness;
- movement owner profile under WAN simulation.

Gate:

- every lane meets its documented semantics under impairment with bounded memory.

### Phase 4: latest-state tables, lifecycle, and scheduler

Deliver:

- dense latest-state slots;
- per-scope item/change tables;
- pending newest versions and tombstones;
- ACK-derived known-state tracking;
- schema-level absolute versus dependency delta modes;
- independent lane scheduler and wire-byte budgets;
- reservation/commit/cancel work descriptors;
- cohort/body reuse and transform pipeline.

Tests:

- state model/property suite;
- one-shot cadence/phase regression;
- sent-but-lost regression;
- remove/despawn delayed-state regression;
- partial output admission/cursor regression;
- no-subscriber/transform-failure ownership regression;
- scheduler fairness/HOL suite;
- zero allocation publish through queue.

Benchmarks:

- Current latest stream versus Next state table;
- scan/filter versus per-scope changed queues;
- shared fanout at 64/128 through dense 300/350 and larger cohorts;
- exact wire budgeting overhead;
- cohort cache break-even.

Gate:

- Next beats Current's allocation decisively and does not lose a state version under any tested cadence/budget/loss case.

### Phase 5: subscriptions, AOI, and coordinate model

Deliver:

- signed 64-bit canonical chunks plus bounded local coordinates;
- stable `ChunkKey` independent of ring;
- dense incremental bidirectional subscriptions;
- per-connection interest metadata/cadence/phase/pending state;
- hysteresis, forced interest, and initial multiple-anchor support;
- cross-chunk lifecycle routing;
- production server bridge uses multi-ring policy;
- position origin negotiation/relative encoding.

Tests:

- brute-force AOI model/property suite;
- negative coordinates and floor division;
- all movement/teleport transitions;
- one chunk with different ring per recipient;
- rapid cross-chunk entity lifecycle;
- multiple reasons/anchors;
- origin shift/position encoding.

Benchmarks:

- Current subscription reconcile versus Next unit shifts;
- spread, dense, and multi-hotspot geometry;
- rings/radii/cadence A/B including immediate radius roughly `16` versus `8`;
- fixed versus adaptive/hysteresis policy;
- publication once/scheduling many.

Gate:

- accepted geometry shows correct recipients and bounded transition cost; no AOI ghost or one-shot loss.

### Phase 6: atomic baseline, deltas, events, and recovery

Deliver:

- reliable segmented baseline protocol;
- staging image and atomic client commit;
- exact descriptor/count/hash/uniqueness validation;
- one active generation and cancellation rules;
- post-cutoff pending data;
- exact ACK validation;
- per-connection filtered event sequencing;
- snapshot cache and recovery policy;
- dense snapshot mode experiment.

Tests:

- fault injection at every baseline step;
- stale/unissued/mismatched ACK;
- no interleaved generations;
- delta exact baseline/base version;
- event visibility oracle under loss/reorder;
- recovery beyond retention;
- snapshot cache visibility isolation;
- join/leave/despawn during baseline.

Benchmarks:

- join baseline latency/bytes/CPU;
- cached cohort snapshot reuse;
- established-client latency during join storm;
- incremental versus snapshot dense mode;
- Current generic runtime versus Next semantic equivalent.

Gate:

- recovery is atomic and protected realtime/control latency stays within budget during baseline storms.

### Phase 7: client movement, projection, and sliding render window

Deliver:

- full input expiry/redundancy/ack path;
- bounded prediction/reconciliation history;
- observer interpolation policies;
- dense client entity/item slots;
- client apply barrier;
- toroidal preallocated render chunk window;
- binding generations and stale-load cancellation;
- floating-origin/render-root rebase;
- terrain/static manifest cache seam.

Tests:

- prediction trace under WAN matrix;
- every dropped input position in start/stop sequences;
- observer interpolation/lifecycle/slot reuse;
- complete render-window suite;
- Godot headless origin-shift/chunk-reuse smoke;
- stable memory/node/resource counts.

Benchmarks:

- client decode versus apply versus presentation rebase;
- dictionary Current store versus dense Next slots;
- window unit shift and teleport;
- 100k visible presentation mapping where client rendering work can be isolated.

Gate:

- player-at-zero enormous-world traversal works without canonical drift, stale content, or steady-state allocation.

### Phase 8: application protocols and built-on feature parity

Deliver:

- secure admission/accounting/character flow;
- chat/commands;
- UI hot/cold schema, model, action, bounded transfer;
- presentation/content IDs and chunk/content transfer seam;
- server operator/status/inspection traffic;
- script/runtime registration descriptors;
- one engine-agnostic server composition through `Server/Core`, `Server/Hosting`, and `Server/Network`;
- one engine-agnostic client composition through `Client/Core` and `Client/Network`;
- thin `_Console`, `_Headless`, server `_Godot`, and client `_Godot` composition roots;
- graceful host behavior in headless, console, and Godot shells;
- embedded/background `_Headless` single-player startup, readiness, localhost auto-connect, save, and shutdown orchestration;
- shared typed lifecycle/control surfaces used by every wrapper.

Tests:

- all ported Current application tests;
- bounds/rate/permission/security for each family;
- transfer cancellation/cleanup;
- identity authorization;
- application traffic under active gameplay;
- launch smokes and reconnect loops;
- wrapper project-reference/engine-dependency enforcement;
- deterministic world/network semantic parity across server wrappers;
- single-player localhost full-session smoke with no direct runtime bypass;
- local bind/startup/crash/cancellation/save-on-stop cleanup;
- real localhost UDP versus loopback semantic trace parity.

Benchmarks:

- full mixed profile;
- chat/UI burst;
- admin inspection isolation;
- content/baseline bulk isolation;
- bandwidth by application family;
- wrapper overhead with operator surfaces idle and active;
- embedded versus separate-process local server resource contention.

Gate:

- retained Current user-facing functionality works through Next without violating lane, ownership, or allocation rules;
- every server wrapper demonstrably hosts the same engine-agnostic runtime;
- single-player demonstrably remains an ordinary server-authoritative network session.

### Phase 9: sharding, platform specialization, and hyper-optimization

Deliver candidates only after profiling:

- connection/transport/assembly shard tuning;
- worker affinity/NUMA-aware allocation where deployed hardware warrants it;
- `sendmmsg/recvmmsg` platform adapter;
- vectorized packet copy/quantization/ACK operations;
- specialized homogeneous batch codecs;
- hot/cold table refinement;
- prefetch/cache-line layout changes;
- static compression dictionaries;
- adaptive cadence/congestion thresholds;
- source-generated fused capture/encode paths.

Tests:

- identical semantics across portable/specialized paths;
- alignment/unaligned/bounds cases;
- shard ownership migration and shutdown;
- no unsafe memory lifetime violation;
- performance-counter-guided regression suite.

Benchmarks:

- worker/shard sweep from one upward;
- hardware counters and disassembly;
- total CPU, not only tick-thread time;
- p99/max and power/thermal stability;
- protected full profiles.

Gate:

- keep only changes with repeatable end-to-end value. A micro win that worsens tail, total CPU, maintainability without payoff, or semantic risk is rejected.

### Phase 10: cutover

Deliver:

- Legacy/Current/Next final comparison report;
- protocol rollout/version policy;
- server/client compatibility deployment plan;
- observability dashboards and alerts;
- rollback path to Current during initial deployment;
- load-shed/capacity defaults;
- operator documentation;
- explicit list of removed/deferred APIs/features.

Cutover gate:

- all required tests pass;
- protected profiles satisfy semantic oracle;
- zero steady-state allocation/GC target is met;
- ownership/pools balance under soak and chaos;
- security review and hostile tests pass;
- Next meets protected p95/p99/max and bandwidth gates;
- dense and spread capacity meet or exceed the agreed Current baseline;
- application/host/Godot smokes pass;
- rollback is rehearsed;
- evidence supports the implementation. If not, revise rather than force cutover.

## 21. Feature Parity Checklist

Every checked item requires tests. Performance-sensitive items require a Current/Next comparison and, where possible, Legacy context.

### Core, protocol, and memory

- [ ] Engine/ECS/Godot-independent kernel
- [ ] Explicit planes, lanes, delivery, service, expiry, and overflow table
- [ ] Generational connection/session/network item/entity/transfer IDs
- [ ] Connection and security epochs
- [ ] Capacity plan and seal/warmup boundary
- [ ] Fixed dense tables and bounded queues
- [ ] Slab buffer pools and size classes
- [ ] Generational ownership tokens
- [ ] Creator/queue/socket/reliable-window ownership transitions
- [ ] Debug poison and ownership verifier
- [ ] Source-generated protocol schema
- [ ] Collision-checked stable message IDs
- [ ] Exact size measure/write codecs
- [ ] Strict full-consumption decode
- [ ] Generated bounds and canonical varints
- [ ] Frozen O(1) dispatch tables
- [ ] Protocol/schema/content feature hash
- [ ] Transform pipeline with real implementations
- [ ] Zero steady-state core allocation

### Transport and security

- [ ] Deterministic loopback transport
- [ ] Completion-driven UDP socket I/O
- [ ] Conservative configurable MTU
- [ ] Same-connection mixed-lane datagram packing
- [ ] No accidental IP fragmentation
- [ ] Stateless cookie and anti-amplification
- [ ] Authenticated key establishment
- [ ] AEAD encryption/authentication
- [ ] Packet replay protection
- [ ] Key phase rotation
- [ ] Unpredictable connection routing token
- [ ] Endpoint/path validation and migration
- [ ] Packet acknowledgement ranges
- [ ] RTT/variance/min-RTT/RTO tracking
- [ ] Loss/reorder estimates
- [ ] Reliable ordered lane
- [ ] Reliable unordered lane if a real use remains
- [ ] Unreliable sequenced lane
- [ ] Unreliable disposable lane
- [ ] Per-lane ordering/no cross-lane HOL
- [ ] Retransmit deadline/backoff update
- [ ] Congestion response and pacing
- [ ] Per-connection/shard/server rate limits
- [ ] Reliable bulk segmentation/reassembly
- [ ] Fragment bounds, hash, timeout, cancellation
- [ ] Graceful shutdown drain and deadline abort
- [ ] TCP/TLS or QUIC fallback experiment if operationally required

### Sessions and admission

- [ ] Full state machine with allowed-message table
- [ ] Pre-auth and post-auth rate/strike policies
- [ ] Protocol/feature/schema validation
- [ ] Session lifecycle snapshots/events
- [ ] Duplicate-session policy
- [ ] Auth and keepalive timeouts
- [ ] Ping/pong and path probe
- [ ] Explicit client/server disconnect notice
- [ ] Cleanup intrinsic to state transition
- [ ] Reconnect with fresh epoch
- [ ] Admission/accounting bridge
- [ ] Character selection and spawn/bind
- [ ] Durable/runtime/network/client identity mapping
- [ ] Credential secrecy and clearing

### Runtime hosts and wrappers

- [ ] `Server/Core` remains engine-agnostic
- [ ] `Server/Hosting` owns one shared fixed-step lifecycle/control surface
- [ ] `Server/Network` remains engine-agnostic
- [ ] `Client/Core` remains engine-agnostic
- [ ] `Client/Network` remains engine-agnostic
- [ ] `_Console` is a thin RunUO-style command-line composition root
- [ ] `_Headless` is a thin silent dedicated/internal/embedded composition root
- [ ] server `_Godot` is a thin operator terminal/control composition root
- [ ] client `_Godot` owns presentation/input/audio/resources only
- [ ] no wrapper-specific gameplay, ECS, persistence, session, AOI, or protocol implementation
- [ ] every wrapper uses the same typed lifecycle/command/settings/save/inspection surface
- [ ] wrapper project-reference and forbidden-namespace architecture tests
- [ ] deterministic server semantic parity across `_Console`, `_Headless`, and `_Godot`
- [ ] single-player starts or attaches to `_Headless`
- [ ] single-player waits for explicit readiness and auto-connects through localhost
- [ ] single-player uses normal handshake, security, admission, authority, replication, persistence, and cleanup
- [ ] no direct client-to-server object/ECS/storage bypass
- [ ] production-equivalence single-player tests use real localhost UDP
- [ ] loopback remains an explicit semantic transport, not a hidden alternate gameplay path
- [ ] local startup failure, crash, cancellation, reconnect, save, and shutdown ownership
- [ ] wrapper and embedded-versus-process overhead benchmarks

### Latest state and lifecycle

- [ ] Dense latest-state item slots
- [ ] Canonical scope-independent item identity
- [ ] Per-scope item and changed-version indexes
- [ ] Coalesced latest writes
- [ ] Pending version survives cadence/budget denial
- [ ] ACK-derived known-state tracking
- [ ] Compact absolute hot samples
- [ ] Exact-base dependency deltas where beneficial
- [ ] Periodic keyframe/repair policy
- [ ] Spawn lifecycle
- [ ] AOI enter/current-state lifecycle
- [ ] AOI leave/dormancy lifecycle
- [ ] Despawn/removal tombstones
- [ ] Delayed-state rejection after tombstone/slot reuse
- [ ] Atomic old-scope leave/new-scope enter
- [ ] Scope/client cohort shared-body caching
- [ ] Realtime homogeneous batching
- [ ] Bulk latest-state lane

### Ordered events and recovery

- [ ] Canonical publish-once event body
- [ ] Permission/interest filter before recipient sequence assignment
- [ ] Contiguous per-connection reliable event sequence
- [ ] Bounded reliable window/replay retention
- [ ] Highest-contiguous/range acknowledgement
- [ ] Duplicate/out-of-order handling
- [ ] No production sequence-gap bypass
- [ ] Recoverable versus non-recoverable family policy
- [ ] Snapshot recovery when retention is exceeded
- [ ] Disconnect on unrecoverable reliable overflow
- [ ] Event idempotency/application IDs where needed

### Baseline and delta

- [ ] First-class baseline descriptor
- [ ] One active generation per connection/scope family
- [ ] Network-owned snapshot source, no live ECS read
- [ ] Shared snapshot cache with visibility-safe key
- [ ] Reliable isolated fragments
- [ ] Client staging image
- [ ] Descriptor/count/size/uniqueness/hash validation
- [ ] Atomic client commit
- [ ] Exact issued ACK validation
- [ ] Snapshot cutoff and post-cutoff pending changes
- [ ] Exact baseline generation on delta
- [ ] Exact base item version for dependency delta
- [ ] Ordered-event floor transition
- [ ] Timeout/restart/cancel behavior
- [ ] Dense snapshot mode experiment

### Scheduling and fanout

- [ ] Independent lane cursors
- [ ] Exact wire-byte and datagram-slot budget
- [ ] Guaranteed critical minimum service
- [ ] Deadline, age, relevance, and debt priority
- [ ] No oversized-item cross-lane HOL
- [ ] Output reservation before selection
- [ ] Commit/cancel after admission
- [ ] No cursor advancement on partial failure
- [ ] Build once per compatible cohort
- [ ] Recipient span/range fanout descriptor
- [ ] Per-connection packetize/encrypt without body rebuild
- [ ] Stable non-starving phasing
- [ ] Immediate/near/far cadence
- [ ] Forced lifecycle/teleport/combat/owner updates
- [ ] Explicit degradation order
- [ ] Slow-client isolation
- [ ] Bulk/realtime/control isolation
- [ ] Shard ownership and epoch-stamped migration

### AOI, chunks, and enormous worlds

- [ ] Signed 64-bit canonical chunk coordinate
- [ ] Bounded local fixed/quantized coordinate
- [ ] Correct negative floor division
- [ ] Stable `ChunkKey` without ring index
- [ ] Per-connection ring/cadence/phase metadata
- [ ] World-space radius semantics
- [ ] Incremental connection-to-chunk subscriptions
- [ ] Incremental chunk-to-subscriber reverse index
- [ ] Hysteresis
- [ ] Forced/gameplay interest
- [ ] Multiple bounded anchors/reason union
- [ ] Cross-chunk lifecycle correctness
- [ ] Fast movement and teleport
- [ ] Separate terrain/entity/physics/audio/prefetch policy layers
- [ ] Network-relative origin encoding
- [ ] Server local physics-island origin seam
- [ ] Client floating-origin seam
- [ ] Player-at-zero presentation
- [ ] Fixed toroidal render window
- [ ] Render-slot binding generation
- [ ] Row/column pool reuse
- [ ] Stale async load rejection
- [ ] Terrain/static manifest revisions/hashes
- [ ] Chunk cache request/transfer/validation
- [ ] Frozen statics as content, stateful statics as entities

### Server bridges and application features

- [ ] Generated ECS replication descriptors/extractors
- [ ] Independent/non-destructive ECS change consumption
- [ ] Realtime/ReliableEvent/Bulk classification
- [ ] Movement input validation/coalescing
- [ ] Expiring/redundant input intent
- [ ] Owner correction/input acknowledgement
- [ ] Observer movement/presentation
- [ ] Chat submit/message/channel/local scope
- [ ] Commands/access/audit
- [ ] Chat cleanup and limits
- [ ] UI panel open/model/chunk/action
- [ ] Bounded UI transfer and cache identity
- [ ] Admission/login/character flow
- [ ] Script command/panel provider bindings
- [ ] Presentation/content logical IDs
- [ ] Content/chunk manifest and transfer seam
- [ ] Status/metrics/system snapshots
- [ ] Entity inspection with authorization/rate/size limits
- [ ] Headless, console, and Godot server hosts
- [ ] Persistence/load rebinding to new runtime/network IDs

### Client features

- [ ] Shared control/gameplay session host
- [ ] Movement start/change/stop/edge/heartbeat send policy
- [ ] Input sequence redundancy and expiry
- [ ] Prediction history
- [ ] Input acknowledgement
- [ ] Reconciliation/replay
- [ ] Correction metrics
- [ ] Dense generational observer store
- [ ] Latest/batch/delta apply
- [ ] Baseline staging/atomic commit
- [ ] Ordered event buffering/apply/ack
- [ ] Lifecycle/tombstone apply
- [ ] Interpolation/jitter/extrapolation/dormancy
- [ ] Chat routing/deduplication
- [ ] UI schema/model/action routing/reassembly
- [ ] Content/chunk cache integration
- [ ] Apply barrier/thread ownership
- [ ] Disconnect cleanup and reconnect
- [ ] Godot presentation hooks
- [ ] Render-window/floating-origin integration

### Tests, benchmarks, and tools

- [ ] Current `257` relevant-test baseline retained/characterized
- [ ] Protocol generator verification tests
- [ ] Codec fuzzing
- [ ] Handshake/security fuzzing
- [ ] Sequence/ACK property tests
- [ ] Deterministic link simulator
- [ ] Semantic delivery/convergence oracle
- [ ] Pool/ownership fault injector
- [ ] Zero-allocation assertions by stage
- [ ] Ten-minute zero-GC soak
- [ ] Long mixed/session churn soak
- [ ] Real UDP separate-process harness
- [ ] Sharded client runner
- [ ] WAN impairment profiles
- [ ] Dense/spread/mixed/multi-hotspot/traversal profiles
- [ ] Join/baseline/content storm profiles
- [ ] Client decode/apply metrics
- [ ] Logical/application/protocol/wire byte accounting
- [ ] p50/p95/p99/max timing
- [ ] Legacy/Current/Next semantic comparison
- [ ] Environment/profile/schema hashes
- [ ] Capacity/bandwidth planner
- [ ] Packet inspector/dissector
- [ ] Trace recorder/replayer
- [ ] AOI/render-window visualizer
- [ ] Hardware-counter/disassembly workflow

## 22. Expected High-Value Performance Changes

The largest expected gains are structural, not clever instructions:

1. **Eliminate per-tick managed allocation and GC.** Current long runs allocate tens of MB/s across server and clients. Removing object/datagram/array churn should reduce both CPU and p95/p99/max stalls.
2. **Stop full snapshot/scope scans per connection.** Per-scope changed queues and pending versions make work proportional to actual local changes plus recipients, not `connections x global entries`.
3. **Make shared fanout cohort-native.** One body plus recipient ranges avoids N datagram objects, refcount operations, and repeated header/body work.
4. **Capture once from ECS.** A network-owned version table prevents destructive dirty drains, duplicate consumers, and cadence re-publication.
5. **Use compact relative/quantized homogeneous batches.** This attacks both payload bytes and send-call/packet rate.
6. **Use persistent socket completion workers.** Remove polling/yield/task scheduling and prevent the simulation tick from synchronously absorbing socket send cost.
7. **Budget before assembly using wire cost.** Avoid wasted builds, partial output loss, and bursty over-budget flushes.
8. **Keep cadence pending rather than lossy.** Correctness permits aggressive near/far phasing without continuous republish hacks.
9. **Separate bulk completely.** Join/chunk/UI/content work can use spare bandwidth without destroying combat latency.
10. **Use dense/hot-cold tables.** Reduce dictionary lookups, cache misses, locks, and pointer chasing in sessions, subscriptions, state, and client stores.
11. **Use per-connection visible event sequencing.** Removes workaround buffering/gap checks and gives the reliable lane a clean bounded state.
12. **Exploit ACK feedback for state knowledge.** Prevents needless baseline recovery and incorrect delta omission after loss.

Correctness/security will add real CPU and bytes: AEAD tags, packet ACKs, retransmit state, atomic snapshots, and strict validation are not free. The goal is for structural savings to more than repay that cost. Report the security/correctness delta separately so optimization remains honest.

Micro-optimizations to consider only after the structural profile is clean:

- source-generated specialized codecs and capture loops;
- power-of-two rings and mask arithmetic;
- generation-stamped tables instead of clears;
- `CollectionsMarshal`/unsafe access only behind proven invariants;
- unmanaged aligned slabs;
- cache-line padding of contended shard state;
- vectorized copy/quantization/checksum;
- batched syscalls;
- precomputed varint/header templates;
- branchless modular sequence comparisons where disassembly proves it;
- per-CPU/shard counters and deferred reduction;
- aggressive method inlining only where code-size/i-cache measurements support it.

Do not preserve an API that makes these structural wins impossible.

## 23. Risks and Explicit Design Questions

### 23.1 Custom UDP complexity

Risk: a correct secure reliable multi-lane UDP protocol is substantial work. Mitigation: implement in vertical layers, use deterministic models/fuzzing, benchmark an alternative transport, and do not port application traffic until each layer is tested.

### 23.2 Zero-allocation versus operational flexibility

Risk: fixed capacity can reject legitimate bursts. Mitigation: explicit per-profile capacity planning, high-water telemetry, safe cold reconfiguration/restart, and bounded overflow/degradation. Never disguise an allocation fallback as flexibility.

### 23.3 ACK-detailed latest state memory

Risk: per-connection per-item known versions can be large. Alternatives to benchmark:

- compact ACK receipt maps only for recent packets;
- scope-level baseline plus self-contained hot samples;
- dense version arrays only for active visible items;
- cohort known-state sharing where receipt histories truly match;
- periodic keyframes instead of dependency deltas.

Use the simplest representation that meets bandwidth and convergence targets.

### 23.4 Shared fanout versus connection-relative encoding

Risk: per-client origins, permissions, feature versions, and known state fragment cohorts. Mitigation: share canonical encoded item bodies where possible, group compatible origins/policies, use relative headers at batch scope, and measure cache/cohort construction overhead. Do not force reuse when rebuilding is cheaper for a tiny cohort.

### 23.5 Floating-origin physics semantics

Risk: keeping presentation player at exact zero can conflict with physics/camera smoothing. Mitigation: canonical fixed coordinates remain authoritative; shift a render/physics island root at tested thresholds; keep presentation smoothing separate. Prototype with Godot before the wire representation freezes.

### 23.6 Adaptive cadence instability

Risk: reacting too aggressively to density/loss can oscillate and make perceived motion inconsistent. Mitigation: hysteresis, bounded rate of policy change, minimum service, deterministic trace tests, and user-perception experiments.

### 23.7 Encryption cost

Risk: per-datagram AEAD CPU/tag bytes may matter at high packet rates. Mitigation: bundle small messages, use hardware-supported algorithms, batch where APIs allow, and reduce packet rate. Disabling security is not the production optimization.

### 23.8 Native/unsafe memory

Risk: leaks/use-after-free can replace GC cost with worse faults. Mitigation: generational tokens, debug ownership state, poison/fault tests, safe reference implementation, and limited reviewed unsafe modules.

### 23.9 Benchmark overfitting

Risk: optimizing one regular grid or single-family localhost trace harms actual play. Mitigation: versioned workload families, randomized recorded geometry/activity, WAN impairment, multi-hotspot/traversal, application mix, and production telemetry feedback.

## 24. Definition of Done

Network Next is not done because it has a fast UDP echo or fanout microbenchmark. It is done when:

- the useful Current kernel, server bridge, client bridge, chat, UI, admission, movement, presentation, inspection, host, and tooling feature set has a tested Next equivalent or an explicit tested replacement decision;
- channel/delivery declarations are real end-to-end semantics;
- production traffic is authenticated/encrypted and replay-protected;
- pre-auth traffic is bounded and anti-amplification-safe;
- all parsers and reassembly paths are strictly bounded and fuzzed;
- baseline recovery is staged, validated, atomic, exact-generation, and non-interleaved;
- latest-state knowledge comes from valid acknowledgement or self-contained encoding, not send assumption;
- ordered events are contiguous in each recipient's visible sequence domain;
- lifecycle tombstones make AOI leave/despawn/slot reuse correct under reordering;
- cadence/phasing never loses a one-shot update;
- canonical chunk identity is independent of recipient ring;
- the client sliding render window and floating origin support enormous worlds without canonical drift;
- hot server and client network paths allocate zero after warmup within capacity;
- designated steady-state soaks perform no network-caused GC and retain flat memory;
- every queue/pool/table/window/transfer has a hard limit, high-water metric, and tested overflow policy;
- slow, lossy, or hostile clients cannot create unbounded work or memory;
- Control/Input/OwnerState latency is isolated from Bulk;
- Current and Next execute the same protected semantic traces;
- Next meets or beats agreed Current p95/p99/max and useful-byte targets in dense and spread full-mix workloads;
- cold join/baseline/content storms do not violate established-player latency gates;
- launch, reconnect, shutdown, and cleanup smokes pass in headless, console, and Godot hosts;
- `Server/Core`, `Server/Hosting`, `Server/Network`, `Client/Core`, and `Client/Network` remain engine-agnostic and independently testable;
- `_Console`, `_Headless`, and both `_Godot` entry points remain thin wrappers over the same server/client runtime and typed control surfaces;
- single-player starts the ordinary silent `_Headless` server, auto-connects the ordinary client over localhost, and passes the same handshake, admission, authority, replication, persistence, disconnect, and cleanup fixtures without a direct-call bypass;
- wrapper parity tests produce identical server/world/network semantic results for identical configuration and inputs;
- the Legacy/Current/Next report is reproducible from checked-in configs and artifacts;
- tests and measurements support the final architecture. If they do not, the architecture changes.

The expected result is a secure, subscription-driven, publish-once network runtime whose tick cost scales with accepted local work, whose memory is planned and flat, whose bytes are deliberate, and whose client can traverse an enormous canonical world while rendering around a stable local origin.

## 25. Related Documentation Audit

### 25.1 Current documents reviewed

The rewrite incorporates the current networking documentation and cross-system plans that define network-facing contracts:

| Area | Documents | Material contribution |
| --- | --- | --- |
| Network kernel | `Kernels/Network/AGENTS.md`, `README.md`, `NETWORK_REWORK_IMPLEMENTATION_PLAN.md`, `NETWORK_PARITY_CHECKLIST.md` | Required layering, immutable buffers, lanes, transform seam, shared fanout, AOI policy, Current feature/status and parity gaps |
| ECS | `Kernels/ECS/README.md`, `Kernels/ECS/ECS_NEXT_DESIGN.md` | Capture barrier, independent change/lifecycle streams, stable item identity, immutable network views, old/current/new comparison method |
| Client | `Client/README.md`, `CLIENT_IMPLEMENTATION_PLAN.md`, `GODOT_CLIENT_SHELL_ROADMAP.md` | Prediction/interpolation, live UDP host, reconnect/keepalive, Godot presentation boundaries and smokes |
| UI/content | `Client/UI_SCHEMA_AND_ADMIN_PANEL_PLAN.md`, `Client/CONTENT_PACK_PIPELINE_PLAN.md`, `Client/UI/README.md` | Server-driven UI hot/cold schemas, bounded action/model flow, server-scoped cache, logical content IDs, manifest/hash/download needs |
| Server | `Server/SERVER.md`, `SERVER_WRAPPER_IMPLEMENTATION_PLAN.md`, `Gameplay/README.md`, `SCRIPT_COMPILER_AND_RUNTIME_LAYOUT_PLAN.md` | Tick/barrier order, host lifecycle, movement authority, script-owned commands/panels/prefabs and content generations |
| Admission/accounting | `Server/Admission/README.md`, `Server/Accounting/README.md`, runtime admission package README | Secure login/character flow, durable identity, session/entity mapping |
| Chat/inventory/persistence | `Server/CHAT_COMMAND_SYSTEM_PLAN.md`, `Server/Inventory/README.md`, `Kernels/Inventory/README.md`, `Server/Persistence/README.md` | Chat permissions/locality, runtime versus durable identity, save/load rebind boundaries |
| Benchmarks | `_Benchmarks/README.md`, `_Benchmarks/UltraSim.Network.BotHarness/README.md` | Current scenario inventory, sharded runner, AOI/cadence options, logical/wire and stage telemetry, allocation/GC observations |

The audit does not move those domains into the kernel. It ensures the kernel and bridges expose fast neutral seams.

### 25.2 Historical documents reviewed

Historical networking/world-streaming documents provided design intent and benchmark context:

- `_OLD/Docs/NETOWRK V2 DESIGN.md`;
- `_OLD/Docs/NETWORK_PRD.md`;
- `_OLD/Docs/NETWORK_IMPLEMENTATION_PLAN.md`;
- `_OLD/Docs/NETWORK_OPTIMIZATION_BACKLOG_2026-02-26.md`;
- `_OLD/Docs/NETWORK_BASELINE_2026-02-22.md`;
- `_OLD/Docs/NETWORK_BASELINE_2026-03-09.md`;
- `_OLD/Docs/NETWORK_BASELINE_2026-03-23.md`;
- `_OLD/Docs/SPATIAL_FIRST_REPLICATION_DESIGN.md`;
- `_OLD/Docs/PARALLEL_REPLICATION_BUILD_DESIGN.md`;
- `_OLD/Docs/REPLICATION_PRIORITY_LOD_PHASE1_SPEC.md`;
- `_OLD/Docs/REPLICATION_SHARED_FANOUT_REUSE_PHASE1_SPEC.md`;
- `_OLD/Docs/CHUNK_TERRAIN_STATICS_IMPLEMENTATION_GUIDE.md`;
- `_OLD/_OLD/Docs/WorldStreamingAndRendering.md` and related terrain/rendering references.

They reinforce:

- control/input/state separation;
- publish, schedule, assemble, send staging;
- coarse scope streams instead of per-entity stream explosion;
- subscription-driven fanout;
- bounded latest/event history and snapshot recovery;
- stable connection/shard ownership;
- budget/degradation policy;
- server spatial chunks, authoritative terrain/static chunks, and client render chunks as distinct layers;
- client render slots that retain both local slot and absolute world chunk identity;
- pool/reuse and near/mid/far presentation;
- the historical importance of measuring AOI before micro-packing.

This specification strengthens those proposals with real channel semantics, security, atomic baselines, ownership, canonical-relative coordinates, and a testable migration plan.

### 25.3 What documentation confirms as implemented Current behavior

Cross-checking code/tests and documents confirms:

1. The engine-neutral kernel boundary is real.
2. Typed packets, span codecs, loopback, UDP, bundling, sessions, subscriptions, streams, scheduler, transform seam, and fanout exist.
3. Movement owner/observer paths run over real UDP.
4. Latest state, ordered events, acknowledgements, replay/retransmit helpers, baseline/delta helpers, and a generic server runtime exist.
5. AOI ring primitives and cadence policy exist, including the `1,2,3` widened profile.
6. Chat, UI, admission, credentials, character selection, script commands/panels, and server host integration exist above the kernel.
7. Client prediction/interpolation primitives and observer/chat/UI apply bridges exist.
8. Real socket, separate-process, sharded, client-apply, allocation/GC benchmark artifacts exist.
9. Stable item identity across scopes and slot-indexed client state were intentional measured improvements.
10. Current Network/Client/Server test suites provide `257` passing relevant tests in the audited tree.
11. The server runtime is engine-agnostic: `Server/Core`, `Server/Hosting`, and `Server/Network` are the reusable implementation, while `_Console`, `_Headless`, and `_Godot` are shells over its shared lifecycle/control surface.
12. The client runtime is also engine-agnostic: `Client/Core` and `Client/Network` own neutral state and protocol behavior, while client `_Godot` owns presentation and device/engine integration.
13. Single-player is intended to launch or attach a silent local `_Headless` server and connect the normal client session over localhost, preserving server authority and the ordinary network/persistence flow; in-memory loopback remains an explicitly selected test/benchmark transport rather than the default production-equivalence path.

### 25.4 Planned or partial features that must not be overstated as Current

- UDP channel metadata is not real reliable/ordered/sequenced delivery.
- encryption, compression, and segmentation are not production transforms.
- credentials are not protected by a kernel secure transport.
- baseline/delta recovery is not atomic or loss-safe enough for production semantics.
- widened reliable-event benchmarking does not always use the real contiguous sequence runtime.
- full production multi-ring AOI/cadence is not uniformly wired across hosts.
- lifecycle tombstones/despawn/AOI leave are incomplete.
- the client sliding render window and floating-origin world are planned, not Current.
- terrain/static manifest and chunk cache transfer are planned.
- prediction/reconciliation is a narrow movement slice.
- content-pack network advertisement/download/delta messages are planned.
- the current benchmark and bot-harness projects do not build at audit time.
- zero steady-state allocation/GC is not achieved.
- security hardening, congestion control, WAN impairment, and hostile fuzz coverage are not complete.
- fully enforced wrapper-parity, forbidden-dependency, and production-equivalent local single-player localhost tests are planned requirements even though the architectural intent already exists.

### 25.5 Requirements that stay outside the Network kernel

Network Next must not absorb:

- ECS storage, scheduling, prefab authoring, or persistence implementation;
- account credential verification, character ownership, or admission business rules;
- gameplay authority, movement physics, combat, stealth, party, or faction relevance policy;
- chat command meaning, permissions, moderation, or history policy;
- UI schema semantics, rendering, focus, or Godot controls;
- content pack trust, asset decoding, resource resolution, or disk cache policy;
- terrain simulation, collision, LOS, nav, or rendering;
- inventory/equipment transaction rules;
- server save-set orchestration;
- Godot scene/node/resource lifecycle.

The kernel contribution is fast secure delivery, bounded state/stream/subscription/scheduling primitives, compact canonical wire formats, owned immutable handoffs, and observability. Bridges retain domain authority.

### 25.6 Documentation maintenance

Current long README/plan files mix historical milestones, present status, benchmark snapshots, and next steps. Test counts and benchmark prose can drift from executable artifacts.

Next should generate a traceability table:

| Feature | Intent source | Current code/test | Next code/test | Semantic trace | Legacy/Current/Next benchmark | Status |
| --- | --- | --- | --- | --- | --- | --- |

Generate test counts, protocol tables, and benchmark summaries from manifests/artifacts. Keep prose for architecture, rationale, ownership, risks, and interpretation. Code/tests prove features; result JSON proves measurements; a profile hash proves comparability.
