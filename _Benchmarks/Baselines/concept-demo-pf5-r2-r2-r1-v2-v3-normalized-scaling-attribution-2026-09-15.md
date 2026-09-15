# Concept Demo PF5-R2-R2-R1 — Corrected V2/V3 Scaling Attribution

**Outcome D, narrowed: workload activity/fanout and timing-scope differences are material, but
the evidence still does not prove that V3's remaining recipient-side cost is an unavoidable price
of exact proof or transport security. No production semantic or policy changed.**

## Lineage and environment

| Item | Commit |
| --- | --- |
| Review starting HEAD | `1808aeae17e727b86b4a14a81607995828d4e80b` |
| PF5-R2 replication semantic candidate | `992fc541460f18534da9036c73cb711277de11ce` |
| HTTP repair / measurement base | `89f26f8bbaa1003b15ddf52d61d6ce2567694b74` |
| Corrected benchmark implementation | `b07f8919e0ccd9080eae6407ec12fad2825e5ee7` |

The accepted run used .NET 8.0.29 on the 16-logical-processor Linux benchmark host, with 30
warmup ticks, 270 rated ticks, and 90 separate detailed-attribution ticks. The selected v29
production policy was unchanged. This remains a synthetic architecture comparison, not product
CCU evidence.

The machine-readable companion is the source of record for complete mean/p50/p95/p99/max timing,
semantic work, protocol, compression, aggregation, memory, and wire counters.

## Corrected server-owned timing

Code tracing confirmed that `P03ChunkAdapter.ApplyAuthority` performs two pieces of shared remote
publication work before the established `NetworkAndRemoteReplication` timer:

```text
ChunkPublication commit
    -> PreparedChunkPublication creation
    -> ChunkPublicationWorkScheduler.ObserveCommit
       (dirty chunks x subscribed memberships; queues recipient work)
    -> NetworkAndRemoteReplication begins
```

The former PF5-R2-R2 server-budget sample added back only prepared-source creation. It omitted the
recipient wakeup/commit-observation stage. The corrected per-tick metrics are:

```text
RemotePublicationPreparation
RecipientCommitObservation
NetworkAndRemoteReplication

ServerOwnedRemoteReplicationBudget = same-tick sum of all three
```

The distribution is computed from those directly paired per-tick sums. No percentiles are added.
`AuthorityMutation` still measures the encompassing mutation/commit interval, and
`BaseAuthoritativeMutation` subtracts both separately measured remote-owned slices.
`AuthorityThroughNetworkAndRemoteReplication` remains the continuous conservation clock and still
contains in-process client work; it is not the four-millisecond server-owned budget.

The focused boundary gate proves that wakeup occurs after prepared-source creation and before the
authority interval ends, that all three components have one sample per measured tick, that wakeup
is nonzero for the exercised workload, and that the server-budget mean reconciles exactly to the
three same-tick component means. Moving or omitting wakeup from the owned budget breaks the gate.

For the current production-shaped D workload:

| R | Prep mean ms | Recipient wakeup mean ms | Network+rep mean ms | Corrected server mean / p95 ms |
| ---: | ---: | ---: | ---: | ---: |
| 128 | 0.0024 | 0.0417 | 2.6440 | 2.6882 / 3.1600 |
| 192 | 0.0022 | 0.0412 | 3.8241 | 3.8675 / 4.2814 |
| 256 | 0.0023 | 0.0743 | 5.1564 | 5.2330 / 5.7973 |
| 300 | 0.0038 | 0.1479 | 6.5162 | 6.6679 / 7.2877 |
| 350 | 0.0024 | 0.1240 | 7.1395 | 7.2659 / 7.9543 |

Thus the omission is real and now charged exactly once. It does not explain the large V2/V3 gap.
The corrected production-shaped server-owned crossing remains between 128 and 192 synthetic
recipients.

## Matched controls

The original A/B/C/D rows deliberately described different workload shapes. Their previous
percentage labels are valid combined-workload differences, not isolated causal estimates. They
must not be quoted as “entity population cost” or “activity cost.”

Three new controls at each population hold constant:

