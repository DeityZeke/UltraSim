# Client Network

`Client/Network` is the hostile remote-protocol boundary between authenticated
Game messages and engine-neutral retained state owned by Client Core. It owns no
authoritative ECS state, no Game authority, no server AOI/subscription policy, and
no Godot/Unity resources.

The current lower architecture is documented in
[Network V3](../../Docs/Plans/NETWORK.md). Game-owned replication semantics live in
[Game replication](../../Game/Replication/README.md).

## Permanent boundary

```text
authenticated Network message
        |
        v
Client/Network
    validate canonical Game envelope/payload
    stage in bounded private scratch/candidate state
    apply through the owning Client Core semantic contract
    emit application proof only after semantic success
        |
        v
Client Core
    retained UI/world/application state
        |
        v
concrete presentation adapter
```

Permanent rules:

- transport delivery is not semantic application;
- malformed/hostile input cannot partially publish client semantic state;
- client storage handles/slots are private implementation identity, never wire
  identity;
- presentation residency and render slots are separate from semantic residency;
- the client does not acquire Game authority by receiving state;
- stale/duplicate/reordered input must converge or fail closed according to the
  owning semantic lineage;
- public world, owner-private UI/state, and retained Event semantics remain
  separate application domains even though they share the same Network session.

## Direct Connect trust/session lifecycle

F0.3a adds `ClientDirectConnectSession` before remote Game admission. It uses Network's bounded
deployment-key probe to obtain an untrusted candidate. An explicit supplied fingerprint must match,
or the human must accept the displayed fingerprint; only then is `HostedUdpProtocolClient`
constructed with that exact P-256 key pinned.

The trust record is not committed at candidate acceptance. After secure transport establishes, the
first Game Control message must contain a nonzero `ApplicationRealmId` and fresh application-session
epoch. That authenticated pair commits key/realm/endpoint through Client Core. Known-key reconnect
skips consent, mismatch blocks without overwriting the pin, and teardown clears candidate/session
lineage. `ClientDirectConnectEndpoint` remains IPv4-only while Hosted H0 is IPv4-only.

F0.3a creates no participant or actor and consumes no world/UI/Chat state. F0.3b now attaches
`RemotePlayableSession` to the established peer. Its bounded guest exchange yields server-issued
participant/control/public identity; retained exact-sequence `GamePlayerCommand` events then use the
same Game authority path as local play. Complete owner state is latest-state, while public chunk,
Chat occurrence, and generic UI adapters preserve their existing independent application-proof
contracts.

F0.3c inserts `RemoteClientProfileClient` after authenticated realm identity and before content or
guest admission. It hostile-validates the finite profile vocabulary introduced in protocol v19,
now retained unchanged in protocol v20, and maps it into
Client Core's stock recommendation, and either applies an exact remembered realm/revision/hash
decision or waits for explicit local consent. Only after durable decision persistence does it emit
the exact application-session/profile proof that lets the server continue. Cached decisions do not
cross realms or changed profile identities, endpoint text is never a scope, and the adapter owns no
input execution or Godot presentation.

The remote playable composition accepts either an already verified/preinstalled
`ClientWorldSemanticCache` or, after A0.7, one exact required WorldPack binding. A0.3 adds a
separate optional `RemoteContentClient` before guest admission. It validates the Game-protocol requirement against the
authenticated application realm, drives exact A0.2 reuse or source-neutral acquisition, retains the
catalog lease, and emits `ContentSetApplied` only after catalog publication. `RemotePlayableSession`
will not submit guest admission before that local application succeeds, while the authority also
independently blocks hostile bypass until the exact session proof arrives. Cache presence alone is
not proof, and reconnect always requires fresh application-session proof.

The A0.8 contentless Online product now composes F0.3b's external semantic path with the accepted
content pipeline. It starts from `ContentBaseCatalog.Empty`, applies the authenticated realm
catalog, resolves and binds the exact required WorldPack, publishes the private structural
candidate, and only then permits guest admission. A0.4 feeds that same gate from canonical
local-file, server-hosted HTTP, and external HTTPS package envelopes and proves Default/Replace/Add
parity with the A0.1 oracle. Source URIs remain authorization hints outside plan/catalog identity;
no package bytes use realtime Network lanes.

A0.5 leaves this protocol authorization/proof boundary unchanged while hardening the injected
source/cache transaction. Approved mirrors retry in order, granular sources transfer only missing
or corrupt SHA-256 blobs, Optional source exhaustion can publish an explicit fallback catalog, and
successful cache repair still requires fresh application-session proof before authority knowledge
advances. A0.6 now lets `RemoteContentClient` resolve an exact typed `.usui` artifact only through
its live applied realm catalog. `RemoteUiClientBridge` uses that definition to decode the existing
compact `HotOpen`, primes Client Core's bounded cache, and sends the ordinary semantic apply proof
only after the view opens. Game continues to own model values/actions; cache possession alone does
not authorize a definition.

A0.7 resolves a selected complete WorldPack only after the realm catalog applies. World performs
canonical open/fingerprint/content binding, then `RemotePlayableSession` constructs and publishes
the complete initial `ClientWorldSemanticCache` privately before exposing it or permitting guest
submission. Failure publishes no partial structural state. The existing dynamic-public adapter is
attached afterward and keeps its independent application-proof lineage. No overlay/chunk artifact
or new protocol message is introduced. A0.8 supplies the native Godot consumer: its primitive
substrate can boot before content exists, required World/UI content still fails closed, and a warm
cache still requires fresh application-session proof.

