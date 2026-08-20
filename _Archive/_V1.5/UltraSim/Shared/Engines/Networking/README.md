# Shared Networking Bridge

Shared bridge helpers used by both client and server networking integrations.

Rules:

- May reference `ECS/` and `Network/`.
- Must not reference client-only host APIs.
- Must not reference server-only host APIs.
- Keep this layer focused on reusable mapping, serialization helpers, and common bridge utilities.
