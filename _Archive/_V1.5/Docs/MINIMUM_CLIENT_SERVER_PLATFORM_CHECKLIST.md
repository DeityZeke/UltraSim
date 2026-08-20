# Minimum Client/Server Platform Checklist

Purpose: track the "bare minimum working client/server platform" milestone before terrain and later gameplay systems.

Status: active checklist. This is not the full product roadmap. It is the minimum platform slice that should feel coherent before major gameplay systems are added.

Related docs:
- `Docs/SERVER_RUNTIME_ARCHITECTURE_AND_ROADMAP.md`
- `Docs/TMP_TO_OFFICIAL_SERVER_MIGRATION_CHECKLIST.md`
- `Docs/SERVER_DISCOVERY_AND_REGISTRY_ARCHITECTURE.md`
- `Docs/SERVER_DISCOVERY_AND_REGISTRY_ARCHITECTURE_V2.md`
- `Docs/WEB_PORTAL_AND_ACCOUNT_ARCHITECTURE.md`
- `Docs/WEB_PORTAL_MVP_IMPLEMENTATION_PLAN.md`
- `Docs/UI_SUBSYSTEM_PACKAGE_AND_IMPLEMENTATION_PLAN.md`
- `Docs/AUTHORITATIVE_PHYSICS_ARCHITECTURE.md`
- `Docs/CHUNK_TERRAIN_STATICS_IMPLEMENTATION_GUIDE.md`

---

## 1) Platform Goal

Before terrain, the platform should support this loop:

1. start client
2. view/select a server from a real list
3. connect
4. log in / account step
5. select or create a character
6. enter the game as a real persisted player archetype
7. restart the server
8. reconnect
9. same account and characters still exist

The client should also be usable enough to play moment-to-moment:

- camera is functional
- movement presentation is sane
- animation hooks exist
- sound hooks exist

This is the milestone that should feel "platform complete enough" before terrain.

---

## 2) Current Summary

Current status by area:

- official tracked server runtime
  - mostly in place
- `_tmp` break-away
  - in progress, but no longer the only real server path
- multiplayer browser/menu shell
  - substantially complete; official registry-backed browsing exists, direct connect works, and registry failures now degrade to friendly unavailable messaging instead of raw socket errors
- server-driven UI subsystem
  - substantially complete for pre-game flow; schema panels, repeater support, and bundled `MessageBox` path are in place
- account/login/character loop
  - implemented and tested end-to-end
- persistence for accounts/characters
  - account data layer (Phase 1) implemented
  - account/character identity persistence implemented
  - character names now flow into in-world nameplates and chat sender labels
  - tracked world persistence infrastructure is implemented
    - explicit `PersistenceId` registry and shared type database
    - save-set root under server base directory `Saves/`
    - entity/system save + load, remap, hydration, and reference fixups
    - server-owned persistence coordinator
    - staged world-save write + promote/replace flow
    - host-driven recovery interaction seam with configured fallback recovery modes
    - save-on-stop and autosave option support
  - first real persistent server component types are registered
    - `PlayerMarker`
    - `PlayerState`
    - `PrefabIdComponent`
    - `PersistentTag`
    - `PlayerNameComponent`
    - `PlayerCharacterComponent`
  - restart-safe character-backed player entity persistence is implemented
  - player persistence policy is now coherent for the current player/bot scope
    - normal disconnect/logout parks the persistent character entity in memory for same-process rebind
    - save snapshots the offline fallback into `CharacterModel`
    - save compacts offline parked players into the durable character snapshot
    - restart/load can either restore the persistent entity directly or respawn from the character snapshot fallback
  - validation now includes:
    - full-stack runtime persistence integration coverage
    - dedicated persistence perf harness under `tests/Server.PersistencePerf/`
    - coordinator phase timing, allocation, GC, and file-size reporting
    - dedicated live-load harness under `tests/Server.NetworkPersistencePerf/`
      - real transport connections
      - active bot movement/input
      - before/during/after tick windows plus save barrier/completion timing
  - current save/runtime behavior:
    - autosave now captures account + world snapshots at the end-of-tick barrier
    - account/world writes then continue in the background from those snapshots
    - direct/manual `ServerRuntime.Save()` is still synchronous when explicitly called
