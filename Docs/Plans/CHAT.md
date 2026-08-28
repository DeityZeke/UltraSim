# UltraSim Chat + Commands

**Status:** C0.0 + C0.0a accepted; C0.1+ stopped forward context

**Prepared after:** Inventory I0.11 (`305e40f4`)

**Accepted checkpoint:** C0.0 — packet-free authoritative speech + command oracle

This document preserves the intended Chat/Commands semantics before implementation pressure causes speech, channels, commands, Network delivery, and presentation to be collapsed into one subsystem.

The central rule is:

> **One text-entry surface does not imply one server semantic.**

UltraSim distinguishes at least:

```text
spatial speech
    a character says something in the world
    recipients are selected by authoritative Game/world hearing policy

routed chat
    a message is addressed to a non-spatial audience
    examples: party, guild, global, staff, direct message

commands
    authenticated text intent asks server/Game authority to do something
    command text is not speech and is not broadcast merely because it came from chat input
```

They may share input parsing and presentation surfaces where useful, but they do not share authority, routing, retention, or permissions merely for convenience.

---

# 1. Archaeology and lessons

The active V3 implementation now contains only the packet-free C0.0 Chat oracle.
Relevant historical references for the deliberately deferred work are:

- `_Archive/References/runuo-2.7`
- `_Archive/References/Knives Chat 3.0 Beta 9`
- `_Archive/_V2/Kernels/Network/Protocol/*Chat*`
- `_Archive/_V2/Server/CHAT_COMMAND_SYSTEM_PLAN.md`
- `_Archive/_V2/Server/Network/ServerChatService.cs`
- `_Archive/_V2/Server/Core/ServerCommandRegistry.cs`

These are executable/reference archaeology, not code to port wholesale.

## 1.1 RunUO

Useful behavior:

- command interpretation occurs before ordinary speech;
- recognized command text does not fall through and become spoken text;
- commands have explicit registration and authorization requirements;
- local speech is world/spatial behavior;
- whisper, normal speech, and yell have different hearing ranges;
- speech can later become gameplay input for NPCs, regions, objects, or other explicit Game systems.

Do not carry forward:

- process-global mutable command registries;
- giant `Mobile` objects as command/speech context;
- global event sinks;
- region/item/mobile callbacks hidden inside the speech router;
- hard-coded access-level assumptions as the only future permission model;
- RunUO map/tile units as if they were UltraSim millimeters.

RunUO's 1 / 15 / 18 range values are useful evidence for the *relative semantic shape* of Whisper << Say < Yell. They are not automatically UltraSim meter values.

## 1.2 Knives Chat

Knives Chat proves that a mature chat product can grow far beyond local speech:

- public/global and regional channels;
- alliance, faction, guild, staff, IRC and custom channels;
- private messages;
- ignore;
- moderation/squelch/ban;
- anti-spam delay;
- bounded channel history;
- per-user presentation settings;
- tabs/categories/colors;
- friends/mail/notifications and other social features.

Useful lessons:

- channel membership is a routing/entitlement policy, not spatial AOI;
- channel history should be explicitly bounded;
- anti-spam policy belongs on the authoritative ingress path;
- ignore and moderation are separate concepts;
- direct messages are a distinct routing semantic;
- client tabs are presentation filters, not channel authority.

Do not carry forward:

- process-global `ArrayList`/`Hashtable` channel state;
- direct `Mobile.SendMessage` coupling;
- one giant channel object that owns routing, persistence, filtering, UI, IRC, moderation, and user state;
- global partial-name scans as authoritative direct-message identity resolution;
- automatic persistence of every chat product concept before gameplay earns it.

## 1.3 UltraSim V2

V2 had useful semantic vocabulary but placed too much of it in Network.

Historical V2 messages included `ChatSubmitPacket`, `ChatMessageEventPacket`, and `ChatMessageFlags`, carrying ideas such as message ID, channel ID, sender entity/name, text, server tick, and flags like System, Local, Party, Guild, Whisper, Staff, Error, Overhead, and Announcement.

The metadata concepts may be useful. The ownership is not.

**V3 must not put Chat domain types into `Kernels/Network`.**

V2's `ServerChatService` also contains two behaviors that should not be repeated unchanged:

1. it accepted a caller-supplied local scope rather than deriving the audience from authoritative current Game/world state;
2. it silently truncated overlong messages.

