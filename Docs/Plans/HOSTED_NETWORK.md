# Hosted Network H0: accepted external UDP lifecycle

Status: **H0.0-H0.5 accepted / localhost deployment gate closed** as of
2026-08-25.

Hosted H0 is the Network-generic lifecycle between an unknown external UDP path and
an authenticated UltraSim protocol session. It is not a replication profile, does
not authenticate a Game account, does not create `ParticipantId`, does not own AOI,
and does not interpret Game messages.

The current Game dynamic-public profile is `ChunkPublication`, but Hosted Network
must remain indifferent to that choice. `MaintainedEntityRelation`, dirty-profile
experiments, owner-private UI/Inventory traffic, retained Events, and future Game
protocols all flatten into the same authenticated Network/session machinery.

General Network/replication policy is recorded in [Network V3](NETWORK.md).

## Frozen boundary

```text
unknown external UDP endpoint
        |
        v
Hosted Network
    deployment-owned server identity
    bounded unauthenticated admission
    stateless cookie / rate / anti-amplification policy
    authenticated transport-session promotion
    exact path ownership and authenticated rebinding
    bounded connection-owner packet handoff
        |
        v
authenticated Network connection/session
        |
        v
Game protocol/application binding
        |
        v
Accounting / Admission / participant authority
        |
        v
Game-owned replication / UI / Event / Control semantics
```

Permanent rules:

```text
IP/PORT != CONNECTION IDENTITY.
CONNECTION IDENTITY != GAME IDENTITY.
THE CLIENT DOES NOT CHOOSE A CONNECTION ID.
THE LISTENER DOES NOT GRANT GAME AUTHORITY.
UNKNOWN BYTES DO NOT ROUTE TO AN ESTABLISHED PEER.
COOKIE VALIDATION PRECEDES EXPENSIVE/STATEFUL HANDSHAKE WORK.
UNAUTHENTICATED RESPONSE MUST NOT AMPLIFY THE TRIGGERING REQUEST.
PATH REBINDING IS AUTHENTICATED AND TRANSACTIONAL.
PACKET MEMORY HAS ONE OWNER AT A TIME.
HOSTED NETWORK CONTAINS NO AOI OR GAME-SEMANTIC VOCABULARY.
```

## Deployment identity and trust

The server uses a caller/deployment-owned `NetworkServerIdentity`. The Hosted host
retains it for handshake signatures but does not own/dispose the deployment key.

Clients receive/pin the server public key out of band. Hosted construction never
creates a self-trusted client and never silently changes an empty key into
trust-on-first-use. Controlled in-process tests may use explicitly named self-trusted
helpers outside the external Hosted boundary.

Client F0.3a adds a bounded pre-handshake deployment-key announcement on the same listener solely
for human onboarding. The request is a fixed 256 bytes, manifest- and nonce-bound, rate-limited
through the existing unknown-source table, and its response cannot exceed the request. The
announcement is self-signed to reject corruption and prove possession of the advertised private
key, but remains explicitly **untrusted**. It cannot construct a secure peer or bypass the pin:
after human or explicit-fingerprint selection, the ordinary Hosted client is restarted with that
exact public key supplied as its required pin.

The handshake authenticates the pinned server endpoint/transport transcript. Game
account/participant authentication remains an application protocol above the secure
connection.

The first current Game application message supplies an independent `ApplicationRealmId` and fresh
application-session epoch inside that authenticated transport. Network does not interpret either
value. Realm verification and durable client trust persistence are Client/Game composition policy
above Hosted Network.

## H0.0/H0.1 — stateless unknown-path admission

`HostedUdpAdmissionHost` owns the first externally sourced server boundary.

Accepted behavior:

- the caller supplies an explicit IPv4 bind endpoint and deployment identity;
- the listener owns one random 256-bit cookie key for its lifetime and clears it on
  disposal;
- an unknown canonical `ClientHello` may receive only a stateless cookie challenge;
- the cookie MAC is bound to protocol manifest version/fingerprint, client nonce/key,
  source IPv4/port, and monotonic time bucket;
- current and immediately previous cookie buckets validate; older cookies fail;
- challenge generation is rate-limited per source IPv4/bucket through fixed capacity;
- challenge bytes do not exceed the triggering hello, keeping unauthenticated
  amplification at or below 1:1;
- no route, `ConnectionId`, ECDH object, protocol peer, Game session, account, or
  participant is allocated before cookie validation;
- cookie-validated candidates occupy bounded generation-fenced rows, deduplicate
  by source path/credentials as specified, expire on monotonic deadlines, and fail
  closed at capacity;
- malformed, stale-cookie, rate-limited, capacity-dropped, refreshed, and timed-out
  work remains separately observable.

The admission-only constructor intentionally retains this boundary for focused
security evidence.

## H0.2 — authenticated candidate promotion

A cookie-validated candidate may enter the existing manifest-bound handshake and
be promoted transactionally into one existing `NetworkProtocolPeer`.

Accepted behavior:

- `HostedCandidateId(slot,generation)` fences candidate reuse;
- credentials become immutable while a candidate is promoting;
- duplicate identical key exchanges reuse the candidate, reserved `ConnectionId`,
  ECDH result, transcript/master secret, signature, and cached server flight;
