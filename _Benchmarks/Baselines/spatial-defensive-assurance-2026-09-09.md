# Spatial defensive-assurance correction — 2026-09-09

## Scope and independent disposition

- External review base: `43d5184377e1a6da4be598a401d50ae9439c3b91`.
- Independent correction base: `fe79a600478cefb82fa4310624031231deb66e6b`.
- External probe source was not supplied or used. Each reported mechanism was
  traced in the current source and covered by repository-owned regression tests.
- This correction changes private lookup/configuration/error-path mechanics. It
  does not change canonical spatial identity, World authority, AOI disclosure, wire
  format, or selected replication profile.

| Review item | Disposition | Permanent evidence |
| --- | --- | --- |
| SP-01 checked signed guards | Confirmed and repaired | `CheckedTryGuardsRejectSignedOutOfRangeInputs`; authored offset-seam, client negative-fragment, and remote subscription-index cases |
| SP-02 historical hash tombstones | Confirmed and repaired | 2,048-key World/client turnover gates; existing randomized World/publication/client and zero-allocation churn gates |
| SP-03 capacity rounding nontermination | Confirmed and repaired | boundary/helper and constructor rejection in `SpatialMapCapacityRejectsUnrepresentableConfiguration` |
| SP-04 narrow dirty-chunk grid | Confirmed and repaired | canonical minimum/maximum World anchor traversal at the admitted 16,000 mm minimum |
| H-01 unbounded empty-cell discovery | Source-confirmed and hardened | dirty-entity and dirty-chunk over-budget construction rejection |
| H-02 implicit movement headroom | Confirmed and hardened | saturated sole-region replacement, shared-source rejection/recovery, and existing-destination collapse |

## 2026-09-10 wide-grid arithmetic follow-up

A geometrically bounded custom dirty-entity grid using `int.MaxValue` for both the
far radius and spatial-region edge independently reproduced two checked-arithmetic
failures. Construction first overflowed in `ValidateSpatialDiscoveryWork` because
the radius/edge addition occurred before promotion. Promoting only that expression
then exposed the original `CellRadius` overflow during recipient admission.

Both admission and runtime discovery now use one ceiling-division helper whose first
operand is promoted to `long`. `P03EDirtyEntityLargeCustomGridArithmeticUsesWideIntermediates`
constructs both entity and chunk-batched variants, admits a recipient, and verifies
the bounded empty discovery without partial-state failure. Default geometry and the
4,096-cell work ceiling are unchanged.

## Corrected contracts

- Boolean/index error protocols explicitly test signed lower and upper bounds before
  checked arithmetic. A malformed additional-terrain seam returns
  `InvalidAdditionalTerrainSurface`, and the builder remains reusable.
- Persistent fixed open-addressed maps in World membership, chunk publication,
  client semantic residency, and dirty-chunk reference indexes use gap-closing
  linear-probe deletion. No normal deletion leaves a tombstone.
- `SpatialStorageCapacity.DoubleAndRoundUpPowerOfTwo` rejects nonpositive,
  doubling-overflow, and rounding-overflow settings. Consumers calculate map sizes
  before proportional array allocation.
- `DirtyChunkSpatialFanout` requires a region edge of at least 16,000 mm. Both
  alternate dirty fanouts reject discovery above 4,096 cells; dirty-entity
  admission independently bounds its point-centered and chunk-batched scan shapes.
- A saturated sole-occupant dirty-chunk region can be replaced only after complete
  subscription/row preflight. Shared-region movement without a destination slot
  remains a transactional `CapacityExceeded` result.

## Focused evidence

- World: 89 checks passed.
- Client Core: 29 checks passed.
- Game Replication: 137 checks passed, including the wide-grid arithmetic follow-up.
- World map after 2,048 distinct one-entity chunk lifetimes: empty-cache miss = one
  lookup / one bucket probe.
- Client semantic cache after 2,048 request/retire lifetimes: empty-cache miss = one
  lookup / one bucket probe.
- Existing warmed movement/publication/client churn allocation gates remain at zero
  managed bytes.

The complete exact-tree repository verifier passed for this correction and its
arithmetic follow-up, including native/exported product and final composition gates.
