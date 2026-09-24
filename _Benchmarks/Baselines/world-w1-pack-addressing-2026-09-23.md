# W1 — WorldPack coordinate primitive

Status: CLOSED. Correctness/contract evidence, not a performance rating.

Starting HEAD: `9ce042096788f3fff878c00af784767a98a67121`.
Tested implementation HEAD: `ba1c631547b88b8293ad6c9dd7a126edf40e918b`.
The final evidence-only commit is the successor to the tested implementation; its
exact SHA is reported in the checkpoint handoff rather than embedded recursively
in this immutable file.

`Game/World/WorldPackAddressing.cs` owns the new vocabulary. `WorldPackCoord`
is an 8-byte pair of signed 32-bit X/Z addresses. `WorldPackLocalChunkCoord`
is a 2-byte pair of validated 0..255 X/Z addresses. The Pack edge is 256
global 16 m SpatialChunks, or 4,096 m. The centering bias is 128 chunks.
These are structural World-owned addresses, not `SpatialSpaceId` or a replacement
for global `SpatialChunkCoord`/`SpatialChunkKey`.

For either axis, `biased = (long)globalChunk + 128`, then
`SpatialMath.FloorDivRem(biased, 256)` yields Pack and local. The inverse computes
`(long)pack * 256 + local - 128` and rejects either axis outside signed 32-bit
global chunk range. Thus every global chunk has exactly one Pack/local address,
while arbitrary Pack addresses outside the representable global range fail
closed. Edge Pack windows are partial. Pack (0,0) covers -128..127 on both axes.

| Global chunk axis | Pack axis | Local axis |
| ---: | ---: | ---: |
| -384 | -1 | 0 |
| -129 | -1 | 255 |
| -128 | 0 | 0 |
| -1 | 0 | 127 |
| 0 | 0 | 128 |
| 127 | 0 | 255 |
| 128 | 1 | 0 |
| 383 | 1 | 255 |
| 384 | 2 | 0 |
| `int.MinValue` | -8,388,608 | 128 |
| `int.MinValue + 1` | -8,388,608 | 129 |
| `int.MaxValue - 1` | 8,388,608 | 126 |
| `int.MaxValue` | 8,388,608 | 127 |

The tests apply the canonical cases independently to X and Z, include mixed-sign
coordinates and different SpatialSpaceIds, and check every seam ±1 for Pack
transitions -2,048..2,048. Every tested address round-trips, and duplicate
Pack/local addresses correspond only to the same global coordinate. The test
rejects invalid locals, arbitrary extreme Pack addresses, and locals beyond
the partial signed-32-bit edge Pack windows. A warmed 100,000-address mapping,
inverse, and containment loop allocated 0 managed bytes on its test thread.

Verification on `ba1c631547b88b8293ad6c9dd7a126edf40e918b`:

- `dotnet run --project _Tests/UltraSim.Game.World.Tests/UltraSim.Game.World.Tests.csproj -c Release` — PASS, 94 tests.
- `./verify.sh --architecture-only` — PASS, four architecture checks.
- `./verify.sh --configuration=Release` — PASS, one uninterrupted canonical Release run, including the World suite, native Godot checks, exports, remote/content and lifecycle gates.

Only the new address file, World tests, and current documentation changed from
the starting HEAD. `WorldPack.cs`, `GameWorldState.cs`, and `Kernels/Spatial` did
not change. Existing `WorldPack/v1` layout/fingerprint tests passed unchanged;
`MaximumPackBytes` remains 256 MiB. The active runtime remains one-Pack. W2
Pack-bound admission and 512 MiB capacity proof, and W3+ composition/residency,
were not started.
