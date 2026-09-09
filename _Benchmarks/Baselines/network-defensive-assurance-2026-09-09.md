# Network defensive-assurance correction — 2026-09-09

## Scope

- Independent review base: repository `main` at
  `f080ff2a377585f27183840a359aa599f665926b` before this correction.
- External report base: `91989cfbe8b0bd5421d955b9459335b1c604a59e`.
- The external probe sources were not supplied or used. Each claim was traced in the
  maintained source and reproduced or fenced with new repository-owned tests.
- This is a correctness/lifetime correction, not a new authority, replication, or
  content-identity checkpoint.

## Independent disposition

| Review item | Disposition | Permanent evidence |
| --- | --- | --- |
| NET-01 delayed ACK crosses send epoch | Confirmed and repaired | `DelayedAcknowledgementCannotCrossSendingSecurityEpoch` |
| NET-02 scalar idle close retains admission/application | Confirmed and repaired | `HostedScalarIdleClosureRetiresAdmissionAndPeerState` |
| NET-03 sharded lifetime differs from scalar | Confirmed and repaired | sharded application-retirement and idle-closure tests |
| NET-04 Direct Connect hides automatic closure | Confirmed and repaired | `DirectConnectObservesAutomaticTransportClosureAndReconnects` |
| NET-05 owner failure interrupts cleanup | Confirmed and repaired | owner-progress and application-dispose failure cleanup tests |
| NET-06 packet token accepted by foreign pool | Confirmed and repaired | expanded foreign-pool token test across access/transfer/return |
| NET-07 one stalled HTTP response blocks all | Confirmed and repaired | `StalledContentResponseCannotBlockIndependentDownload` |
| H-01 cold content blocks protocol/UI pump | Source-confirmed and hardened | `ContentAcquisitionRunsOutsideProtocolPumpAndCancelsWithSession` |
| H-02 owner ingress pass unbounded under refill | Source-confirmed and hardened | `HostedShardedIngressBudgetPreservesApplicationProgress` |
| V-01 application-suite failures | Not reproduced canonically | complete application suite passed repeatedly and in exact-tree verification |

## Corrected contracts

### Epoch-scoped acknowledgement

The authenticated ACK payload now carries the exact opposite-direction sending
`SecurityEpoch` before its packet-number window. A valid delayed ACK for an older
epoch is ignored for flight, RTT, and congestion state. This is transport wire
revision 2; the revision participates in every protocol-manifest fingerprint, so an
old/new deployment fails protocol agreement rather than misreading the new frame.

### Complete Hosted lifetime

Scalar and sharded Hosted modes use one lifetime decision for application-requested
retirement, idle transition, reliable drain, and drain deadline. Sharded mutation
still occurs on the connection-owner thread; the listener reaps only a stopped
generation-matching transport. Terminal cleanup releases the application, peer,
endpoint/path, handoff binding, connection-table row, and admission slot.

Shutdown requests and joins every owner, reclaims each quiescent owner, attempts
independent application/peer/endpoint and listener cleanup, then reports collected
failures. Non-quiescent workers remain unreclaimed rather than creating use-after-free.

### Owner and client liveness

- `MaximumIngressTransfersPerPass` defaults to 256 and is visible with its observed
  peak in the sharded runtime snapshot.
- Direct Connect observes an underlying automatic drain/close before its established
  fast path, withdraws its peer, retires semantic consumers, and permits reconnect.
- `PacketBufferToken` is now 16 bytes and includes an issuing-pool identity in
  addition to slot/generation.

### Content service and cold acquisition

`StaticContentHttpHost` uses four fixed response workers by default, a bounded queue
of eight, and a 30-second per-response deadline. Queue overflow is rejected; one
stalled response no longer owns the accept/serve loop.

Authenticated cold content acquisition runs on one cancellable, session-fenced
worker rather than in the protocol/Godot pump. The owner accepts a completion only
when application epoch, requirement sequence, and plan hash still match. No catalog
proof or guest admission advances while acquisition is pending, and teardown cancels
and joins the operation before releasing source/cache lifetime.

## Compatibility and physical evidence

- Transport wire revision: `2`.
- Current UltraSim Game manifest: version `26`, fingerprint
  `493304622423153552` after the transport revision is included.
- Network capability probe: protocol `1/7552250e9cf6aac2` identically for
  ClientOnly, ServerOnly, and Full.
- Release DLL sizes in the capability gate:
  ClientOnly `204,288` bytes; ServerOnly `331,264` bytes; Full `348,672` bytes.
- Full remains the canonical NuGet artifact; stripped capability builds still reject
  canonical package publication.
- Historical accepted evidence retains the fingerprints measured at those commits.

## Verification

Focused Release gates passed:

- Network kernel/Hosted suite, including every new regression;
- 107 Network application checks, including shared dedicated/embedded authority;
- 15 Server.Hosting checks;
- 27 Client.Content checks;
- 134 Game.Replication checks after advancing the current manifest assertion;
- architecture/documentation link gate;
- Full/ClientOnly/ServerOnly API, role-presence, manifest, probe-byte, and package
  compatibility gate.

The complete exact-tree repository verifier passed. It included the real Direct
Connect server smoke, native Godot headless compositions, product graph/capability
inspection, the exported Contentless Online cold/warm/profile-change journeys, and
the Complete hosted packet-free-local plus two-remote-client journey.
