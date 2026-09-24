# W5 — connected two-Pack local World proof

Date: 2026-09-24. Starting HEAD: `47e8926d196bc6c191cf12b230d62314fd25744b`.
Tested implementation HEAD: `9b430d0ef3bb525a5dfc770fbc16763d029416eb`.
The separate commit containing this evidence is identified by Git; embedding its
own SHA in the file would be self-referential. Release verification ran on the
tested implementation commit, with a clean worktree after the evidence commit.

Environment: Linux 7.2.6-1-cachyos x86_64; AMD Ryzen 7 7800X3D (8 cores,
16 threads); .NET SDK 8.0.131; Godot 4.7.1 stable Mono. These are correctness
acceptances and resource observations, not performance ratings.

## Exact fixture and structural result

Two **independently compiled declared** `WorldPack/v1` artifacts share one Game
content binding and one `SpatialSpaceId`. Pack `(0,0)` authors X=125..127,
Z=-2..2; Pack `(1,0)` authors X=128..130, Z=-2..2. The 30-chunk strip supplies
the native 5×5 semantic working set around the 127/128 seam. Both Packs have
flat 0 mm base terrain and one frozen static plus one one-piece Multi in the
seam chunk. The two-entry `WorldStructuralManifest/v1` and revision-1 prepared
composition are separate from either Pack's identity.

| Identity | Fingerprint |
| --- | --- |
| Pack `(0,0)` | `FEC7690954F83329C54C21EF72EA69D3199D9F0439821A235C55BB21FCEDC46C` |
| Pack `(1,0)` | `66C1B70EB4908248926392CF76F01A0BD069F7AE94004755DA2E5CAD5112EE84` |
| Manifest | `AF4DF32E909774981590BADF21E1281094A40E10977C162F69853BEDAB832BDB` |

`PreparedWorldComposition.TryValidateComplete` requires every manifest Pack to
be prepared. It checks exact integer/q5 base-height equality for adjacent
authored cross-Pack edges, and unique effective `WorldStaticId` and
`MultiInstanceId` ownership. The valid fixture passes. Independently valid
Packs with a mismatched edge, duplicate static ID, or duplicate Multi ID are
each rejected with their distinct error. A one-Pack prepared subset remains a
valid runtime query root but fails complete validation. The world test checks
all 33 shared edge vertices, both native mesh edges, and the expected global
triangle parity on either side; a seam-spanning bounds query yields exactly
four hits (two frozen statics, two Multi fragments), and both Multi roots are
retrievable through their owning prepared views.

One logical Multi extending across Pack artifacts is **not** represented by
the current v1 profile; W5 only proves separate, globally collision-checked
Multis on both sides. Repeated `WorldSurfaceId` is not globally rejected, but
additional-surface cross-Pack continuation is not claimed.

## Authority, persistence, and client acceptance

The integrated fixture admits a real playable and submits ordinary direct
locomotion commands. Its global home-chunk sequence is
`127 → 128 → 127 → 128`; after saving it makes one more ordinary crossing
back to 127 before restore. The same persistent actor and generational public
identity survive the crossings. `UltraSimSimplePhysics` probes legal occupancy
on both sides and supported sweeps in both directions; the runtime movement
test confirms ordinary `WorldSpatialIndex` home migration and target-present /
old-source-absent `ChunkPublication` snapshots. No Pack identity was introduced
into movement, Physics, spatial membership, or public entity identity.

`worldview/v2` stores exact manifest fingerprint, `SpatialSpaceId`, durable
world ID, independent World composition revision, and sorted durable per-Pack
overlay rows. Immutable manifest/Pack bytes are resolved externally. The
integrated test uses composition revision 3 and independent revision-1 terrain
overlays in Packs 0 and 1; both restore along with the exact saved actor global
position in chunk 128. A deliberately unavailable exact-Pack resolver fails
without publishing a new generation or changing the divergent active actor
position. The focused v2 test also round-trips a **revision-2 zero-changed-row
overlay**, rejects duplicate rows and absent manifest/Pack resolvers, and
observes a 4,582-byte v2 artifact for that sample. Current capture explicitly
refuses a partially prepared composition rather than silently omitting durable
overlay state; the artifact format itself contains no residency list. Historical
`worldview/v1` capture/restore remains unchanged and green.

The local `ClientWorldSemanticCache` uses the canonical composition without
claiming an arbitrary single-Pack fingerprint. `ClientStructuralResidency`
holds a 25-chunk semantic working set spanning both Packs. The dedicated native
Godot scene realizes both seam chunks and their static/Multi batch groups in a
9-chunk render window. It moves the center across the seam while retaining the
same cache, renderer root, and seam chunk handles, with zero reset transitions.
Native work records 12 builds and 12 retirements; after explicit retirement,
active render chunks/resources are zero and only the renderer root node remains.
The existing Concept Demo has not been migrated and its Local/Online/Complete
behavior remains covered by the unchanged canonical product gates.

## Verification and limits

Commands run on the implementation commit:

```text
dotnet run --project _Tests/UltraSim.Game.World.Tests/UltraSim.Game.World.Tests.csproj -c Release --no-restore
dotnet run --project _Tests/UltraSim.Game.World.Tests/UltraSim.Game.World.Tests.csproj -c Release --no-restore -- --w2-full-pack
dotnet run --project _Tests/UltraSim.S0.Integration.Tests/UltraSim.S0.Integration.Tests.csproj -c Release --no-restore
./verify.sh --architecture-only
./verify.sh --configuration=Release
```

Results: 112 World tests, the retained 65,536-chunk W2 full-Pack acceptance,
38 integrated acceptances, and the architecture gate passed. The **second**
canonical Release run completed uninterrupted, including all 13 Game
Persistence, 34 Client Core, 22 Local client, historical T0.8/T0.10 native,
the new W5 native scene, exports, Contentless Online and mixed-session gates;
it ended `PASS repository verification`. The first canonical run on the same
commit stopped at an existing `M00SharedAuthorityTests` presentation assertion
(line 319). The unchanged Network Application suite passed on isolated rerun,
then the complete canonical rerun passed. This is recorded as an intermittent
pre-existing gate outcome, not erased or attributed to W5 without evidence.

WorldPack/v1 and WorldStructuralManifest/v1 are unchanged; Game manifest is
v32 and Network transport revision is 2. W6 state vocabulary, W7 remote
acquisition, W8 live composition mutation, and W5R Concept Demo migration were
not implemented. **W5 is closed; W5R is next.** Structured results are in the
adjacent JSON artifact.
