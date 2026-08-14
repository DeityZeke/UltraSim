# Accepted ECS comparison baselines

`ecs-v2-v3-2026-08-09.json` is the first immutable joined V2/V3 semantic
baseline for protocol `ecs-v2-v3-semantic-comparison-v1`. It was captured with
4 warmups and 15 measured iterations per scenario, V2 first, tiered compilation
and ReadyToRun disabled, on the environment embedded in the JSON.

Do not overwrite an accepted artifact. Create a newly dated file when the
protocol, host, runtime, measurement policy, or intentionally accepted V3 state
changes. The orchestrator enforces this for `--accept-baseline`.

Unless an artifact explicitly declares a cadence experiment, accepted runtime
and simulation performance baselines use fixed 60 Hz, 1x playback, runtime
cadence changes off, and adaptive cadence off. Lowering cadence is not an
allowed way to satisfy a normal regression gate.

Historical joined artifacts preserve the descriptor text that was true—or was
recorded—when they were accepted. In particular, several pre-correction files
say V3 lifecycle publication was absent. Do not rewrite those immutable JSON
files. The live comparison catalog now states the actual reason the row remains
unsupported: V3 has a globally ordered bounded independent-cursor lifecycle
contract and frozen V2 has no admitted equivalent.

For an ordinary future regression capture:

```bash
dotnet run --project _Benchmarks/UltraSim.ECS.Comparison/UltraSim.ECS.Comparison.csproj -c Release -- --warmups=4 --iterations=15 --baseline=_Benchmarks/Baselines/ecs-v2-v3-2026-08-09.json --output-dir=_Benchmarks/results/ecs-v2-v3-current
```

The regression report reruns frozen V2 as a control. Similar V2 and V3 movement
usually indicates host/runtime noise; V3-only movement is evidence of a rewrite
change. Canonical-state, descriptor, methodology, and environment checks take
precedence over timing ratios.

`ecs-bitmap-block-pool-2026-08-09.json` is the first immutable standalone
mechanism baseline for the five-policy bitmap block-pool experiment. It contains
425 zero-allocation measured rows at a requested 50,000-row scale and requires
canonical equality within each of 85 logical cases. It is directional against
production ECS generations; exact V2/V3 claims continue to come only from the
semantic comparison artifact above. Reproduction and interpretation live in
`../BITMAP_BLOCK_POOL_EXPERIMENT.md`.

`ecs-bitmap-block-pool-2026-08-09-alt-seed.json` repeats the complete matrix with
a different deterministic scenario-order seed. Large structural conclusions
must agree across both; observed ranges in the experiment document use both
artifacts so small or order-sensitive differences are not presented as stable.

`ecs-v30-barrier-hole-reuse-2026-08-09.json` is the accepted focused production
V30 capture. Its 12 rows compare scalar, exact phase-indexed packed V29-style,
and production V30 playback for balanced 10%/50% destroy-create churn over
100,000 resident narrow/512-byte archetypes. All rows are zero-allocation and
validate command outcomes; the production rows additionally require exact hole
creation/reuse/finalization metrics.

`ecs-v2-v3-v30-2026-08-09.json` is the accepted four-warmup/fifteen-iteration
V30 joined semantic capture, V2 first. It retains 87 comparable rows, zero
canonical-state mismatches, 15 explicit unsupported rows, and zero V3 timed
managed allocation. Use it for post-V30 longitudinal regression. Keep the first
artifact above as the immutable pre-V28 reference rather than overwriting it.
The embedded V3 `KernelProfile` remains the comparison protocol's stable
`ecs-next-phase1-planned-source-compaction-v27` identifier so the regression
joiner can compare longitudinal timings; the artifact name and this manifest
identify the V30 source milestone.

`ecs-v2-v3-v31-2026-08-10.json` is the accepted V31 structural-consolidation
capture, V2 first. It expands the catalog to 96 comparable rows with nine
provisional balanced-wide churn rows, retains zero canonical mismatches and 15
unsupported rows, reports the truthful
`ecs-next-phase1-structural-consolidation-v31` profile, and allocates zero timed
managed bytes in every V3 row. A reversed-order verification remains in
`_Benchmarks/results/ecs-v2-v3-v31-consolidation-reverse`.

