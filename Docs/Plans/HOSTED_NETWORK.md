# Hosted Network H0: externally admitted shared UDP

Status: active separate Network milestone. H0.0-H0.4 are implemented and stopped
for review. N1.0-N1.4 replication semantics remain unchanged and N1.5 has not
started.

This milestone exists to prove the lifecycle between an unknown external UDP path
and an authenticated UltraSim protocol session. It is not another replication
profile and it does not move game admission, AOI, or participant identity into
`Kernels/Network`.

## Frozen boundary

```text
unknown UDP endpoint
        |
        v
Hosted Network
    deployment-owned server identity
    bounded unauthenticated admission
    cookie / anti-amplification / rate policy
    authenticated transport-session promotion
    path ownership and rebinding
    connection-owner packet transfer
        |
        v
Game Admission / participant binding
        |
        v
existing protocol adapters
        |
        +--> N0 entity-relation replication
        |
        +--> N1 chunk-publication replication
```

Network identity is not an account, participant, ECS entity, chunk, or IP address.
An endpoint is only a currently observed transport path. Hosted Network must
remain indifferent to the active Game replication profile.

The accepted framing, manifest, lanes, secure outer protocol, application-proof
separation, shared listener, and native batching remain in place. H0 extends the
lifecycle around them; it does not replace them.

## H0.0/H0.1 implementation stop

`HostedUdpAdmissionHost` now owns the first externally sourced server boundary:

- the caller supplies an explicit IPv4 bind endpoint and a deployment-owned
  `NetworkServerIdentity`;
- clients can copy/pin the server public key through an explicit bounded copy;
- no self-trusted or in-process client is constructed by this host;
- the listener owns one random 256-bit cookie key for its lifetime and clears it
  on disposal;
- a canonical `ClientHello` from an unknown path receives only a stateless cookie
  challenge whose MAC is literally bound to protocol manifest version/fingerprint,
  client nonce/key, source IPv4/port, and
  current monotonic time bucket;
- challenges are limited per source IPv4 address and bucket by a fixed-capacity,
  stale-reusable table;
- a challenge is emitted only when its bytes do not exceed the triggering hello,
  making the H0.1 unauthenticated response factor at most 1:1;
- current and immediately previous cookie buckets validate; older cookies fail;
- no endpoint route, `ConnectionId`, ECDH object, protocol peer, or Game session
  is allocated before cookie validation;
- validated candidates occupy fixed nonce/public-key rows, deduplicate by source
  path, fail closed at capacity, and expire on a monotonic deadline;
- malformed, invalid-cookie, rate-limited, capacity-dropped, refreshed, and timed-
  out work is separately observable;
- normal shared-loopback hosts retain their old unknown-path drop behavior.

The focused H0.1 tests prove stateless pre-cookie behavior, public-key pin copy,
previous-bucket acceptance, stale-cookie rejection, candidate deduplication,
candidate and rate capacity, timeout reclamation, non-amplification, malformed
flood containment, and zero warmed allocation for a 64-hello receive/challenge
pass.

The admission-only constructor retains this exact H0.1 stop for isolated evidence.
The H0.2 constructor additionally receives explicit protocol limits and enables
authenticated promotion.

## H0.2 implementation stop — authenticated candidate promotion

`HostedUdpAdmissionHost` now promotes a validated candidate through the existing
handshake cryptography and protocol manifest into the existing
`NetworkProtocolPeer` machinery. `HostedUdpProtocolClient` supplies the matching
external-process-capable client side without constructing or self-trusting a
server identity.

Accepted behavior:

- `HostedCandidateId(slot,generation)` fences candidate-slot reuse;
- validated credentials become immutable while a candidate is `Promoting`;
- duplicate identical key exchanges reuse the same candidate, reserved
  `ConnectionId`, ECDH result, transcript, master secret, signature, and cached
  `ServerKeyExchange` flight;
- different credentials on that path cannot mutate an in-flight promotion;
- ECDH/signature work occurs only after cookie validation, and malformed keys,
  signer failure, capacity pressure, timeout, or invalid finish publish no route
  or secure session;
