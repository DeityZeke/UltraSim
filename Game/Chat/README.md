# Chat

Chat is authoritative Game semantics. C0.0 deliberately stops before Game
protocol messages, Client Core state, Network delivery, or Godot presentation.

The packet-free `GameChatService` accepts only a trusted entered
`ParticipantId`, bounded text, and one of `Whisper`, `Say`, or `Yell`. Admission
establishes the participant-to-actor binding. Chat reads the live actor and its
current authoritative `WorldPosition`; no caller API accepts a sender name,
position, range, or recipient list.

## Spatial speech

The current provisional exact X/Z hearing radii are:

```text
Whisper    2,000 mm
Say       15,000 mm
Yell      30,000 mm
```

Discovery enumerates only intersecting 16 m world chunks through
`WorldSpatialIndex`, copies their bounded member sets into preallocated scratch,
maps live entered actors to participants, and performs exact integer distance
tests against authoritative positions. It does not scan the entire ECS and does
not use dynamic-public replication membership as hearing authority.

Default limits are 256 entered participants, 25 candidate chunks, 4,096 candidate
entities, 256 recipients per speech, 64 retained speech facts, and 256 UTF-8 bytes
per input. Malformed Unicode, empty text, over-limit text, or any candidate,
recipient, or retention overflow rejects without truncation or partial delivery.

Every accepted speech is an independently sequenced occurrence with its complete
recipient set retained in a bounded FIFO until the packet-free consumer copies and
consumes it. Speech never coalesces as latest state.

## Commands

The instance-owned finite command registry uses `/`. Prefix recognition happens
before speech routing, so unknown or malformed commands never fall through into
speech. C0.0 implements only:

- `/help`, returning the finite command list privately;
- `/where`, returning the caller's current authoritative Game position privately.

There is intentionally no generalized permission/RBAC, scripting, moderation,
channel, direct-message, history, or event-bus framework.

## Checkpoint boundary

C0.0 is accepted with packet-free tests for authority, exact hearing, current
movement, ordered occurrence retention, command interception, Unicode/text
bounds, complete-or-reject fanout, generation/world fencing, preexisting Admission
census, and zero warmed allocation on ordinary speech discovery.

C0.1 remote protocol, application proof, `ClientChatState`, and Godot feed/bubbles
remain stopped pending review. See [the Chat plan](../../Docs/Plans/CHAT.md).