`ecs-v2-v3-3.0.32-2026-08-10.json` is the first accepted artifact using the
three-field ECS version convention. Its V3 profile is
`ecs-3.0.32-exact-whole-source-transitions`. It retains the 96 comparable rows,
zero canonical mismatches, 15 explicit unsupported rows, and zero V3 timed
managed allocation while materially reducing exact complete-source add/remove
transition cost. The reversed-order verification remains in
`_Benchmarks/results/ecs-v2-v3-3.0.32-reverse`.

`ecs-v2-v3-3.0.33-2026-08-10.json` is the accepted validate-once whole-source
commit capture, V2 first. Its V3 profile is
`ecs-3.0.33-whole-source-trusted-commit`. It retains 96 comparable rows, zero
canonical or contract mismatches, 15 explicit unsupported rows, and zero V3
timed managed allocation. The reversed-order verification remains in
`_Benchmarks/results/ecs-v2-v3-3.0.33-reverse`.

`ecs-v2-v3-3.0.34-2026-08-10.json` is the accepted prepared-migration trusted
commit capture, V2 first. Its V3 profile is
`ecs-3.0.34-prepared-migration-trusted-commit`. It retains 96 comparable rows,
zero canonical or contract mismatches, 15 explicit unsupported rows, and zero
V3 timed managed allocation. The shared catalog is a regression control for
this checkpoint because its transition rows select exact whole-source work; the
causal partial-prefix A/B evidence is recorded in `_Benchmarks/README.md`. The
reversed-order verification remains in
`_Benchmarks/results/ecs-v2-v3-3.0.34-reverse`.

`ecs-v2-v3-3.0.35-2026-08-10.json` is the accepted exact route-level packed-
suffix transition capture, V2 first. Its V3 profile is
`ecs-3.0.35-exact-route-packed-suffix-transition`. It retains 96 comparable
rows, zero canonical or contract/catalog mismatches, 15 explicit unsupported
rows, and zero V3 timed managed allocation. The shared catalog has no equivalent
partial exact-suffix workload, so this artifact is a regression gate; causal
same-binary grouped/suffix evidence is recorded in `_Benchmarks/README.md`. The
reversed-order verification remains in
`_Benchmarks/results/ecs-v2-v3-3.0.35-reverse`.

`ecs-v3-3.0.35-100k-mixed-soak-2026-08-11.json` is the first immutable
single-owner production-shaped V3 soak. Two independently constructed worlds
each warm for 2,000 ticks and measure 30,000 natural-GC ticks over a fixed
100,000-entity population. Every tick performs 400,000 generated-query visits,
5,000 exact tracked writes, 500 checked reads, exact change consumption, 100
destroy/create replacements, and 200 transitions. Interval and final canonical
replays match, timed allocation and all collection counts are zero, no command is
rejected, and chunk occupancy is stable. This is a longitudinal V3 control, not a
V2 comparison or a scheduler/network/persistence claim; full methodology and
interpretation live in `../ECS_100K_MIXED_SOAK.md`.

`ecs-v3-3.0.35-change-tracking-decomposition-forward-first-2026-08-11.json`
and
`ecs-v3-3.0.35-change-tracking-decomposition-reverse-first-2026-08-11.json`
are the accepted opposite-pass-order instrumented change-tracking controls.
Each contains 314 measurements at eight warmups and 31 samples with tiered JIT
disabled. Together they separate checked/prebound access, dense/exact stamping,
experimental shared-version/chunk-grouped publication, and dense/exact consumer
cost across changed density and locality. All timed rows allocate zero bytes,
collect no GC generation, and pass post-timing cursor/publication validation.
They are diagnostic 3.0.35 evidence, not a new kernel checkpoint. Methodology,
caveats, and the resulting implementation priority live in
`../ECS_CHANGE_TRACKING_DECOMPOSITION.md`.

