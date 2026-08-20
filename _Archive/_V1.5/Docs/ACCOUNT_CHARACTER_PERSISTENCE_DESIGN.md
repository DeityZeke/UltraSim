# Account, Character, and Persistence Design

Purpose: full design specification for the server-side identity, account management, character
selection, and persistence systems. Covers architecture decisions, data models, save/load design,
ECS integration, session lifecycle, and how account/character flows integrate with the
server-driven UI panel system.

Related docs:
- `Docs/MINIMUM_CLIENT_SERVER_PLATFORM_CHECKLIST.md` §4 — milestone tracking
- `Docs/SERVER_RUNTIME_ARCHITECTURE_AND_ROADMAP.md` — ServerRuntime lifecycle
- `Docs/UI_SCHEMA_AND_PREFAB_PROTOCOL_SPEC.md` — server-driven panel protocol
- `Docs/UI_SUBSYSTEM_PACKAGE_AND_IMPLEMENTATION_PLAN.md` — UI subsystem layout
- `Docs/ECS_Archetypes_Prefabs_Scripts.md` — ECS component/archetype conventions

Implementation home:
- `UltraSim/Server/Engines/Accounting/` - server-side engine (models, services, system, current player-name component)
- `UltraSim/Server/Gameplay/` - live session/world bind path
- `Client.Godot/` + `UltraSim/Client/Engines/Networking/` - client/runtime consumption of account-backed character identity

There is no `UltraSim/Network/Protocol/Accounting/` folder for most flows.
Login uses packet-bound panel actions (`LoginRequestPacket` 0xE8 / `LoginResponsePacket` 0xE9)
rather than plain `UiPanelResponse` — credentials must not travel as untyped panel payloads.
Character flows use the standard `UiPanelResponse` path. See §6 and §8.

---

## Implementation Status (as of 2026-04-06)

| Phase | Description | Status |
|-------|-------------|--------|
| Phase 1 | Data layer — models, storage, registry, system | ✅ Complete |
| Phase 2 (login) | `ServerAccountService.TryLogin`, `LoginPanel`, packet-bound auth | ✅ Complete |
| Phase 2 (characters) | character select/create/delete schema flow | ✅ Complete |
| Phase 2 (session states) | Authenticated → CharacterSelect/Create → Ready → InWorld; `BindSession` timing | ✅ Complete |
| Phase 3 | `PlayerNameComponent`, `PlayerCharacterComponent`, character-backed bind payload, world persistence infrastructure | Partial |
| Phase 4 | Character position persistence on disconnect/restart | Partial |

Key deviation from original design:
- Login submit uses `LoginRequestPacket` (0xE8) as a packet-bound intercept on the login panel
  action, not a plain `UiPanelResponse`. The credentials (passwordHash + challengeResponse) must
  never travel as untyped panel payload bytes. The `GodotUiCommandConsumer` packet-binding
  mechanism intercepts the action before it reaches the normal response path.
- `BindSession` now occurs only after the full login → character select/create → `Ready` flow
  completes. Raw transport connect no longer implies world entry.
- Client-bundled UI should stay limited to `MainMenu`, `ServerSelect`, `Settings`, and the
  generic `MessageBox`. Everything else in the account/character flow should be schema UI
  streamed from the server.
- Character select/create are now schema UI panels. Repeater support exists in the schema system
  for repeated character-entry rows, and delete confirmation is also schema-driven.
- `Quit to Character Select` is now implemented.
- The selected/created character name now follows the ready-state spawn path into:
  - `PlayerNameComponent`
  - client world nameplates
  - local chat sender labels
- World persistence infrastructure is now implemented in the tracked ECS/server paths:
  - explicit `PersistenceId`-based type registry
  - `/Saves/meta.bin`, `/Saves/types.tdb`, `/Saves/Entities/entities.idx`, `/Saves/Entities/entities.bin`, `/Saves/Systems/<System>.bin`
  - entity save/load, direct archetype create/remap, hydration, and entity-reference fixups
  - server-owned `WorldPersistenceCoordinator`
  - staged save write + promote/replace flow for live save roots
  - host-driven recovery interaction seam with configured fallback recovery modes
  - save-on-stop and interval autosave support in `ServerRuntime`
