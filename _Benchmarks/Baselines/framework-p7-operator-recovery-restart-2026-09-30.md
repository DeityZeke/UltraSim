# P7 active — cold recovery, supervised restart and selected backup maintenance

This companion does not replace the immutable [headless foundation](framework-p7-operator-foundation-2026-09-29.md) or its [live Kick follow-up](framework-p7-operator-foundation-2026-09-29.r1.md). P7 remains **ACTIVE**, not closed. P8 has not begun.

Starting clean `main`: `75e7b1c9e3c99c8199459104b0a0d8aeec263b2a`. Production/test commits in this slice: `360901fbef2059cd06c607d51a115adbe3411ffd`, `75dd6c19be18c7f385dab1983efba6a7dde97566`. The latter is the exact final implementation tree tested by the uninterrupted Release verifier. Earlier P7 foundation and Kick commits remain separately attributed in the linked records.

## Accepted bounded implementation

- Direct Connect cold-starts a saved Concept Demo through the existing staged Game restore, before its generation-local remote-publication objects bind. The fixed authored scene entities are rebound by saved persistent identity and checked prefab provenance. Persisted accountless guest actors and their backpack state retire through the existing Game lifetime policy; a fresh process never revives an obsolete guest session. Strict `CURRENT` fails before Network admission on corruption; configured fallback selects the newest complete compatible candidate without rewriting `CURRENT` merely to inspect it.
- `restart confirm` captures a fresh final generation, including when another save or backup was already in flight. It closes admission at the coherent final-capture boundary, drains sessions and exits code 75 only after durable success. The exported `run-managed.sh` interprets only code 75 as a cold relaunch with the same arguments, identity directory, server key, realm and operator configuration. A failed final save is visible and does not claim a restart.
- A stopped-server `--restore-backup-sequence=N` maintenance invocation takes the deployment's exclusive instance lease, validates a complete selected backup, stages a private SaveSet-shaped root, executes ordinary staged Game restore, then publishes a **new forward** live generation. No Network host is opened. Missing/corrupt selection leaves the live `CURRENT` untouched. It is not a live GUI/console restore action.
- The owner-local 256-event operational log has bounded severity/category/time/sequence/text and a read-only Control Plane collection. It is distinct from action audit and Game history, and has no idle-tick producer.

## Verification and provenance

Host: Linux 7.2.6-1-cachyos x86_64, AMD Ryzen 7 7800X3D (16 logical CPUs), .NET SDK 8.0.131 / .NET and ASP.NET Core runtime 8.0.31, Godot 4.7.1 stable Mono. Dedicated server Network flavor: ServerOnly.

| Control | Exact result |
| --- | --- |
| `dotnet run --project _Tests/UltraSim.Server.Operator.Tests/UltraSim.Server.Operator.Tests.csproj -c Release` | 9/9 pass on final implementation: authorization/audit/fencing; save/backup/restart; autosave/console; retention/integrity; shutdown/backup serialization; strict/fallback cold recovery and guest retirement; exhausted save sequence; selected backup staging/forward publication; bounded log. |
| Network.Application Release executable | pass, including real loopback UDP Kick of one exact entered guest and stale-target rejection. |
| `./verify.sh --architecture-only` | pass on final implementation. |
| Published ServerOnly Direct Connect and `run-managed.sh` | physical console restart exited 75; cold reopen selected save sequence 1, preserved server public key/realm, and created fresh authority generation 2. A two-process launcher restart/shutdown control passed. |
| Published selected-backup maintenance | backed up sequence 2; `--restore-backup-sequence=2` reported `P7 BACKUP RESTORE PASS selected=2 retired-guests=0 live-generation=4`; subsequent ordinary server launch selected 4 under the unchanged deployment identity and exited cleanly. |
| First `./verify.sh --configuration=Release` on `360901fb…` | failed in existing W7 far-gate/seam traversal threshold: stale in-flight rejections 10 versus permitted 4. The unchanged Network.Application Release rerun passed with zero such rejections. This intermittent control is **not** represented as a P7 repair. |
| Unchanged-tree full Release rerun on `360901fb…` | uninterrupted pass. |
| Full Release on exact `75dd6c19…` | **uninterrupted pass**: architecture, persistence/operator/network suites, native exports, Online/content acquisition and impairment controls, product dogfood, final composition parity, `git diff --check`. Final line: `PASS repository verification`. |

The failed W7 log, isolated Network rerun, and two passing full logs remain separate under `.output/p7-operator-verification/`. SHA-256 respectively: `7c23c47139239f423e9e8db2ad3650d0ee9813b3620d988c1071db224a9c75d8`, `270bcff3c403f793221cd7c64970d4b9194b707880fc871987707c2862137f44`, `a3630f8b95dee9ad80c05067abd7ae809688838fb88bc42e37d218891be591ed`, `55641e94aa70d4622c25abdc400ed8608bc30f867ebf20c41375c50dccaf3294`. The last log has 4,587 lines. Its early `Build FAILED` lines are the verifier's intentional invalid-Network-flavor negative controls, each followed by a `PASS` assertion, not a failed canonical result. These ignored workspace logs are diagnostic, not immutable source artifacts; the outcomes and digests are preserved here.

## Still required for P7 closure

An exported Godot server GUI over the same owner actions, a frontend-accessible recovery candidate view/restore workflow, richer participant/session diagnostics, and a credible authorized in-game Admin route (or an explicit accepted Gate-1-dependent limitation with trusted local/test frontend parity) remain open. Console/GUI/Admin semantic equivalence and GUI native-lifetime gates cannot be claimed yet. P7 is not marked closed, P8 is not started, and PF6 is not made ready by this slice.
