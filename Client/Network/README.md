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
