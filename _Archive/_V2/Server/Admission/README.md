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
- Optional `IAdmissionRuntimePolicy` hooks for server-owned dynamic behavior,
  such as publishing character-select rows after a panel opens or validating
  character creation before an action route advances.
- Single-owner replacement semantics through the script registry.
- A narrow `IAdmissionCredentialValidator` seam.
- A dev/test `AcceptAllAdmissionCredentialValidator` for isolated flow tests.
- The default movement server wires an accounting-backed admission policy and
  credential validator for real account, character, and world-entry behavior.

The real credential path must remain packet-bound and trusted. Password or
challenge-response data must not travel through generic UI field values.

Admission flow stays generic: it opens the script-selected panel, tracks the
active step, validates credential completion, and evaluates explicit action
routes. Domain-specific policy belongs in a runtime policy implementation above
this layer. The default movement server uses an accounting-backed policy to
populate character rows and create character records, but the admission kernel
does not depend on accounting.