- `PlayerNameComponent` and `PlayerCharacterComponent` are now registered as real persistent server component types.
- Remaining QoL follow-up for this flow: persistence/settings integration for the
  "skip delete warning" preference.
- Live character-backed player entities now participate in the persistence contract:
  - persistent player entities carry `PersistentTag`
  - `PlayerMarker`, `PlayerState`, `PrefabIdComponent`, `PlayerNameComponent`, and
    `PlayerCharacterComponent` now have a restart-safe persistence path
  - restart load can rebind a reconnecting session to the loaded entity via `CharacterId`
- Character-backed persistent player entities now survive normal unbind/logout in memory:
  - unbind clears transient connection/input state instead of destroying the entity
  - reconnect can rebind to the parked entity by `CharacterId` without requiring an intervening save/load
- `CharacterModel` now carries last-known position/yaw snapshot fields as an offline fallback:
  - runtime save updates the character record from the live player entity state
  - fresh respawn can now reuse the stored snapshot when no loaded persistent entity is present
  - tracked host/runtime now wires account-store load/save for that fallback path
- Save now compacts offline parked persistent player entities after their snapshot is captured:
  - unbind without save still preserves fast same-process rebind
  - once saved, offline characters fall back to account-backed snapshot respawn instead of indefinite parked-entity retention
- The current player persistence policy is now explicit:
  - if the character is still live or parked, `CharacterId` rebind resolves the existing entity
  - if the character was compacted on save, respawn falls back to the durable `CharacterModel` snapshot
- Persistence validation/measurement now includes:
  - a full-stack runtime integration test covering account + world persistence together
  - a dedicated `tests/Server.PersistencePerf/` harness with phase timing, allocation, GC, and file-size reporting
  - a live-load `tests/Server.NetworkPersistencePerf/` harness that now:
    - pumps real transport connection events, inbound datagrams, and outbound flushes
    - auto-promotes harness clients into ready sessions so connections produce active bots instead of idle sockets
    - measures before/during/after tick windows plus async save barrier and background completion timing
- Current save architecture is now snapshot-based and partially asynchronous.
  - `RequestSave()` captures account + world snapshots at the end-of-tick barrier
  - account/world writes then continue on a background worker path
  - the hot-path number to watch is now barrier time, not full write completion time
  - explicit/manual `ServerRuntime.Save()` still exists and still completes synchronously when called directly
- Remaining gameplay persistence gap: broader offline retention policy is still intentionally minimal.
  - save-time compaction and snapshot fallback are implemented
  - time-based retention or more advanced offline-character policies are still future work if needed
- Remaining non-blocking UX follow-up:
  - save/autosave warning + completion notifications should get a polish pass later
  - that work should be folded into the planned chat rework/polish pass so player-visible messaging lands once in the final chat UX

---

## 1) Architecture Overview

### 1.1 Not an ECS simulation subsystem — a managed service engine

Account and character data is NOT primarily stored in ECS entities/components. ECS components require
`unmanaged` structs (no strings, no arrays). Account data is persistent, cold-read, and
fundamentally a service concern - not a per-tick simulation concern.

Live session/world identity is the exception:
- the currently selected character name is now projected into the live player entity via `PlayerNameComponent`
- this exists so world/chat presentation can use the selected character identity without dragging the full account model into ECS

The accounting engine is structured as:

```
ServerAccountRegistry      — in-memory store (sealed records, volatile array)
ServerAccountService       — event-driven operations (login, IP check, ban, create)
ServerCharacterService     — character operations (create, select, delete, auto-join)
ServerAccountSystem        — SystemBase scheduler wrapper (periodic flush + save hook)
IAccountStore              — persistence abstraction
  BinaryAccountStore       — AES-256-GCM encrypted binary (default)
  JsonAccountStore         — plaintext JSON fallback (operator opt-in)
AccountStoreRegistry       — named profile registry (dynamic; custom backends register here)
```

### 1.2 Why ServerAccountSystem extends SystemBase

`SystemBase` is a scheduler contract, not strictly an ECS entities/components contract. Using it
for the account engine gives:

- Tick cadence management via `PeriodTicks` — no `_worldTick % 30 == 0` scattered elsewhere
- Stage placement in the pipeline (`Cleanup` stage, after simulation)
- Empty read/write component masks — never conflicts with any other system
- `Serialize(IDataWriter)` / `Deserialize(IDataReader)` hooks for world save/load
- Shows up in system diagnostics alongside all other systems

