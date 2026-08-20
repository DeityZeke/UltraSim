# Server Networking Bridge

Server-side bridge between `Network/` runtime and authoritative `ECS/` world flow.

Rules:

- May reference `ECS/`, `Network/`, and server host/runtime APIs.
- May reference `Shared/Engines/Networking/`.
- Must not reference client host/runtime APIs.
- Owns session-to-entity mapping, inbound command apply wiring, and outbound replication sourcing.
