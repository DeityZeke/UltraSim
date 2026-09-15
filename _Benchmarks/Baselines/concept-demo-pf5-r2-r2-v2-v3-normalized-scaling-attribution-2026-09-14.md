# Concept Demo PF5-R2-R2 Phase B — V2/V3 Normalized Scaling Attribution

**Outcome D: the apparent V2/V3 gap has multiple causes. Workload shape and timing-boundary
differences are material, while V3's recipient-local exact publication/proof and current transport
path remain a substantial non-equivalent cost. No production policy or replication semantic was
changed.**

## Lineage and scope

| Item | Commit |
| --- | --- |
| Task starting HEAD | `8472038eb01e39f10a78e200f85d19f5b7e37ad4` |
| PF5-R2 replication semantic candidate | `992fc541460f18534da9036c73cb711277de11ce` |
| Integrated Phase-A correctness candidate | `389fb3a0de274fcba0c9d4f64e686b94818f9c71` |
| Phase-B measurement implementation | `482826b1d0ef99996eb970a64f5d034c13b0bd2e` |

The selected v29 policy remained `ChunkPublication`, message-129 when beneficial, selective
message-148 LZ4 at canonical size >= 384 bytes and net saving >= 32 bytes, production cadence
1/2/4, CrossLane cap two, transport revision 2. This is a synthetic architecture benchmark, not
product CCU evidence. It uses the current Game, proof, compression, framing, secure-packet, queue,
and shared loopback protocol paths; it does not use the exported-product/real-socket swarm.

The machine-readable companion contains every per-tick mean/p50/p95/p99/max distribution and raw
work counter used below.

## Historical V2 reference

Source of record:

`_Archive/_V2/_Benchmarks/results/dense_hotspot_v1_shape_linux_4families_gc_pooled/network_bot_harness_20260428_023430.json`

SHA-256: `2eb4ee30cc08a4d598dc271cfc9db29f268abc96db688f235c8dee2b44575cd1`.

The artifact establishes:

- 256/300/350 clients, independent 60-second stages after 3 seconds settle, 62 Hz;
- one session-owned authoritative ECS movement entity per client and no separate 1,000-entity
  ambient population;
- 35% active, 20 Hz input represented as every third tick, phased sends;
- 1 m spacing, 20-column dense grid; AOI radii 7.5/23.5/39.5 m; ring cadence 1/2/3;
- movement latest state, visual latest state, bulk latest state every 16 ticks, and ordered observer
  events every eight ticks;
- recipient phasing on with cadence two, latest-state batching off;
- Chat disabled; async server flush enabled; async client receive disabled;
- a single `role=combined` process for this pooled artifact, not separate client worker processes.

The historical results were:

| Clients/entities | Main tick mean/p95 ms | Dispatch mean/p95 ms | Previous async flush mean/p95 ms | Client pump mean/p95 ms |
| ---: | ---: | ---: | ---: | ---: |
| 256 | 6.438 / 11.406 | 0.990 / 1.498 | 1.746 / 3.001 | 4.895 / 9.616 |
| 300 | 8.295 / 14.686 | 1.282 / 1.806 | 2.299 / 3.889 | 6.450 / 12.516 |
| 350 | 10.689 / 18.814 | 1.642 / 2.307 | 2.889 / 5.040 | 8.406 / 16.130 |

The V2 main tick includes in-process client work. The previous async flush is a separate background
distribution. Its p95 is not added to main-tick or dispatch p95.

An unchanged contemporary V2 build was attempted with:

```text
dotnet build _Archive/_V2/_Benchmarks/UltraSim.Network.BotHarness/UltraSim.Network.BotHarness.csproj -c Release --no-restore
```

It is unavailable unchanged: current repository-wide output rules include archived generated
`obj` trees twice and produce duplicate target-framework/assembly attributes. No archived source or
project was modified to manufacture a contemporary result.

## Correct server-owned timing boundary

The benchmark now records these distinct per-tick samples:

```text
AuthorityMutation
    complete Mutate + ApplyAuthority interval

BaseAuthoritativeMutation
    AuthorityMutation less the measured prepared-source slice

RemotePublicationPreparation
    exact opt-in ChunkPublicationStore interval which groups this tick's source changes
    and creates shared immutable prepared publications/records

NetworkAndRemoteReplication
    begins after ApplyAuthority; recipient scheduling, Game encode/proof, current
    Network/secure transport, and the in-process delivery/service work already owned
    by the established boundary

ServerOwnedRemoteReplicationBudget
    per tick: RemotePublicationPreparationTicks + NetworkAndRemoteReplicationTicks
    distribution calculated only after those same-tick sums are formed

AuthorityThroughNetworkAndRemoteReplication / Tick
    continuous conservation clock; includes intervening in-process client work and is
    not the four-millisecond server-owned budget
```

Prepared-source data is stored in Game's publication store and is eagerly constructed even in the
current packet-free local composition. Its prepared chain is consumed by remote replication, while
ordinary current public records/identity lookup are used locally. For this attribution it is
therefore shown separately and charged as the remote-publication representation cost; the complete
authority clock remains present so the eager local cost is not hidden. The former proxy which
charged all of `ApplyAuthority` to Network was rejected because it also included ordinary
public-world commit and reconciliation.

The focused gate verifies equal per-tick sample counts and exact mean reconciliation. It fails if
the server-budget sample ceases to include source preparation. No phase percentiles are added.

## V3 normalized workloads

| Shape | Entities | Dirty source rows/tick | Activity model | AOI/cadence |
| --- | --- | ---: | --- | --- |
| A — V2-shaped | E=R | floor(35% of E) | V2-like 20 Hz / three-phase label | 7.5/23.5/39.5 m, 1/2/3 |
| B — V2 population/current activity | E=R | 20 | same active/input label | V2-like AOI, 1/2/3 |
| C — 1,000/V2 activity | 1,000 | 350 | V2-like 20 Hz / three-phase label | V2-like AOI, 1/2/3 |
| D — current production-shaped | 1,000 | 20 | accepted fixed-source fixture | current AOI, 1/2/4 |

There are three important limits on equivalence:

1. The V3 fixture directly applies equivalent authoritative movement mutations. Its reported
   modeled input count is a label; it emits no input-command transport.
2. V3 has one unified public-world latest-state record family. V2 independently published movement,
   visual, bulk, and ordered-event families. Inventing fake V3 protocol families would be less
   honest than recording the mismatch.
3. V2's 1 m grid exceeds V3's bounded 64-record-per-16 m publication-chunk baseline. V3 uses the
   closest admissible 2 m, 20-column grid. Active movers are spread across it because concentrating
   the first 35% can create a canonical per-chunk delta larger than the production Game-message
   bound.

## V3 timing matrix

Each cell is mean / p95 milliseconds over 270 measured ticks after 30 warmup ticks. `Game` is
schedule/codec/queue, `Server remote` is the same-tick server-owned budget defined above, and
`Whole` is the in-process conservation clock.

| R | Shape | Game mean/p95 | Server remote mean/p95 | Whole mean/p95 |
| ---: | --- | ---: | ---: | ---: |
| 128 | A | 3.021 / 3.563 | 4.402 / 5.193 | 9.791 / 11.015 |
| 128 | B | 1.194 / 1.600 | 2.104 / 2.542 | 6.451 / 7.474 |
| 128 | C | 10.084 / 11.104 | 12.795 / 14.057 | 25.922 / 28.674 |
| 128 | D | 1.457 / 1.721 | 2.560 / 2.886 | 11.279 / 12.600 |
| 192 | A | 7.293 / 7.896 | 9.934 / 10.639 | 22.173 / 23.643 |
| 192 | B | 1.793 / 2.381 | 3.180 / 3.797 | 10.088 / 11.712 |
| 192 | C | 16.558 / 18.022 | 20.878 / 22.746 | 42.601 / 45.794 |
| 192 | D | 2.175 / 2.498 | 3.824 / 4.205 | 16.897 / 18.588 |
| 256 | A | 11.111 / 11.889 | 14.858 / 16.034 | 32.922 / 34.481 |
| 256 | B | 2.374 / 3.123 | 4.214 / 4.985 | 13.029 / 15.189 |
| 256 | C | 23.178 / 25.842 | 29.203 / 32.600 | 59.993 / 66.021 |
| 256 | D | 2.978 / 3.411 | 5.247 / 5.765 | 23.103 / 25.296 |
| 300 | A | 16.213 / 16.993 | 21.072 / 22.253 | 45.173 / 47.104 |
| 300 | B | 2.842 / 3.642 | 5.048 / 5.924 | 15.604 / 18.163 |
| 300 | C | 28.951 / 31.605 | 36.815 / 40.284 | 76.392 / 82.492 |
| 300 | D | 3.462 / 3.934 | 6.121 / 6.671 | 27.106 / 29.755 |
| 350 | A | 21.819 / 22.510 | 28.437 / 29.545 | 61.291 / 63.740 |
| 350 | B | 3.298 / 4.281 | 5.838 / 6.889 | 18.159 / 21.575 |
| 350 | C | 34.711 / 37.004 | 43.683 / 46.526 | 90.233 / 95.268 |
| 350 | D | 4.047 / 4.603 | 7.169 / 7.863 | 31.688 / 34.595 |