- conflicting credentials on the same path cannot mutate an in-flight promotion;
- ECDH/signature work occurs only after cookie validation;
- malformed keys, signer failure, capacity pressure, timeout, or invalid finish
  publish no route/secure session;
- only a valid client secret-possession proof transactionally registers the exact
  path, establishes the reserved `ConnectionId`, creates the server peer, and
  publishes one session;
- terminal success is committed locally before the terminal server flight is sent;
- a rejected/lost first terminal flight does not create another session;
- repeated exact `ClientFinish` resends the cached terminal acknowledgement;
- the client verifies the pinned-server canonical transcript and proves possession
  of the derived secret;
- promoted sessions expose transport/application-session identity upward; Game
  Admission still creates/binds `ParticipantId` separately;
- established traffic reuses the normal lanes, replay, pacing, batching, drain, and
  application-proof contracts.

No Hosted-specific secure packet format or replication path exists.

## H0.3 — authenticated IPv4 path ownership / rebinding

An endpoint is a mutable transport path, not connection identity.

The accepted rebinding lifecycle is:

```text
connection C owns path A / path generation G
        |
authenticated Request from unregistered path B
        |
one bounded pending attempt for C
        |
server Challenge bound to B + attempt + new generation
        |
client Proof from exactly B
        |
atomic listener route A -> B
        |
server Commit on B
```

Accepted behavior:

- ordinary/malformed bytes from an unknown path never route to or mutate the secure
  peer;
- one dedicated 256-bit path key is derived from authenticated handshake material,
  connection/security identity, and protocol manifest;
- Request/Challenge/Proof/Commit are canonical manifest-bound authenticated control
  flights;
- each established session owns at most one pending rebinding attempt, bounded by
  session capacity and monotonic timeout;
- the challenge records the server-observed replacement IPv4/port; proof must return
  from that exact path with the exact attempt/generation/nonce/challenge binding;
- the old path remains authoritative until proof succeeds;
- valid proof commits listener route + `HostedPathGeneration` before terminal Commit;
- the connection/security/replay/lane/application state is retained; rebinding never
  allocates a second connection/peer;
- lost/rejected Commit leaves the new path authoritative and exact duplicate Proof
  can recover the terminal flight;
- timeout/cancel before Proof leaves the old path untouched;
- once Proof has been sent, missing terminal confirmation cannot safely imply the old
  path remains valid; terminal client failure closes rather than guessing;
- stale generation/attempt, wrong-path, corrupt, or delayed proofs cannot reclaim
  ownership;
- native datagrams queued for the old endpoint are discarded before route movement;
- retirement clears path-key and pending/cached path-control material.

The current binding is explicitly IPv4. IPv6 is future reviewed work, not an
implicit extension of the accepted path encoding.

## H0.4 — connection-owner packet-memory handoff

H0.4 separates listener/socket ownership from connection/protocol ownership without
sharing mutable packet buffers.

```text
listener-owned fixed packet slot
        |
transactional exclusive transfer token
        |
connection-owner ingress queue
        |
authenticate / process on owning thread
        |
owner->listener egress transfer
        |
release slot to owning pool
```

`HostedUdpConnectionOwnerHandoff` freezes these rules:

- `HostedConnectionOwnerId` and `HostedPacketTransferId` are generation-fenced;
- listener thread is sole ingress producer / egress consumer;
- each registered owner thread is sole ingress consumer / egress producer for its
  fixed queues;
- one fixed payload slab owns packet bytes; transfer moves exclusive ownership,
  never a shared mutable packet object;
- the receiver obtains a read-only span while it owns the transfer;
- exact `ConnectionId` generation binds to exactly one owner generation;
- binding/migration/unbinding fail while packets for that connection remain queued
  or acquired;
- ingress queue, egress queue, and global packet-capacity failure are separate and
  transactional;
- failed offer publishes neither bytes nor a transfer token;
- owner close/reclaim rules require explicit quiescence/failure handling;
- stale/foreign/returned/double-released transfer identities cannot regain packet
  ownership;
- listener may route a clear connection claim to an owner queue but does not
  validate H0.3 HMAC/path authority; the owning secure endpoint does;
- path-control output returns through the same bounded owner->listener mechanism;
- the handoff contains no Game, participant, AOI, replication-profile, or UI
  vocabulary.

This is intra-server-process ownership machinery. It is not multi-host or
server-process connection migration.

## H0.5 — dedicated server process and independent clients

H0.5 composes the accepted mechanisms into the first realistic deployment topology
without creating another protocol stack:

```text
dedicated server process
    listener thread
        external admission
        native/shared UDP socket
        route/path table
        packet slab
            |
            v
    fixed connection-owner threads
        NetworkProtocolPeer
        caller-supplied application adapter

independent client processes
    HostedUdpProtocolClient
    existing Client/Network application adapters
```

The listener remains Network-generic. Promotion constructs the secure peer and
application adapter on the selected owner thread. Owner-produced secure/path-control
datagrams return through the exclusive H0.4 slab.

