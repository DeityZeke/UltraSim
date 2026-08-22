# Network N1.4 adoption diagnostics and first crossover stop

This follow-up accepts the shared dirty-work mechanism from `682fcdcd` and
measures the two risks identified at checkpoint review. It remains semantic-only:
there are no sockets, fake-client CPU, transport-replay timings, Dense-400 claim,
or current-game AOI adoption decision in this evidence.

## Mixed-cadence pending traversal

The first diagnostic exposed repeated rescans of cadence-deferred prefixes in
the fixed pending array. The implementation retains that bounded array, but each
cadence step now owns one pass marker: every pending membership is classified at
most once in that pass, and repeated no-work calls at the same step return
without rescanning the deferred set. No tier queues, timing wheel, or new
scheduler abstraction was introduced.

At 32 recipients with the 1/2/4 evidence cadence:

| Dirty chunks | Due work | Memberships examined/work | Drain p95 |
|---:|---:|---:|---:|
| 1 | 32 | 4.000 | 2.220 us |
| 8 | 256 | 4.113 | 5.010 us |
| 32 | 1,024 | 4.093 | 243.087 us |
| 128 | 4,096 | 3.874 | 69.030 us |
| 497 | 15,904 | 3.489 | 307.497 us |

The non-monotonic p95 samples are host-tail observations, not an optimization
ranking. The structural result is the bounded pending-array examination count, and all warmed
diagnostic paths allocated zero managed bytes. A permanent regression also proves
that a repeated same-step no-work call performs zero additional array scans.

## Dirty-density shape

For 256 recipients sharing the same 497-chunk window, scheduler work scales with
dirty-chunk subscriber fanout:

| Dirty chunks | Fanout | Observe p95 | Drain p95 |
|---:|---:|---:|---:|
| 1 | 256 | 0.890 us | 3.840 us |
| 8 | 2,048 | 5.980 us | 189.048 us |
| 32 | 8,192 | 29.860 us | 110.889 us |
| 128 | 32,768 | 104.359 us | 385.037 us |
| 497 | 127,232 | 378.206 us | 1,364.157 us |

The all-dirty case intentionally approaches recipient-times-chunk work again.
The gain is therefore workload-dependent rather than a claim that a reverse
index makes dense global mutation free.

For 256 spatially dispersed windows with one independently populated home chunk
per recipient, dirtying all 256 target chunks produced 256 fanout operations:
7.990 us observe p95 and 3.890 us drain p95. This prevents the shared-anchor
fixture from being mistaken for the only supported topology.

## Stable-interest roaming census

At 100 mm authority movement steps over 4,096 steps:

| Path | Exact-query rate | Stable fast-path rate | Budget p50/p95 | Topology changes |
|---|---:|---:|---:|---:|
| ordinary cardinal travel | 55.59% | 44.41% | 63 / 499 mm | 1,999 |
| 1 mm chunk-boundary oscillation | 0.05% | 99.95% | 0 / 164 mm | 2 |
| 1 mm chunk-corner oscillation | 0.05% | 99.95% | 0 / 497 mm | 8 |

The exact shortcut is useful but does not eliminate sustained-travel queries.
The accepted first-game hysteresis caps any budget at 499 mm, and ordinary
travel still performs exact queries on roughly 56% of these samples.

## First 512/768/1024 semantic crossover

The broad 1,024-subscription reservation records:

| Recipients | One dirty chunk p95/recipient | Aggregate | All-recipient crossing p95/recipient | Combined reserved bytes |
|---:|---:|---:|---:|---:|
| 512 | 641.342 ns | about 328 us | 94.262 us | 192,633,528 |
| 768 | 412.438 ns | about 317 us | 97.694 us | 287,066,984 |
| 1,024 | 619.643 ns | about 635 us | 97.106 us | 387,580,360 |

The one-dirty-chunk path includes publication commit, shared observation,
recipient query, and immediate packet-free semantic commit. It is not a complete
transport or sharded-client comparison. The crossing result is the synchronized
all-recipient transition stress, not a prediction that every player crosses on
one authority step.

Same-machine N0 one-entity/shared-anchor controls reserve 35,835,820 / 56,716,148 /
72,632,876 bytes at 512 / 768 / 1,024 recipients. Their timings remain narrow
causal controls with different internal work, and the 1,024 capture contains a
large host-tail sample; no formal N0-to-N1 ratio is published from them.

A 1,024-recipient capacity-sensitivity run lowers the declared subscription
capacity from 1,024 to 640 while retaining 503 current subscriptions. Combined
reservation falls from 387,580,360 to 242,183,400 bytes, but remains materially
above the N0 control. This is evidence that capacity selection matters; 640 is
not frozen as the production maximum and the retained subscription/scheduler
layout remains an adoption blocker.

## Decision

Do not escalate to 2,048/4,096 or begin N1.5 yet. The next N1.4 work should
attribute and compact recipient subscription/cursor plus scheduler reverse-index
storage without changing interest, publication, wire, recovery, or proof
semantics. After that, rerun this crossover and continue with Dense-400,
dense-town/snapshot recovery, boundary/corner, mixed-private, transport replay,
and sharded-client evidence.

Machine-readable evidence:

- `network-n1.4-adoption-diagnostics-2026-08-22.json`
- `network-n1.4-semantic-crossover-512-1024-2026-08-22.json`
- `network-n1.4-capacity-sensitivity-1024x640-2026-08-22.json`
- `network-n1.4-n0-shared-one-dirty-{512,768,1024}-control-2026-08-22.json`
