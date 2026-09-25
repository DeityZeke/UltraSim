# W7 — Remote canonical WorldPack streaming (2026-09-25)

W7 is closed on tested implementation `e30a541025ccdaf4c13986fbb6f659781aa95537`.
Starting HEAD was `854d58526714cf4eb68b4f9456d56f81b8241e0e`; the W7
foundation was `dfce6f1ab19da3114792b444dc781ee7f18348dc` and its
accepted correctness repair was the starting HEAD. The separate evidence/docs
commit is discoverable through Git history of this artifact. No W8 edit or
WorldPack/manifest/worldview format change is part of W7.

## Product result

The authenticated server advertises `SpatialSpaceId`, composition revision,
manifest fingerprint, manifest content ID, and server-owned initial anchor.
After normal required Content application, World resolves and validates the
exact manifest and sends a separate manifest-applied proof. The client then
publishes a zero-Pack composition under one `ClientPreparedStructuralWorld` and
one continuously retained `ClientWorldSemanticCache`. A coordinate-only Pack
request receives a session/sequence/fingerprint/package/source-bound grant;
Content's shared exact-package acquirer verifies the package into the realm CAS
without republishing the application catalog. World validates the opened Pack,
spatial window, content binding, and prepared-subset compatibility before
publishing the new immutable subset. Exact Pack-applied and bounded semantic
window proofs gate recipient delivery; they do not change Game movement,
interaction, physics, AOI truth, or other recipients. The Godot renderer sees
semantic chunks and remains Pack-blind.

| Artifact | Authored chunks | Canonical raw bytes | Frozen fingerprint |
| --- | ---: | ---: | --- |
| Manifest | 2 entries | 144 | `41DB6F68C26AC85DF5ABEB60BEDA3F3F07DD1E76CF30A5164874446D902B6B53` |
| Pack `(0,0)` | 117 | 531,138 | `A76FAE182AA566BE91D312E26FCE29EA6E2854E84402D50579A6C5C2009553CB` |
| Pack `(1,0)` | 45 | 199,910 | `CE9F0CCDB591F3C3E592B8105E43C1AEB8323A5CB686533FC4735A8AA15D7B1D` |

The old 162-chunk combined fingerprint
`323141A827CD25ECC5BC144CE860FECF717F472E1C0ED57626BCC89EC29CF795`
remains a regression/save-identity oracle; normal Online and Complete-host
Remote structural delivery uses the manifest and independent Pack packages.
Game protocol is v33 (Control messages 150 advertisement, 151 manifest proof,
152 Pack request, 153 grant, 154 Pack proof, 155 window proof, 156 retirement,
157 interest retirement); Network transport revision remains 2.

Cold central Online admission prepared only `(0,0)`: two manifest entries, one
prepared Pack, 25 semantic resident chunks (not all 117 authored chunks), and
nine rendered chunks in the native product smoke. Pack `(1,0)` transferred zero
payload bytes before admission; the first Far-Island demand fetched its 199,910
raw bytes independently. Native Far Gate and ordinary 127→128→127 movement
passed within one application session, semantic cache, manifest/revision,
renderer, and public-actor identity. The client safely retired `(1,0)` only
after semantic/render borrowers retired; W6 then reported
`KnownPackButNotPrepared`. A later authorized request reused CAS with zero Pack
payload network bytes. Existing `(0,0)` prepared-view identity was retained
across add/remove/reprepare. The Content catalog was not reactivated per Pack.
The 10,000-entry sparse-manifest control prepared one Pack with no eager
acquisition or coordinate-span allocation. Unsupported nonempty authority
overlay fails Remote deployment closed.

Failure/authorization coverage is layered: Game Protocol tests enforce bounded
canonical v33 controls; application tests cover admission ordering, stale,
contradictory and wrong-fingerprint advertisements, absent-Pack requests,
stale/contradictory proofs and grants, exact request lineage, failed
Pack acquisition rollback, canceled in-flight acquisition, Far Gate/seam,
retirement, and unsupported overlay. Content tests cover unauthorized or
corrupt manifest/package/blob/hash input and exact catalog/realm authority;
World tests cover malformed/versioned manifest and Pack bytes, fingerprint,
spatial window, binding, and subset conflicts. An authenticated conflicting
duplicate grant was additionally reproduced as a retirement blocker before
`222fe749`; that repair rejects the conflicting message without changing the
previously accepted grant or its exact validation/proof/retirement lifecycle.
No failed acquisition publishes partial World truth.

Rapid seam movement can leave a small number of already-admitted public
deliveries in flight for a subsequently retired semantic chunk. These are
explicitly rejected, never counted as applied. The focused and native gates
distinguish this bounded still-prepared-Pack case from any attempted delivery
to an unprepared Pack; their limit is four such tail deliveries for the fixed
journey. The focused final-tree run observed one, with matching missing-resident
and still-prepared diagnostics. No structural-acquisition rejection or false
applied proof is accepted by the gate.

## Verification and provenance

Environment: Linux `7.2.6-1-cachyos` x86-64, AMD Ryzen 7 7800X3D (16
logical CPUs), .NET SDK 8.0.131/runtime 8.0.31, Godot 4.7.1 Mono, Release.
The repository verifier uses separate server, native exported Online/Complete
clients, and bot processes for product gates. Reproduce with:

```bash
dotnet run --project _Tests/UltraSim.Network.Application.Tests/UltraSim.Network.Application.Tests.csproj -c Release
./verify.sh --architecture-only
./verify.sh --configuration=Release
```

The focused Network application suite passed (eleven W7 tests), as did
architecture-only verification and one **uninterrupted complete** canonical
Release run on `e30a5410`. The latter includes Game World/Protocol/Replication,
Client Core/Content/Network/Local, S0 integration, W2 full-Pack control,
ServerOnly/ClientOnly boundaries, real UDP, Contentless Online, exported
Single Player/Online/Complete, native W7 Far Gate/seam/retirement/warm journey,
Complete-host clients, bots/impairment, and mixed F0.7 lifecycle. The verifier
ended `PASS repository verification`; worktree was clean before evidence edits.

Earlier attempts are not recast as passes: an initial `da86f663` canonical run
failed an overstrict native zero-tail-delivery assertion despite a completed
W7 journey; `663fd19c` introduced exact missing-residency diagnostics and
passed full Release. On test commit `b0a46bd8`, one canonical attempt stopped
at the previously documented F1.8A sparse-public interpolation assertion and
another at the W7 focused seam assertion `≤1` while the native gate allowed
bounded tails `≤4`. The focused gate now uses the same bound, requires matching
still-prepared/missing-resident diagnostics, and drains after return. A later
adversarial same-lineage grant test reproduced and repaired the separate Pack
retirement defect. The final post-repair Release run passed in full.

W8 atomic Add/Replace/Remove Pack publication, W9 editor authoring, and W10
durable mutable topology were not started.