The system is thin — it holds references to the registry and store and drives the flush cadence.
All real logic lives in the service and registry classes.

### 1.3 Folder location

`UltraSim/Server/Engines/Accounting/` — mirrors the existing pattern:
- `UltraSim/Server/Engines/Networking/` — ECS/network bridge
- `UltraSim/Server/Engines/Chunking/` — spatial partitioning

---

## 2) Server-Owner Configuration (`ServerAccountOptions`)

```csharp
public sealed record ServerAccountOptions(
    // -1 or 0 = unlimited; 1+ = hard cap per remote IP
    int AccountsPerIp = 1,

    // 1–16; fits in nibble range 0x0–0xF
    int MaxCharactersPerAccount = 5,

    // If true, MaxCharactersPerAccount == 1, and account has exactly 1 character:
    // skip the character select panel and enter the world directly.
    bool AutoJoinSingleCharacter = false,

    // If false, the delete button is hidden/disabled in character select.
    bool AllowCharacterDeletion = true,

    // Name of the registered storage backend. Must match a name in AccountStoreRegistry.
    // Default = "EncryptedBinary" (AES-256-GCM). Custom backends register at startup.
    string StorageProfile = "EncryptedBinary",

    // Rolling backup slot count.
    //  -1 = infinite (never deleted; slots increment forever)
    //   0 = disabled (live file overwritten atomically; no backups)
    //  N  = fixed ring of N slots (oldest beyond N dropped on each save; default 2)
    int BackupCount = 2,

    // Path for account data files, relative to server data directory.
    string AccountDataPath = "accounts"
)
```

### 2.1 Storage profile registry

Instead of a format enum, `AccountStoreRegistry` maps names to factory lambdas.
Built-in names: `"EncryptedBinary"`, `"Json"`. Constants on `AccountStorageFormat` alias these.

Custom stores register at startup:
```csharp
AccountStoreRegistry.Register(new AccountStoreProfile(
    "MySqlite", "SQLite-backed store",
    (dir, backups) => new SqliteAccountStore(dir, backups)));

new ServerAccountOptions(StorageProfile: "MySqlite")
```

### 2.2 Auto-join rules

```
Login OK (or skipped entirely — server choice)
  └─ account.CharacterCount == 0
       └─ → Character Create panel  (ALWAYS, regardless of AutoJoinSingleCharacter)
  └─ account.CharacterCount > 0
       └─ AutoJoinSingleCharacter == true AND MaxCharactersPerAccount == 1
            └─ → BindSession (spawn entity; zero friction)
       └─ else
            └─ Character Select panel
                 ├─ Pick existing character → BindSession
                 ├─ Pick empty slot (available if count < max) → Character Create panel
                 └─ Delete character (if AllowCharacterDeletion == true)
```

Combining `AutoJoinSingleCharacter = true` + `AllowCharacterDeletion = false` +
`MaxCharactersPerAccount = 1` effectively gives a "permanent single character" server mode.

---

## 3) Data Models

Both models are `sealed record` — immutable value objects. Mutation always produces a new record.
This is required for the lock-free snapshot save pattern (see §5).

### 3.1 AccountModel

```csharp
public sealed record AccountModel(
    ulong AccountId,                      // stable uint64, auto-increment, never reused
    string Username,
    string PasswordHash,                  // SHA-256(username + ":" + password + ":" + salt)
    string PasswordSalt,                  // per-account random salt
    ulong[] CharacterSlots,               // length = MaxCharactersPerAccount; 0 = empty slot
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastLoginUtc,
    string[] KnownIps,                    // last ~4 IPs for audit
    bool IsAdmin,
    bool IsBanned,
    string- BanReason
)
```

Password is never stored plaintext. SHA-256 with a per-account salt is applied on registration
and on each login attempt. A per-login challenge nonce is issued by the server (carried in the
login panel payload — see §8.2) so the wire never carries a bare hash. The client sends
`SHA256(storedHash + ":" + challenge)` in the panel response, preventing replay attacks.

### 3.2 CharacterModel

```csharp
public sealed record CharacterModel(
    ulong CharacterId,                    // stable uint64, auto-increment, never reused
    ulong AccountId,                      // back-reference to owning account
    byte SlotIndex,                       // 0-15
    string Name,
    ushort ArchetypeId,                   // resolves to PlayerArchetypeDefinition
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastPlayedUtc,
    float X,
    float Y,
    float Z,
    float YawRadians
)
```

