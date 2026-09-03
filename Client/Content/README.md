# Client Content

`UltraSim.Client.Content` is the engine-neutral A0.0 contract and deterministic resolver for
passive client content. It contains no Godot or Network dependency.

The accepted boundary is:

```text
authenticated Game/application authority (later remote composition)
    ApplicationRealmId
    ContentActivationId + PlanHash
    exact required base fingerprint
    ordered PackageId + ManifestHash + Required/Optional
            |
            v
Client.Content
    validate immutable package identity and passive payloads
    apply Add/Replace in authority order
    publish immutable resolved catalog generation
    produce exact resolved fingerprint/application proof vocabulary
            |
            v
Godot realization (later checkpoint)
```

Logical ids are strict lowercase ASCII `namespace:path/segments`. File paths are package-local
addresses, never semantic identity. Exact SHA-256 identifies immutable blobs globally;
`ApplicationRealmId + ContentActivationId` owns authorization and detects reuse of an activation
identity with different plan bytes. Download URLs, local paths, connection identity, and
process-local catalog-generation handles do not enter resolved fingerprints.

Payload validation produces a `VerifiedContentPackage` capability. Base-catalog construction and
activation resolution accept that capability, not a bare manifest, so parsed metadata or
partially/hash-failed bytes cannot become publishable catalog content.

## A0.0 formats and operations

The first normal passive manifest permits only:

- typed Texture and Icon definitions backed by PNG/WebP;
- declarative `StandardLit` materials;
- typed complete WorldPack definitions backed by World-owned canonical artifacts;
- `Add` and complete `Replace` operations.

Raster admission performs exact path/size/SHA verification plus a bounded passive envelope check;
it is classification before a later hardened decoder/engine realization and is not a claim that
arbitrary image bytes are safe. WorldPack admission delegates to `Game.World`'s complete bounded
container/hash/schema validator instead of defining a second WorldPack format here.

Generic Godot resources, scenes, scripts, shaders, executable code, downloaded GLB/audio, partial
Patch, dependencies/version solving, and executable extensions are outside A0.0. User resource
packs and executable extensions have vocabulary values so they cannot be confused with normal
server-passive trust, but the A0.0 manifest factory rejects both.

## Current stop

A0.0 provides contracts, validation, local package descriptors, deterministic base/activation
resolution, immutable catalog generations, and application-proof values. It does not fetch, cache,
install, activate over Network, realize Godot resources, or contain Cabin-specific catalogs.
A0.1 supplies the local Default/Replace/Add Cabin oracle; A0.2 later owns persistent CAS/install.

See [`ASSET_STREAMING.md`](../../Docs/Plans/ASSET_STREAMING.md) for the owning plan.
