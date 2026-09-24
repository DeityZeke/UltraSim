# W4 — prepared World composition and global structural queries

Status: CLOSED. Correctness/runtime-query evidence, not a performance rating.

Starting HEAD: `a8d26a8e1f6ab5f03b62f79fcf299b392ce93ded`.
Tested implementation HEAD: `24ad1d31a3361feaa134cc7907b1710e4ab7eb38`.
The final evidence-only commit is its successor; git history and the checkpoint
handoff give its SHA without recursively changing this immutable artifact.

`PreparedWorldComposition` retains one valid `SpatialSpaceId`, nonzero
`WorldCompositionRevision`, immutable complete `WorldStructuralManifest`, and a
private sorted flat array of `PreparedWorldPack(Coord, WorldPreparedView)`
references. The array is copied before validation/publication and never exposed
mutably. Lookup is an allocation-free binary search. Its 64-Pack diagnostic
used 16 bytes per prepared array entry on this x64 host; underlying Pack bytes
are borrowed and not counted as composition-owned storage.

Cold construction rejects invalid revision/Space, excess prepared count,
duplicate coordinates, Pack absent from manifest, wrong exact Pack fingerprint,
wrong Space, W2 spatial-window mismatch, and inconsistent effective content
fingerprint. `WorldPreparedView` continues to enforce overlay/base fingerprint
and Space equality before it can be supplied. A failed candidate returns no
composition and mutates no existing one. Revision and prepared subset leave the
manifest fingerprint unchanged.

Global `SpatialChunkKey` queries derive the Pack coordinate internally. Base
terrain probe/height/material/mesh, additional surface bands, vertical-column
support/ceiling, frozen statics, Multi fragments, replacement identity, and
bounds scans retain the accepted borrowed views and algorithms. The base mesh
source can read its neighbor halo from a different prepared Pack. A small
synthetic two-shard 127/128 test produced exactly the same mesh vertices,
indices, materials, and summaries as a legacy combined-artifact oracle; this
is a W4 routing test, not W5's complete connected playable-world proof.

## Focused observations

| Fixture | Result |
| --- | --- |
| Empty manifest and zero prepared Packs | Valid revision-1 composition; terrain unavailable, static query empty |
| Two-entry manifest with zero/one/two prepared Packs | All valid; one-Pack subset does not erase other manifest entry |
| One real W2-valid Pack | Direct `WorldPreparedView` and composition terrain, mesh, statics, Multi fragments, columns, and bounds answers equal |
| Overlay replacement | Direct and composed material/static/replacement revision/hash answers equal |
| T1.0 intentional void | Every cell probe matched, including Absent versus Unavailable |
| T1.1 additional surface | Band count and ordered column/support answers matched |
| Three sparse Packs, including (2000,-500) | Exactly three prepared references, 48 index bytes, no coordinate-span allocation |
| Static bounds across Pack 0/1 seam | Two exact home-chunk-owned static hits, no duplicate/missing result |
| Per-Pack overlay revisions | Pack 0 revision 2, Pack 1 revision 7; composition revision 3 independent |
| GameWorldState | Legacy and canonical terrain/material/static/Physics occupancy answers equal; dual roots rejected |
| `worldview/v1` capture of canonical root | Explicit `NotSupportedException`; cannot silently persist a partial World |
| 64-Pack warmed diagnostic | 100,000 exact Pack lookups: 38.3 ns/op; 100,000 chunk queries: 244.7 ns/op; combined lookup/query/sample loops allocated 0 managed bytes |

The timing figures are one local diagnostic run, not an admission/rating target.
The full-Pack W2 fixture still opened all 65,536 chunks in its unchanged
280,363,264-byte WorldPack/v1 artifact under the 512 MiB admission limit.

`GameWorldState` has mutually exclusive legacy `PreparedWorld` and canonical
`PreparedComposition` roots. A canonical composition can be installed only in
an unpublished staging shell. Simple Physics now consumes GameWorldState's
column query surface instead of reading the legacy view directly; it knows no
Pack coordinate. The current Concept Demo remains on the legacy unaddressed
one-Pack path because its historical artifact spans Pack (0,0)/(1,0). No
Concept Demo split, runtime Pack mutation, acquisition, Client, AOI, ECS,
Network, or persistence-format migration was started.

## Verification on `24ad1d31`

- Release World suite: PASS, 110/110 tests, including six W4 tests.
- Retained W2 full-Pack acceptance: PASS, 65,536 chunks and 280,363,264 bytes.
- `./verify.sh --architecture-only`: PASS, four checks.
- `./verify.sh --configuration=Release`: PASS, one uninterrupted canonical run,
  including Game/Physics/persistence, native/export, Contentless Online,
  bot/native, lifecycle, and final composition gates. The verifier's expected
  invalid-Network-flavor build failures were classified PASS by the verifier.
- `git diff --check`: PASS on the implementation tree.

Environment: Linux 7.2.6-1-cachyos x86_64, AMD Ryzen 7 7800X3D,
.NET SDK 8.0.131, Godot 4.7.1 stable Mono, Release configuration.
WorldPack/v1 and WorldStructuralManifest/v1 bytes/fingerprints remain unchanged.
Game manifest remains v32 and Network transport revision 2. W1-W4 are closed;
W5 is next, and W5/W5R work was not started here.
