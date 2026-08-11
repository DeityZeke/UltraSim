# ECS 3.1.2 Scheduler/Structural Proof Hardening

Status: accepted on 2026-08-11 under semantic profile
`ecs-3.1.2-scheduler-proof-hardening`.

ECS 3.1.2 keeps the 3.1 scheduler architecture. It closes every reproduced
correctness failure from the independent audit, strengthens the cold proof
boundaries, and does not add another scheduler, executor, command kind, or public
mutation model.

## 1. Confirmed findings fixed

The following audit findings reproduced and are fixed:

1. a late-ready lower-registration system could share a wave with an already-
   compiled conflicting higher-registration system;
2. ordinary transitions and typed Add/Remove routes could collide by numeric ID;
3. the old disjoint-random-writer flag did not cover read/write or generated/
   supplemental ownership intersections;
4. random enable updates could lose writes in one shared 64-bit enable word;
5. admitted random writes used unsynchronized shared dense change metadata;
6. a retained main `StructuralCommandBuffer` could bypass graph-time authority;
7. failed system registration could leak masks into the reused dense slot;
8. dependency-capacity failure could poison the dependency cursor;
9. exhausted RangeJob work capacity could surface as division by zero;
10. scheduled tracked handwritten 1R1W was absent;
11. changed-row snapshots reported matching population rather than delivered rows;
12. invalid scheduler enum values were not uniformly rejected;
13. partial worker startup could attempt to join an unstarted thread;
14. the scheduled parity report counted its query phase twice;
15. caller-thread allocation was presented too broadly for parallel comparison;
16. world/scheduler reservation ownership was ambiguous and could double-count
    the main arena.

## 2. Concerns not accepted as defects

- Deferred structural declarations remain capabilities and route metadata, not
  in-graph component hazards. Structural mutation occurs at the barrier; explicit
  semantic order uses `Requires` or deterministic command merge/phase order.
- A struct job may contain references. Participant copies are shallow by design;
  referenced state must be immutable, thread-safe, or partition-disjoint. An
  `unmanaged` restriction would reject legitimate bounded jobs without repairing
  a reproduced ECS defect.
- Zero-payload tag Add was already correct. Regression coverage remains.
- The global cross-archetype partition cap, ordinary worker synchronization,
  retained-empty initialization, entity generation/relocation, and terminal
  fault sequencing survived adversarial verification and were not redesigned.

## 3–4. Graph compiler and the reproduced graph

Seal uses a deterministic dependency-aware cold compiler. A node becomes ready
only after all dependencies compile. Its initial wave is one greater than its
latest same-stage dependency; the compiler then considers every already-compiled
same-stage conflicting node, regardless of registration index, and advances past
that node's wave. Registration order remains the deterministic ready-node tie
break. Missing, self, duplicate, cyclic, and reverse-stage dependencies reject at
Seal; Tick performs no graph construction.

For the reproduced `A(0)→C(3)`, `E(1)`, `B(2)→E(1)`, `C(3)` graph, both A and B
write X:

| Node | Defective wave | 3.1.2 wave |
| --- | ---: | ---: |
| E | 0 | 0 |
| C | 0 | 0 |
| B | 1 | 1 |
| A | 1 | 2 |

Sixteen forced-overlap executions observed maximum concurrency `1` for A/B.
Sixty-four deterministic randomized eight-node DAGs also match an independent
test compiler; dependency and conflict invariants are checked separately.

## 5–8. Supplemental random-access ownership and synchronization

`ParallelRandomAccessPolicy` has two states:

- `SerialOnly` is the default. Any supplemental write, and a supplemental read
  intersecting a generated write, prevents parallel dispatch.
- `PartitionDisjoint` is an explicit caller proof. For each participant, its
  supplemental reads must not overlap another participant's generated or
  supplemental writes. Its supplemental writes must not overlap another
  participant's generated or supplemental reads or writes. Duplicate targets are
  therefore outside the contract.

Generated component masks and supplemental lookup masks remain distinct. The
former establish chunk ownership and graph conflicts; only `AdditionalReads` and
`AdditionalWrites` authorize `SystemJobContext` lookup calls. Read/read remains
parallel-safe. A future prepared target-batch API may mechanically prove these
domains; 3.1.2 deliberately does not add one.

The scheduler internally protects private shared metadata on the exceptional
parallel supplemental path:

- enable mutation uses a compare/exchange loop on the containing 64-bit word;
- the per-chunk/per-column dense change version uses atomic publication;
- exact row history remains a row-local stamp under the declared disjoint-domain
  proof.

Generated chunk-owned enable and change writes remain on the existing non-atomic
path. At 5K operations over 100K residents:

| Random path | Serial mean/p95 | C4 parallel mean/p95 | C4 ns/op |
| --- | ---: | ---: | ---: |
| component write + dense-version CAS | 286.970/317.178 us | 129.588/151.429 us | 25.918 |
| enable CAS + dense-version CAS | 459.460/491.875 us | 156.740/179.018 us | 31.348 |

Repeated same-word set/set, clear/clear, and set/clear contention, different-word
controls, dense gates, exact rows, and independent cursor consumption all pass.