For V3:

> **The server derives the speaker and audience. The client never supplies a trusted sender, location, radius, recipient list, or spatial scope.**

And:

> **Over-limit text rejects explicitly. Do not silently rewrite/truncate a player's submitted fact.**

V2's command registry had several good ideas: explicit command-vs-not-command result, command sources, case-insensitive names, aliases, quoted arguments, named arguments, distinct syntax/unknown/permission outcomes, command replies, and audit hooks.

Those are candidates for later checkpoints. C0.0 should not port the entire V2 permissions/audit/scripting framework before V3 has a real privileged command.

---

# 2. Frozen V3 principles

```text
GAME OWNS CHAT AND COMMAND SEMANTICS.
NETWORK DOES NOT OWN CHAT.
NETWORK DOES NOT OWN COMMANDS.
AOI / HEARING POLICY DOES NOT MOVE INTO NETWORK.

CONNECTION IDENTITY != PARTICIPANT IDENTITY.
PARTICIPANT IDENTITY != CHARACTER IDENTITY.
CHARACTER / GAME IDENTITY != RUNTIME ENTITY IDENTITY.
RUNTIME ENTITY IDENTITY != WIRE / CLIENT PRESENTATION IDENTITY.

THE CLIENT DOES NOT CHOOSE ITS SPEAKER.
THE CLIENT DOES NOT CHOOSE ITS SPEAKER POSITION.
THE CLIENT DOES NOT CHOOSE SPEECH RANGE.
THE CLIENT DOES NOT CHOOSE RECIPIENTS.

COMMAND TEXT IS INTENT.
SPEECH IS AN OCCURRENCE / FACT.
A RECOGNIZED COMMAND NEVER FALLS THROUGH INTO SPEECH.

FACTS RETAIN OCCURRENCE.
CHAT / SPEECH MUST NOT COALESCE AS LATEST STATE.
TRANSPORT ACK != APPLICATION PROOF.
LOSS MUST NOT SILENTLY ERASE AN ACCEPTED CHAT FACT.

PUBLIC SPATIAL REPLICATION != SPATIAL SPEECH ROUTING.
THE SAME WORLD POSITION MAY INFORM BOTH WITHOUT MAKING THEM ONE PROFILE.

CHAT FEED STATE != AUTHORITATIVE CHAT HISTORY.
OVERHEAD BUBBLE LIFETIME IS PRESENTATION POLICY.
TABS / COLORS / FONT / BUBBLE STYLE ARE PRESENTATION POLICY.

REMOTE TEXT AND RECIPIENT WORK ARE BOUNDED.
HOSTILE TEXT MUST NOT CAUSE UNBOUNDED ALLOCATION OR FANOUT.
```

---

# 3. Current V3 substrate to reuse

Do not create parallel replacements for mechanisms V3 already has.

## 3.1 Authority / identity

Remote sessions already authenticate and enter through Accounting + Admission. The remote protocol adapter therefore has enough trusted context to resolve the currently entered actor before invoking Chat.

The remote wire must not contain an authoritative speaker `Entity`, participant ID, display name, or world position supplied by the client.

The first packet-free Chat API may receive a trusted runtime `Entity speaker` from Game composition in the same sense that Inventory receives a trusted owner after Admission. Chat itself must then read authoritative Game/world facts for that entity.

Display name should come from authoritative Game/Accounting character state or an equivalent Game-owned identity projection, never from submitted text.

## 3.2 World/spatial state

`GameWorldState` owns the current generation's authoritative spatial projection.

Useful existing operations include:

```text
GameWorldState.TryReadPosition(...)
WorldSpatialIndex.TryGetHome(...)
WorldSpatialIndex.GetMemberCount(...)
WorldSpatialIndex.CopyMembers(...)
```

`WorldSpatialIndex` is bounded, generation-owned, and keyed by home chunk. It does not currently expose a generic radius query.

For C0.0, local speech may perform a concrete Game-owned exact hearing query by:

```text
speaker authoritative WorldPosition
        ↓
enumerate bounded chunk keys intersecting the speech radius
        ↓
WorldSpatialIndex.CopyMembers into preallocated scratch
        ↓
read candidate authoritative positions
        ↓
exact distance / same-space test
        ↓
map eligible entered actors to recipients
```

Do not scan every ECS entity for every speech message.

Do not add a generic AOI interface.

