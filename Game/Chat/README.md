# Chat

Chat is authoritative Game semantics. C0.0 owns packet-free interpretation and
hearing; C0.1/C0.1a carry those accepted semantics through the authenticated Game
protocol and bounded client state; C0.2 adds the first routed Global audience and
explicit authority-originated System occurrence without moving Chat into Network.

The packet-free `GameChatService` accepts only a trusted entered
`ParticipantId`, bounded text, and one of `Whisper`, `Say`, `Yell`, or `Global`. Admission
establishes the participant-to-actor binding. Chat reads the live actor and its
current authoritative `WorldPosition` for spatial speech; no player API accepts a sender name,
position, range, or recipient list. System publication is a separate authority-only call and does
not manufacture a participant, actor, or public anchor.

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

Every accepted public Chat fact is an independently sequenced occurrence with its complete
recipient set retained in a bounded FIFO until the packet-free consumer copies and
consumes it. The canonical server-derived display label and optional public actor
anchor are captured at acceptance. Occurrences never coalesce as latest state, and an
authority-generation replacement does not erase an already accepted occurrence.

## Routed Global and System

`Global` is routed to the exact current entered-participant census in the one Game authority. It
does not query spatial hearing or dynamic-public AOI, and it does not imply a cross-server service,
persistent history, party/guild channel, direct message, or moderation system.

`PublishSystem` creates one authority-originated occurrence for that same bounded census. The fact
has an explicit `SystemBroadcast` kind, `None` speech mode, and no speaker participant, ECS entity,
or public presentation anchor. A system occurrence is therefore never represented as a fake player
named `SYSTEM`. Empty, malformed, over-limit, capacity-blocked, or audience-free publication fails
without a partial retained fact.

## Commands

The instance-owned finite command registry uses `/`. Prefix recognition happens
before speech routing, so unknown or malformed commands never fall through into
speech. C0.0 implements only:

- `/help`, returning the finite command list privately;
- `/where`, returning the caller's current authoritative Game position privately.

There is intentionally no generalized permission/RBAC, scripting, moderation,
channel, direct-message, history, or event-bus framework.

## Remote consumer

C0.1/C0.2 use three bounded Game messages: sequenced `chat-submit` on reliable Event,
recipient-relative `chat-occurrence` on reliable Event, and explicit
`chat-applied` proof on Control. Submission contains only application-session
epoch, sequence, speech mode, and at most 256 canonical UTF-8 bytes. The server
derives participant, actor, display label, routing policy, audience, and—only for spatial
speech—position, range, and optional `WorldPublicEntityId` presentation anchor.

`RemoteChatDeliveryService` transactionally moves a complete live C0.0 audience
into a fixed delivery ledger only after every live recipient has a registered
session and quota. The ledger is a fixed row array with an O(1) free list and one
intrusive FIFO head/tail/count per application session; idle progress does not scan
the global row capacity. Each recipient has independent ordered debt and
application proof; packet acknowledgement never retires it. Terminal session loss
explicitly abandons that session's unproved obligations without counting them as
applied, and cannot permanently head-of-line block surviving recipients or later
speech. A reconnect starts a new application-session lineage and never inherits
old debt.

`ClientChatState` is an engine-neutral bounded append-only feed. It fences
application-session lineage, applies duplicates idempotently, rejects gaps and
malformed facts, and may evict the oldest already-applied presentation row. Its
recipient sequence remains monotonic across Game generation replacement, and
already-applied rows survive replacement. Origin generation is presentation
metadata: an old occurrence still reaches the feed, but its public anchor cannot
produce overhead speech against a different resident world generation. The
playable Godot client supplies an always-available feed, Global-capable mode selector, text
entry/focus isolation, private command/error rows, a presentation-only four-second overhead label
when a spatial public actor anchor is resident, and a 4.5-second top banner derived locally from an
already-applied System occurrence. The banner is not a second protocol fact and its expiry does not
erase the retained feed row.

## Checkpoint boundary

C0.0 and its C0.0a review hardening are accepted with packet-free tests for authority, exact hearing, current
movement, ordered occurrence retention, command interception, Unicode/text
bounds, complete-or-reject fanout, generation/world fencing, preexisting Admission
census, rejection/session reconciliation, fail-closed default results, and zero
warmed allocation on ordinary speech discovery.

C0.1 remote protocol, application proof, `ClientChatState`, and Godot feed/bubbles,
plus C0.1a occurrence-lineage, intrusive-ledger, and terminal-abandonment hardening,
are accepted. C0.2 Global/System routing, shared local/remote delivery, and the transient native
banner are also accepted. Composition must size `MaximumParticipants`
against the server's admitted-session capacity; the C0.0 default of 256 is an
oracle default, not a production CCU ceiling. `MaximumRecipientsPerSpeech` remains
an independent complete-audience bound. Additional routed channels, moderation, history, social,
and privileged-command policy remain stopped. Accepted evidence covers spatial-versus-routed
audience independence, explicit non-player System identity, reconnect without invented history,
zero warmed allocation for both routed occurrence kinds, and local/remote/Godot convergence. See
[the Chat plan](../../Docs/Plans/CHAT.md).

D0.6 later consumes `PublishSystem` for trusted test-marker START/COMPLETE transitions. That
producer remains outside participant Chat input and emits the same one retained System occurrence
used by the feed/banner; it adds no Chat wire kind, audience policy, history, or player permission.
