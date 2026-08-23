# Hosted Network H0.3 path-ownership evidence

Captured: 2026-08-22
Checkpoint: H0.3 implementation stop
Scope: authenticated IPv4 UDP endpoint ownership transfer on the existing secure session

## Frozen bounds

| Item | Bound |
| --- | ---: |
| Path identity | 4 B monotonic `HostedPathGeneration` |
| Path authentication key | 32 B / established session |
| Pending attempts | 1 / established session |
| Request | 91 B |
| Challenge | 134 B |
| Proof | 134 B |
| Commit | 134 B |
| Server session path/terminal byte arrays | 588 B / established session |
| Pending lifetime | `ValidatedCandidateTimeoutMilliseconds` |

The path arrays include cached H0.2 terminal flights, the derived path key,
pending nonce/challenge, cached Challenge, and the last Proof/Commit used for
terminal retry. They exclude object/array headers, the existing protocol peer,
listener route table, socket buffers, and operating-system state.

## Permanent acceptance

Debug and Release run these H0.3 gates in addition to all retained Network and
Hosted H0.1/H0.2 gates:

```text
HostedPathGenerationIsFixedWidthAndMonotonic
HostedPathRebindPreservesSessionAndSecureTraffic
HostedPathCommitLossRecoversOnTheCommittedPath
HostedStaleOrWrongPathProofCannotReclaimOwnership
HostedPathRebindTimeoutPreservesTheCurrentPath
HostedPostProofTimeoutFailsClosedInsteadOfReusingOldPath
HostedPathRebindNativeReceiveMatchesScalarLifecycle
WarmedHostedSecureDeliveryAfterPathRebindAllocatesZero
```

The suite proves that Request/Challenge does not replace the current path; only
an authenticated Proof from the challenged address commits the listener route.
One `ConnectionId`, session object, client/server secure peer, lane state, and
registered route survive multiple moves. A rejected first Commit recovers by an
exact duplicate Proof on the already-committed new path. Stale earlier-generation
proofs from another path do not reclaim ownership, and pending timeout leaves the
old route usable. If terminal Commit never arrives after Proof, the client fails
closed rather than guessing that its possibly retired old path still owns the
connection. Scalar and native receive paths converge identically. Ordinary
warmed secure delivery after the move allocates 0 managed bytes on the owner
thread.

## Interpretation

This proves one explicit owner-thread IPv4 path transaction. It is not transparent
mobile networking, multipath, IPv6, connection-owner sharding, Game Admission,
account/participant identity, a public-WAN deployment claim, or an N1 semantic
change. H0.4 must independently earn bounded packet-memory transfer between the
listener owner and connection owners. H0.5, N1.5, N0 deletion, and 2K/4K remain
unstarted.