`CharacterId` is the stable persistent identity. The ECS entity handle is transient (dies on
disconnect). `CharacterId` survives restarts and is stored on the entity via `PlayerCharacterComponent`.

### 3.3 Identity layering

These identities must stay separate:

- `AccountId` - durable account identity
- `CharacterId` - durable playable identity
- `sessionId` - runtime session identity
- `connectionId` / `OwnerConnectionId` - transient live transport attachment only

Rules:
- entity lookup and rebind use `CharacterId`
- account auth resolves an account first, then a character
- `OwnerConnectionId` is never a persistence key
- IP is never a persistence key

### 3.4 IpBanEntry

```csharp
public sealed record IpBanEntry(
    string Ip,
    string Reason,
    DateTimeOffset BannedUtc,
    DateTimeOffset- ExpiryUtc    // null = permanent
)
```

IP bans are stored alongside accounts and characters. They are checked before any account lookup
on each connection. `ExpiryUtc == null` means permanent; timed bans auto-expire on next check.

---

## 4) ECS Components (`UltraSim/Server/Engines/Accounting/Components/`)

These are ECS components that live on the player entity at runtime.

### 4.1 PlayerNameComponent

`[InlineArray(32)]` char buffer — no heap allocation, satisfies unmanaged constraint. Holds the
base canonical display name. Future `PlayerTitleComponent` (title catalog lookup) will extend
display without touching this component.

### 4.2 PlayerCharacterComponent

```csharp
public struct PlayerCharacterComponent : ISerializableComponent<PlayerCharacterComponent>
{
    public ulong CharacterId;   // stable persistent identity
    public byte SlotIndex;      // 0–15
}
```

Allows any system to reference the persistent character identity from the entity.

### 4.3 Updated player entity signature

Before: `[PlayerMarker, PlayerState, PrefabIdComponent]`

After (character-backed persistent player path):
`[PlayerMarker, PlayerState, PrefabIdComponent, PersistentTag, PlayerNameComponent, PlayerCharacterComponent]`

---

## 5) Persistence Design

### 5.1 The snapshot save pattern

`AccountModel`, `CharacterModel`, and `IpBanEntry` are `sealed record` — immutable.
The registry holds a `volatile` reference to the current arrays.

```
Save path (no lock needed):
  1. var snapshot = Volatile.Read(ref _accounts)   // O(1) ref copy
  2. Task.Run(() => _store.WriteAsync(snapshot))    // background; reads immutable records
  3. Main thread can mutate (new records) while save runs
```

No lock is needed because the background task holds the old immutable array reference and the
main thread creates entirely new records/arrays.

Current runtime note:
- the registry/store layer supports immutable snapshot semantics
- the tracked runtime save path now captures account + world snapshots at the end-of-tick barrier
- account/world file writes then run in the background from those snapshots
- autosave no longer blocks on full file IO, but it still pays the barrier capture cost on the runtime thread
- direct/manual `ServerRuntime.Save()` remains synchronous when called explicitly

### 5.2 The two-file split (RunUO .idx/.bin analogy)

| File | Analogy | Content |
|------|---------|---------|
| World save output via `system.Serialize(worldWriter)` | `.idx` / manifest | Account + character + ban counts for integrity cross-check |
| `accounts.dat` / `accounts.json` | `.bin` | The actual account, character, and IP ban data |

The account data file is self-describing — each record has a version byte and length prefix baked
in. The world save manifest is an integrity cross-check, not a required index. The account file
can be loaded cold without world save context.

### 5.3 Binary format (EncryptedBinary mode)

```
accounts.dat layout:
  [4 bytes]   magic "ACCT"
  [1 byte]    file format version
  [12 bytes]  AES-256-GCM nonce (random per save)
  [4 bytes]   ciphertext length
  [N bytes]   AES-256-GCM ciphertext of plaintext payload
  [16 bytes]  AES-256-GCM authentication tag

Plaintext payload:
  [4 bytes]   account record count
  per account:  [1 byte schema version][4 bytes length][N bytes BitWriter payload]
  [4 bytes]   character record count
  per character: [1 byte schema version][4 bytes length][N bytes BitWriter payload]
  [4 bytes]   IP ban record count
  per ban:    [1 byte schema version][4 bytes length][N bytes BitWriter payload]
```

