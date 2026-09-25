# W6 — explicit structural-state baseline

Tested implementation: `72fcfacd209a7c2c79ea6e20ab8a235bdd098372` (starting
HEAD `4acbfc101fded0df1bc12b695ed5d3391d0545c7`). Final evidence HEAD is
the commit containing this immutable report; see Git history. Linux
7.2.6-1-cachyos, Ryzen 7 7800X3D, .NET SDK 8.0.131/runtime 8.0.31,
Godot 4.7.1 Mono; Release configuration.

`PreparedWorldComposition.TryProbeStructuralChunk(key, out probe)` resolves the
owning Pack with W1 addressing. Wrong `SpatialSpaceId` returns `false` and a
default result, never `NotAuthoredPack`. A manifest miss yields
`NotAuthoredPack` with no expected fingerprint. A manifest hit yields its exact
Pack fingerprint; absent prepared Pack yields `KnownPackButNotPrepared`, absent
effective chunk yields `AuthoredPackMissingChunk`, otherwise `PreparedChunk`.
`TryProbeBaseTerrainResolution(location, out result)` further resolves a prepared
chunk's base-cell presence to `IntentionalVoid` or `Present`; height/material
exist only for `Present`. Y does not affect this base result. Neither probe
acquires or mutates a Pack. Legacy `GameWorldState` and legacy client cache
return unsupported for this manifest-dependent detail.

| State | Exact exercised example | Legacy base | Vertical column |
| --- | --- | --- | --- |
| NotAuthoredPack | Concept Demo chunk (384,0), owner Pack (2,0) absent from manifest | Unavailable | Unavailable |
| KnownPackButNotPrepared | partial Concept Demo, Far East chunk (128,0), Pack (1,0) declared but omitted | Unavailable | Unavailable |
| AuthoredPackMissingChunk | Concept Demo chunk (40,40), inside prepared Pack (0,0) but outside authored clusters | Unavailable | Unavailable |
| IntentionalVoid | Concept Demo Sky Island destination, base presence Absent | Absent | Complete, two T1.1 faces |
| Present | Concept Demo PlayerStart, valid base height/material | Present | Complete, base face |

The controlled same-manifest before/after pair changes Far East from
`KnownPackButNotPrepared` to `PreparedChunk` while manifest and expected Pack
fingerprints remain equal. An empty manifest returns `NotAuthoredPack`; a
prepared sparse Pack distinguishes its authored chunk from an absent chunk.
One authored chunk supplies both Present and Absent cells. Extreme signed
chunk coordinates do not overflow. Repeated, re-ordered probes do not prepare
content. A 10,000-entry sparse manifest and 100,000 warmed mixed chunk/base
probes allocated **0 managed bytes**; diagnostic elapsed ticks were 94,624,589
for the mixed loop (not a performance rating). Client staging refuses all three
non-prepared/unauthored statuses with `MissingTerrain` and retains Requested state.

W5R content identities are unchanged: Pack (0,0)
`A76FAE182AA566BE91D312E26FCE29EA6E2854E84402D50579A6C5C2009553CB`,
Pack (1,0)
`CE9F0CCDB591F3C3E592B8105E43C1AEB8323A5CB686533FC4735A8AA15D7B1D`,
manifest `41DB6F68C26AC85DF5ABEB60BEDA3F3F07DD1E76CF30A5164874446D902B6B53`,
and remote-only combined compatibility Pack
`323141A827CD25ECC5BC144CE860FECF717F472E1C0ED57626BCC89EC29CF795`.
The complete product composition retains 162 chunks. WorldPack/v1,
WorldStructuralManifest/v1, worldview/v2, Game manifest v32 and Network
transport revision 2 are unchanged. Prepared residency is not persisted as
authored truth. W7 acquisition/protocol and W8 mutation were not started.

Verification on the tested implementation commit:

- `dotnet run --project _Tests/UltraSim.Game.World.Tests/UltraSim.Game.World.Tests.csproj -c Release` — 115/115 pass, including W5/W5R, persistence, physics, W6 matrix and Concept Demo.
- Same command with `-- --w2-full-pack` — pass, 65,536 chunks/280,363,264 bytes.
- `dotnet run --project _Tests/UltraSim.Client.Tests/UltraSim.Client.Tests.csproj -c Release` — 34/34 pass, including composition-backed client probe and fail-closed staging.
- `./verify.sh --architecture-only` — pass.
- `./verify.sh --configuration=Release` — one uninterrupted pass, including canonical World/Client tests, Local Godot product smokes, all required exports, Contentless Online, mixed lifecycle and final repository verification.

W6 is closed; W7 is next. Detailed statuses are read-only explanations of
existing structural truth, not new persistence or wire state.
