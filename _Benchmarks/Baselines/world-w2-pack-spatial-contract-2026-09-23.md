# W2 — declared WorldPack spatial contract and full-Pack capacity

Status: CLOSED. Correctness/capacity evidence, not a performance rating.

Starting HEAD: `ecd23de77c9fab875233d8932cc589512e417d79`.
Tested implementation HEAD: `a8bc01e0bdf9d6fb8ee71c9bb182f1484f54b047`.
The final evidence-only commit is the successor to that tested implementation;
its exact SHA is reported in the checkpoint handoff rather than recursively
embedded in this immutable file.

## Contract and compatibility

`WorldPackAddressing.MaximumChunksPerWorldPack` derives 256 × 256 = 65,536
from the W1 address edge. `WorldPackLimits` rejects a higher chunk limit and
retains lower custom limits. The default total artifact-byte ceiling changed
from 268,435,456 bytes (256 MiB) to 536,870,912 bytes (512 MiB); independent
section maxima are unchanged. Stream open still rejects an over-limit declared
length before proportional body allocation/read.

An additive `WorldPackBuilder(..., WorldPackCoord declaredPackCoord)` rejects a
foreign terrain chunk at insertion with `WorldPackBuildError.ChunkOutsidePackWindow`.
`SpaceMismatch`, `DuplicateChunk`, and `CapacityExceeded` remain distinct.
Existing unaddressed builders still work. An opened v1 artifact can be checked
against an external declaration using
`WorldPack.TryValidateSpatialWindow(declaredPackCoord, out offendingKey, out error)`.
It walks canonical chunk-directory keys, reports
`WorldPackSpatialValidationError.ChunkOutsidePackWindow` on mismatch, and does
not mutate the artifact. A warmed 10,000-check valid loop allocated zero
managed bytes on its test thread. `WorldPackArtifact.TryOpen(limits, ...)`
revalidates owned canonical bytes against a specified admission policy without
cloning a large artifact.

The declared builder accepts Pack (0,0) edge chunks -128 and 127 on either
axis, rejects -129 and 128, and accepts Pack (1,0) X=128..383. Negative Pack
and signed-int32 extreme partial-window cases pass. A static owned by chunk
(127,127) may geometrically cross the Pack seam without changing chunk
ownership. A three-chunk declared Pack remains under 20,000 bytes; no dense
65,536-chunk placeholder storage is serialized. An opened Pack (0,0) artifact
fails validation as Pack (1,0), and a legacy artifact containing X=127 and
X=128 fails both single-Pack declarations.

For an identical 25-chunk Cabin-shaped source fixture, declared and legacy
builders produced exactly equal length, `WorldPackFingerprint`,
`WorldContentFingerprint`, and canonical bytes. The Pack coordinate remains
external composition metadata: `WorldPack/v1` bytes, format version, and
normal fixture fingerprints are unchanged.

The current Concept Demo still contains central X/Z=-4..4 and Far Island
X=124..132 terrain. Its historical one-Pack artifact therefore spans future
Pack (0,0) and (1,0) windows; the test confirms it fails either single-shard
declaration. This is intentional transitional compatibility, not a canonical
multi-window shard. Game runtime remains one-Pack. No World manifest, Pack
residency, multi-Pack runtime, or remote protocol change was introduced.

## Real full-Pack acceptance

The dedicated automated acceptance authored every global chunk at X/Z=-128..127
in declared Pack (0,0), built a real canonical v1 artifact, reopened it, checked
all 65,536 directory entries through spatial validation, and checked first and
last keys. Its single artifact was tested against both admission limits without
a second giant copy.

| Observation | Result |
| --- | ---: |
| Chunk count | 65,536 |
| Canonical artifact bytes | 280,363,264 |
| Canonical artifact MiB | 267.375244 |
| Chunk-directory bytes | 3,145,728 |
| Foundation terrain bytes | 277,217,280 |
| Custom 256 MiB admission | `CapacityExceeded` |
| Default 512 MiB admission | `None`, opened |
| Declared Pack (0,0) validation | passed |
| Observed build time | 836 ms |
| Observed open/validate time | 518 ms |

The two elapsed observations are from one local run and are not rated
performance results. The artifact exceeds 256 MiB by 11,927,808 bytes and is
below the new 512 MiB total ceiling.

## Verification on the implementation commit

- `dotnet run --project _Tests/UltraSim.Game.World.Tests/UltraSim.Game.World.Tests.csproj -c Release` — PASS, 99/99 routine World tests.
- `dotnet run --project _Tests/UltraSim.Game.World.Tests/UltraSim.Game.World.Tests.csproj -c Release -- --w2-full-pack` — PASS, real full-Pack acceptance above.
- `./verify.sh --architecture-only` — PASS, four architecture checks.
- `./verify.sh --configuration=Release` — PASS on an uninterrupted second run, including World, Network/application, Godot/native, export, Contentless Online, mixed lifecycle, and final composition gates. Retained local log: `/tmp/ultrasim-w2-release-2026-09-23.log`.

The first canonical attempt exited 134 while executing the F0.3b remote
playable Network Application suite. The initial tool output was truncated, so
the specific assertion is unavailable; it is not attributed to W2 or declared
flaky without evidence. The isolated Release Network Application suite then
passed, and the unchanged implementation commit completed the uninterrupted
canonical rerun. The passing rerun, not the isolated test, is the closure gate.

Game manifest remains v32; Network transport revision remains 2. W1-W2 are
closed, W3 is next, and W3+ work was not started here.
