# Server operator foundation (P7 active)

`ServerOperatorService` owns server-side operations on the Direct Connect owner thread.
`ServerOperatorProvider` publishes those operations through `Framework.ControlPlane`;
`OperatorConsole` is a text adapter over the same typed, capability-checked actions.
No frontend opens save files, mutates Game, or drains a Network session directly.

Current actions: Save Now, Backup Now, graceful Shutdown, and exact current-participant Kick.
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
`kick <participant-id> confirm`. Ctrl+C uses the same Shutdown action. The verifier's
stop-file remains test plumbing, not an operator API.

This is not the completed P7 product. There is no selected-backup restore, startup recovery,
or durable session rehydration yet. `restart confirm` is visible but returns `Disabled`;
it does not kill a server under the pretense that a supervisor can restart it. No Godot
server GUI or remotely authorized in-game Admin route is present. Ordinary player
principals cannot see this provider, and no client-claimed capability list is trusted.
