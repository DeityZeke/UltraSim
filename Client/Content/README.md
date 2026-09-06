# Client Content

`UltraSim.Client.Content` is the engine-neutral A0.0/A0.2 contract, deterministic resolver, and
persistent store for passive client content. It contains no Godot or Network dependency.

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

The catalog is above the renderer-owned primitive/bootstrap emergency substrate. Local/default
content is optional ordinary verified content and may be absent without changing Game semantics;
primitive fallback is not a package and never satisfies a missing required activation or
application proof. Server-authorized Add/Replace content and future user presentation-leaf
overrides resolve above local/default content while preserving independent model/material fallback.

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

## A0.2 persistent cache

The accepted source-independent cache uses only fixed, validated identities in physical paths:

```text
root/
    blobs/sha256/ab/<64-lowercase-hex>.blob
    realms/<32-lowercase-hex ApplicationRealmId>/
        manifests/<ManifestHash>.manifest
        activations/<ContentActivationId>.activation
    metadata/access/<BlobHash>.access
    staging/<opaque temporary name>.tmp
    writer.lock
```

Manifest-controlled relative paths and source URLs never become cache paths. Exact blobs deduplicate
globally, but manifest and activation metadata remain realm-scoped: physical possession of bytes
does not authorize another realm to publish them. A0.2's realm cache admits only
`ServerAuthorizedPassive` packages. Local/default installation and future user/executable content
retain separate lifecycles.

Install first validates the complete supplied package through the A0.0 passive allowlist. Missing
blobs are written and flushed through private staging names, verified by their descriptors, and
atomically moved before the realm manifest becomes visible. A crash may leave an unreferenced
immutable blob; it cannot leave a published partial package, and abandoned staging is removed on
the next open. Canonical binary manifest/activation metadata is strictly decoded and re-encoded on
reopen. No source location is persisted.

Catalog publication reloads every selected package from the cache, resolves privately, persists
the exact realm/activation/plan/result tuple, and only then returns a new immutable catalog
generation. Restart reopening repeats that validation and must reproduce the stored fingerprint.
Required packages fail closed; primitive/local presentation fallback does not manufacture a
successful activation.

Quota eviction is reachability-based and oldest-access-first. Persistently pinned activations,
live catalog leases, and live blob/resource leases protect their manifests and blobs. Unpinned
activation/manifest metadata is retired before an unreachable blob is deleted. If enough safe
space cannot be reclaimed, admission fails without partial arbitrary eviction. Corruption is
detected on use and is not repaired in A0.2; repair/source fallback belongs to A0.5.

The initial implementation gives one owning client process exclusive mutation ownership for a
cache root. Multiple simultaneously running clients use distinct roots until cross-process live-pin
coordination is explicitly earned; this does not alter the canonical on-disk identities.

## Current boundary

A0.0 provides contracts, validation, local package descriptors, deterministic base/activation
resolution, immutable catalog generations, and application-proof values. A0.2 persists verified
server-passive packages and resolved activations without acquiring them from any source. The
accepted A0.1 Cabin Default/Replace/Add oracle lives in the optional
[`Client.BuiltInTestWorlds`](../BuiltInTestWorlds/README.md) capability rather than coupling the
generic resolver to one Test World. A0.2 adds persistent CAS/install/reopen but deliberately does
not advertise requirements, fetch bytes, use HTTP/Network, stream a WorldPack, or realize native
resources. Those remain A0.3+ work.

See [`ASSET_STREAMING.md`](../../Docs/Plans/ASSET_STREAMING.md) for the owning plan.