- authoritative physics seam
  - planned only
- camera / animation / sound platform hooks
  - partial, depending on area

### 2.1 Remaining work triage

Use this as the practical read of the checklist right now.

Done enough for this milestone phase:
- official tracked server runtime baseline
- server-driven pre-game UI flow
- account/login/character loop
- core persistence/save-load path
- restart-safe character-backed player persistence
- registry-backed browser/select flow baseline
- official-runtime load/perf validation baseline

Near-term remaining work:
- chat completion/polish
  - permission/access-level integration
  - command expansion
  - save/autosave notification polish folded into the chat pass
- remaining `_tmp` migration that still affects official runtime ownership
- menu/client usability polish
  - camera/controls feel
  - animation command-path completion
  - sound command-path completion
- operator-facing recovery/UI polish on top of the current persistence/recovery seam

Later work after the platform slice is coherent:
- authoritative physics seam and first backend integration
- broader offline retention policy beyond the current parked-player + snapshot fallback
- more advanced snapshot/incremental save only if current async snapshot saves stop being sufficient
- broader world-content systems that should follow, not precede, platform completion

### 2.2 Post-persistence review pass

Before moving on to the next major subsystem, run a short review/verify/refactor pass.

Review pass 1 - flow and ownership verification:
- trace the real login -> account verified -> character select/create -> ready -> bind flow
- verify which steps are event-driven versus direct host orchestration
- verify quit-to-character-select and disconnect edge cases do not leak session/world state
- identify what still lives in `Server.Godot/ServerRuntimeHostNode.cs` that should remain host glue versus move inward

Current findings:
- the flow is only partially event-driven today; `ServerEventHub` events exist, but login, character select/create, and the authenticated -> ready transition are still directly orchestrated in `Server.Godot/ServerRuntimeHostNode.cs`
- actual player bind/unbind remains runtime-owned in `UltraSim/Server/ServerRuntime.cs`, triggered from session lifecycle transitions once the host has marked the session ready
- return-to-character-select is internally coherent: it forces `Ready -> Authenticated`, drains lifecycle immediately, unbinds the session entity in runtime, clears pending spawn state, then reopens character select from refreshed account data
- disconnect cleanup is also coherent in the normal host loop: transport disconnects are processed before `ServerRuntime.Tick()`, lifecycle events are drained at the start of the tick, runtime unbinds the session/player state, and the host clears cached login/account/UI state in `OnClientDisconnected(...)`
- autosave during disconnect should therefore observe post-disconnect state in the normal hosted runtime ordering, but this is currently a traced conclusion rather than a focused regression test
- headless-host validation is still expected to expose cleanup work because `Server.Godot/ServerRuntimeHostNode.cs` owns meaningful connection/login/panel orchestration instead of being only a thin bootstrap shell

Remaining gaps from review pass 1:
- add focused tests for quit-to-character-select, disconnect during character select, and autosave during disconnect
- verify whether the player archetype fallback path is ever used in normal login/character flows or is only a safety net

Review pass 2 - mixed-load validation:
- run active bot load together with ECS-heavy scenarios
- rerun autosave under that combined load
- measure tick avg/p95/max, save barrier, save completion, and save overlap/coalescing behavior

Review pass 3 - network/bandwidth telemetry:
- add direct bytes-in/bytes-out and packet-count telemetry
- measure per-connection and total network cost under realistic CCU
- compare the current runtime against the intended low-bandwidth design target

Review pass 4 - headless host validation:
- prove a minimal console/headless host can drive `ServerRuntime`
- treat any extra complexity as evidence that host/runtime boundaries still need cleanup

---

## 3) Core Platform Tasks

### 3.1 Official server path fully replaces `_tmp`

Status:
- substantially complete

