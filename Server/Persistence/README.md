# UltraSim Server Persistence

`Server/Persistence` is the bridge layer for server-owned save coordination. It
does not own account policy, ECS serialization rules, or runtime shell lifecycle;
it composes those pieces into a single save-set.

Current scope:

- `ServerPersistenceCoordinator` writes every save into a staged pending
  directory first.
- A successful save writes `save_manifest.json` with one save sequence and
  timestamp shared by every participant.
- The manifest records each participant artifact's relative path, artifact kind
  (`file` or `directory`), file count, and byte count. Those facts are also
  returned in save/load timing results for operator status surfaces.
- The staged directory is promoted into the live save root only after every
  participant succeeds. In exported runtimes, that live save root is `saves/`
  itself.
- Failed saves delete their pending directory and leave the live save root
  untouched.
- Participant artifact paths are validated as safe save-set-relative paths before
  promotion/load. A bad participant cannot point outside `saves/`.
- Whole save-set backups are rotated together, rather than letting account and
  world backups drift independently. The default backup storage is still a
  directory. Operators can opt into archive compression with
  `ServerPersistenceSaveSetOptions.BackupCompressor`; the built-in compressors
  are ZIP and ZSTD-backed `.tar.zst`, and the interface is intentionally small so
  other archive formats can replace them without changing the coordinator
  contract. The default rotation
  follows the RunUO naming shape: `Most Recent (...)`, `Second Backup (...)`,
  and `Third Backup (...)`. Backup timestamps use the host machine's local time.
- `AccountingPersistenceParticipant` writes the accounting JSON snapshot into
  the staged save-set.
- `EcsWorldPersistenceParticipant` writes the current ECS binary world snapshot
  into the staged save-set.
- ECS component serialization is still owned by `Kernels/ECS/Extensions/Persistence`.
  A future source-generator pass should generate component serializers,
  migration records, and admin metadata for component structs; the server
  coordinator should continue to treat that as one participant inside the
  save-set rather than becoming a component serializer itself.
- The movement server factory now wires this coordinator into
  `ServerApplicationHooks.Load` / `Save`; shell `save`, deferred save requests,
  save-on-stop, and startup load all use the same save-set path when the movement
  world application is enabled.
- The movement server exposes a `Persistence` system runtime snapshot with the
  last save sequence, load source, warning count, failure count, and current
  save-set path in its detail field, so operator/admin status views can surface
  persistence state without a dedicated persistence UI.
- Load runs participants in registration order, so account/character state can
  be restored before world entities that may reference account or character ids.
- Startup recovery is conservative:
  - pending save-set directories are warnings and are deleted, never promoted;
  - by default, the live save root loads first;
  - by default, corrupt or incomplete live save roots fall back to the newest
    valid backup;
  - if a backup manifest has a higher sequence than the live save root, the load
    result reports a warning instead of silently ignoring the mismatch.
- `ServerPersistenceSaveSetOptions.RecoveryMode` makes recovery explicit:
  - `CurrentThenBackups` is the default RunUO-like recovery path;
  - `CurrentOnly` refuses to load backups, so a bad current save fails loudly;
  - `RequireCurrent` refuses both backup-only and fresh-start recovery.
- `ListBackups()` and `RestoreBackup(...)` provide the operator primitive for
  backup recovery. Restore copies the selected backup through a pending restore
  directory first, validates the backup manifest/artifacts, and moves the
  previous current save-set into `Pre-Restore Backup (...)` before promotion.
- Movement-world console and Godot operator shells expose those primitives as
  `backups` / `list-backups` and `restore <backup-folder-name>` /
  `restore-backup <backup-folder-name>`. Restore is rejected while the runtime is
  running; stop/shutdown first so files and in-memory state cannot drift.
- Save/load results include total elapsed milliseconds and per-participant
  timings. The movement server folds those into the existing `Persistence`
  runtime snapshot detail as sequence, load source, warnings, failures, file
  count, byte count, and current save-set path.

## V2 Comparison Checkpoint

The v2 persistence path had a richer operational shape than the first modular
rewrite:

- staged world writes with a final manifest/commit step;
- explicit save/load timing snapshots;
- recovery-mode concepts for incomplete optional sections;
- root save-set validation;
- autosave/save-on-stop hooks;
- account persistence backup rotation;
- RunUO-inspired operator ergonomics for current saves and backups.

The current modular path now covers the high-value coordination pieces:

- shared account + ECS world save-set under one `saves/` root;
- whole-save backup rotation under root `backups/`;
- pending-save cleanup and fallback to valid backups;
- local-time RunUO-style backup names;
- artifact metadata and per-participant timing telemetry;
- safe participant artifact path validation;
- shell/runtime status exposure through the `Persistence` system snapshot.
- explicit backup listing and restore primitives with a pre-restore safety
  backup.
- console and Godot operator shell commands for backup list/restore.

Still worth reworking/expanding next:

- add an operator recovery prompt/path on top of the non-interactive recovery
  modes;
- expose backup listing/restore through future admin panels;
- make retention richer than slot count alone, such as manual keep markers,
  hourly/daily generations, or size limits;
- add artifact checksums so load can detect content drift, not just missing files
  or changed sizes;
- add operator/server settings for choosing the backup compressor. The
  coordinator already supports directory, ZIP, and ZSTD-backed archive backups
  through the compression interface, and live current saves remain uncompressed
  for fast startup.
- add optional parallel participant save/snapshot execution once there are enough
  participants to justify the coordination cost. Manifest stamping and promotion
  stay single-coordinator-owned; participant snapshot/write work can fan out.
- split ECS world persistence into richer artifacts again when gameplay data
  grows beyond the single `world/entities.bin` participant;
- eventually revive background snapshot/write behavior if autosave cost becomes
  visible on live ticks.

This is intentionally the coordination layer above the existing pieces:

- `Server/Accounting` owns account and character records, validation, JSON
  storage, and future encrypted/database storage profiles.
- `Kernels/ECS/Extensions/Persistence` owns ECS world snapshot/restore and
  binary serialization.
- `Server/Core` owns `ServerApplicationHooks.Save` / `Load` and save metrics.

Dirty flushing, async writes, operator recovery prompts, and richer retention
policies should layer on top of this save-set contract.

## Operator status snapshot

Movement-world server applications expose persistence health through the existing
`ServerSystemRuntimeSnapshot` stream instead of a separate persistence-specific
status API. The snapshot uses:

- `Name = "Persistence"` and `Stage = "Persistence"`.
- `Manual = true`, because persistence is triggered by load/save hooks rather
  than a fixed per-tick system.
- `UpdateCount` as the last completed save sequence.
- `ControlCount` as the warning count from the last load.
- `Detail` as a compact operator string:
  `sequence=<n> load=<fresh|current|backup> warnings=<n> failures=<n> path=<current-save-root>`.

Admin panels should treat this as status text, not as a command surface. Recovery
actions such as restoring a backup or discarding a corrupt current save-set should
be explicit operator commands layered on top of the coordinator.
