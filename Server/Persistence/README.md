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

This is intentionally the coordination layer above the existing pieces:

- `Server/Accounting` owns account and character records, validation, JSON
  storage, and future encrypted/database storage profiles.
- `Kernels/ECS/Extensions/Persistence` owns ECS world snapshot/restore and
  binary serialization.
- `Server/Core` owns `ServerApplicationHooks.Save` / `Load` and save metrics.

The next integration slice should attach this coordinator to the real
`ServerApplicationHooks.Save` and later `Load` path used by the movement server
factory. Dirty flushing, async writes, recovery prompts, and richer retention
policies should layer on top of this save-set contract.