Done:
- tracked server host boots
- real transport accept/disconnect works
- session lifecycle works
- player spawn/input/replication/AOI baseline works
- chat and UI control path have tracked homes
- `ServerDrivenUiShapeExamples.cs` prototype removed (tracked `ServerUiPanelService` is the real impl)
- `MainSceneRoot` routing fixed: server builds now check `server_build` feature first, so legacy
  harness flags (`--server`, `--bot`) can never accidentally route a server build to `island_slice.tscn`

Still in `_tmp` intentionally (harness/experimental per migration checklist §6.3):
- `ECSNetworkIslandSliceNode.cs` + `island_slice.tscn` — still needed as target for Godot-based
  bot clients (`--bot` flag on client export); can be retired once official `client.tscn` supports
  the bot protocol
- `_tmp/chat/` (FloatingActorLabel*, ChatPresentationProfile, IslandChatCommandRegistry) — overhead
  label rendering and prototype command dispatch; classified as local preview visuals / debug tooling

### 3.2 Bot/stress harness targets official runtime

Status:
- substantially complete

Done:
- official server host accepts runtime command-line overrides for harness launches
- `_tmp/run_island_bot_ramp.py` can launch the official server executable
- `_tmp/run_island_autoscale_bots.py` can launch the official server executable
- standalone C# bot harness exists under `_tmp/csharp bot harness/`
- standalone C# sharded runner at `_tmp/run_csharp_bot_harness_sharded.py`
- official-runtime bot ramp validated against `.Output/Server.exe` and `.Output/Client.exe`
- C# sharded harness is now the primary high-CCU load validation tool (UDP shared-socket,
  multi-worker, server metrics integration)
- extensive load validation runs completed against official runtime:
  - 1280 CCU: tick_avg ~5.5ms, p95 ~25ms (stress profile)
  - 2048 CCU: tick_avg ~9.7ms, p95 ~47ms (stress profile, with lod+cad3)
  - 2560 CCU: tick_avg ~12.4ms, p95 ~48ms (stress profile, with lod+cad3)

Still in `_tmp` intentionally:
- old Godot-based Python harnesses (`run_island_bot_ramp.py` etc.) remain valid for mixed
  Godot client + server scenarios but are secondary to the C# harness for pure perf validation
- all harness scripts stay in `_tmp` per migration checklist §6.3

### 3.3 End-to-end revalidation on official runtime

Status:
- substantially complete

Done:
- manual export/run/connect sanity checks
- basic replication/AOI sanity checks
- official-runtime bot ramp validation at small and moderate CCU (CCU 2/4/8/16)
- official-runtime async socket and sharded socket ramp validation up to CCU 1024
- official-runtime autoscale validation (primary stayed connected, p95 settled to `45.1 ms`)
- full optimization pass (priority LOD, dense observer cadence, stream cadence, build workers)
- confirmed all-params operating envelope — staged ramp 1024→2560 CCU, 13/13 PASS:
  ```
  CCU    tick_avg  pump_avg  flush_avg  ping_p95   SLO
            (ms)      (ms)       (ms)      (ms)  <50ms
  ------------------------------------------------------
  1024      4.42     0.061      0.533     32.18   PASS
  1152      5.21     0.073      0.690     32.38   PASS
  1280      6.04     0.092      1.093     32.59   PASS
  1408      5.96     0.096      1.134     32.96   PASS
  1536      6.84     0.155      1.454     33.67   PASS
  1664      7.37     0.163      1.674     39.53   PASS
  1792      8.37     0.199      2.051     45.64   PASS
  1920      8.64     0.211      2.161     46.89   PASS
  2048      9.59     0.339      2.576     47.75   PASS
  2176     10.17     2.781      2.852     48.02   PASS
  2304     10.69     4.075      2.999     48.16   PASS
  2432     12.07     4.718      3.638     48.32   PASS
  2560     14.35    11.204      3.548     49.04   PASS
  ```
  Full details and interpretation: `Docs/NETWORK_BASELINE_2026-03-23.md`