The current production-shaped D curve crosses the 4 ms server-owned p95 budget between 128 and
192. The E=R/current-activity B curve crosses between 192 and 256. A is already over at 128 because
35% activity means 45 dirty rows/tick there. These are synthetic recipient crossings, not CCU.

## Work-count normalization at R=300

| Shape | E | Dirty/tick | Prepared pubs/records | Recipient pub/record refs | Semantic pubs/proofs | Game messages | UDP datagrams | UDP bytes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| A | 300 | 105 | 1,620 / 28,350 | 751,505 / 14,510,881 | 343,032 / 343,032 | 284,298 | 347,529 | 289,126,602 |
| B | 300 | 20 | 810 / 5,400 | 269,708 / 1,830,712 | 180,522 / 180,522 | 96,717 | 163,095 | 61,650,652 |
| C | 1,000 | 350 | 5,670 / 94,500 | 1,545,047 / 32,079,432 | 529,448 / 529,448 | 461,451 | 539,647 | 507,684,674 |
| D | 1,000 | 20 | 2,160 / 5,400 | 668,441 / 1,675,406 | 648,000 / 648,000 | 101,956 | 163,630 | 95,921,281 |

All rows converged with zero capacity drops. Delivery identity and proof count remain one-for-one
with semantic publication entries. Prepared-history steady-state reconstruction calls remained
zero.

At R=300, compression selections were 58,734 / 66,665 / 49,952 / 83,603 for A/B/C/D, saving
43.07 / 39.81 / 30.61 / 49.32 MB of canonical bundle bytes respectively. CrossLane cap-two
pairings were 18,743 / 14,922 / 4,519 / 20,441. Compression, aggregation, cadence, and proof policy
were unchanged.

## Detailed R=300 waterfall

The following are mean milliseconds per measured tick from separate 90-tick Detailed runs. They
are attribution evidence, not the rated rows: detailed clocks themselves have measurable tax.

| Stage | A | B | C | D |
| --- | ---: | ---: | ---: | ---: |
| Shared source preparation | 0.009 | 0.001 | 0.014 | 0.011 |
| Prepared chain traversal | 0.072 | 0.025 | 0.151 | 0.063 |
| Recipient duplicate collapse | 0.412 | 0.064 | 0.939 | 0.091 |
| Recipient materialization | 0.358 | 0.073 | 0.617 | 0.103 |
| Recipient sort | 1.338 | 0.188 | 2.433 | 0.111 |
| Complete source read | 2.768 | 0.569 | 5.172 | 1.045 |
| Canonical standalone encode | 4.043 | 0.340 | 7.707 | 0.048 |
| Bundle staging | 0.059 | 0.031 | 0.084 | 0.085 |
| Bundle encode | 0.082 | 0.078 | 0.067 | 0.133 |
| Network queue admission | 0.093 | 0.035 | 0.183 | 0.035 |
| Delivery/outstanding bookkeeping | 1.283 | 0.547 | 2.744 | 1.075 |
| Proof processing | 0.875 | 0.485 | 1.429 | 1.663 |
| Complete schedule/codec/queue | 18.193 | 3.832 | 32.890 | 5.660 |
| Current server protocol stages | 1.893 | 1.062 | 2.806 | 1.150 |

