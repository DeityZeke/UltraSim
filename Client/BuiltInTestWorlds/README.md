# Built-in Test Worlds

`UltraSim.Client.BuiltInTestWorlds` contains optional, engine-neutral client content for controlled
UltraSim Test Worlds. It depends on the generic `Client.Content` contracts/resolver and contains no
Godot, Network, cache, or remote acquisition implementation.

## Cabin A0.1 oracle

The accepted local Cabin oracle constructs three immutable resolved catalogs against one exact
built-in base:

- `Default` uses only the built-in Cabin texture/material/icon definitions;
- `Replace` replaces `cabin:texture/wall`, which also changes the resolved file reference of the
  unchanged built-in `cabin:material/wall` definition;
- `Add` introduces exactly `test-cabin:texture/blue-glow`,
  `test-cabin:material/blue-glow`, and `test-cabin:icon/streamed-badge`.

Every package is validated from exact local PNG payload bytes before the resolver can consume its
`VerifiedContentPackage` capability. `CabinContentFingerprints` freezes the base and three resolved
catalog hashes, while the tests freeze the exact owner/definition/reference delta for each logical
id. F0.2 resolves those same verified bytes by catalog SHA-256 identity rather than depending on
package-relative presentation paths.

The fixture realm is stable test identity only; it is not presented as authenticated remote realm
establishment. Physical local source paths do not enter the package, plan, or resolved fingerprints.

This first oracle intentionally covers passive texture/material/icon presentation. The existing
authoritative Cabin WorldPack and bundled X/Y meshes are not repackaged here. F0.2 now consumes this
catalog in the real Single Player shell, while A0.2 owns persistent CAS/install and A0.7 later carries
a complete World-owned artifact through the streaming substrate.

The accepted A0.1 fingerprints should remain a stable content-resolution oracle. The newer Cabin
**visual benchmark** deliberately builds on the same overall Cabin/world content architecture but is
not permission to churn this fixture whenever real art changes. Intended pine/rock/grass/log/door/
window assets and lighting-test presentation should arrive through ordinary Local/Official content
or additional logical presentation identities. See the canonical
[Visual Art / Lighting / Cabin Benchmark](../../AIOS/canonical/visual-art-lighting-and-cabin-benchmark.md).

See [`ASSET_STREAMING.md`](../../Docs/Plans/ASSET_STREAMING.md) for the owning plan.