The IP ban section is optional — older files without it load cleanly (detected by remaining bytes).

Key: `server.key` — 32 random bytes, generated on first boot, stored separately from `accounts.dat`.
Loss of `server.key` = accounts are unrecoverable (intentional).

### 5.4 Backup rotation

On each save, before the atomic `.tmp` rename, backups rotate:

```
accounts.dat      — live (newest)
accounts.dat.1    — one save ago
accounts.dat.2    — two saves ago  (with BackupCount=2)
```

Controlled by `ServerAccountOptions.BackupCount`:
- `-1` = infinite (shift all slots up indefinitely; nothing deleted)
- `0`  = disabled (live file overwritten atomically only)
- `N`  = fixed ring of N slots (oldest beyond N dropped on each save)

JSON mode rotates all three files (`accounts.json`, `characters.json`, `ip_bans.json`) identically.

### 5.5 Schema versioning

Per-record version byte allows forward-compatible loading: records with an unknown future version
are skipped (length-prefixed, so the reader can advance past them). When breaking changes are
needed, increment the model's `SchemaVersion` constant.

### 5.6 Validation and performance coverage

Tracked validation now includes:

- full-stack runtime persistence integration coverage
  - account store + world save-set written through the real runtime hook composition
  - restart/load verification for account data, entity files, and `CharacterId` rebind
- dedicated persistence performance coverage under `tests/Server.PersistencePerf/`
  - players-only, items-only, and mixed scenarios
  - save/load timing, allocation, GC, and file-size output
  - coordinator phase timing for:
    - world save: structural flush, type db, systems, entities, manifest, commit
    - world load: manifest read, type validation, systems, create, hydrate, fixups
- dedicated live-load persistence coverage under `tests/Server.NetworkPersistencePerf/`
  - real TCP connections
  - ready-session auto-promotion for harness bots
  - active movement input during the save window
  - before/during/after tick windows plus async save barrier and completion timing

Representative current baseline (`--iterations 3`, Release perf harness):

- players `1000`
  - save avg `5.491 ms`
  - load avg `5.966 ms`
- items `100000`
  - save avg `28.889 ms`
  - load avg `12.629 ms`
- mixed `1000 players + 100000 items`
  - save avg `42.941 ms`
  - load avg `16.062 ms`
  - save p95 `54.627 ms`
  - load p95 `19.191 ms`

Representative live-load runs (`tests/Server.NetworkPersistencePerf/`, Release):

- `100 players + 1000 items + 50 active bots`
  - before tick avg/p95 `0.469 / 0.729 ms`
  - save barrier `10.594 ms`
  - save completion `66.024 ms`
  - after tick avg/p95 `0.432 / 0.604 ms`
- `1000 players + 100000 items + 100 active bots`
  - before tick avg/p95 `1.540 / 1.946 ms`
  - save barrier `12.496 ms`
  - save completion `89.610 ms`
  - background write `32.017 ms`
  - after tick avg/p95 `1.447 / 1.836 ms`
- `1000 players + 100000 items + 250 active bots`
  - before tick avg/p95 `17.150 / 70.694 ms`
  - save barrier `21.727 ms`
  - save completion `90.266 ms`
  - after tick avg/p95 `10.252 / 14.403 ms`

Current measured scaling shape:

- save completion cost is still dominated by world entity write time
- save barrier cost is now materially lower than full completion cost because writes run from snapshots in the background
- load cost is dominated by hydrate time, then entity create time
- fixup cost is negligible in the current player/item scenarios
- large mixed scenarios show some GC pressure, but not a catastrophic spike pattern yet

---

## 6) Session Flow - Server Defines It, Not the Protocol

### 6.1 Core principle

The accounting and character systems do NOT define one hardcoded MMO flow.
The server decides which pre-game steps exist and what screens are shown.

Login is the one packet-bound exception:
- `LoginRequestPacket` / `LoginResponsePacket` are typed packets used for credential submit and
  retry feedback only.

Everything else in the standard account/character flow is server-driven UI using the existing
schema/panel system. The server opens schema panels, receives generic `UiPanelResponse` actions,
calls the account/character service methods, and decides what to show next.