## 9–10. Structural run identity and differential proof

`StructuralRunKey` is computed from `Kind`, `Phase`, and `Argument`; it consumes
no command storage. Whole-source, exact packed-suffix, and grouped transition
analysis accept only one complete discriminated key. The command remains 24 bytes
and the total slot remains 40 bytes.

Production-versus-scalar differential coverage mixes ordinary Transition with
typed payload/tag Add and Remove using colliding and non-colliding numeric IDs at
7/8/9 and 255/256/257 boundaries. It covers live and deferred targets, whole-
source, suffix, grouped, and fallback layouts. Command/applied/rejected counts and
the canonical logical hash match. The permanent 100K grouped transition control
is effectively unchanged: `25.105 ns/entity` versus `25.215` in 3.1.1 (`-0.4%`);
wide retained is `26.732` versus `26.916` (`-0.7%`).

## 11–18. Boundary and reporting repairs

11. The main structural arena and world share a small `StructuralRecordingGate`.
    Every public arena mutation rejects while a scheduler graph is active,
    including retained references used from Prepare, Complete, caller jobs, or
    background jobs. Scheduler lanes remain separately owned; trusted merge runs
    after the graph gate closes. Normal external recording remains legal.
12. Registration builds five local masks and validates definitions, identities,
    enum values, dependencies, and capacity before copying any slot state. Failed
    AdditionalReads, AdditionalWrites, structural, and dependency registrations
    leave the reused slot indistinguishable from a fresh scheduler.
13. Range partitioning checks remaining bounded work-item capacity first and
    throws an explicit `InvalidOperationException`; zero, one, exact-full, and
    one-over cases do not leak arithmetic/index exceptions.
14. Handwritten 1R1W now has direct and scheduled tracked surfaces over the same
    `TrackedWritableComponentChunk<T>` and one system publication gate.
15. Changed-row nodes retain their completed delivered count; snapshot and
    `SystemCompleteContext.Rows` now mean callback-delivered rows.
16. Worker construction tracks how many threads actually started. Failure signals
    and joins only those threads, disposes every created event, and is safe to
    dispose again.
17. `WorldCapacityPlan` is explicitly a preconstruction estimate.
    `WorldReservedPayloadSnapshot` is a post-Seal enumeration of entity/store,
    route, change, main structural, scheduler graph/worker/payload, and fixed
    scratch categories. The main arena has exactly one owner. CLR headers,
    schema/cold nested arrays, `Thread` objects, OS stacks, and event internals are
    explicit exclusions.
18. The scheduler parity aggregation counts Query once. Workload profile names
    distinguish the scheduled parity profile from the older scheduler-shaped
    stress workload. Cross-generation allocation now reports caller allocation,
    V3 scheduler-worker allocation where instrumented, and V2 worker allocation
    as unknown.

## 19–25. Performance evidence

The exact 30,000-tick parity profile performs 400K generated visits, 5K unique
RNG-selected arbitrary writes, 500 RNG reads, 100 destroys, 100 creates, 200
transitions, and exact changed-row consumption per tick:

| Surface | Mean | p95 | p99 | Caller/worker allocation |
| --- | ---: | ---: | ---: | ---: |
| public generated-job control | 1,200.475 us | 1,244.628 | 1,300.397 | 0/0 B |
| scheduled serial | 1,279.727 us | 1,323.817 | 1,378.586 | 0/0 B |
| scheduled parallel C8 | 597.434 us | 696.283 | 819.872 | 0/0 B |

All counters and logical hash `55107F0D2B7B1F2F76BF7E710E9C6AD5` match. The
serial scheduler overhead is `6.60%`. Parallel is `2.01x` faster than public and
`2.14x` faster than scheduled serial.

Crossover is workload-dependent. At one operation/row, 1K is neutral/slightly
serial (`1.701` versus `1.712 us`); 10K favors C8 (`15.655` versus `11.624 us`),
50K is `82.733` versus `25.879 us`, and 100K is `162.743` versus `51.091 us`.
At eight operations/row, 10K is `64.477` versus `22.054 us`.

RangeJob at 100K/one operation scales from `73.264 us` C1 to `31.329 us` C8,
then worsens to `39.265 us` C16. At 16 operations it scales from `630.132 us`
C1 to `99.302 us` C8. The isolated cross-generation row remains order-sensitive:
V2-first favors V2 by `7.8%`; V3-first favors V3 by `4.0%`. This is not presented
as a stable generational win or regression.

The 20K destroy/create scheduler structural case reports mean playback of
`45.52/45.45/45.27/45.00/28.48 ns/command` at C1/C2/C4/C8/C16. Merge cost grows
with segment count until C16's different partition shape; canonical command and
state validation remain authoritative. Source-independent typed Add/Remove at
100K C4 measure `140.824/128.704 ns/entity`, respectively.

Cold Seal at 4K systems measures:

| Shape | Mean | p95 |
| --- | ---: | ---: |
| no dependencies | 37.583 ms | 38.376 |
| chain | 38.354 ms | 38.891 |
| mixed conflicts | 38.627 ms | 38.956 |
| random DAG | 38.528 ms | 38.738 |
| late ready | 39.289 ms | 39.746 |