If implementation exposes a genuinely useful small Game/World exact-radius helper, keep it concrete and dependency-correct. Do not move speech policy into the dependency-neutral Spatial kernel.

## 3.3 Network delivery

Use existing V3 Network lane semantics.

Accepted speech/chat is an application fact. It therefore belongs on reliable application Event semantics, not `WorldState` and not `OwnerState`.

A chat-specific Game protocol message may use the existing `Event` lane.

However:

> **Reliable transport alone is not permission to forget `SENT != KNOWN`.**

If the server retains an accepted speech fact until client semantic application, the remote path must have an application-level proof or use the existing retained application-event machinery in a way that preserves its proof semantics.

Do not claim successful chat delivery merely because Network ACKed a packet.

Do not add Chat vocabulary to `Kernels/Network`.

## 3.4 UI / Client Core / Godot

The hot player chat panel should be a normal local client surface, not a server-authored inventory-style schema window.

Desired long-term shape:

```text
always-available chat feed
text input
keyboard focus
local bounded presentation history
tabs / filters

plus

overhead speech presentation anchored to the corresponding visible public actor
```

Server-authored UI remains suitable for chat settings, channel browser, moderation tools, diagnostics, and administrative chat configuration.

---

# 4. Domain model

## 4.1 Text ingress

One player-facing text box can submit raw text.

Server interpretation order:

```text
1. recognized command prefix?
       -> command path
       -> never speech

2. future recognized routed-chat alias?
       -> channel/direct routing

3. otherwise
       -> default local Say
```

The first checkpoint only needs commands + spatial speech.

Do not implement party/guild/global/PM aliases before those routing domains exist.

## 4.2 Spatial speech

First current-game modes:

```text
Say
Whisper
Yell
```

They differ only in Game-owned hearing policy and presentation metadata.

Do not freeze RunUO's numeric units into a framework contract.

C0.0 should define one explicit current-game speech-distance profile in millimeters. The values are Game policy and may be tuned without changing Network or kernel contracts.

A good initial profile for implementation/testing is:

```text
Whisper  2,000 mm
Say     15,000 mm
Yell    30,000 mm
```

These values are intentionally current-game defaults, not framework invariants.

All range tests use integer/wide distance math consistent with current Spatial contracts. Avoid authoritative floating-point distance.

The speaker normally receives their own accepted speech fact so local presentation does not require a client-side fake message.

### Speech fact

Conceptually:

```text
GameSpeechFact
    server-created fact/message sequence
    authoritative speaker Game/public presentation identity
    speech mode
    canonical bounded text
    authoritative simulation tick / world time as needed
```

Do not include redundant authority if the recipient can resolve it from existing Game/client semantic identity.

For overhead presentation, the final remote fact should carry a Game-owned public presentation anchor identity that the client can try to resolve to a currently resident public actor.

Do **not** expose persistence identity merely to solve overhead rendering.

If the actor is not currently present in the client semantic world, the chat-feed message may still display while the overhead bubble is simply not realized.

### Speech occurrence vs history

The speech fact is an occurrence.

Server-side long-term chat history is not required for C0.0/C0.1.

Client feed history may be bounded presentation state and may evict oldest already applied rows without changing the fact that delivery occurred.

Later server-side history/search/logging is a distinct policy checkpoint.

## 4.3 Routed chat

Deferred until a concrete social domain earns it.

Examples:

```text
Global
Party
Guild
Staff
Direct / PM
System / announcement
```

Their recipient selection is not spatial AOI unless a specific channel says so.

Party membership belongs to Party. Guild membership belongs to Guild. Staff entitlement belongs to server/admin policy. Direct message target resolution belongs to Game/account/character identity policy.

Do not invent these domains inside `Game/Chat`.

## 4.4 Commands

Commands share text ingress but are not Chat facts.

Conceptually:

```text
raw submitted text
        ↓
command-prefix recognition
        ↓
trusted command caller context
        ↓
command registry/parser
        ↓
Game/server authority
        ↓
private command result/reply
```

A remote client may supply only text/submission identity. The server constructs caller context from authenticated/admitted state.

A future caller context may contain source, application session, participant, current controlled actor, account/character where authorized, future permissions, and a reply sink.

Do not let the remote client submit any of those as trusted values.

The command registry is not a Network service. The first implementation should be Game/server composition owned, instance/generation safe, and finite.

