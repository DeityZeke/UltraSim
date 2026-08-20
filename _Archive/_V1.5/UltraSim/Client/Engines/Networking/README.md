# Client Networking Bridge

Client-side bridge between `Network/` runtime and projected `ECS/` world flow.

Rules:

- May reference `ECS/`, `Network/`, and client host/runtime APIs.
- May reference `Shared/Engines/Networking/`.
- Must not reference server host/runtime APIs.
- Owns input send wiring, reconciliation enqueue/apply hooks, and interpolation/render feed integration.
