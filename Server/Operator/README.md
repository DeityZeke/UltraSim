# Server operator foundation (P7 active)

`ServerOperatorService` owns server-side operations on the Direct Connect owner thread.
`ServerOperatorProvider` publishes those operations through `Framework.ControlPlane`;
`OperatorConsole` is a text adapter over the same typed, capability-checked actions.
No frontend opens save files, mutates Game, or drains a Network session directly.

Current actions: Save Now, Backup Now, graceful Shutdown, supervised Restart-with-save,
and exact current-participant Kick.
Save captures one coherent owner-thread boundary and publishes through `GameRuntimePersistence`
and `SaveSetCoordinator`. Autosave uses that same path with one operation slot and a cold
cadence policy. Backup copies a hash-validated, complete published generation to a disjoint
root through private staging, with bounded retention. The bounded audit records action
requests, including rejected ones; text arguments are redacted.

The Direct Connect identity directory owns `operator-policy.json` (cold configuration),
`saves/`, and `backups/` by default. A configured path must be absolute and the save and
backup roots must be disjoint. The operator is responsible for filesystem access control
if moving those roots outside the private identity directory. The console accepts `help`,
`status`, `participants`, `audit`, `save`, `backup`, `shutdown confirm`, and
`kick <participant-id> confirm`, and `restart confirm`. Ctrl+C uses the same Shutdown
action. The verifier's
stop-file remains test plumbing, not an operator API.

On startup, Direct Connect automatically reopens a complete saved generation when `CURRENT`
or any generation directory exists. `operator-policy.json` selects `RecoveryMode`:
`0` is strict CURRENT (default), `1` allows the existing SaveSet fallback to the newest
complete compatible generation. A corrupt strict CURRENT fails startup before Network
admission. The selected sequence and number of retired transient guests appear in the
startup line and Control Plane diagnostics. The server-side cold callback loads before
`SharedPlayableAuthority` binds generation-local publication state. Its fixed scene handles
are rebound by validated persistent identity. This Concept Demo admits only accountless
guests: saved guest actors/inventories retire through the normal Game lifetime policy,
and a fresh process never claims to have restored their old sessions.

`restart confirm` captures a fresh final save and closes Network admission before its
durable publication completes. On success the process drains and exits with code 75.
The exported `run-managed.sh` alone interprets 75 as an instruction to start a fresh
process with the same arguments, identity directory, policy, and save root. Directly running
the binary returns 75 to an external supervisor; it never self-spawns. A failed final save
does not request restart; admission remains closed so the operator can diagnose/retry
without admitting new work outside the intended final boundary.

This is not the completed P7 product. There is no selected-backup restore or durable
account/session rehydration. No Godot
server GUI or remotely authorized in-game Admin route is present. Ordinary player
principals cannot see this provider, and no client-claimed capability list is trusted.