- the deployment owns the `NetworkServerIdentity`; the host retains it for
  transcript signatures but never disposes it;
- a valid `ClientFinish` registers the exact IPv4/port path, advances the reserved
  connection, creates the server peer, and publishes one session as one
  transaction;
- terminal success is committed locally before `ServerFinish` is submitted; a
  rejected or lost first terminal flight leaves the one established session in
  place, and a repeated `ClientFinish` resends its cached acknowledgement;
- the client pins the deployment public key out of band and verifies the canonical
  manifest-bound transcript before proving possession of the derived secret;
- duplicate `ClientFinish` on the established path resends the cached
  `ServerFinish` without creating another candidate, route, connection, ECDH key,
  or peer;
- protocol mismatch, signature failure, malformed finish, timeout, and capacity
  pressure publish no partial session;
- the promoted session exposes only transport/session identity to the Server
  adapter; Game Admission still creates/binds `ParticipantId` separately;
- established traffic uses the existing lanes, replay windows, pacing, batching,
  drain, and application-proof contracts.

The permanent H0.2 acceptance uses independent client/server sockets and proves
the pre-finish zero-route boundary, bidirectional secure application traffic,
candidate reuse/conflict fencing, malformed-key and invalid-finish rollback,
timeout and connection-capacity reclamation, duplicate-finish recovery, scalar/
native receive parity, terminal-send failure recovery without a second session,
an immediate canonical `ServerFinish` after exactly one raw `ClientFinish`, caller-
owned signer lifetime, literal cookie-manifest binding, and zero warmed managed
allocation for secure delivery.

Path ownership is deliberately strict in H0.2. Only the exact path established by
the handshake routes to the secure peer. No endpoint change is interpreted as
rebinding, and an IP endpoint is still not connection or participant identity.

Do not create a second secure packet format or a hosted-only replication path.

## H0.3 implementation stop — authenticated path ownership

H0.3 freezes one concrete IPv4 endpoint-change policy without making an endpoint
the connection identity:

```text
established connection C owns path A / generation G
        |
authenticated Request arrives from unregistered path B
        |
one bounded pending attempt for C
        |
server Challenge bound to B + attempt + generation G'
        |
client Proof from B
        |
atomic listener route A -> B
        |
server Commit on B
```

Accepted behavior:

- ordinary or malformed bytes from an unknown path never route to or mutate the
  established secure peer;
- a dedicated 256-bit path key is derived from the authenticated handshake master,
  exact connection/security identity, and protocol manifest, then retained only
  by the transport session endpoints;
- Request, Challenge, Proof, and Commit are canonical manifest-bound HMAC flights;
  the 91-byte Request proves session-key possession before the server emits the
  134-byte Challenge;
- every established session owns at most one pending attempt, bounded by existing
  session capacity, with a monotonic attempt floor, generation-fenced replacement,
  and the existing validated-candidate timeout as its deadline;
- the Challenge records the server-observed replacement IPv4/port. Proof must
  return from that exact path and echo the attempt, generation, nonce, challenge,
  and path binding under authentication;
- the original path remains current while Request/Challenge is pending. A valid
  Proof commits the listener route change and `HostedPathGeneration` first, then
  sends Commit; it never allocates a second connection or secure peer;
- a lost/rejected Commit leaves the new path authoritative. Repeated exact Proof
  on that now-known path resends the cached Commit and converges the client;
- server timeout and client cancellation before Proof leave the current route
  untouched. Once Proof has been sent, the client cannot safely infer ownership
  from a missing Commit; terminal timeout fails the client transport closed instead
  of falling back to a potentially retired path;
- stale generation, stale attempt, wrong-path, corrupt, or delayed proofs cannot
  reclaim ownership;
- the client swaps the existing secure peer to its new connected UDP endpoint only
  after authenticating Commit. Existing lane, packet-number, replay, congestion,
  and application state survive unchanged;
- queued native server datagrams for the old endpoint are discarded before the
  route moves, so no retained send is submitted to a retired path;
- retirement clears the path key and all pending/cached path-control material;
- IPv6 remains a named follow-up and is not inferred from this exact seven-byte
  IPv4 path binding.