`ecs-v2-v3-3.0.36-2026-08-11.json` is the accepted public serial generated
chunk-job comparison capture. It retains 96 comparable rows, zero canonical or
contract/catalog mismatches, 15 explicit unsupported rows, and zero timed V3
allocation. Its causal same-process public/internal execution evidence lives in
`../../ECS_JOB_EXECUTION.md`; the joined artifact is primarily the immutable
longitudinal regression gate.

`ecs-v2-v3-3.1.0-2026-08-11.json` is the accepted bounded-system-scheduler joined
capture. It expands the catalog to 102 comparable rows with serial generated,
parallel generated, and parallel RangeJob contracts; it retains zero canonical
mismatches, 10 unsupported rows, and zero timed V3 allocation. The complete
3.1.0 scheduler matrix and its originally admitted scheduler-shaped stress soak
are preserved in
`ecs-v3-3.1.0-bounded-system-scheduler-2026-08-11.json`. That stress soak is not
the same workload as the older 3.0.35/3.0.36 longitudinal profile; 3.1.1 records
the repaired evidence without rewriting either immutable artifact.

`ecs-v2-v3-3.1.1-2026-08-11.json` is the accepted scheduler capability/parity
hardening joined capture, taken V2 first after verification in both process
orders. It retains all 102 comparable contracts, zero canonical or catalog
mismatches, 10 unsupported rows, and zero timed V3 allocation. The corrected
145-row scheduler matrix and exact longitudinal public/serial/parallel parity
soak live in
`ecs-v3-3.1.1-scheduler-capability-parity-hardening-2026-08-11.json`. Timing
triage remains process-order sensitive: the targeted serial scheduler row is
effectively unchanged and parallel generated traversal improves in both orders.
The RangeJob row is 2.2-6.4% slower; focused 31-sample reruns reproduce a
2.7-6.3% range. Treat that modest regression as a follow-up signal accepted for
the required safety repair, not a generalized scheduler result. Full
interpretation is in `../../ECS_3_1_1_HARDENING_REPORT.md`.

`ecs-v2-v3-3.1.2-2026-08-11.json` is the accepted scheduler/structural proof-
hardening joined capture, verified in both process orders. It retains all 102
comparable contracts, zero canonical/catalog mismatches, 10 explicit unsupported
rows, and zero caller-thread V3 allocation. The three scheduler-capable V3 rows
also report zero worker allocation; V2 worker allocation remains unknown because
the frozen adapter exposes no equivalent counter. The 149 timed scheduler rows,
25 cold graph-Seal rows, corrected 100K parity profile, random enable/version CAS
controls, long stress soak, and deterministic multi-worker replays are preserved
in `ecs-v3-3.1.2-scheduler-proof-hardening-2026-08-11.json`. Full correctness,
performance, and complexity interpretation is in
`../../ECS_3_1_2_HARDENING_REPORT.md`.

`ecs-v2-v3-3.2.0-2026-08-12.json` is the accepted lifecycle/public-filter
checkpoint joined capture, verified in both process orders. It retains 102
comparable contracts, zero state/descriptor/measurement/catalog mismatches, 10
explicit unsupported rows, and zero timed V3 allocation. Lifecycle itself has no
honest frozen-V2 equivalent in this catalog; its dedicated accepted evidence is
`ecs-v3-3.2.0-lifecycle-publication-2026-08-12.json`.

That lifecycle artifact records the 24-byte record, 16-byte cursor, paired
capacity-zero/capacity-enabled structural costs, 100/1K/10K/100K publication and
drain scales, one versus three consumers, small partial spans, explicit overflow,
and public filtered traversal. Every timed row allocates zero managed bytes. Full
interpretation is in `../../ECS_3_2_0_LIFECYCLE_REPORT.md`.

`ecs-v3-3.3.0-compiled-content-authoring-2026-08-12.json` is the accepted
compiled-content cold/startup and warmed-spawn matrix. It records deterministic
catalog compilation across 10–10,000 prefabs and 1–128 signatures, catalog
payload, immediate/deferred spawn, a 16–1,024-byte default ladder, direct/stable
lookup, exact command/lifecycle counts, and zero warmed runtime allocation.

