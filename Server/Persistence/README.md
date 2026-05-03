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

The next integration slice should attach this coordinator to the real
`ServerApplicationHooks.Save` / `Load` path used by the movement server factory.
Dirty flushing, async writes, operator recovery prompts, and richer retention
policies should layer on top of this save-set contract.
