# W3 — immutable WorldStructuralManifest and composition identity

Status: CLOSED. Correctness/identity/capacity evidence, not a performance rating.

Starting HEAD: `21fd44a6cdb06bfe3d674a28bf0a427159fbfc09`.
Tested implementation HEAD: `59c348fe358fb86a5ee1866906fad5d18d696a16`.
The final evidence-only commit is the successor to that tested implementation;
its exact SHA is reported in the checkpoint handoff rather than recursively
embedded in this immutable file.

## Immutable artifact and runtime boundary

`Game/World/WorldStructuralManifest.cs` owns a canonical
`WorldStructuralManifest/v1`. It retains one private flat byte array. A
64-byte header (`USWMAN01`, version 1, fixed centered-grid profile 1,
16,000-mm chunk edge, 256-chunk Pack edge, 128-chunk centering bias, entry
count, and SHA-256 fingerprint) precedes sorted 40-byte entries. Each entry
is exactly an 8-byte `WorldPackCoord` plus a 32-byte `WorldPackFingerprint`.
SHA-256 covers the complete header except its own 32-byte slot, followed by
the complete entry table. The fingerprint value is a distinct 32-byte
`WorldStructuralManifestFingerprint`, not a WorldPack or chunk hash.

`TryCreate` boundedly copies and sorts input, rejects duplicate coordinates
even when the fingerprints match, and rejects default/invalid Pack
fingerprints. Input enumeration order cannot change canonical bytes or the
manifest fingerprint. `TryOpen(ReadOnlySpan<byte>)` validates magic, version,
profile, count/byte limits, exact length, strictly increasing coordinates,
fingerprints, and SHA-256 before one owned copy/publication. Truncation,
trailing data, corruption, unsupported formats/profiles, and noncanonical
ordering fail closed. `TryGetPack` uses binary search without warmed
allocation; `TryGetEntry` returns a value, not a mutable backing view.

Zero-Pack manifests are valid. Repeated Pack fingerprints at distinct
coordinates are allowed by the sparse index; W4 must validate the actual
opened artifact against each coordinate and fingerprint. The selected
default admission is 65,536 entries and 2,621,504 bytes
(64 + 65,536 × 40). Limits are not encoded in identity. A smaller sufficient
limit produced/opened the same canonical bytes and fingerprint.

`WorldCompositionRevision` is a separate 8-byte monotonic runtime/save
publication value; W3 does not yet persist or publish it. The immutable
manifest contains no `WorldId`, `SpatialSpaceId`, runtime revision, residency,
overlay, Pack bytes, or runtime object reference. An entry states only the
authored expectation `PackCoord -> PackFingerprint`, not that the Pack is
prepared or resident. The current `GameWorldState` remains one-Pack and the
Concept Demo's legacy cross-window Pack was not inserted into a W3 manifest.

## Reproducible focused results

| Fixture | Result |
| --- | --- |
| Empty manifest | 0 entries, 64 bytes, valid and stable fingerprint `2DB59A27C45494CFD3480F95D1970CFC24DFE0D43B7F10D6F1D5C499AB8C197B` |
| Valid declared one-Pack shard | 1 entry, 104 bytes, round-trip and exact lookup; manifest fingerprint `D135EE9696B17F26A41F7107EECE958EE83E212C5ECAB093DDD75C4829A280E4` |
| Sparse multi-Pack fixture | 4 sorted entries, 224 bytes, including a 2,000/-500 distant address and a repeated fingerprint at distinct coordinates; fingerprint `7B1D41AA70578D675DF0F16F67E4637EE3BE0A2FBA24736D0E3DAA4E05C5ED34` |
| 10,000 widely separated Packs | 400,064 canonical bytes, 400,000 entry-payload bytes; exact lookup and reopen; warmed 100,000 lookups allocated 0 managed bytes on the test thread |
| Exact capacity | 65,536 entries and 2,621,504 bytes created and reopened |
| One over capacity | 65,537 input entries rejected with `CapacityExceeded` |

The 10,000-entry test observed 9 ms build and less than 1 ms open on this
machine. These are diagnostic single-run observations, not rated timings.
Mutation tests changed a coordinate, fingerprint, addition, and removal;
each changed manifest identity. Duplicate same/different-fingerprint
coordinates were rejected. A caller's subsequent source-array or input-byte
mutation did not change a published manifest. Corruption tests covered magic,
version, all profile fields, count/length, truncation, trailing bytes,
fingerprint slot, coordinate and Pack-fingerprint content, duplicate/out-of-order
entries (including resigned bytes), and an invalid zero Pack fingerprint.

## Verification and lineage

Environment: Linux 7.2.6-1-cachyos x86_64, .NET SDK 8.0.131, Godot 4.7.1
stable Mono, Release configuration.

- `dotnet run --project _Tests/UltraSim.Game.World.Tests/UltraSim.Game.World.Tests.csproj -c Release` — PASS, 104/104 World tests (99 prior plus five W3 tests).
- `dotnet run --project _Tests/UltraSim.Game.World.Tests/UltraSim.Game.World.Tests.csproj -c Release -- --w2-full-pack` — PASS: real 65,536-chunk v1 artifact, 280,363,264 bytes; old 256 MiB admission rejected, 512 MiB admitted, declared window valid.
- `./verify.sh --architecture-only` — PASS, four checks.
- `./verify.sh --configuration=Release` — PASS, one uninterrupted run on the tested implementation commit, including World, Network/application, native Godot/export, Contentless Online, mixed swarm/lifecycle, and final composition gates. Retained local log: `/tmp/ultrasim-w3-release-2026-09-23.log`.

The canonical verifier deliberately runs negative Network-flavor builds; the
early expected `Build FAILED` text in that log is followed by
`PASS Network_UnknownCapabilitySetFailsClosed`, and the verifier exits 0 with
`PASS repository verification`.

`WorldPack/v1` bytes/fingerprints and W2 declared-shard validation are
unchanged. Game manifest remains v32; Network transport revision remains 2.
No World runtime, persistence, Client Content, or Network integration was
started. W1-W3 are closed; W4 is next, and W4+ was not begun here.