`ecs-v2-v3-3.3.0-2026-08-12.json` is the accepted compiled-content checkpoint
joined capture, verified in both process orders. It expands the catalog to 103
comparable contracts by admitting a provisional constant-default prefab spawn,
retains zero canonical mismatches and nine explicit unsupported rows, and reports
zero timed V3 allocation in 103/103 rows. The previous 3.2 artifact remains
immutable; its one expected descriptor mismatch against 3.3 is the deliberate
admission of this formerly unsupported scenario.

`ecs-v3-3.4.0-persistence-2026-08-12.json` is the accepted standalone full-world
persistence scale artifact. It contains the 100K/1M canonical snapshot,
preflight, and staging-restore measurements, authoritative capture-pause facts,
bytes/entity, fixed-width logical component throughput, managed allocation/GC,
section/scratch/staging memory accounting, and byte-identical recapture proof. It is deliberately V3-only: current
V2 has no equivalent accepted full persistence benchmark, and its existing
`PersistenceView` row is not mislabelled as save/load throughput.

`ecs-v2-v3-3.4.0-2026-08-12.json` is the accepted 3.4 regression/control join,
verified in both process orders. It retains 103 comparable contracts, zero
canonical mismatches, and nine explicit unsupported rows. The two persistence
descriptors intentionally changed from “V3 not implemented” to the accurate
3.4 contract mismatch; no fake V2 ratio was admitted.

`ecs-v3-3.4.1-persistence-2026-08-12.json` is the accepted adversarial-hardening
scale artifact. Unlike the original 3/5-sample milestone run, it uses 21 samples
at both 100K and 1M so p95 is not merely the maximum. Canonical digests and
artifact sizes are byte-identical to 3.4.0. Its larger restore-plan temporary
figures are intentional conservative accounting for the artifact clone, section
copies, parsed tables, and transient decode ownership that 3.4.0 omitted.

`ecs-v2-v3-3.4.1-2026-08-12.json` is the accepted post-hardening regression join,
verified in both process orders against 3.4.0. It retains 103 comparable rows,
zero canonical/contract mismatches, nine unsupported rows, and zero timed V3
managed allocation. Focused 31-sample create controls verified that the cold
durable-identity creation hook does not retain the initial experimental hot-path
regression in non-persistent worlds.

`s0.8-integrated-acceptance-2026-08-13.json` is immutable historical evidence
from the first S0 integration capture, but it is superseded rather than accepted
for regression. Its two-participant save and canonical oracle omitted
authoritative Runtime clock state, so it could not detect restoration of ECS at
tick 12 with the active host clock still at tick 19.

`s0.8-integrated-acceptance-hardened-2026-08-14.json` is the accepted corrected
S0 baseline for workload `ultrasim-s0.8-integrated-acceptance-v2`. It records
account-backed InternalHost save/diverge/restore and fresh HeadlessHost parity
over ECS + Accounting + authoritative Clock, accountless replacement policy,
session rebinding, artifact size, detached durable latency, owner-thread capture
pause, save/load allocation, and a 21-sample alternating paired-median
direct-ECS/InternalHost tick control over 4,096 scheduled rows. The accepted run
allocates zero warmed tick bytes and measured a 1.019x hosted/direct ratio. This
is an architecture/regression fixture, not a cross-machine performance claim;
full interpretation is in `../../S0.8 Acceptance Report.md`.

`s0.8-integrated-acceptance-cadence-independent-2026-08-14.json` advances the
integrated protocol to `ultrasim-s0.8-integrated-acceptance-v3`. It retains the
same account-backed and accountless canonical hashes, adds a real persisted
character to exact 60->20 and 20->60 Hz `world + accounting + clock` round
trips, records the frequency-independent clock-v2 artifact, and keeps the
loading host's cadence as local policy. Its performance row explicitly uses
fixed 60 Hz, 1x playback, runtime cadence changes off, and adaptive cadence off;
timed tick allocation remains zero. Full interpretation is in
`../../Authoritative Cadence Independence Report.md`.