Do not use process-global static registration.

### First commands

C0.0 should prove commands with harmless operations that require no new permission framework:

```text
/help
    lists the first registered commands / help summaries

/where
    returns the caller's authoritative current world position
```

`/where` is preferable to a fake `/ping` because it proves command execution is using the trusted entered actor and current Game authority.

The response is private/system output to the caller. It is never spatial speech.

### Unknown / invalid command

Once the configured command prefix is recognized, `/help`, `/where`, `/not-a-command`, and malformed command syntax all remain command-path inputs.

Unknown, unauthorized, or invalid commands must not fall through and become Say text.

### Permissions

The active V3 Accounting model currently has no broad AccessLevel/RBAC system.

Do not block C0.0 by manufacturing one.

When the first privileged command is earned, add the smallest explicit permission contract required by that feature, with command source/caller identity and audit semantics.

Long-term command sources may include:

```text
PlayerChat
ServerConsole
VisualAdmin
Script / Mod
System
```

They should eventually converge on the same command dispatcher while retaining their source and trusted context.

That future convergence is not a reason to force console/admin infrastructure into C0.0.

---

# 5. Text validation and abuse policy

Chat text is remotely influenced input.

At minimum reject:

- empty/whitespace-only text;
- malformed Unicode / unpaired UTF-16 before encoding;
- encoded UTF-8 payload beyond the configured byte maximum;
- protocol lengths beyond canonical decode limits;
- invalid speech mode;
- stale/foreign submission lineage.

Prefer a byte limit over only a UTF-16 character count because wire cost is UTF-8.

Do not silently truncate.

A reasonable first current-game bound is **256 UTF-8 bytes** of message text.

C0.0 may remain packet-free and focus on semantics.

C0.1 remote ingress must add bounded per-session/per-speaker abuse control before expensive routing/fanout.

Rate policy must use bounded storage, be fenced by application-session lineage, distinguish rejection from accepted speech, avoid unbounded dictionaries under hostile reconnects, and use monotonic or authoritative elapsed time as appropriate rather than treating tick count as elapsed time.

Rate limiting happens before acceptance. It may reject a submission; it may not silently lose an already accepted fact.

---

# 6. Recipient semantics

For the first spatial-speech vertical:

- speaker must resolve to a live trusted actor;
- recipient must resolve to a live entered actor/client;
- speaker and recipient must be in the same spatial space;
- exact authoritative distance must satisfy the selected speech mode;
- sender receives their own fact.

Do not use camera distance.

Do not use the selected public replication profile as the hearing authority.

A recipient may hear speech even if presentation chooses not to realize an overhead bubble.

Future Game rules may add deafness, invisibility/hidden-speech policy, walls/occlusion, region language rules, ghosts, skills/languages, GM listening, and mute/squelch. None belong in C0.0 unless current gameplay already requires them.

## 6.1 Capacity

Speech fanout must have an explicit maximum recipient count.

If the true authoritative audience exceeds configured recipient capacity, do not publish a partial arbitrary audience as if it were semantically complete.

Preferred first behavior:

```text
preflight candidate/recipient capacity
    enough
        -> accept and publish complete fact

    insufficient
        -> reject speech with named capacity status
```

Later evidence may earn paging or another deterministic large-audience mechanism.

This is intentionally different from a global channel, whose policy may have a different bounded delivery shape.

---

# 7. Client semantics

## 7.1 ClientChatState

C0.1 should add one engine-neutral Client Core state for applied chat/speech facts.

Conceptually:

```text
ClientChatState
    bounded recent message/feed rows
    last applied semantic sequence / lineage
    duplicate/stale rejection
    append-only application
```

It is not authoritative and does not own Game recipient routing.

A duplicate applied fact is idempotent. Out-of-order facts obey the declared reliable application sequence.

Capacity behavior must be explicit. A bounded local feed may evict oldest already applied presentation rows. It may not pretend a never-applied fact was applied merely to make room.

## 7.2 Overhead speech

Overhead speech is a second presentation of the same accepted speech fact.

Conceptually:

```text
accepted speech fact
        ├── append to chat feed
        └── if speaker public anchor is currently renderable
                create/replace bounded overhead bubble
```

Bubble lifetime, fade, stacking, font, screen clamping, and maximum simultaneous bubbles are presentation policy.