The server defines the flow. The client follows it.

Examples of valid flows:
```
// Standard MMO:
TransportConnected
  -> IP gate / ban check
  -> SessionAccepted
  -> server opens LoginPanel (schema; challenge in payload)
  -> LoginRequestPacket arrives
  -> AccountVerified
  -> if account has no characters: open CharacterCreatePanel (schema)
  -> else open CharacterSelectPanel (schema)
  -> CharacterSelected or CharacterCreated
  -> Sessions.TryMarkReady()
  -> BindSession(sessionId, characterModel)

// No-auth server:
TransportConnected
  -> IP gate / ban check
  -> SessionAccepted
  -> Sessions.TryMarkReady()
  -> BindSession(sessionId, null)

// Auth-only server:
TransportConnected
  -> IP gate / ban check
  -> SessionAccepted
  -> server opens LoginPanel
  -> LoginRequestPacket arrives
  -> AccountVerified
  -> Sessions.TryMarkReady()
  -> BindSession(sessionId, null)
```

### 6.2 BindSession timing

Currently `BindSession()` is called immediately on session creation (before any UI flow).
This must change: `BindSession()` is called only when the chosen pre-game flow is complete and
the session is marked `Ready`. Until then, the session remains in a pre-world state and only
login/panel traffic is accepted. Gameplay packets from pre-world sessions are rejected.

### 6.3 Optional `ServerSessionFlowService`

`ServerSessionFlowService` is an optional, opt-in service that implements the standard MMO flow
(IP gate -> login -> character select/create -> in world) on top of the panel protocol. Servers that
want this behavior register it; servers with custom flows write their own panel response handlers
and call account/character service methods directly.

`ServerAccountService` and `ServerCharacterService` are always available as pure business
logic regardless of which flow is used.

### 6.4 Session states (additions to NetworkSessionManager)

```
Handshaking
  -> ConnectedPreGame  - transport accepted, IP gate passed
       -> Authenticated   - account verified (if the flow uses auth)
            -> Ready        - flow complete; BindSession() called here; gameplay active
```

The exact intermediate state names can change, but the architectural rule is fixed:
transport acceptance is not the same thing as entering the world. `Ready` is what triggers
`BindSession()` and gameplay packet acceptance.

---

## 7) Service Layer

### 7.1 ServerAccountService

Business logic behind login and account management. Stateless — all state lives in the registry.

Methods:
- `IssueLoginChallenge(sessionId)` → nonce (for login panel payload)
- `TryLogin(sessionId, username, challengeResponse, remoteIp)` → LoginResult
- `TryCreateAccount(username, password, remoteIp)` → AccountModel or rejection reason
- `IsIpAllowed(remoteIp)` → false if banned or at AccountsPerIp limit

### 7.2 ServerCharacterService

Business logic behind character CRUD. Stateless.

Methods:
- `TrySelectCharacter(account, characterId)` → CharacterModel or rejection
- `TryCreateCharacter(account, name, archetypeId)` → CharacterModel or rejection
- `TryDeleteCharacter(account, characterId)` → success or rejection
- `GetAutoJoinCharacter(account)` → CharacterModel- (null if auto-join conditions not met)

---

## 8) Built-In Panel Types and Action Model

The client ships with definitions for the following panels as warm cache entries.
They are referenced by stable `ushort` IDs. Servers open them via
`ServerUiPanelService.OpenPanel(sessionId, BuiltinPanelTypes.Login, payload)`.
Servers with custom flows open different panels, or no pre-game panels at all.

```
BuiltinPanelTypes:
  Login               0x0100  — username/password fields; challenge nonce in payload
  CharacterSelect     0x0101  — slot list with name, archetype, last played
  CharacterCreate     0x0102  — name field + archetype picker
  CharacterDelete     0x0103  — confirmation dialog
```

### 8.1 Three-tier action model

Not all panel actions route through `UiPanelResponse`. The accounting flow uses all three tiers:

| Tier | Submit path | Used by |
|------|-------------|---------|
| Generic | `UiPanelResponse` | CharacterSelect, CharacterCreate, CharacterDelete |
| Packet-bound | Named `IPacket<T>` → server handler → event/callback | Login (security-sensitive) |
| Future: scripted | Server-streamed packet type (asset-streaming phase) | Custom server flows |

