# Hosted H0.5 dedicated/sharded composition — 2026-08-22

This immutable implementation-stop artifact records one dedicated server process,
eight independent externally admitted client processes, and four fixed connection-
owner threads through the accepted H0.0-H0.4 mechanisms. It composes the unchanged
N1 scheduler and remote chunk adapters with the production client semantic cache.
It is localhost process/socket evidence, not public-WAN, production-CCU, IPv6,
multi-host, N1.5, 2K/4K, or N0-deletion authority.

The canonical observation is retained in
`network-hosted-h0.5-dedicated-sharded-2026-08-22.json`.

## Process and semantic shape

```text
dedicated server process
    one native-batched shared UDP listener
    four fixed connection-owner threads
    H0.4 bounded packet slab / queues
    unchanged N1 scheduler + RemoteChunkWorldStateServer

eight independent client processes
    HostedUdpProtocolClient
    pinned deployment identity and external admission
    unchanged RemoteChunkWorldStateClient + semantic cache

per client
    two fresh connection/application phases
    24 authoritative updates per phase
    one chunk-boundary migration per phase
    one authenticated H0.3 path rebind per phase
```

Each phase carries public `WorldState`, private `OwnerState`, retained `Event`,
client `Control`, chunk application proof, and Event application proof. Completion
is acknowledged only after the final public revision and retained Event have been
proved at the server. Ordinary unreliable public state is reoffered from the
recipient's still-applied cursor; snapshot recovery remains reliable Bulk.

## Convergence and ownership result

| Observation | Result |
| --- | ---: |
| Expected/completed applications | 16 / 16 |
| Public semantic updates proved | 384 |
| Public snapshots / deltas applied | 32 / 368 |
| Private owner states applied | 384 |
| Retained Events applied/proved | 384 / 384 |
| Client Controls received | 400 |
| Authenticated path rebinds | 16 |
| Disclosure violations | 0 |
| Unhandled owner application messages | 0 |
| Path-control rejections | 0 |
| Handoff capacity/stale drops | 0 |
| Queued ingress / egress at stop | 0 / 0 |
| Rented packet slots at stop | 0 |

The first multi-connection composition run exposed a real ownership defect: an
individual peer drained the owner-shared ingress queue and discarded leases for
other connections assigned to the same owner. The accepted implementation instead
has the owner drain each lease once, demultiplex by exact generation-fenced
`ConnectionId`, stage it only into that peer, and release it after consumption.
A permanent two-clients/one-owner regression freezes this behavior.

Host shutdown requests all owner workers to stop, joins them, and only then invokes
failed-owner reclamation. This satisfies H0.4's thread-quiescence precondition;
the captured handoff released all 4,464 acquired leases and retained none.
Explicit session retirement likewise waits for that session's owner-side transport
to stop before clearing session secrets, then releases the H0.4 binding. A
permanent retire/reconnect regression proves the capacity is immediately reusable.

## Split observation

These are aggregate cold process-run observations, not warmed percentile claims.

| Server observation | Result |
| --- | ---: |
| Wall time | 946.018 ms |
| Process CPU | 390.000 ms |
| Game semantic work | 46.711 ms |
| Listener/socket pumps | 82.856 ms |
| Owner protocol/application passes | 78.240 ms |
| Listener + owner transport total | 161.096 ms |
| Listener pumps | 820 |
| Native send / receive calls | 587 / 450 |
| Hosted known reservation | 22,051,906 B |
| Listener known reservation | 3,145,936 B |
| Owner-runtime known reservation | 3,172,311 B |

Across 384 authoritative client observations, end-to-end latency was 6.106 ms
mean, 5.382 ms p50, 10.088 ms p95, 45.123 ms p99, and 50.210 ms maximum. The
large cold-start tails belong to the independent-process localhost run and are not
presented as a steady-state latency target. Client socket/protocol work totaled
210.705 ms and client semantic application totaled 44.291 ms across all workers.

## Checkpoint conclusion

H0.5 proves that the accepted external-admission, secure-session, H0.3 path-
ownership, H0.4 packet-ownership, and N1 application-proof contracts compose across
one dedicated server and independent client processes without a hosted-specific
replication path. Together with the retained N1.4 representative, mixed-lane, and
shared-listener matrix, this closes selection of chunk-publication replication for
the current UltraSim game.

N0 remains compiled as historical/reference evidence. This checkpoint does not
start N1.5, authorize N0 deletion, establish production admission-rate policy, or
claim public Internet readiness or production CCU.
