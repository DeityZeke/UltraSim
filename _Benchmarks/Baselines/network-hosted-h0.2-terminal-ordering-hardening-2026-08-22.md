# Hosted Network H0.2 terminal-ordering hardening

Captured: 2026-08-22
Base checkpoint: H0.2 authenticated promotion
Scope: local terminal commit before remote acknowledgement

`ClientFinish` now commits the one local route, established `ConnectionId`, secure
peer, and hosted session before attempting `ServerFinish`. A rejected terminal
send does not roll back locally established truth. The client repeats its exact
`ClientFinish`; the known-path handler resends the cached `ServerFinish` without
allocating another candidate, ECDH operation, connection, route, peer, or session.

Permanent regression:

```text
HostedTerminalSendFailureKeepsOneRecoverableSession
```

Debug and Release prove one established session/connection/route immediately
after the deliberately rejected first terminal flight, followed by convergence
through a duplicate finish on that same session. H0.2 packet/security semantics
and all earlier immutable evidence remain otherwise unchanged.