```text
two-metre, 20-column positions
V2-like AOI
cadence 1/2/3
current V3 protocol/proof/transport
a deterministic distributed-prefix mutation ordering
```

The E=R and E=1000 low-mutation rows mutate the exact same 20 common entities. The high-mutation
row retains those 20 and varies only the number of selected mutations to approximately 35% of R.
The distributed prefix prevents a diagnostic fixture from violating the real per-chunk canonical
payload bound.

### Corrected server-owned p95

| R | E=R, dirty=20 | E=1000, dirty=20 | Population delta | E=1000, high dirty | Mutation-driven delta |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 128 | 1.929 ms | 1.925 ms | -0.2% | 3.850 ms (45) | +100.0% |
| 192 | 2.943 ms | 2.993 ms | +1.7% | 9.575 ms (67) | +220.0% |
| 256 | 4.328 ms | 4.242 ms | -2.0% | 16.521 ms (90) | +289.4% |
| 300 | 4.796 ms | 4.758 ms | -0.8% | 23.028 ms (105) | +384.0% |
| 350 | 5.765 ms | 5.831 ms | +1.2% | 27.370 ms (122) | +369.4% |

Within this controlled steady-state fixture, increasing retained entity population from R to
1,000 while holding the same relevant mutations and publication work is within approximately
plus/minus two percent and has no consistent direction. The prior 11.2% B-versus-D figure was
confounded by layout, AOI, cadence, and mutation placement and is withdrawn as an entity-only
estimate.

Increasing only selected mutation count causes the expected downstream increase in dirty chunks,
source records, recipient record references, message/wire work, and timing. “Mutation-driven” here
means that complete causal chain; it is not presented as the isolated cost of the mutation call.
At R=300 the low/high matched rows retained the same 343,032 semantic publications, while prepared
records increased from 5,400 to 28,350 and
recipient source-record references increased from 1,620,000 to 14,971,406.

## Historical V2 evidence retained

The original combined-process artifact remains in the machine-readable report with its timing
limitations. The stronger separate-process artifact is now also retained explicitly:

```text
_Archive/_V2/_Benchmarks/results/
dense_hotspot_new_runner_4families_60s/sharded_20260428_031706/aggregate.json
SHA-256 4a93c44883924fa53dc20814faaca22377d8121d3da13135871fd0c83dac93d5
```

It used one dedicated server process and four client-worker processes:

| Clients/entities | Server tick mean / p95 | Previous background send p95 |
| ---: | ---: | ---: |
| 256 | 1.584 / 2.350 ms | 2.584 ms |
| 300 | 1.976 / 2.913 ms | 3.374 ms |
| 350 | 2.560 / 4.018 ms | 4.522 ms |

Server-tick and background-send percentiles are independent and are not added. An unchanged
contemporary V2 build remains unavailable because current repository-wide output rules duplicate
archived generated `obj` inputs; no archived code was changed to manufacture a result.

Source inspection also confirms V2 reapplied held movement every server tick. Its 20 Hz input
arrival does not imply that an active V2 entity mutated only every third authority tick. The V3
fixture still directly applies equivalent mutations rather than emitting input-transport traffic,
so modeled input counts remain labels.

## Architectural disposition

Shared latest-source preparation remains effective and steady-state history reconstruction remains
zero. The repaired evidence supports these narrower conclusions:

1. inert retained entity population is not the dominant steady-state cost in the matched control;
2. active mutation, spatial fanout, recipient records, encoding, delivery debt, and proof work are
   strongly workload-sensitive;
3. recipient wakeup is real server-owned replication cost and is now included;
4. the current evidence does **not** establish that all remaining recipient-local materialization or
   record encoding is inherently required by independent proof identity or secure transport.

Independent delivery IDs, session fencing, exact proof, and absence debt must remain recipient
local. Whether compatible immutable recipient materialization/encoding can also be shared is still
a legitimate, nonblocking future performance investigation. No such optimization was attempted
here.

PF6 may proceed under the existing 64-participant policy once the integrated canonical
Release/export verification recorded by the HTTP companion is complete. It passed three
consecutive times. The focused PF5-R2, PF5-R2-R1, and corrected PF5-R2-R2 gates also passed.