Do not store bubble expiry in authoritative ECS.

If the speaker is not currently renderable, the chat-feed occurrence remains valid.

---

# 8. Persistence

C0.0/C0.1 do not require durable chat history.

Do not add Chat to save sets merely because Chat exists.

Durable concepts may later include moderation bans/mutes, server channel definitions if content-authored, player ignore/preferences if the game chooses server persistence, or product-specific history.

Transient live speech facts, client feed rows, application-proof debt, and overhead bubbles are not world-save state by default.

There are currently no external users/saves requiring backward compatibility during this development phase. Do not spend Chat checkpoints building historical migration for unreleased formats unless a concrete compatibility requirement is introduced.

---

# 9. Checkpoint plan

## C0.0 — packet-free authoritative speech + command oracle — ACCEPTED

C0.0a review hardening makes default submission results fail closed and treats
Admission rejection notifications as operation outcomes that must be reconciled
against the current authoritative session. A failed duplicate join therefore
cannot evict an existing entered speaker, while a real session rejection still
removes that authority.

Goal:

> Prove the Game semantics of text interpretation, trusted speaker authority, exact bounded spatial hearing, occurrence retention, and command interception before adding a wire message.

### Required implementation

Create a concrete `Game/Chat` domain/project or equivalent dependency-correct Game surface.

No reference from `Kernels/Network` to Chat.

Implement:

1. bounded text validation;
2. `Say`, `Whisper`, `Yell`;
3. explicit current-game speech-distance policy;
4. packet-free trusted speaker submission;
5. exact bounded spatial recipient discovery from current Game/world state;
6. server-created speech fact sequence/identity;
7. complete recipient result;
8. instance-owned finite command registry;
9. configured player command prefix;
10. `/help`;
11. `/where`;
12. command interception before speech;
13. private command result;
14. metrics/status sufficient to prove accepted/rejected/routed work;
15. current Chat documentation.

### C0.0 must not add

- Network messages;
- Network-kernel Chat types;
- client UI;
- Godot;
- party/guild/global/staff channels;
- PM;
- ignore;
- moderation;
- persistence/history;
- scripting/plugin API;
- RBAC/AccessLevel framework;
- generic AOI interface;
- generic event bus;
- NPC speech reaction framework;
- audio/voice chat;
- profanity filter;
- automatic language system.

### Required C0.0 tests

At minimum:

```text
plain text becomes Say

recognized /help is command and no speech fact occurs
recognized /where is private command result and no speech fact occurs
unknown prefixed text remains command failure and does not become speech

Whisper < Say < Yell audience shape

recipient exactly inside boundary hears
recipient exactly outside boundary does not
negative-coordinate routing
cross-chunk routing
speaker near chunk corner
speaker movement changes later audience from current authoritative position

no API allows caller/client-selected speaker position, sender name, range, or recipient list
stale/missing speaker rejects
speaker without authoritative position rejects

overlong UTF-8 rejects, does not truncate
empty text rejects
malformed Unicode rejects

recipient capacity failure is complete/transactional
no partial arbitrary fanout

two accepted speech messages remain two ordered facts
no latest-state coalescing

generation/world mismatch rejects
ordinary warmed speech discovery does not allocate if the chosen contract claims it
```

The packet-free oracle should make recipient sets and accepted fact sequences easy to compare in later remote tests.

### C0.0 stop

After tests and docs, STOP.

Do not add remote protocol in the same checkpoint.

Report:

- exact commit;
- files/projects added;
- speech distance defaults;
- text byte limit;
- candidate/recipient capacities;
- command prefix;
- packet-free semantic test count;
- warmed allocation result if measured;
- full repository verifier result.

---

## C0.1 — authenticated remote speech + command vertical

Begin only after C0.0 review.

Goal:

```text
Godot text input
    -> Client Core / Client Network
    -> authenticated Game protocol
    -> trusted entered actor
    -> C0.0 text interpreter
        -> command result OR speech fact
    -> Game Event delivery
    -> remote ClientChatState
    -> Godot chat feed + overhead speech
```

### Required remote ingress

Add a Game protocol submit message, not a Network-kernel packet.

The message should contain only information the client is allowed to choose, e.g.:

```text
ApplicationSessionEpoch
monotonic ChatSubmissionSequence
speech mode / input mode if needed
bounded raw text
```

