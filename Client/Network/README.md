# Client Network

`Client/Network` translates authenticated game protocol messages into the
engine-neutral retained state owned by Client Core. It owns no authoritative ECS
state and no Godot/Unity resources.

For Network N1.2, `RemoteChunkWorldStateClient` consumes canonical chunk deltas
and absences from the independent-unreliable WorldState lane. It validates into
fixed scratch, applies atomically to `ClientWorldSemanticCache`, and queues an
application proof only after semantic success. A repeated delivery whose revision
is already covered is harmless and produces another applied proof, allowing a
lost prior proof to recover without pretending transport delivery was knowledge.

The bridge does not implement first-interest/history-recovery snapshots; N1.3
owns that Bulk transaction. Presentation residency, rendering, authority, chunk
publication history, and subscription entitlement remain separate owners.
