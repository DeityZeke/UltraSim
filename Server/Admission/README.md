# UltraSim Server Admission

`Server/Admission` owns session admission progression: connection event -> active
script-registered step -> panel open -> trusted completion signal -> next event.

It intentionally does not own network transport. The network layer feeds UI
action documents into the admission runtime and exposes a small panel gateway
for opening server-driven panels. This keeps future account, character, and
world-entry logic out of the transport project.

Current scope:

- Per-connection active admission state.
- Script-registered flow steps from `IUltraSimScriptAdmissionRegistry`.
- Explicit action routes on active steps for non-credential panel actions.
- Single-owner replacement semantics through the script registry.
- A narrow `IAdmissionCredentialValidator` seam.
- A dev/test `AcceptAllAdmissionCredentialValidator` until the accounting engine
  supplies real username/password/session policy validation.

The real credential path must remain packet-bound and trusted. Password or
challenge-response data must not travel through generic UI field values.