Client F0.5 adds no remote UI protocol. Its accepted preinstalled-standard canary converges
with this A0.6 resolver on the same `UiDefinitionCacheKey` and existing `HotOpen`; when a realm
requires content application, a matching preinstalled definition may avoid redundant realization
work but cannot replace the authenticated catalog or `ContentSetApplied` proof.

## Dynamic public chunk state

`RemoteChunkWorldStateClient` is the production client adapter for the selected
current-game `ChunkPublication` profile.

Ordinary chunk deltas and absence messages arrive on independent-unreliable
WorldState. The client validates into fixed private scratch, applies atomically to
`ClientWorldSemanticCache`, and queues `ChunkApplied` only after semantic success.
A duplicate delivery whose revision is already covered is harmless and can produce
another proof so a lost prior proof converges without pretending transport delivery
was knowledge.

Exceptional snapshot recovery uses one bounded private Bulk candidate. Reliable
Begin/segments/Commit reconstruct the canonical snapshot body; the complete body is
hostile-validated and converted to private semantic scratch before one atomic cache
publication. Cancel/timeout retires only the matching candidate. Per-resident-key
delivery lineage orders snapshot, delta, and absence so delayed same-session data
cannot erase or resurrect newer state.

Cross-chunk movement is resolved by canonical public identity/version lineage, not
by chunk-local row position. A chunk snapshot establishes complete membership for
that chunk only.

### Canonical remote-removal rule

The real public-actor consumer vertical exposed and permanently regresses an
important decoder boundary: a remote `Remove` delta reconstructs
`WorldPublicDeltaRecord.Current` as canonical `default`. Identity and public version
are carried by the delta itself; inventing a partial current record causes the
semantic cache to reject a valid authoritative removal.

## Real public-actor consumer

The native T0.10 acceptance now consumes the complete production path:

```text
Game ECS/public authority
    -> ChunkPublication scheduler/server
    -> secure Network protocol
    -> RemoteChunkWorldStateClient
    -> ClientWorldSemanticCache
    -> GodotWorldRenderer
```

It proves spawn, movement/state replacement, interest leave/re-entry,
fresh-session reconnect/resync, destruction, and save/load publication-generation
replacement without a benchmark-only client world adapter.

This acceptance does not move rendering or visibility policy into Client/Network.

F0.4 adds `RemotePublicPresentationMetadataClient` beside the hot chunk consumer. It applies a
bounded, relatively cold custom label/eligibility fact to Client Core only after validating the
application session, server generation, full public identity, and metadata revision, then retains
an exact Control-lane application proof. The adapter owns no nameplate visibility, Ctrl behavior,
Godot node, or AOI request. `RemotePlayableSession` also accepts this message during the narrow
post-admission activation transition so a queued metadata occurrence cannot be consumed before the
complete semantic composition becomes active.

## Retained entity-profile clients

`RemoteWorldStateClient` and `RemoteDirtyEntitySpatialFanoutClient` remain the
client-side consumers for the retained entity-semantic profile families used by
conformance/comparison evidence. They share the same core rules: bounded hostile
decode, canonical identity/lifetime, application proof after semantic commit, and
no client-side authority.

Their existence does not create runtime cross-profile ownership transfer. Current
clients have no shared cross-profile ordering/deduplication fence for one logical
public object.

## Owner-private UI / Inventory

`RemoteUiClientBridge` carries the generic authenticated UI semantic protocol into
`UiClientRuntime`. This path is deliberately separate from public spatial AOI.

The I0.8 remote Inventory vertical proves:

```text
Admission-authorized owner-private Inventory view
    -> RemoteUiSemanticPort / secure Network
    -> RemoteUiClientBridge
    -> UiClientRuntime
    -> Godot UI renderer
```

Move/split/equip/unequip actions, reconnect, and save/load resynchronization use the
same lower Network session while foreign-container, stale/duplicate, and unknown-view
intent remains rejected by the owning application semantics. Inventory contents do
not enter `ChunkPublication` merely because their authorized owner can see them.

## Controlled simulation and Events

`RemoteControlledSimulationClient` consumes bounded authoritative input/owner-state
and revisioned clock semantics. Input/OwnerState are latest/superseding streams;
non-coalescible application facts use the retained Event path instead.

`RemoteApplicationEventClient` preserves ordered/idempotent application Event
semantics and application proof. A state baseline or public-world resync cannot
silently erase independently retained facts.

## Chat occurrences

`RemoteChatClient` is the C0.1 authenticated consumer for reliable,
non-coalescible spatial speech and private command/error rows. It submits only
session epoch, monotonic sequence, speech mode, and bounded text. Incoming
occurrences commit to `ClientChatState` before an explicit Control-lane proof is
queued; duplicates re-prove the same delivery without appending twice, while a
rejected semantic occurrence never advances server knowledge.

The optional `WorldPublicEntityId` is presentation-only. A missing resident actor
suppresses an overhead bubble but never suppresses the feed occurrence.

## Current stop

Client/Network is consumption-ready. New adapters should be added only for a concrete
Game semantic domain with an explicit authority/application contract. Do not place
presentation policy, AOI, ECS state, persistence ownership, or generic replication-
profile switching in this layer.