- bot-chat validation against official server:
  - 6 Godot bots with `--bot-chat=1`, `chat_interval=30-60 ticks`
  - `chat_rx=129`, `chat_tx=774` in 49s — fan-out ratio exactly `chat_tx / chat_rx = CCU = 6`
  - `ServerChatService` routing is correct; no messages dropped
- pending_inputs validated under Godot bot ramp:
  - 4/8/16 bots with `--bot-chat=1`
  - `pending_inputs p50=1, p95=1, max=1` across 693+ samples — **not elevated**
  - server is processing all client inputs within 1 tick; prediction loop is healthy
- transport-pump defaults confirmed and locked:
  - `DecoupleTransportPumpFromSimulation = false`
  - `MaxInboundDatagramsPerPump = 8192`
- per-bot UDP sockets confirmed as harness-side bottleneck above ~1024 CCU; `--udp-shared-socket` is required for high-CCU runs

Still needed:
- none (3.3 is now substantially complete; remaining work is tracked in 4.x, 5.x, 6.x, 7.x, 8.x)

Load profiles:
- `stress`
  - `active_ratio=1.0`
  - no outbound bot chat
  - purpose: worst-case movement/replication ceiling
- `realistic`
  - `active_ratio=0.35`
  - outbound bot chat enabled
  - purpose: practical operating CCU / mixed-load baseline

Recent C# harness findings:
- the main blocker was transport inbound backlog in the official host, not the gameplay command queue
- the fix that materially moved capacity was:
  - `DecoupleTransportPumpFromSimulation = false`
  - `MaxInboundDatagramsPerPump = 8192`
- with those defaults, single-process `realistic` profile reruns are:
  - CCU `1408`: ping p95 `47.99 ms`
  - CCU `1536`: ping p95 `48.35 ms`
  - CCU `1664`: ping p95 `61.82 ms`
  - CCU `1792`: ping p95 `63.88 ms`
  - CCU `1920`: ping p95 `64.53 ms`
- with the same fixed server defaults, sharded C# harness reruns are:
  - `4` workers:
    - CCU `2048`: ping p95 `64.02 ms`
    - CCU `2304`: ping p95 `79.27 ms`
    - CCU `2560`: ping p95 `95.03 ms`
  - `8` workers:
    - CCU `2560`: ping p95 `80.25 ms`
    - CCU `2816`: ping p95 `111.80 ms`
    - CCU `3072`: ping p95 `143.06 ms`
    - CCU `3328`: ping p95 `112.38 ms`
    - CCU `3584`: ping p95 `412.39 ms`
- current interpretation:
  - the catastrophic knee is gone; current scaling is mostly ordinary rising tick / replication / flush cost until the next knee near `3.5k`
  - the old `~2.5k` class result is restored on the realistic mixed-load profile
  - the next practical boundary check should focus on the `3072 -> 3584` band, not on the old `1408 -> 1536` cliff

### 3.4 Chat migration completion

Status:
- substantially complete

Done:
- tracked chat service
- cooldown
- local/system scope routing
- dense local-chat caps/rate limits
- batching
- default command registry
- gameplay-aware sender naming
- local chat sender labels now prefer the selected character name via `PlayerNameComponent`
- fallback remains archetype/connection-derived labeling when a live character name is not attached yet

Still needed (non-blocking):
- permission/access-level integration (access level currently hardcoded to 0 for all players)
- command expansion beyond `/help`, `/ping`
- persistence/save polish pass after the chat rework lands
  - autosave/manual-save warning broadcasts
  - snapshot/completion timing notifications
  - player-facing wording/formatting routed through the final chat UX instead of ad hoc messages

---

## 4) Identity / Character / Persistence Loop

These tasks define the minimum durable server/client identity flow.

Full design for §4.1–4.4: `Docs/ACCOUNT_CHARACTER_PERSISTENCE_DESIGN.md`

### 4.1 Player archetype prefab

Status:
- partial, but further along than the original Phase 3 plan

