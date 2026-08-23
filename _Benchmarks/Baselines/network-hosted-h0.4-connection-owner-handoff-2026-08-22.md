# Hosted H0.4 connection-owner handoff — 2026-08-22

This immutable implementation-stop artifact records the bounded packet-memory
ownership seam between one hosted listener thread and connection/protocol owner
threads. It does not claim H0.5 dedicated-process deployment, public-WAN readiness,
IPv6, Game Admission, N1 adoption, or production CCU.

## Frozen shape

```text
listener/socket owner
    exact ConnectionId -> generation-fenced owner
    fixed packet slot + immutable metadata
                 |
                 v
       bounded owner ingress
                 |
                 v
connection owner
    H0.3 path-key authentication
    NetworkProtocolPeer / crypto / lanes
                 |
                 v
        bounded owner egress
                 |
                 v
listener/socket owner
```

The clear H0.3 `ConnectionId` is routing information only. A deliberately corrupted
HMAC Request reaches the correct owner and is rejected there; it never authorizes
path ownership. A valid Request is authenticated by the owner and produces a
canonical Challenge targeted to the exact listener-observed seven-byte IPv4 path.

## Bounds and retained evidence

```text
HostedConnectionOwnerId       8 B
HostedPacketTransferId        8 B

test owners                     2
test connections                8
test packet slots              16
test queue slots / direction    8
test maximum datagram         512 B
known reserved payload      9,184 B
```

Every packet offer copies into one fixed slab before publishing a token. Full
owner queues and exhausted global packet capacity fail independently with no
partial publication. Binding migration/unbind refuses outstanding packet leases.
Graceful owner close refuses bindings or leases; explicit failed-owner close is
allowed only after its thread has stopped and reclaims/clears all bounded state.
Owner and packet slot reuse advance generations. Duplicate release is rejected.

Permanent H0.4 acceptance:

```text
HostedOwnerAndPacketIdentitiesAreGenerationFenced
HostedOwnerIngressEgressTransfersAreExclusive
HostedOwnerQueuesAndPacketCapacityFailTransactionally
HostedOwnerFailureReclaimsAndFencesSlotReuse
HostedConnectionMigrationAndReuseAreFenced
HostedClearPathConnectionRoutesButDoesNotAuthenticate
HostedSeparateConnectionOwnerProcessesSecureProtocol
WarmedHostedOwnerHandoffAllocatesZero
```

The separate-thread canary carries an authenticated reliable request and response
through the existing `NetworkProtocolPeer` while the listener and protocol owner
retain distinct packet-memory roles. The warmed bidirectional handoff reports zero
managed allocation after runtime/monitor priming.

The accepted H0.0-H0.3 external host remains the single-owner behavioral control.
H0.5 must compose this mechanism with the real external listener and independent
server/client processes before final N1 remote adoption can be claimed.
