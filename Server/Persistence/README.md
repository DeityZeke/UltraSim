# UltraSim Server Persistence

`Server/Persistence` is the bridge layer for server-owned save coordination. It
does not own account policy, ECS serialization rules, or runtime shell lifecycle;
it composes those pieces into a single save-set.

Current scope:

- `ServerPersistenceCoordinator` writes every save into a staged pending
  directory first.
- A successful save writes `save_manifest.json` with one save sequence and
  timestamp shared by every participant.
- The staged directory is promoted to `current` only after every participant
  succeeds.
- Failed saves delete their pending directory and leave `current` untouched.
- Whole save-set backups are rotated as directories, rather than letting account
  and world backups drift independently.
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
  - valid `current` save-sets load first;
  - corrupt or incomplete `current` save-sets fall back to the newest valid
    backup;
  - if a backup manifest has a higher sequence than `current`, the load result
    reports a warning instead of silently ignoring the mismatch.

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
