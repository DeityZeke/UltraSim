# P9 two-room Cabin playtest repair

Status: **Local SinglePlayer travel and installed-art repair verified. P9 remains ACTIVE; Online/Complete multi-space remains unimplemented.** This companion does not replace the frozen v1 benchmark or its earlier evidence.

Starting commit: `d9b7fb98b1ef7730d175dc6f251b45963fb18931`. Tested implementation commit: `bed919766666cb5a5adc6ad08ab63ff35df69af6`. Host: Linux `7.2.6-1-cachyos` x86_64, .NET SDK `8.0.131`, Godot `4.7.1.stable.mono.official.a13da4feb`. Configuration: Release. The full canonical verifier passed uninterrupted on the tested implementation commit; its local log is `.output/playtest-verify-scratch/run.3qiVVv/verify-release.log` (SHA-256 `553d6fd791b88ebe4ef2a07ff8ea7c73039a61cd3596de0fa963f3f04e2b2b9d`).

## Playtest finding and repair

The screenshot showed the actor at Space 2, approximately `(16.185 m, 9.151 m)`, beside the south exterior opening. This opening is **not** an interactive door. The return telepad is outside near `(16 m, 7 m)`. The existing raised, collidable floor had a top at 1,100 mm while the clearing was at 1,000 mm. The grounded actor could enter but could not step out across the exterior edge while its footprint still overlapped the floor. Before repair, a direct movement regression stalled at global Z = 9,200 mm. The current v2 WorldPack places the floor top flush with the clearing and arrival at 1,000 mm. The regression now walks out to the return pad and back in, then continues the Classic → Benchmark → Classic save/reload journey. Classic Cabin and the original v1 benchmark remain unchanged; v1 is reachable through `CreateLegacyV1()` and its exact fingerprint remains pinned. The standalone benchmark's save root is versioned separately so prior v1 saves are not reinterpreted as v2.

The screenshot's plain walls and flat/translucent roof are the primitive fallback, not the painted installed P9 result. An existing warm `world.local` cache held an older candidate activation under the same ID, with a different package hash. Exact plan reopen returned `CorruptActivation`; the optional art path then used fallback although the current archive was installed. The final P9 activation and the combined Cabin/P9 activation now have new, exact identities. Old metadata is retained, not overwritten or treated as authorization for the final package. A permanent content test installs the older candidate and current package in one store, proves isolation and exact warm reopen. The normal Local native map-transition smoke now rejects an installed-art run unless the expected 11 catalog world-mesh selections resolve.

The Space 2 HUD now says `open exterior doorway` instead of reporting Classic Cabin's unrelated `door closed` state. No door interaction or new Game mechanic was introduced.

## Verification

| Control | Outcome |
| --- | --- |
| Pre-repair direct exterior walk | Reproduced a stalled actor at global Z = 9,200 mm. |
| Local Client Release | 25/25 pass, including bidirectional exterior walk, actor/Inventory retention, repeated transitions, save/reload. |
| Client Content Release | 40/40 pass, including legacy warm activation coexistence and exact current warm reopen. |
| S0 Integration Release | 41/41 pass, including frozen v1 fingerprint, v2 floor support and two-player space isolation. |
| Exported SinglePlayer native smoke | Fresh isolated cache, warm isolated cache, and an isolated **copy** of the older user cache: all pass; installed P9 selects 11 world meshes. Original user cache was not modified by these controls. |
| Refreshed default SinglePlayer export | `Client/Godot/export-client.sh SinglePlayer` succeeds; exported executable `--spatial-space-smoke` passes on an isolated cache/save root. |
| `./verify.sh --configuration=Release` | Pass uninterrupted on `bed91976`, including architecture, all three exports, native product controls and final composition parity. |

The system `dotnet` initially lacked the ASP.NET shared runtime for S0 Integration; that environmental invocation failed before tests. The project-local .NET runtime was then selected and S0 passed. The canonical verifier used that runtime and a task-specific writable temp directory. No gate was weakened or marked passed because of the initial environment failure.

## Human retest and limits

Run the refreshed `Client/Godot/.output/clients/single-player/UltraSimSinglePlayer.x86_64`. From Classic Cabin, select the second pad near `(22 m, 12 m)` and press `E`. In Space 2, walk south through the **open** exterior doorway, select the cyan pad near `(16 m, 7 m)` and press `E` to return. No door needs opening. With the installed P9 package, walls/roof should use the painted catalog realization; the primitive-only mode remains a separate valid fallback control. The native and content tests prove resolution/ownership, not final human visual judgment.

This does not enable Space 2 on Online or the Complete edition's one-space hosted path. No network space-transition protocol, remote structural switching, or P9 visual-acceptance claim is made. The user's normal cache was not cleared or deleted; the new activation lineage is intended to coexist with it.
