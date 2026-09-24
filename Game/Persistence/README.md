# Game persistence

Game persistence composes four concrete save-set participants into one atomic
generation:

```text
world        ECS durable entities/components
worldview    exact legacy Pack or canonical manifest identity and durable overlays
accounting   game account/character authority
clock        authoritative world time
```

`WorldSaveSetParticipant` never serializes immutable `WorldPack` bytes. Its
bounded versioned artifact records the durable world/space, exact pack and
content fingerprints, overlay revision, sorted changed-chunk revision/hash rows,
complete terrain/static/Multi replacements, and the optional complete Multi-root
replacement. Parse, exact pack resolution, base binding, replacement hashes,
terrain seams, static identities, capacities, and effective Multi piece totals
must all validate before publication.

The staging composition creates `GameWorldState` while its private ECS world is
still open so lookups/plans can be prepared. ECS restore may then seal and run
the candidate. The resolved exact pack/overlay is installed into that staging
shell before Inventory and world-derived state rebuild and before the one
`GameGenerationState` publication. Failure disposes candidate ownership and does
not mutate active authority.

W5 retains the exact legacy `worldview/v1` path and adds `worldview/v2` for a
canonical composition. V2 records the save sequence, durable World/Space,
exact immutable `WorldStructuralManifestFingerprint`, independent
`WorldCompositionRevision`, and sorted durable per-Pack overlay rows. A revised
but now-empty overlay remains a row; a canonical empty overlay may be omitted.
The row payload reuses the validated v1 Pack/overlay representation, while
restore resolves the manifest and every exact Pack externally, reconstructs
all overlays privately, and runs complete composition validation before the
single generation publication. Partial prepared compositions cannot currently
be captured: there is no separate durable overlay catalog from which to save
unprepared Packs. This is a runtime safety limitation, not a prepared-residency
field in the v2 format. A missing/mismatched resolver or invalid seam fails
without changing active authority.

The artifacts deliberately omit immutable pack bytes, runtime entity/chunk
slots, Spatial membership storage, publication history/cursors, semantic-client
residency, meshes, Godot objects, and edit-session/provenance history. Those are
content, derived state, presentation, or separate future gameplay facts—not the
durable current-world overlay.