Done:
- tracked player archetype catalog now exists
- tracked runtime/player world spawn path now uses archetype definitions instead of hardcoded actor-selection logic alone
- spawned player entities now carry `PrefabIdComponent`
- `PlayerNameComponent` is now attached during character-backed world entry
- selected/created character names now reach:
  - in-world nameplates
  - local chat sender labels
- runtime now carries pending session display names through the character-select/create -> ready transition

Still needed (Phase 3 of design doc):
- `PlayerCharacterComponent`
- replace the temporary pending-display-name handoff with a fuller character-backed bind payload
- update `BindSession` to take `CharacterModel` or equivalent durable character bind context
- unify any remaining gameplay-side identity surfaces around the same character-backed source

### 4.2 Account/session binding

Status:
- Phase 1 complete; Phase 2 complete; early Phase 3 identity handoff is partially in place

Design: `Docs/ACCOUNT_CHARACTER_PERSISTENCE_DESIGN.md` §7 (session lifecycle), §2 (config)

Phase 1 (data layer): ✅ complete
- `AccountModel`, `CharacterModel` — sealed records, uint64 IDs
- `ServerAccountOptions` — AccountsPerIp, MaxCharactersPerAccount, AutoJoinSingleCharacter, etc.
- `IAccountStore`, `BinaryAccountStore` (AES-256-GCM + BitWriter), `JsonAccountStore`
- `ServerAccountRegistry` — volatile array, dirty tracking, lock-free snapshot save
- `ServerAccountSystem` — SystemBase wrapper, periodic flush, Serialize/Deserialize hooks

Phase 2 (services + wire): partial
- ✅ `ServerAccountService.TryLogin` — SHA-256 challenge-response; new account auto-registration
- ✅ `LoginPanel` (0xFF02) — schema-rendered, challenge nonce in open payload
- ✅ `LoginRequestPacket` (0xE8) / `LoginResponsePacket` (0xE9) — packet-bound credential flow
- ✅ End-to-end login tested: account creation, invalid credentials, valid login all verified
- ✅ schema `CharacterSelectPanel` / `CharacterCreatePanel`
- ✅ schema `CharacterDeleteConfirmPanel`
- ✅ create/select/delete character flow with server-authoritative slot enforcement
- ✅ Session state additions: connect → IP gate → Authenticated → CharacterSelect/Create → Ready → InWorld
- ✅ `BindSession` moved to post-flow `Ready`; no raw-connect world bind
- ✅ `Quit to Character Select` keeps the transport session alive and re-enters the select/create flow
- ✅ selected/created character name is now carried into the ready-state spawn path
- ✅ client nameplates and local chat labels now use the selected character name when available

### 4.3 Character select/create flow

Status:
- implemented as the production flow
- schema UI, not bundled client panels

Design: `Docs/ACCOUNT_CHARACTER_PERSISTENCE_DESIGN.md` ?2.1 (auto-join rules), ?6 (session flow), ?8.3/?8.4 (schema character panels)

Covered in Phase 2 of design doc:
- `CharacterSelectPanel` ✅ schema-rendered list/select/delete/create-entry flow
- `CharacterCreatePanel` ✅ schema-rendered name + archetype picker
- `CharacterDeleteConfirmPanel` ✅ schema-driven confirmation (`DELETE` challenge + session-local skip toggle)
- auto-join: 0 chars -> always go to create; 1 slot + AutoJoinSingleCharacter -> skip select entirely
- `AllowCharacterDeletion` config knob
- `Quit to Character Select` from the in-game pause flow

Remaining QoL follow-up:
- persist the delete-warning skip toggle and expose a settings re-enable path

### 4.4 Persistence/save-load

Status:
- partial / infrastructure complete, gameplay persistence incomplete

Design: `Docs/ACCOUNT_CHARACTER_PERSISTENCE_DESIGN.md` §5 (persistence design), §10 (phases)

