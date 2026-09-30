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
`status`, `participants`, `audit`, `logs`, `save`, `backup`, `shutdown confirm`, and
`kick <participant-id> confirm`, and `restart confirm`. Ctrl+C uses the same Shutdown
action. The verifier's
stop-file remains test plumbing, not an operator API.
The entered-participant collection reuses the already sanitized Game Server Monitor
snapshot for display label, Local/Remote kind, round-trip estimate and public chunk,
then joins it to the exact current control-session epoch and public actor identity.
It does not invent Account/Character identities or publish private coordinates.
The selected Concept Demo composition has fixed playback; its read-only playback
control explicitly reports that variable playback and Pause are disabled.

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
The owner-local `OperatorLogRing` retains 256 bounded operational events with severity,
category, UTC time and sequence. Its Control Plane `log` collection is distinct from
the action audit and authoritative world history; no event is generated per idle tick.

To restore one selected backup while the server is **stopped**, run the published
`UltraSim.Server.DirectConnect --identity-dir=<same directory>
--restore-backup-sequence=<sequence>`. The maintenance process takes the same exclusive
deployment lease as the live server, validates and privately stages the selected backup,
runs normal staged Game restore, retires saved transient guests, then publishes a **new**
forward live SaveSet generation. It does not open Network admission or replace a running
authority. Start the server normally afterward. Missing/corrupt backup selection fails
without changing the live `CURRENT` pointer. `run-managed.sh` exits normally after a
maintenance invocation; it does not loop the restore argument.

`--list-backups` is a separate read-only maintenance process that can run while the
server is live. It hashes each bounded published candidate and reports complete or
rejected status without putting disk scans on the authority thread. The optional
Godot server window starts this inspector on demand; the live Control Plane retains
cheap latest-backup diagnostics. Inspection does not select or restore a generation.

The optional exported `Server/Godot` frontend starts one separately published
ServerOnly headless child. Its inherited bounded stdio frames carry Control Plane
action identities, not an unauthenticated Network endpoint or duplicate save
implementation. The server resolves each frame on its owner thread with its trusted
local principal and existing confirmation/audit semantics. GUI parent loss closes
stdin; the child treats EOF as the same graceful Shutdown request only when launched
with `--shutdown-on-stdin-eof`. The GUI shows status, participants, bounded log/audit,
on-demand backup candidates, discoverable categories, and the current action set.

This is not the completed P7 product. Backup restoration is cold maintenance rather than
a live GUI/console action, and there is no durable account/session rehydration. A remotely
authorized in-game Admin route is not present. Ordinary player
principals cannot see this provider, and no client-claimed capability list is trusted.
