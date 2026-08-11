# Accepted ECS comparison baselines

`ecs-v2-v3-2026-08-09.json` is the first immutable joined V2/V3 semantic
baseline for protocol `ecs-v2-v3-semantic-comparison-v1`. It was captured with
4 warmups and 15 measured iterations per scenario, V2 first, tiered compilation
and ReadyToRun disabled, on the environment embedded in the JSON.

Do not overwrite an accepted artifact. Create a newly dated file when the
protocol, host, runtime, measurement policy, or intentionally accepted V3 state
changes. The orchestrator enforces this for `--accept-baseline`.

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
