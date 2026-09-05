# Game Admission

`UltraSim.Game.Admission` owns the authoritative transition from an authenticated or local
participant into a playable Game session. Participant, Account, durable Character, ECS Entity, and
client display identity remain distinct contracts.

The existing accountless path still creates the composition's default playable without creating an
Accounting record. D0.5 adds `JoinGuest` for the Concept Demo. Its input is untrusted name/body
intent; `GameGuestAdmissionPolicy` trims and validates the name, admits only the finite supported
bodies, and produces a canonical session-scoped `GameGuestIdentity`. Current bounds are 24 Unicode
scalar values and 96 UTF-8 bytes, with empty, control-containing, and malformed Unicode names
rejected.

Admission retains the accepted identity with the entered participant session. A concrete
`IGuestAdmissionEntityBinder` maps the finite body choice to a compiled Game playable definition;
Admission does not select a Godot mesh. Chat and local owner presentation may consume the retained
canonical identity. Rejected operations do not mutate existing entered sessions.

Guest entry deliberately creates no Account, durable Character ownership, uniqueness policy,
profanity system, or saved identity. Remote transport for the same intent/result is deferred to the
authenticated client checkpoint.