Correctness and simplicity take precedence on this cold path; allocations are
recorded rather than forbidden.

## 26–27. Allocation and validation totals

Every warmed scheduler, public-job, kernel, change, soak, and V3 comparison row
reports zero caller allocation and zero GC. Scheduler instrumentation reports
zero worker allocation for the scheduler suite, parity/stress soaks, deterministic
replays, and all three V3 scheduler-capable comparison rows. V2 worker allocation
is not captured by the frozen protocol.

Passing suites:

- 218 kernel/runtime tests, Debug and Release;
- 42 scheduler tests, Debug and Release;
- 23 generator tests;
- 12 comparison tests;
- 18 bitmap experiment tests.

That is 313 named test cases, plus 149 scheduler timing rows, 25 graph-Seal rows,
141 permanent kernel rows, the public-job matrix, 314 change-decomposition rows,
two 30K-tick longitudinal soaks, 60K-tick serial/parallel scheduler stress, and
seven 10K-tick deterministic concurrency replays.

The repository's local Qwen reviewer then inspected the focused concurrency and
structural diff. Its seven suggestions were checked against source: the gate was
already null-conditional, change indices were already bounded, exact history was
intentionally optional, mask scratch had an exact checked length, the run argument
was already `uint`, and intersection already used wordwise `&`. Its bounded-retry
CAS suggestion would weaken correctness under contention. No Qwen finding was
accepted without executable/source confirmation, and no production change was
made from that review.

Both isolated V2/V3 process orders retain 102 comparable rows, zero state/catalog
mismatches, and 10 unsupported rows. The complete 102-row metric tables are:

- [V2-first full comparison](_Benchmarks/results/ecs-v2-v3-3.1.2-v2-first/ecs-v2-v3-comparison.md)
- [V3-first full comparison](_Benchmarks/results/ecs-v2-v3-3.1.2-v3-first/ecs-v2-v3-comparison.md)

Representative scheduler rows:

| Scenario | Order | V2 ns/op | V3 ns/op | Result |
| --- | --- | ---: | ---: | --- |
| parallel generated tick | V2 first | 5.576 | 0.534 | V3 10.44x faster |
| parallel generated tick | V3 first | 5.883 | 0.490 | V3 12.00x faster |
| RangeJob | V2 first | 1.645 | 1.774 | V2 7.8% faster |
| RangeJob | V3 first | 1.749 | 1.679 | V3 4.0% faster |
| serial scheduled tick | V2 first | 4.208 | 1.323 | V3 3.18x faster |
| serial scheduled tick | V3 first | 4.145 | 1.337 | V3 3.10x faster |

## 28–30. Complexity and deliberate deferrals

The correction adds five small concepts: one two-state random-access policy, one
computed structural key, one graph recording gate, one tracked 1R1W view, and one
post-Seal reservation snapshot. It removes the incomplete random-write boolean,
naked numeric run identity, partial registration commits, and the graph compiler's
registration-prefix assumption. The hot execution architecture is unchanged:

```text
Seal: validate → compile routes/dependencies/conflicts → freeze
Tick: pre-barrier → due waves over owned work → merge → post-barrier
```

No optional performance experiment was retained. Query-shape reuse, flat stage/
wave node ranges, participant cache-line layout, `NextDueTick`, and a prepared
entity-batch/random-write API remain measurement-driven follow-ons. Work stealing,
dynamic graphs, lifecycle, prefabs, persistence, replication, and networking
remain outside 3.1.2.

## Artifacts

- [accepted scheduler evidence](_Benchmarks/Baselines/ecs-v3-3.1.2-scheduler-proof-hardening-2026-08-11.json)
- [accepted V2/V3 baseline](_Benchmarks/Baselines/ecs-v2-v3-3.1.2-2026-08-11.json)
- [internal 100K longitudinal soak](_Benchmarks/results/ecs-v3-3.1.2-100k-mixed-soak-internal-2026-08-11.json)
- [public-job 100K longitudinal soak](_Benchmarks/results/ecs-v3-3.1.2-100k-mixed-soak-public-jobs-2026-08-11.json)
- [permanent kernel validation](_Benchmarks/results/ecs-v3-3.1.2-permanent-validation-2026-08-11.json)
- [public-job validation](_Benchmarks/results/ecs-v3-3.1.2-public-jobs-validation-2026-08-11.json)
- [change decomposition](_Benchmarks/results/ecs-v3-3.1.2-change-decomposition-validation-2026-08-11.json)

## Final answer

Yes. The scheduler's concurrency and structural proofs are now mechanically
aligned with the public API rather than relying on assumptions that the API can
violate. The one remaining caller promise—`PartitionDisjoint`—is explicit,
narrow, defaults to serial, and is backed by internal synchronization for private
shared metadata.

Yes. ECS 3.1 is sufficiently hardened that the next engineering work should be a
real simulation vertical rather than another speculative scheduler correctness
pass. Scheduler work should resume only when integration or measured production
pressure identifies a concrete gap.