A key composition bug was found and permanently fixed during H0.5: an owner queue is
shared by all connections assigned to that owner, so one peer may not drain/discard
foreign leases. The owner runtime acquires each lease once, demultiplexes it to the
exact generation-matching peer, and releases it only after that peer consumes it.

Permanent H0.5 acceptance proves:

- multiple promoted sessions share one owner without cross-connection lease loss;
- listener-thread access to owner-bound peers is rejected;
- H0.3 Request/Challenge/Proof/Commit completes across the owner split;
- lost first Commit recovers through exact duplicate Proof on the newly authoritative
  path;
- session retirement waits for owner-thread quiescence, releases the generation-
  fenced binding, and makes capacity reusable;
- packet leases and owner queues drain to zero;
- host disposal requests worker stop, observes join/quiescence, then performs any
  required failed-owner reclamation.

### Dedicated-process evidence

The accepted H0.5 harness uses:

- one dedicated server process;
- four connection-owner threads;
- eight independent client processes;
- two fresh connection phases per client;
- 24 authoritative updates per phase;
- real cookie admission and deployment-key pinning;
- authenticated promotion and secure lanes;
- the shared native-batched listener and H0.4 handoff;
- public WorldState, owner-private latest state, retained Event, client Control,
  application proofs, one public cross-chunk migration, and one authenticated path
  rebind per phase.

All 16 application sessions/reconnect phases and 16 rebinds converged; the accepted
run recorded 384 public updates, 384 owner states, 384 retained Events, no disclosure
mismatch, no handoff capacity drop, and no packet lease remaining.

This closed the process-topology gate used when `ChunkPublication` was selected as
the current game's dynamic-public profile. It did **not** establish a public-WAN or
production-CCU deployment claim.

## Game/replication independence

Hosted H0 proved the lower deployment lifecycle with the then-selected N1 adapter,
but Hosted Network does not own N1 semantics.

The current Game-owned public profile catalog contains five concrete dynamic-public
profiles. They all flatten to the same Network session/lane boundary. Owner-private
Inventory/UI traffic also uses the same lower authenticated protocol without entering
public AOI.

Later real-consumer acceptances (the ECS woodland actor and remote Inventory UI)
strengthen application composition but do not widen H0.5's deployment claims. In
particular, they do not turn localhost process evidence into WAN readiness.

## Security and failure policy

Expected remote rejection is transactional. Before secure/application publication,
malformed input, stale identity, failed authentication, rate/capacity pressure, and
invalid path control must leave no partial route/session authority.

Once a terminal ownership change is committed locally (successful promotion or
rebinding), loss of the terminal network flight does not roll back authoritative
local ownership. Recovery uses the exact cached/reconstructible terminal protocol
semantics or fails closed.

Unauthenticated traffic must never allocate unbounded memory, trigger unbounded
cryptographic work, or create amplification beyond the accepted policy.

## Current exclusions / non-claims

H0.5 is an accepted localhost deployment/process gate. The following remain
**explicitly unclaimed**:

- public-WAN readiness;
- production remote CCU;
- production admission/rate-limit values;
- IPv6 admission/rebinding;
- multi-host or cross-server-process live connection migration;
- DDoS-provider / edge-proxy integration;
- NAT traversal / relay service;
- global load balancing / matchmaking;
- automatic server-owner failover;
- external certificate/PKI lifecycle beyond deployment-key pinning;
- permission to infer account/participant identity from connection/path identity.

Do not hide any of these inside unrelated replication, gameplay, or performance work.
Each requires a separately reviewed deployment/security checkpoint.

## Performance / evidence interpretation

Hosted process/socket evidence is kept separate from Game semantic profile evidence.
A valid report states which work is included:

```text
Game semantic replication
Network protocol/security/packetization
listener/socket work
client decode/application
process/scheduler overhead
end-to-end freshness
```

Same-process or localhost results are not WAN latency/RTT measurements. The accepted
H0.5 client count is a topology correctness gate, not a production capacity number.

Deterministic N1.2/N1.3 impairment tests remain the authoritative loss/dup/reorder/
corruption/snapshot-recovery evidence for the selected chunk client/server semantics.
H0.5 did not need to repeat every deterministic impairment permutation in separate
processes.

## Current stop / forward policy

Hosted Network is consumption-ready for local/dedicated application development.
Do not reopen Hosted architecture merely to extend benchmark scale.

New Hosted work must be earned by a concrete deployment requirement such as:

- public remote/WAN exposure;
- IPv6;
- deployment-rate policy;
- NAT/relay requirements;
- multi-host ownership/migration;
- demonstrated listener/owner capacity or security pressure.

Until then:

- keep H0.0-H0.5 semantics frozen;
- keep endpoint identity separate from connection identity;
- keep connection identity separate from Game identity;
- keep Game/AOI semantics out of the listener/handoff;
- retain bounded packet ownership and transactional admission/rebinding;
- do not claim production CCU or WAN readiness from localhost evidence.

Full checkpoint history and measurements remain in git and the accepted Hosted
baseline artifacts under `_Benchmarks/Baselines`.