Do not include trusted ParticipantId, Entity, CharacterId, SenderName, WorldPosition, range, recipient list, public identity, or permissions.

The server derives those.

C0.1 composition must explicitly size `MaximumParticipants` against the server's
admitted-session capacity. The C0.0 default of 256 is an oracle default, not a
production Chat CCU limit. `MaximumRecipientsPerSpeech` remains independently
bounded and may be smaller than total admitted population.

Duplicate/stale submission must not execute a command or create a speech fact twice.

### Required remote delivery

Use Game-owned application Event semantics.

The delivered speech message needs enough canonical metadata to append exact text to the client feed, identify speech kind, identify a speaker presentation anchor without exposing persistence identity as a shortcut, preserve occurrence order, and prove application before server knowledge advances.

Either extend/reuse the current retained application-event machinery honestly or add a narrow chat-specific applied proof. Do not replace application proof with transport ACK.

### Client/Godot

Implement:

- bounded `ClientChatState`;
- always-available chat feed;
- text input;
- Enter focus/submit behavior;
- command results as private/system rows;
- Say/Whisper/Yell rows;
- overhead bubble for a currently resident visible speaker;
- safe no-overhead fallback if speaker is not currently renderable.

### C0.1 hostile/recovery tests

At minimum:

```text
remote client cannot spoof speaker/name/position
duplicate submission does not duplicate command side effect/fact
stale application session rejects
foreign participant data cannot route
loss/dup/reorder converges
lost application proof recovers without inventing another occurrence
client apply failure does not advance server knowledge
reconnect does not trust client-local sequence identity
generation replacement fences old speaker/public lineage
overhead presentation absence does not erase feed fact
command output is private to caller
speech goes only to exact authoritative audience
```

No public-CCU/performance tournament yet.

---

## C0.2 — first routed chat semantics

Only when the game has a concrete reason.

Potential first additions:

- server/system announcement;
- one explicit global channel;
- direct message with stable identity resolution;
- bounded local client tab/filter state.

Do not manufacture Party/Guild ownership before Party/Guild exist.

---

## C0.3 — moderation / preferences / bounded history

Only after real channel usage.

Potential earned features:

- mute/squelch;
- server-side rate policy tuning;
- ignore;
- bounded server history where required;
- moderation audit;
- channel settings;
- staff monitoring.

Keep authority distinctions explicit:

```text
ignore
    usually recipient preference/filter

mute/squelch
    authoritative sender restriction

ban
    authoritative entitlement restriction

tab hidden
    presentation only
```

---

## C0.4 — command extensibility

Only when privileged/admin/scripted commands create real pressure.

Potential additions:

- explicit permission keys;
- command audit;
- console/visual-admin source convergence;
- script/mod registration;
- aliases;
- richer quoted/named argument parser;
- typed target resolution.

Do not recreate the V2 command framework speculatively.

---

# 10. Non-goals and traps

## Do not make Chat a Network subsystem

Bad:

```text
Kernels/Network
    ChatSubmitPacket
    ChatChannelId
    ChatMessageFlags
    guild recipient logic
```

Correct:

```text
Game/Chat
    semantics/routing/facts
        ↓
Game/Protocol
    canonical bounded messages
        ↓
Kernels/Network
    descriptor/lane/bytes
```

## Do not use public replication AOI as speech authority

The current game's public actor replication profile and local hearing both consume world position, but they answer different questions.

```text
public replication:
    what dynamic public state is disclosed/recovered?

speech:
    who hears this accepted occurrence?
```

Do not route Say by asking `ChunkPublication` which recipients currently have the speaker's chunk.

## Do not make client presentation history server truth

A user closing/reopening a chat tab must not alter the fact that speech occurred.

## Do not treat command output as speech

`/where` result belongs only to the caller unless the command explicitly performs a public Game action.

## Do not add universal callbacks

NPCs eventually need to hear/react to speech.

That does not justify a global speech event bus. A future AI/social system should consume explicit Game speech facts through an owned, bounded relationship earned by that feature.

## Do not silently normalize semantic content

Whitespace policy may trim outer whitespace if documented.

Do not silently truncate overlong text.

Do not transform unknown commands into speech.

Do not convert capacity failure into partial recipient delivery.

---

# 11. Definition of foundational Chat/Commands closure

The foundational pass is complete when UltraSim has proven:

```text
C0.0
    authoritative packet-free spatial speech + commands

C0.1
    authenticated remote speech/command vertical
    bounded Client Core state
    real Godot feed + overhead speech

then, if no gameplay pressure requires channels/moderation immediately:
    STOP
```

At that point Chat/Commands joins Inventory as a consumption-ready subsystem.

Party/guild/global/PM/moderation/history/script extensibility can be added later as real Game/server features without reopening Network ownership or the basic speech/command distinction.

---

# 12. Ready-to-give SOL instruction — C0.0

> Implement **Chat/Commands C0.0: packet-free authoritative speech + command oracle**.
>
> Start from current head after Inventory I0.11. Read `Docs/Plans/CHAT.md` (or this document) and the current Network/Game/World/Accounting contracts before changing code. Archive references are archaeology only; do not port their ownership.
>
> **Goal:** prove text interpretation, trusted speaker authority, exact bounded spatial hearing, retained occurrence semantics, and command interception entirely inside Game before adding any Network message or client UI.
>
> Create the smallest concrete `Game/Chat` domain/surface that fits the existing dependency graph. Chat is Game semantics. `Kernels/Network` must remain unaware of Chat, commands, channels, speaker identity, and hearing policy.
>
> Implement:
>
> - bounded canonical text validation; reject empty/malformed/over-limit input; do not silently truncate;
> - `Say`, `Whisper`, and `Yell`;
> - one explicit current-game speech-distance profile in integer millimeters; use provisional defaults Whisper=2,000 mm, Say=15,000 mm, Yell=30,000 mm unless current world-scale evidence gives a clearly better Game-only profile;
> - a packet-free trusted submission path. The remote-client shape does not exist yet. The caller supplies only a trusted speaker/actor resolved by Game composition; Chat reads current authoritative position and identity itself;
> - bounded recipient discovery using the existing `GameWorldState` / `WorldSpatialIndex`: enumerate intersecting chunks, copy candidates into preallocated scratch, then exact-distance filter using authoritative `WorldPosition`; do not scan the whole ECS population and do not create a generic AOI abstraction;
> - one server-created ordered speech fact identity/sequence. Every accepted speech occurrence remains independently meaningful; two messages do not coalesce;
> - complete-or-reject recipient capacity. Never publish a partial arbitrary audience as a successful speech fact;
> - one instance-owned finite command registry and one current-game player command prefix;
> - command parsing before speech. Once the prefix is recognized, unknown, invalid, or failed commands must never fall through into local speech;
> - `/help`;
> - `/where`, which returns the caller's authoritative current world position;
> - private command reply/result semantics;
> - bounded diagnostics/metrics useful for later remote acceptance.
>
> Do **not** implement in C0.0:
>
> - Game protocol messages or Network changes;
> - Client Core or Godot chat UI;
> - global/party/guild/staff channels;
> - PM/ignore/moderation/history;
> - scripting/plugin command APIs;
> - generalized permissions/RBAC/access levels;
> - generic event bus;
> - generic AOI/recipient interface;
> - NPC speech reaction framework;
> - profanity filtering or voice chat.
>
> Permanent tests must prove:
>
> - plain text becomes Say;
> - `/help` and `/where` execute as commands and create no speech;
> - unknown/invalid prefixed text remains command-path failure, never speech;
> - Whisper/Say/Yell produce the expected nested audience sets;
> - exact in/out range boundaries;
> - negative coordinates, cross-chunk and chunk-corner routing;
> - current authoritative speaker movement changes later audience;
> - stale/missing/no-position speaker rejection;
> - malformed/empty/over-limit UTF-8 rejection without truncation;
> - recipient-capacity rejection is transactional and publishes no partial fact;
> - two accepted messages remain two ordered facts;
> - world/generation mismatch rejects;
> - no caller/client-selected position, sender name, range, or recipient list exists in the authoritative API.
>
> Use existing V3 authority and boundedness conventions: expected rejection before mutation; unexpected failure after authoritative publication begins is an invariant/terminal fault, not a disguised domain rejection.
>
> Run the focused Chat tests plus the full canonical Debug+Release verifier.
>
> **STOP after C0.0.** Do not begin remote protocol/UI work in the same checkpoint.
>
> Report the commit, files/projects added, chosen speech distance profile, text byte limit, candidate/recipient capacities, command prefix, test count, warmed allocation result if measured, and full verifier result.