`Complete source read` encloses chain/collapse/materialization/sort plus remaining read-loop work;
the child timings are not added to it. `Complete schedule` likewise owns enclosing work. The raw
artifact retains lookup/cadence, validation, cursor, proof, compression, aggregation, and protocol
operation counters.

## Attribution result

### Entity count

At R=300 and 20 source mutations/tick, E=R (B) reduced server-owned p95 by 11.2% relative to the
1,000-entity production-shaped D row: 6.671 ms to 5.924 ms. Entity population matters, but does not
explain most of the historical gap on its own.

### Activity and fanout

At E=R=300, increasing source activity from 20 to 105 dirty entities/tick (B to A) increased
server-owned p95 by 275.6%, from 5.924 to 22.253 ms. Restoring 1,000 entities at the 35% activity
shape (A to C) added a further 81.0%. Source activity, the spatial memberships it dirties, and the
resulting recipient-publication/record references dominate this matrix.

This does not mean V2 executed equivalent work cheaply. Its four families, one-metre geometry,
sent-based latest-state tracker, batching policy, and asynchronous flush boundary are different.
The V2 artifact reports tens of millions of logical fanout deliveries, but prepares immutable
source items once and leaves much transport work in the separately timed async flush.

### Stronger recipient semantics and transport

V3 still spends substantial recipient-local time after PF5-R2 shared source preparation:
prepared-chain reads, collapse/sort, exact delivery debt, exact applied proof, Game encoding,
current compression/aggregation, authenticated framing, and secure transport. The 300-D run creates
648,000 independent semantic publications/proofs from only 5,400 source records. That is legitimate
recipient obligation, not repeated authoritative source reconstruction, but it explains why source
preparation alone cannot close the historical timing gap.

The cost of exact proof/fencing versus V2's sent tracker, and the cost of current security versus
V2 transport, cannot be isolated into causal counterfactual percentages without weakening V3 or
building non-production variants. This checkpoint therefore reports their actual stage costs and
does not claim a fabricated semantic-equivalence result.

## Decision

**Outcome D — mixed explanation.**

The best current answer to “why did V2 appear to support more dense clients?” is:

1. the quoted V2 rows had E=R, not a separate 1,000-entity source population;
2. V2 and V3 used materially different activity, geometry, semantic families, batching, and timing
   boundaries;
3. V2 prepared source-side immutable latest-state items once, used weaker sent-based recipient
   knowledge, and moved flush work outside its main server timing;
4. PF5-R2 removed V3's recipient-specific authoritative history reconstruction, but V3 correctly
   retains independent recipient publication, delivery, absence/rebase, and exact applied-proof
   work plus its current secure transport;
5. in the controlled V3 matrix, mutation/fanout volume explains much more than ambient entity count,
   while recipient obligation remains the largest residual scaling surface.

No correctness defect, source gap, queue loss, or semantic divergence was found. No production
rate, AOI, format, compression, proof, or replication policy changed. The replication semantic
candidate remains `992fc541...`; the integrated PF6 correctness candidate remains `389fb3a0...`.

Verification passed:

- `--pf5-r2-r2-only` normalized timing/work-count boundary gate;
- `--pf5-r2-only` shared-publication semantic and equality gate;
- `--pf5-r2-r1-only` authority-through-network timing gate;
- the complete `./verify.sh --configuration=Release`, including actual exports, Contentless Online,
  composition, and lifecycle gates.

Rated rows disable all per-recipient stage clocks. The required source clock is one bounded
timestamp pair per prepared source publication and its measured slice is shown directly (for
example, 0.0021 ms mean in the 300-D row). Detailed runs are separate and are not substituted for
rated timings.

Phase A has already satisfied the PF6 correctness blocker, so **PF6 may resume**. A later, nonblocking
performance investigation may isolate recipient materialization/encoding/proof and secure transport
with paired semantic-work controls if product evidence makes that optimization necessary. This
checkpoint does not authorize that work.