See `Docs/UI_SCHEMA_AND_PREFAB_PROTOCOL_SPEC.md §8.7` for the full design rule and rationale.

### 8.2 Login panel — packet-bound action

The `LoginPanel` uses a packet binding for its primary submit action.

**Why:** Login credentials are security-sensitive. Routing through a named `LoginRequestPacket`
gives the full packet pipeline — rate limiting, typed validation, replay protection — rather
than a generic payload blob. Critically, a server streaming a custom panel definition cannot
override this binding. The binding is compiled into the client's built-in login panel
implementation and cannot be reached by server-provided schema data.

**Server → client payload (`OpenPanel`):**
```
[16 bytes] challenge nonce (single-use; new nonce issued after every attempt)
[1 byte]   failure count (0 on first open; incremented on retry)
[1 byte]   failure reason (0=none, 1=bad credentials, 2=banned, 3=IP limit)
```

**Client behavior on submit:**
1. Reads challenge from payload
2. Computes `SHA256(SHA256(username + ":" + password + ":" + salt) + ":" + challenge)`
3. Fires `LoginRequestPacket(username, challengeResponse)` — NOT a `UiPanelResponse`
4. Panel stays open (loading state) until server closes it

The server never receives a plaintext password or a bare `SHA256(password)`.

**Server-side flow:**
```
LoginRequestPacket arrives
  → ServerAccountService.TryLogin()
  → success: close LoginPanel, fire LoginSucceeded(sessionId, account)
  → failure: issue new challenge, re-open LoginPanel with failure reason
```

The flow service subscribes to `LoginSucceeded`/`LoginFailed` events and opens the next panel.

### 8.3 CharacterSelect panel — generic UiPanelResponse

**Server → client payload:**
```
[1 byte]   slot count (= MaxCharactersPerAccount)
per slot:
  [1 byte]   slotIndex
  [1 byte]   occupied (0 = empty)
  if occupied:
    [8 bytes]  characterId
    [1 byte]   nameLength
    [N bytes]  name (UTF-8)
    [2 bytes]  archetypeId
    [8 bytes]  lastPlayedUtcMs
[1 byte]   allowDeletion (0/1)
```

**Client → server (UiPanelResponse payload):**
```
[1 byte]   action (0=select, 1=createInSlot, 2=delete)
[8 bytes]  characterId (select/delete) or 0 (create)
[1 byte]   slotIndex (create only)
```

### 8.4 CharacterCreate panel — generic UiPanelResponse

**Client → server (UiPanelResponse payload):**
```
[1 byte]   nameLength
[N bytes]  name (UTF-8)
[2 bytes]  archetypeId
```

---

## 9) File Structure (Current)

```
UltraSim/Server/Engines/Accounting/
    Models/
        AccountModel.cs
        CharacterModel.cs
        IpBanEntry.cs
        AccountStorageFormat.cs        // static string constants (aliases for registry names)
    Components/
        PlayerNameComponent.cs         // InlineArray(32) char buffer, ISerializableComponent
        PlayerCharacterComponent.cs    // ulong CharacterId + byte SlotIndex
    Storage/
        IAccountStore.cs
        AccountStoreProfile.cs         // named factory descriptor
        AccountStoreRegistry.cs        // dynamic profile registry (custom stores register here)
        BinaryAccountStore.cs          // AES-256-GCM + BitWriter/BitReader + backup rotation
        JsonAccountStore.cs            // plaintext JSON + backup rotation
    Services/
        ServerAccountOptions.cs
        ServerAccountService.cs        // Phase 2 — login, IP check, ban, create account
        ServerCharacterService.cs      // Phase 2 — create/select/delete character, auto-join logic
    ServerAccountRegistry.cs           // volatile array swap + dirty tracking + flush
    ServerAccountSystem.cs             // SystemBase: periodic flush + Serialize/Deserialize
```

Note: `ServerAccountRegistry` sits at the Accounting root, not under Services, because it is
the shared data store that both service classes depend on.

---

## 10) Phase Sequencing

### Phase 1 — Data layer ✅ Complete