The permanent H0.3 acceptance proves repeated rebinding with one connection/peer,
bidirectional secure traffic after the move, local-commit recovery after a lost
Commit, stale/wrong-path rejection after later generations, timeout preservation
of the old path, scalar/native receive parity, fixed canonical flight sizes, and
zero warmed managed allocation for ordinary secure delivery after rebinding.

## H0.4 implementation stop — connection-owner sharding

Separate listener/socket ownership from connection/protocol ownership without
sharing mutable packet buffers.

```text
listener-owned packet slot
    -> transactional ownership transfer
    -> bounded connection-owner ingress
    -> authenticate/process
    -> release to the owning pool
```

Required evidence includes full/closed/stale destination queues, owner failure,
shutdown, connection migration/reuse fencing, no double release, and zero warmed
allocation on the admitted handoff. Do not pass shared packet objects between
owners and do not teach the handoff about Game messages.

`HostedUdpConnectionOwnerHandoff` now provides the concrete bounded mechanism:

- `HostedConnectionOwnerId` and `HostedPacketTransferId` are fixed eight-byte,
  generation-fenced identities;
- the listener thread is the sole ingress producer and egress consumer, while
  every registered connection thread is the sole ingress consumer and egress
  producer for its fixed queues;
- one fixed payload slab owns all packet bytes. Publication moves an exclusive
  slot token between owners; the receiver obtains only a read-only span, and
  release clears the slot before it returns to listener capacity;
- exact `ConnectionId` generation maps to one exact owner generation. Binding,
  migration, and unbinding fail while any packet for that connection remains
  queued or acquired;
- ingress/egress queue capacity and global packet capacity fail separately and
  transactionally. A failed offer publishes neither bytes nor a transfer token;
- graceful owner close requires zero bindings and leases. After the failed owner
  thread has stopped, the listener can explicitly reclaim its bounded bindings,
  queued packets, and acquired leases; reuse advances owner generation;
- stale, foreign, returned, or double-released transfer identities cannot regain
  packet ownership;
- an H0.3 Request/Proof clear `ConnectionId` may select an owner queue, but the
  listener does not validate the HMAC or authorize a route change. The connection
  owner receives the exact observed path binding and performs path-key validation;
  authenticated path-control output returns through the owner-to-listener queue;
- `HostedUdpConnectionOwnerEndpoint` proves an existing `NetworkProtocolPeer`
  can own protocol/crypto state on a separate thread while listener-owned packet
  memory crosses the bounded handoff in both directions;
- the handoff contains no Game, participant, AOI, N0, or N1 vocabulary.

The permanent acceptance covers ingress/egress exclusivity, full queues, packet
pressure, graceful and failed-owner close, generation reuse, connection migration
and reuse, exact no-double-release behavior, clear-ID-with-invalid-HMAC rejection,
valid owner-authenticated H0.3 challenge production, a separate-thread secure
protocol request/response, and zero warmed managed allocation. The measured
two-owner/16-packet/512-byte test reservation is 9,184 bytes.

H0.4 freezes the cross-owner mechanism and secure-protocol ownership canary. The
accepted H0.0-H0.3 host remains the single-owner behavioral control. H0.5 must
compose this mechanism with the real external listener and dedicated process
topology; H0.4 does not claim that sharded deployment result in advance.

## H0.5 — dedicated server and sharded clients

Run a genuine dedicated server process with multiple independent client workers
through the externally admitted listener. Preserve separate evidence for:

1. server semantic replication;
2. server transport/security/packetization;
3. client decode/application;
4. end-to-end freshness and process/socket cost.

H0.5 is the remaining N1 remote-adoption gate. It must repeat representative
roaming, mixed public/private/Event/Control traffic, application proof, loss,
reorder, reconnect, and bounded shutdown without weakening disclosure or identity
contracts.

## Current stop

Stop after H0.4 review. Do not begin H0.5 dedicated/sharded process evidence,
N1.5, N0 deletion, 2K/4K, public-WAN exposure, or production CCU claims until
the connection-owner transfer boundary is accepted.
