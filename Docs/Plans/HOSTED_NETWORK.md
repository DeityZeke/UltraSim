# Hosted Network H0: externally admitted shared UDP

Status: active separate Network milestone. H0.0-H0.2 are implemented and stopped
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
caller-owned signer lifetime, literal cookie-manifest
binding, and zero warmed managed allocation for secure delivery.

Path ownership is deliberately strict in H0.2. Only the exact path established by
the handshake routes to the secure peer. No endpoint change is interpreted as
rebinding, and an IP endpoint is still not connection or participant identity.

Do not create a second secure packet format or a hosted-only replication path.

## H0.3 — path ownership and rebinding

Freeze one explicit policy for endpoint changes after authentication:

- an IP/port is never identity;
- bytes from an unproven replacement path cannot mutate the established peer;
- an authenticated path challenge/response (or an explicitly selected stricter
  policy) fences rebinding;
- the old path remains valid or retires according to one bounded transition;
- stale-path packets and delayed rebind proofs cannot reclaim current ownership;
- IPv6 is added deliberately here or in a named follow-up, not silently inferred
  from the IPv4 H0.1 layout.

## H0.4 — connection-owner sharding

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

Stop after H0.2 review. Do not begin H0.3 path rebinding, owner sharding, N1.5,
N0 deletion, 2K/4K, public-WAN exposure, or production CCU claims until this
authenticated-promotion boundary is accepted.
