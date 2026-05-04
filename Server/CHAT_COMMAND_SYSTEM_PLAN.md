# Chat And Command System Plan

## Goal

UltraSim should have one text input model that covers local speech, chat channels, and server commands.

The target feel is RunUO-like:

- press Enter to focus the input line
- submit plain text to speak locally
- local speech appears in the chat feed and as an overhead bubble
- command-prefixed input runs server commands
- the command prefix is a single server-owner configured string, such as `[`, `/`, `.`, `#`, or `command:`

The target UI grows toward a Knives Chat-style panel:

- bottom-left chat panel
- browser-like tabs
- each tab filters message categories independently
- channels can have their own colors, display names, history, and visibility rules

## Core Boundary

Commands and chat are related, but they are not the same service.

The shared path is command execution:

- chat input can dispatch a command
- server console can dispatch a command
- visual wrappers can dispatch a command
- scripts/plugins can register and dispatch commands

All command sources should route through the same registry, parser, permission checks, result model, and audit surface.

The source stays explicit:

- `ChatInput`
- `ServerConsole`
- `VisualWrapper`
- `Script`
- `System`

This lets commands enforce source-specific rules when needed. A visual wrapper should not fake a chat message. It should create a command request that carries its source, access level, optional session/entity context, and optional target context.

## Input Interpretation

Server-side interpretation order:

1. If input starts with a configured command prefix, dispatch as a command.
2. Else if input starts with a configured channel alias, route to that channel.
3. Else route as default local speech.

The live server should advertise and use one gameplay/chat command prefix. The registry still supports multiple prefixes internally for console/specialized tooling, but normal chat command dispatch should not expose multiple competing declaration symbols. Script command registrations are normalized onto the server's configured chat prefix at runtime.

Channel aliases should usually be command handlers:

- `/p hello` routes to party chat
- `/g hello` routes to guild chat
- `/w name hello` routes to whisper
- plain `hello` routes to local speech

This keeps command parsing and permission handling in one place.

## Server Components

Planned modules:

- `ServerCommandRegistry`
  - source-neutral command registration and dispatch
  - string command prefixes for low-level dispatch
  - quoted argument parsing
  - `key=value` named argument extraction
  - access checks
  - reply callback
- `ServerAccessRuleRegistry`
  - named runtime rules with display text and descriptions
  - grant/deny permission keys such as `command.spawn` or `admin.entity.inspect`
  - wildcard permission families such as `command.*`
  - direct subject grants/denies for account-, session-, or entity-scoped overrides
  - legacy integer access-level fallback while current command paths are migrated
- `ServerChatService`
  - validates submitted chat
  - rate limits
  - routes local/channel/system messages
  - emits chat message events through the ordered event stream
  - owns chat metrics
- `ChatChannelRegistry`
  - channel id/name/aliases
  - recipient provider
  - default display settings
- `IChatRecipientProvider`
  - local AOI recipients
  - party recipients
  - guild recipients
  - staff/system recipients
- `IChatCommandModule`
  - script/plugin registration hook for commands

## Script And UI Boundary

The script compiler does not make the core chat transport a script module. The
following pieces should stay in core/server glue for now:

- `ChatSubmitPacket` / `ChatMessageEventPacket`
- `ServerChatService` validation, rate limits, and reliable-event publishing
- client chat event bridge and overhead-chat projection
- the always-available client chat input panel

Scripted content belongs around the edges:

- commands registered through `IUltraSimScriptContext.Commands`
- future channel/routing rule providers
- future chat settings/admin panels
- optional shard-specific command modules under `RuntimeTemplate/scripts/Commands`

The default `help`, `ping`, and `announce` bootstrap commands can remain core
until script command facades can express every required behavior cleanly. In
particular, `announce` needs a first-class chat/system announcement facade
before it should move into runtime scripts.

Current command-prefix policy:

- `ServerMovementWorldApplicationOptions.CommandPrefix` is the server-owned chat command declaration string.
- `ServerChatCommandBootstrap` registers `help` and `ping` under that one prefix.
- script commands and built-in script helper commands such as `panel`/`spawn` are normalized onto that same prefix when bound into the live movement host.
- unprefixed console-only commands can still exist for operator shell flows, but they are not chat command declaration symbols.

The visible chat panel should also remain a local client surface for now, not a
server-authored schema panel. Chat needs persistent local history, append-only
message delivery, keyboard focus capture/release, and direct submit packets even
when no server menu is open. Server-driven schema panels are a better fit for
chat configuration, moderation tools, channel browsers, or diagnostics than for
the hot chat input/feed itself.

## Network Shape

Client to server:

- `ChatSubmitPacket`
- plane: `Input`
- class: `ReliableEvent`
- channel: `ReliableOrdered`
- payload: raw submitted text

Server to client:

- `ChatMessageEventPayload`
- carried as a payload inside the existing ordered event stream
- delivered through the client ordered-event bridge

The reliable ordered event lane is appropriate for server chat delivery because chat is low volume and should not silently disappear.

## Message Metadata

A delivered chat message should carry metadata, not just a string:

- message id
- channel id
- sender entity id
- sender display name
- text
- server tick
- flags

Flags include:

- `System`
- `Local`
- `Party`
- `Guild`
- `Whisper`
- `Staff`
- `Error`
- `Overhead`
- `Announcement`

Tabs are client display filters over these fields. Channels are server routing concepts; tabs are client presentation concepts.

## First Implementation Slice

The first useful slice should be intentionally small:

- command registry with string prefixes
- shared request/source/result model
- `help`, `ping`, and `announce` style registration support
- chat submit packet
- chat message event payload
- tests for command parsing/dispatch and packet round trips

The next slice can wire `ServerChatService` into `ServerMovementSessionHost`, add harness bot chat, and rerun the single-family parity ramp with chat enabled.
