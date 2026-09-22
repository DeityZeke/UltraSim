# F1.2 semantic activation / presentation resolution — 2026-09-22

F1.2 is closed on tested production commit `d61fea95` (implementation `682b5f7b`;
starting HEAD `ddf17f75b761131e4162254e824e5071eb9a55b4`). The requested starting
`4d9a4751…` preceded AIOS documentation/index commits; no intervening commit changed F1.1
content semantics. The machine-readable companion records the exact cases and values.

## Accepted boundary

The required Realm semantic activation now contains one bootstrap package: the exact Concept
Demo WorldPack and compiled Inventory/Server Monitor UI definitions. The actor Add-X/Y and
Replace-X packages are still exact, passive, Realm-authorized content, but belong to a separate
non-gating presentation plan. Authenticated Game message 149 advertises that plan and its exact
resolved-result fingerprint after semantic proof. Game manifest is v31; Network transport remains
revision 2. No Game authority, AOI, proof, or application identity was weakened.

`AuthorizedSemanticActivation` retains scope, activation, plan hash, resolved fingerprint, and
application epoch without native art. `PresentationGeneration` resolves the two current Concept
Demo actor IDs from Realm → `world.local` → Primitive, owns selected catalog/blob leases, and
publishes a process-local generation distinct from its stable effective-selection fingerprint.
Publication is owner-thread-bound; stale asynchronous work is canceled/fenced. Godot refreshes
visual children under the existing actor root and releases old native leases only after old
visuals are freed. An explicit stricter per-call GLB limit remains enforced on generation hits.

## Direct acceptance evidence

The F1.2 Network application test used the actual Concept Demo deployment and HTTP source over
authenticated UDP. Art fetching was held while required content applied, the WorldPack became
ready, a guest was admitted, a public actor appeared, Inventory opened, and Chat applied. Releasing
art published a new Realm generation without changing the semantic activation, WorldPack, or
participant. Unavailable or deliberately corrupted actor packages left entry valid and selected
Primitive. A persistent `world.local` actor catalog selected Local when Realm art was unavailable;
the content unit test also proved compatible Realm overrides Local and incompatible Realm entries
fall through. A canceled in-flight candidate could not replace a newer generation.

```text
semantic plan       deb29568cef3649324da640af997a2b97e856eb56ba65869ff8bfc3824e4c294
semantic catalog    7bac23dafe36a2fa6a2fa9e7a899daad8cefb02e7e248fca641d527bd6aa1b22
presentation plan   b79e788cf7216b9b529923d0f309dbd51c28ea0781c0779614b5864a3756c3b7
example generations Primitive 1 → Realm 2
```

The strict bounded GLB/skeleton/Idle-Walk-Jump validation remains in the content resolver.
Content tests prove the stable effective fingerprint across cache reopen, different process-local
generation IDs, old native lease survival across publication, and final zero live blob/catalog
pins. T0.10 native acceptance passed its real primitive→content generation path while preserving
the actor root, public identity, pose, animation, nameplate, overhead Chat, equipment/action state,
and selection/interaction state.

## F1.0/F1.1 comparison

| Observable | F1.0/F1.1 | F1.2 |
| --- | ---: | ---: |
| required remote packages | 3 | 1 |
| cold payload bytes gating semantic entry | 7,669,834 | 733,198 |
| actor-art payload bytes, independently acquired | included above | 6,936,636 |
| total cold payload bytes when art acquired | 7,669,834 | 7,669,834 |
| warm required packages reused | 3 | 1 |
| Local resident chunks | 49 | 49 |
| F0.7 clean retirement boundaries | 6 | 6 |

The one-time semantic plan/catalog fingerprint change is intentional: actor art was removed from
required authorization. WorldPack bytes, Game/Inventory/Chat authority, actor identities, remote
profile semantics, and shipped Local X/Y assets did not change. The successful F0.7 mixed journey
repeated Local → Local → Realm A → Realm B → Realm A reconnect → Local, with fresh remote epochs,
remembered exact Realm profile only on A reconnect, and clean root/session retirement. A separate
Contentless Online cold/warm gate observed 733,198/0 required bytes fetched and a content-backed
actor resource after optional art arrived.

## Verification and reproducibility

- `./verify.sh --architecture-only`: pass.
- `dotnet run --project _Tests/UltraSim.Client.Content.Tests/UltraSim.Client.Content.Tests.csproj -c Release`:
  31/31 pass.
- `dotnet run --project _Tests/UltraSim.Network.Application.Tests/UltraSim.Network.Application.Tests.csproj -c Release`:
  pass, including the F1.2 end-to-end and protocol/parser cases.
- Godot T0.10 headless native acceptance: pass.
- `ULTRASIM_GODOT=<Godot-4.7.1-Mono> ./verify.sh` on clean temporary worktree at
  `d61fea95`: **PASS repository verification**, including Release builds, editions,
  SinglePlayer/Online/Complete exports, Contentless Online cold/warm, PF1/PF2 mixed
  exported/headless controls, F0.7 lifecycle, and composition parity.

Environment: CachyOS Linux 7.2.6-1-cachyos, Ryzen 7 7800X3D (16 logical CPUs), .NET SDK
8.0.131/runtime 8.0.31, Godot 4.7.1 Mono. The source mount is `noexec`; direct runs there used
`UseAppHost=false` and Bash script launching. The accepted canonical run used an executable
temporary worktree at the exact commit. Its S0.8 Godot test project was explicitly built in
Debug before the run, as a clean checkout needs that assembly to instantiate its C# scene.

Earlier mount-based full attempts exposed intermittent Godot `ExportRelease` manifest/output
loss and one PF1 sword-observation miss; the standalone export, isolated PF1 gate, and accepted
clean-worktree full gate all passed without weakening assertions. A later optional evidence
recapture attempt failed an unrelated shared-authority convergence test; it does not replace the
accepted uninterrupted full pass. These attempts are retained here rather than silently omitted.

F1.3+ is unstarted. In particular, this is not Official Local asset productization: shipped X/Y
assets remain in the Local PCK. No user packs, broad primitive substrate, atlas, UI manager,
camera redesign, or RenderingServer rewrite was added.
