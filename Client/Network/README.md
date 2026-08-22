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

N1.3 adds one bounded private snapshot candidate. Reliable ordered Bulk
Begin/segments/Commit reconstruct the frozen canonical snapshot body, which is
fully validated before one atomic semantic-cache publication. Reliable Cancel
retires only the matching private candidate. Per-resident-key delivery lineage
orders snapshot, delta, and absence results so delayed same-session recovery data
cannot erase or resurrect newer state.

Presentation residency, rendering, authority, chunk publication history, and
subscription entitlement remain separate owners.
