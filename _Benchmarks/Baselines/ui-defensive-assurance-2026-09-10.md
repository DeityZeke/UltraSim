# UI defensive-assurance correction — 2026-09-10

## Scope and independent disposition

- External review base: `0c8e01fba50437fc0251ad87eddfb268efd8c3eb`.
- Independent correction base: `e662d8dfbd139d11e0f15afb7dfe417388835106`.
- External probe source was not supplied or used. Each reported mechanism was independently traced
  and exercised with repository-owned regressions. The two source-grounded Godot observations were
  also executed natively against the reviewed revision.
- This correction changes UI failure/lifetime/admission mechanics. It does not change Game
  authority, UI definition identity, application-session proof, content authorization, or Network
  protocol identity.

| Review item | Disposition | Permanent evidence |
| --- | --- | --- |
| UI-01 local shutdown escapes owner thread | Confirmed and repaired | pre-correction child exited 134; `LocalShutdownMustContainUiCloseBackpressure` exits normally |
| UI-02 view capacity faults authority | Confirmed and repaired | `LocalUiCapacityMustNotFaultAuthority`; subsequent monitor open succeeds after capacity returns |
| UI-03 proof pressure replays operation | Confirmed and repaired, including bridge replacement | `ProofBackpressureMustNotReapplyUiOperation`; deterministic capacity-one `ProofBackpressureMustSurviveUiBridgeReplacement` withholds proof consumption until after disposal; `BridgeReplacementRetainsSessionProjectionAndActions` retains the full live-session gate |
| UI-04 failed close loses cleanup identity | Confirmed and repaired | `ControllerCloseFailureMustRemainRecoverable`; close-first ordering also covers unbind failure |
| UI-05 action reason bypasses definition limit | Confirmed and repaired | `ActionReasonMustRespectCompiledTextLimit` at 64/65 characters |
| UI-06 truncated delta allocates from impossible counts | Confirmed and repaired | reviewed 44-byte payloads allocated 2,097,144 / 2,621,448 / 1,572,912 bytes; corrected rejection stays below 16 KiB |
| UI-07 renderer callback disposes/repopulates runtime | Confirmed and repaired | `RendererCallbackCannotResurrectDisposedRuntime` covers Create, Apply, and Close callbacks |
| UI-08 renderer callback reenters bridge pump | Confirmed and repaired | `ReentrantBridgePumpCannotRejectSuccessfulOpen`; nested pump rejects before channel mutation, outer open applies once, and its action endpoint remains live |
| UI-09 rejected callback bridge disposal strands sink | Confirmed and repaired | `RejectedCallbackBridgeDisposeIsAtomic`; rejected disposal leaves the bridge operational, and later ordinary detach/reattach preserves actions |
| NET-10 newer activity underflows idle time | Same adjacent race, already repaired | `HostedLifetimeIgnoresActivityNewerThanOwnerSample`; complete content-backed remote Inventory journey passes |
| UI-L1 retained true toggle ignores Unset | Natively confirmed and repaired | reviewed Godot result remained pressed; corrected native true-to-Unset gate is unpressed with zero emitted actions |
| UI-L2 finite flow value exceeds native integer range | Natively confirmed and hardened | reviewed adapter faulted during conversion; corrected adapter rejects before a view/control root is retained |

No reported item was patched without reproduction or a directly executed source-boundary failure.

## Corrected contracts

### Semantic publication and cleanup

`UiSemanticPublicationException` is the explicit expected family for retained-view capacity and
bounded transport pressure. Local sign publication occurs after the authoritative Game callback;
local administration and remote post-command presentation catch only that family. Unexpected Game
or implementation exceptions retain their terminal behavior.

Sign and Server Monitor retain their view/action identity when close publication throws. Accepted
close or an explicitly absent projection clears the obligation; an unbind failure cannot prevent
the close attempt. Inventory likewise retains a session on expected close pressure. Local terminal
retirement stops accepting operations and retires authority-side UI state without requiring a
client proof/close round trip. Owner-thread cleanup attempts every sibling resource and records the
first failure rather than allowing a `finally` exception to terminate the process.

### Exactly-once application and client callback lifetime

The retained local channel records a completed operation status when its proof queue is full. It
retries only proof publication before reading another operation, including after disposal and
replacement of the client bridge. Reattachment is limited to the same `UiClientRuntime` projection;
a different projection requires local-session retirement. Renderer and model effects happen once.
The bridge pump itself is non-reentrant and rejects before observing channel debt. Disposal does not
mark the bridge disposed or release its session registration until runtime action-sink detachment
succeeds, making callback-time rejection side-effect-free.

`UiClientRuntime` forbids synchronous mutation/disposal throughout renderer `Create`, `Apply`, and
`Close`. A callback violation faults that operation before candidate state publication; existing
state remains owned and can be retired by later disposal.

### Bounded model, wire, and Godot admission

Action-state text is checked against the exact compiled definition during direct model mutation and
delta validation. The wire codec retains its outer operation bound, passes configured definition
limits into delta decoding, and checks minimum feasible bytes before allocating scalar/list/action
or replacement-row arrays.

Godot validates all flow spacing and padding conversions before materializing a control tree.
Retained CheckBoxes assign their pressed state on every value update, with `Unset` defined as
unpressed.

## Verification

Focused Release gates passed during correction:

- 27 UI kernel checks;
- 30 Client Core checks;
- 22 packet-free Client Local checks, including deterministic proof-pressure replacement, reentrant-callback, and isolated process-survival gates;
- 33 Game Inventory and 18 Game Admission checks;
- 136 Game Replication checks;
- the complete Network application suite, including content-backed remote Inventory;
- the complete Full-flavor Network kernel/Hosted suite, including newer activity sampling;
- native Godot S0.8/I0.11 UI acceptance with the new adapter-boundary cases.

The complete exact-tree repository verifier passed after correction, including all managed suites,
native Godot UI/world acceptance, Single Player/Online/Complete publish boundaries, the exported
Contentless Online cold/warm/profile-change journey, the exported Complete-host multiplayer
journey, F0.7 mixed-session lifecycle, and final F0.8 composition parity.