Covered across phases:
- Phase 1: AES-256-GCM encrypted binary store, BitWriter/BitReader codec, schema versioning
- tracked world save-set layout implemented:
  - `/Saves/meta.bin`
  - `/Saves/types.tdb`
  - `/Saves/Entities/entities.idx`
  - `/Saves/Entities/entities.bin`
  - `/Saves/Systems/<System>.bin`
- Phase 1: `SystemBase.Serialize/Deserialize` hooks for world save lifecycle
- world persistence infrastructure implemented:
  - explicit `PersistenceId` registration
  - `PersistentTag` entity eligibility
  - entity save/load with archetype-grouped persistence signatures
  - create/remap load phase, hydration phase, and entity-reference fixups
  - root save-set validation and recovery interaction seam with configured fallback recovery modes
  - `SaveOnStop` and `AutoSaveEverySeconds`
  - staged world-save write + promote/replace flow
- first tracked server gameplay persistence registrations implemented:
  - `PlayerMarker`
  - `PlayerState`
  - `PrefabIdComponent`
  - `PlayerNameComponent`
  - `PlayerCharacterComponent`
- first restart-safe gameplay entity contract implemented:
  - character-backed player entities now save/load
  - reconnecting sessions can rebind to loaded entities by `CharacterId`
  - disconnect/logout now preserves the parked character entity for same-process rebind
- Phase 4: partial
  - `CharacterModel` now stores offline fallback position/yaw snapshot state
  - runtime save now refreshes that snapshot from live persistent player entities
  - tracked host/runtime now wires durable account-store load/save for character snapshot fallback
  - save now compacts offline parked persistent player entities into the account snapshot fallback
  - autosave now captures account + world snapshots at the end-of-tick barrier and writes them in the background
  - runtime persistence is validated end-to-end through the real composed account + world save/load hooks
  - dedicated persistence perf harness now measures players-only, items-only, and mixed save/load scenarios
  - dedicated live-load harness now validates active bot traffic during save windows
  - current representative perf baseline (`Release`, `--iterations 3`):
    - `1000` players: save avg `5.491 ms`, load avg `5.966 ms`
    - `1000` players + `100000` items: save avg `42.941 ms`, load avg `16.062 ms`
  - representative live-load baseline (`Release`, serial save mode):
    - `1000` players + `100000` items + `100` active bots:
      - before tick avg/p95 `1.540 / 1.946 ms`
      - save barrier `12.496 ms`
      - save completion `89.610 ms`
      - after tick avg/p95 `1.447 / 1.836 ms`
  - current measured scaling shape:
    - save completion cost is dominated by world entity write time
    - save barrier cost is now materially lower than completion cost because writes run in the background from snapshots
    - load cost is dominated by hydrate time, then entity create time
    - fixups are negligible in current scenarios
  - remaining work: only if you want a more advanced retention/compaction policy than save-time compaction
  - current default policy is explicit:
    - unbind without save preserves same-process fast rebind
    - save compacts offline parked players into durable character snapshots
  - non-blocking follow-up:
    - save/autosave notification polish pass should happen alongside the later chat polish/rework, not as a separate persistence redesign
- Phase 5: `ServerAccountSecurityFlags` in server discovery broadcast

Still needed:
- interactive operator recovery UI on top of the current recovery interaction seam
- more advanced snapshot/incremental save policy only if the current async snapshot architecture stops being sufficient

---

## 5) Menu / UI Completion

### 5.1 Server-driven cached menu system

Status:
- substantially complete for the pre-game account flow

Done:
- shared panel contracts
- bundled `MessageBox`
- client definition cache / cold-path negotiation
- server UI panel service

Done:
- `LoginPanel` (0xFF02) — schema-rendered via `UiSchemaRenderer`; challenge-response auth wired;
  packet-bound action intercepts login submit before it reaches `UiPanelResponse` path
- `CharacterSelectPanel` / `CharacterCreatePanel` — schema-rendered production panels
- repeater support with stable row-key action context
- server-driven delete confirmation panel
- server-driven pre-game flow with `BindSession` deferred until `Ready`
- in-game `Quit to Character Select` flow