- `AccountModel`, `CharacterModel`, `IpBanEntry`
- `AccountStorageFormat` (static string constants), `AccountStoreProfile`, `AccountStoreRegistry`
- `ServerAccountOptions` (StorageProfile string, BackupCount)
- `IAccountStore`, `BinaryAccountStore` (AES-256-GCM, backup rotation), `JsonAccountStore`
- `ServerAccountRegistry` (volatile array, dirty tracking, IP bans, flush)
- `ServerAccountSystem` (SystemBase wrapper, Serialize/Deserialize manifest)
- `PlayerNameComponent`, `PlayerCharacterComponent`
- ECS persistence layer: `IDataWriter`, `IDataReader`, `SystemBase.Serialize/Deserialize`

### Phase 2 — Service layer + optional standard flow

- `ServerAccountService` (TryLogin, IssueLoginChallenge, TryCreateAccount, IsIpAllowed)
- `ServerCharacterService` (TrySelectCharacter, TryCreateCharacter, TryDeleteCharacter, GetAutoJoinCharacter)
- Session state machine additions for the pre-game flow; `BindSession()` deferred until
  `TryMarkReady()`
- `ServerSessionFlowService` (optional, opt-in): implements the standard flow on top of
  panel opens/responses; servers with custom flows skip this and wire panel handlers directly
- schema definitions and payload codecs for Login, CharacterSelect, CharacterCreate, CharacterDelete
- `ServerEventHub` gains pre-game flow events; `SessionReady` drives BindSession

### Phase 3 - ECS integration

Completed / partially completed:
- `ServerPlayerSessionWorld.BindSession()` now carries the selected character name into the spawned entity via `PlayerNameComponent`
- `ServerChatService.ResolveChatSenderContext()` now prefers `PlayerNameComponent`
  instead of the old archetype display-name placeholder
- client nameplates now consume the character-backed display name propagated from the ready-state spawn flow
- `PlayerCharacterComponent` now exists as a serializable component with a stable persistence id
- tracked world persistence now exists in `UltraSim/ECS/Persistence/` and `UltraSim/Server/`:
  - stable `PersistenceId` registration, shared `types.tdb`, and save-set manifest
  - `PersistentTag` save eligibility marker
  - persistence-signature grouping and archetype-shaped entity save/load
  - entity create/remap, hydration, and entity-reference fixup passes
  - server-owned coordinator, staged save promotion, root save-set validation, and recovery interaction seam
  - `SaveOnStop` and `AutoSaveEverySeconds` runtime options
- first real server-owned persistent gameplay component types are wired into the coordinator:
  - `PersistentTag`
  - `PlayerMarker`
  - `PlayerState`
  - `PrefabIdComponent`
  - `PlayerNameComponent`
  - `PlayerCharacterComponent`
- character-backed player entities now save/load and can rebind by `CharacterId` after restart
- character-backed player entities now survive normal unbind and can rebind by `CharacterId`

Still needed:
- replace the temporary pending-display-name handoff with a fuller character-backed bind payload
- update `ServerPlayerArchetypeCatalog.ResolveSpawnDefinition()` to require character
  selection (from `CharacterModel.ArchetypeId`) instead of connection-ID parity fallback
- decide whether persistent long-lived character state should stay on the session player entity
  or move to a separate world-owned character entity later
- add interactive/operator recovery UX on top of the existing recovery interaction seam

### Phase 4 — Character position persistence

- `CharacterModel` now carries `X, Y, Z, YawRadians` as the offline fallback snapshot
- `ServerRuntime.Save()` now snapshots live persistent player state into the selected `CharacterModel`
- On spawn, the runtime can reuse the character snapshot when no parked/loaded persistent entity exists
- Tracked host/runtime now persists and reloads the character snapshot through the account store
- Save now compacts offline parked persistent player entities after their snapshot is durable
- Still needed:
  - any future time-based retention or more advanced long-offline compaction rules beyond the current save-time policy

---

## 11) What Does NOT Change

- `PlayerArchetypeDefinition` / `ServerPlayerArchetypeCatalog` — still the visual body catalog
- `ServerSessionEntityBindings` — unchanged bidirectional session↔entity map
- `ServerPlayerReplicationService` — no changes; reads `PlayerState` which is unaffected
- ECS archetype registration — gains two new component types, otherwise unchanged
- `ServerChatService` - routing/fanout unchanged; the sender name source now partially moved to `PlayerNameComponent` in Phase 3
- The handshake (protocol version, schema hash) — still a separate lower-level concern;
  account "login" happens after handshake completes, not as part of it
