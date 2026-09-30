# P7 active follow-up — real UDP Kick acceptance

This is a companion to the immutable [foundation record](framework-p7-operator-foundation-2026-09-29.md),
not a replacement for its original test scope. Test commit:
`56ca124333d1efc1cb3560a4bfa81cdc31f7f807`.

`P7LiveOperatorKickRetiresExactGuestSession` uses the existing M0.0 loopback
Direct Connect/Network application fixture. It authenticates a real guest,
enters one Game participant, reads the owner-published participant ID and
the Hosted session's trusted factory correlation, invokes `server.operator/kick`
as a Test principal through `ControlPlaneRegistry`, drives the actual UDP host
until Game Admission's participant count reaches zero, then verifies a stale
second Kick rejects and both action outcomes are audited. It passed as part of
the full `_Tests/UltraSim.Network.Application.Tests` Release executable.

This closes the **live scalar-host Kick acceptance** item from the foundation
record. It does not prove every sharded-owner/network impairment case, nor
does it implement an in-game remote operator grant. P7 remains ACTIVE because
recovery/restart and the Godot GUI server are still missing. The prior
uninterrupted canonical Release pass tested production commit `93311a97…`;
this follow-up changes tests only and is not a claim of a new full canonical run.