Still needed:
- persisted delete-warning preference / settings re-enable path

### 5.2 Finish client-side menus with the new system

Status:
- substantially complete for the account/session flow

Done:
- multiplayer browser shell exists
- provider-backed menu seam exists
- category tab/provider cycling works
- direct-connect and double-click join path work
- login/select/create/delete schema flows are integrated with server responses
- official registry-backed provider path exists
- registry failures now surface friendly browser messaging instead of raw connection-refused error rows

Still needed:
- additional post-login gameplay/admin server-driven panels as needed

---

## 6) Server Discovery / Selection

### 6.1 Server tracker API / lightweight service

Status:
- substantially complete for the official registry path

Done already:
- discovery contracts
- runtime advertised/status models
- client provider/query abstraction
- real official registry service exists
- server registration, runtime polling, and public query flow exist
- admin/runtime moderation surfaces exist through the portal/admin tooling

Still needed:
- non-official provider breadth such as LAN/favorites/history when needed
- tighter launcher/gateway integration if that becomes a separate consumer
- continued hardening, metrics evolution, and operational polish

### 6.2 Wire server tracker into server select menu

Status:
- substantially complete

Done:
- menu is provider-backed
- official registry-backed provider exists
- real connect flow uses published server rows
- registry/provider failures now degrade gracefully in the UI
- direct connect remains available alongside the official provider

Still needed:
- richer provider tabs such as LAN/favorites/history when those become priorities
- any additional sort/filter polish beyond the current browser shell

---

## 7) Usability-Complete Client Layer

These are part of the minimum platform experience, even if they are not identity/persistence blockers.

### 7.1 Camera / controls

Status:
- partial

Done:
- usable orbit-follow camera rig exists
- local player controller path is in place
- movement is camera-relative through the current local controller/runtime path

Still needed:
- mouse-look / joystick-look polish and finalized feel tuning
- broader input/settings polish across gameplay and menu modes

### 7.2 Animation command path

Status:
- partial

Done:
- engine-agnostic animation presentation commands exist
- client dispatch/sink path exists
- actor/model presenter integration exists on the Godot side

Still needed:
- real server-authored gameplay animation usage beyond the current plumbing
- content/profile-driven animation mapping and polish

### 7.3 Sound command path

Status:
- partial

Done:
- semantic SFX/VFX cue contracts exist
- client dispatch/sink seam exists

Still needed:
- real sound playback integration in the Godot client
- server-authored gameplay sound usage beyond the current plumbing

---

## 8) Authoritative Physics Before Terrain

### 8.1 Authoritative physics seam

Status:
- not started

Needed:
- engine-agnostic physics contracts
- tracked server physics service
- Godot/Jolt-backed authoritative server backend

### 8.2 Minimum pre-terrain physics integration

Status:
- not started

Needed:
- gravity
- grounded checks
- capsule/character sweep movement
- static-world collision
- server-authoritative movement resolution

Reference:
- `Docs/AUTHORITATIVE_PHYSICS_ARCHITECTURE.md`

### 8.3 Portal / forum / account polish pass before terrain

Status:
- not started as an explicit bundled pass

This is intentionally after the core client/server platform loop and the minimum pre-terrain physics seam, but still before terrain starts consuming the broader platform.

Needed:
- forum UX overhaul in `UltraSim.Portal`
- client/game login should update shared platform account activity (`LastActiveUtc` or equivalent), not just web-portal activity
- activity-window vote projection rules layered on top of shared account activity
- remaining portal/forum hardening and test expansion, especially forum flow coverage
- any final portal/forum operational polish that should land before attention shifts fully to terrain/world-content work

---

## 9) After This Milestone

Once the checklist above is substantially complete, terrain becomes the next potential major system:

- chunk content
- terrain data
- frozen statics
- chunk-backed collision registration
- later housing/flora/farming dependencies

Terrain should not be the next platform-defining step; it should be the first major gameplay/world-content step after the platform is coherent.
