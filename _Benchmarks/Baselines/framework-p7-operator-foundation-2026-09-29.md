# P7 active — headless operator foundation, not product closure

Starting `main`: `75e7b1c9e3c99c8199459104b0a0d8aeec263b2a` (clean).
Implementation commits: `e060879960a154eedb6453bf0d8f54ebafe917f0`,
`93311a973294280022b81ab000f3dfc93ac75f17`.
The latter is the exact implementation commit tested by the final uninterrupted Release run.
P1–P6 remain closed; P7 is **ACTIVE**; P8 and later work has not begun.

## Delivered and exercised

- One owner-thread `server.operator` Control Plane provider, not a second action registry.
  Explicit read/save/backup/lifecycle/session capabilities, generation/revision fencing,
  bounded audit of accepted and rejected actions, cold policy diagnostics, and bounded
  participant rows. A normal player principal cannot discover the provider.
- Direct Connect console input is bounded and read off-thread; only the host owner dispatches
  typed requests. `save`, `backup`, `shutdown confirm`, `kick <id> confirm`, discovery/help,
  status and audit are routed through the provider. Ctrl+C uses the same Shutdown action.
- Save Now and autosave share `GameRuntimePersistence.TrySaveAsync` and one operation slot.
  Capture occurs on the authority owner thread; durable publication is asynchronous.
  Backup copies only a hash-validated complete CURRENT generation into disjoint private
  staging before publication and keeps a bounded count. Shutdown coalesces behind an
  in-flight save or backup, performs its configured final save, closes admission, and
  drains the existing sessions through the normal host loop.
- Kick uses a factory-published participant/session correlation after verifying that the
  participant is currently entered. Stale target rejection is tested; a live-client Kick
  journey is **not yet** an acceptance result.
- `restart confirm` returns `Disabled`. An exit disposition without a tested supervisor,
  durable world reopening and ephemeral-session cleanup is **not** represented as restart.

## Verification

Host: Linux 7.2.6-1-cachyos x86_64, AMD Ryzen 7 7800X3D, 16 logical CPUs;
.NET/ASP.NET Core 8.0.31 from `/tmp/ultrasim-p5-dotnet-8.0.131`;
Godot 4.7.1 stable Mono. Network build flavor for dedicated server: ServerOnly.

| Control | Result |
| --- | --- |
| `dotnet run --project _Tests/UltraSim.Server.Operator.Tests/UltraSim.Server.Operator.Tests.csproj -c Release` | 5/5 pass: capability/audit/fence, Save/backup/disabled restart, autosave/console, retention/integrity, shutdown-after-backup |
| `./verify.sh --architecture-only` | pass |
| `dotnet publish Server/DirectConnect/UltraSim.Server.DirectConnect.csproj -c Release -p:UltraSimNetworkFlavor=ServerOnly` | pass; output contains operator assembly, no Godot assembly |
| Physical published binary, `backup` then `shutdown confirm` on stdin | both accepted; clean exit 0 after final save |
| First final-tree `./verify.sh --configuration=Release` | failed at a Godot Debug build: transient missing `Kernels/ECS.Generators/obj/project.assets.json`; file existed immediately after failure; no product test failure established |
| Unchanged-tree `./verify.sh --configuration=Release` rerun | **pass uninterrupted**, including 39 integrated gates, native exports, Direct Connect/Online controls and final composition parity |

The failed and passing logs were retained separately under
`.output/p7-operator-verification/`. Their SHA-256 digests are respectively
`deb43c33665de816f4cacccfa89937cd325aad1c15179546dac3128020821909`
and `3f5639ad794b7ce47b2fbb33f191d37bf0043dd45c3a198bd35f090a8931574a`.
The passing log contains 4,645 lines and ends with `PASS repository verification`.
These workspace logs are diagnostic records, not a claim of an immutable source artifact.

## Open P7 acceptance work

1. Product recovery/restore: strict CURRENT and fallback candidate inspection, backup restore
   through staged validation, startup world reopening, rejection of corrupt/incomplete candidates,
   and proof that persisted former guest actors do not become orphaned live sessions.
   `SharedPlayableAuthority` retains generation-local scene/publication objects; calling
   `GameRuntimePersistence.TryLoad` under that live wrapper without rebinding is unsafe.
2. A real restart supervisor/launcher, preserving cold deployment identity and rebuilding
   authority from validated durable state; only then enable restart-with-save.
3. Live-session Kick/reconnect/retirement tests, an operator-visible structured live log,
   and fuller participant state/latency diagnostics.
4. Optional exported Godot server GUI over this exact provider, with native-wrapper lifetime
   assurance. Remote in-game Admin remains unavailable until a server-trusted, session-bound
   operator grant can be established without inventing Gate 1 account semantics.
5. Frontend equivalence and failure-path campaigns across console, GUI and authorized Admin.

No P7 closure or PF6 readiness is inferred from this foundation slice.
