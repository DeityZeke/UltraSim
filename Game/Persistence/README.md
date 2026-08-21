# Game persistence

Game persistence composes four concrete save-set participants into one atomic
generation:

```text
world        ECS durable entities/components
worldview    exact WorldPack/content identity and durable mutable overlay
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

The artifact deliberately omits immutable pack bytes, runtime entity/chunk
slots, Spatial membership storage, publication history/cursors, semantic-client
residency, meshes, Godot objects, and edit-session/provenance history. Those are
content, derived state, presentation, or separate future gameplay facts—not the
durable current-world overlay.
