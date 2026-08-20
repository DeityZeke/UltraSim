# Persistent Presence-Driven World — Comprehensive Game Design Summary

---

## 1. The Elevator Pitch

This is a third-person action-adventure RPG set inside a persistent, living, server-authoritative online world. It is not a content-driven game. There are no scripted quest chains to complete, no level-gated story beats, and no "endgame" you eventually reach. Instead, you step into a world that already exists, that has been running without you, and that will continue running long after you log off. Think of it as **Dark Souls meets Fable meets Ultima Online meets The Sims**, wrapped in a technically rigorous MMO architecture and driven by a philosophy that prizes systemic depth over manufactured content.

The tone is **fun and whimsical** — bright, curious, a little irreverent, like Fable — not grim or punishing. Combat is tight and action-driven, like Ocarina of Time or Dark Souls, but death is not punishing or frustrating. The world is mysterious, reactive, and alive in ways that surprise even its own inhabitants.

---

## 2. Core Philosophy

**Nothing truly despawns. Everything exists in some form, always.**

This single sentence is load-bearing for the entire design. Entities — players, NPCs, creatures, objects, structures — do not disappear when they die or are destroyed. They transition between states. This creates a world with genuine continuity and history.

Five design pillars hold this philosophy up:

**Persistent Existence.** Death is transformation. A creature killed in combat becomes a corpse, then a spirit, then eventually reforms into physical existence. Deep "harvesting" disrupts this cycle, forcing a full collapse to pure spark and delaying reformation — but existence continues.

**Systemic Consistency.** The world operates by coherent, deterministic rules. Ecology grows and depletes. NPCs have real needs that generate real demand. Dungeons intrude physically on the world and leave permanent scars. These systems run whether players are online or not.

**Presence Over Progression.** There are no character levels. Players grow through expanded capability, accumulated knowledge, built infrastructure, and earned reputation. A master blacksmith isn't "high level" — they just know more, can do more, and have the tools and workshop to prove it.

**Mystery Over Explanation.** The world contains three layers of secrets: logical ones that can be solved through observation, unusual ones that reward patient experimentation, and inexplicable ones that may never be fully understood. The world should never feel fully mapped.

**Stability and Instability.** The world was perfect before players entered it. Player "sparks" — the anomalous forces powering player characters — introduce instability. Instability is not evil; it is possibility. Without players, nothing new emerges. But their presence has consequences.

---

## 3. Cosmology and Lore

A feminine, luminous, shapeless intelligence created this world. She made plants, animals, and people. The world was beautiful and stable — and utterly predictable. Then she discovered sparks that did not originate from her: player sparks, arriving from somewhere unknown.

She allowed them entry out of curiosity.

Players are not heroes chosen by destiny. They are **permitted anomalies** — foreign elements the Creator allowed in because she wanted to see what would happen. She does not fully control what their presence enables. The world evolves differently because of them.

**Ontology of Existence — Three States:**

Every being in the world exists as one of three things simultaneously:

- **Spark**: The irreducible core of existence. Cannot be destroyed. Has no shape. It is pure being.
- **Spirit**: The layer of identity — personality, behavioral pattern, shape-memory. Appears as a ghost-like form. Binds spark to matter.
- **Physical Body**: Accumulated matter temporarily organized into a living form. Can be destroyed; it disperses back into the world.

When a creature dies, its physical body collapses into a corpse, then disperses. The spirit lingers, then begins reforming a new body. In a **Deep Harvest**, even the spirit is disrupted — collapsed back to the raw spark level — and identity must painstakingly reconstruct itself over a much longer period.

**Entity Lifecycle:**

- Standard: Physical → Corpse → Spirit → Reforming → Physical
- Deep Harvest: Physical → Corpse → Spark → Reforming Spirit → Reforming Physical

Players who die enter Spirit state immediately. As a spirit, you can observe the living world, communicate with other spirits, and navigate differently. Certain player abilities allow communication across the living/spirit boundary, or even assistance in restoring a spirit to physical form.

---

## 4. Genre, Feel, and Tone

**View and Camera:** Third-person, action-adventure camera. Close-in for combat, pulled back for exploration. Similar framing to Dark Souls or Ocarina of Time.

**Combat:** Tight, responsive, skill-based action combat. Not tab-target. The feel target is Ocarina of Time / Dark Souls — deliberate attacks, readable enemy tells, meaningful positioning — but without being punishing or punitive on death. Combat rewards reading your enemy, not memorizing a rotation.

**Death:** When you die, you become a spirit. You don't lose your gear, your bank, or your progression. You just have to find your way back to physical form. It's a setback with narrative flavor, not a punishment. The world might look different as a spirit — things glow, spirits of creatures or old NPCs might be visible — making death its own brief experience rather than a loading screen.

**Traversal:** Multiple movement modes inspired by Ocarina of Time / Majora's Mask. Walking, jogging, sprinting (stamina-limited), sneaking, and potentially mounted traversal (horses, and eventually wagons for trade). Movement speed follows human-relatable logic — your average person walks at roughly 3 mph, an athletic player might sprint significantly faster, and Usain Bolt tier is a distant mastery ceiling.

**Tone:** Whimsical and fun, like Fable. Bright palette, expressive characters, a sense of humor. The world is serious about its own internal logic but doesn't take itself too seriously. There's room for silly quests, goofy NPCs, unexpected moments. Dark themes exist, but they're handled with a light touch.

---

## 5. Character Creation and Progression

**Character Appearance:**

Simple but expressive. One male body type, one female body type. Players choose skin tone, eye color, hair style, and hair color. From there, the depth comes entirely from clothing — a massive wardrobe of equippable items in the UO tradition, allowing radically different visual identities without complex body-sculpting systems.

**Skills — The UO/Mabinogi/Runescape Hybrid:**

There are no class levels. Skills are individual values on a 0–255 scale, spread across a broad catalog:

- Combat skills (swords, bows, magic, blocking, parrying, etc.)
- Life skills (cooking, tailoring, smithing, farming, fishing, taming, etc.)
- Survival skills (foraging, tracking, first aid, etc.)
- Social skills (trading, persuasion, music, etc.)

**Professions / Classes:** At character creation, you pick a profession (and optionally a "counter" profession — your weak point). Your profession gives a **2x gain rate** for skills in its domain, a **1x rate** for everything else, and a **0.5x rate** for your counter-profession skills. You can still do everything. You'll just excel at your niche, be competent elsewhere, and find your weakness genuinely feels like a slog.

**Rebirth:** Mabinogi-inspired. With some effort (not trivial, not backbreaking), you can rebirth and change profession. This lets long-term players eventually develop broad competence while early identity choices still matter.

**Mastery Skills:** A second layer of meta-skills that accelerate learning in related domains. A master woodworker picks up glass blowing faster than a total novice. Breadth of experience across a domain family compounds into a bonus — up to roughly 2.5x learning speed at peak mastery. Getting to 100 in a skill is a real achievement. Getting past 100 requires special effort — "potential upgrades" that raise your skill's ceiling, plus exponential XP requirements.

**Carry Weight:** Human-relatable. The threshold for "overencumbered" is what you can carry while maintaining your normal movement speed — not a bodybuilder squat max, but a practical load. Minimum ~10 units, average ~30, physically active characters ~60. Overburdening slows you down; you don't become physically incapable.

---

## 6. The Living World — NPCs and Simulation

This is where the game earns its "Sims meets Radiant AI" description.

NPCs are autonomous entities, not quest dispensers. Each one has:

- **Needs**: Food, shelter, social contact, employment, income.
- **Skills**: A set of actual skills, same system as players, that determine what they're good at.
- **Jobs**: Dynamically generated from world needs. If there's no blacksmith in town, that creates demand. Demand creates an economic opportunity. That opportunity can be filled by an NPC or a player.
- **Preferences and personality**: Likes, dislikes, relationships, routines.
- **Housing**: NPCs live somewhere. Their living situation can improve or deteriorate based on their economic conditions.
- **Clothes and money**: They dress according to their economic status and personality, and have personal finances that change over time.

NPCs create **dynamic job boards** — not scripted quest lists, but actual reflections of real-world demand. "We need more iron swords" is not a fabricated fetch quest; it's because the local militia really is under-equipped and NPCs in that faction actually need them.

The world simulation runs continuously on the server regardless of whether any players are online. When you log back in after a week, things have changed. Settlements have grown or declined. A drought killed crops in one region. A new craftsman set up shop. A dungeon emerged in the forest. These changes aren't scripted events timed to player activity — they are the natural output of ongoing systems.

---

## 7. Housing and Property

**The System — UO + Valheim:**

Players can purchase plots of land. Plots are fenced, meaningfully sized, and tied to actual world coordinates — not instanced bubbles. Once you own a plot:

- Clear the land (terraform within limits)
- Build structures freely using a Valheim-style construction system
- Farm, store items physically in containers, establish workshops
- Create a presence that other players can visit

If a plot is lost (relinquished, taxes lapse, or territory changes), everything inside gets packed into a portable crate and sent to your bank. You don't lose your stuff. The land reverts to its natural state.

**Plot Types:**

- **Wilderness plots**: Available throughout the world in appropriate zones. Varied sizes, purchased at auction or from the world's systems.
- **Town plots**: Fixed locations within settlements. Much more expensive. Smaller footprint, but prime real estate with existing foot traffic.
- **Protected land**: Roads, rivers, public areas — cannot be claimed. This protects world infrastructure from being walled off.
- **Free housing**: Everyone gets access to a small instanced starter space — a room with some storage and a bank. No customization beyond what's there. It's a starting point, not a home.

**Building:** Full free-form construction. Not a template system. You place structures, walls, roofs, and furnishings with positional freedom. The server tracks the physical geometry as persistent world state — it exists in the simulation, affects NPC pathfinding, blocks LOS, and integrates with the game world as a real physical structure.

---

## 8. Economy and Trade

**Player-Driven Core:**

The economy runs on player activity, not NPC vendor arbitrage. Crafters produce goods. Other players (or NPCs with real needs) consume them. Prices fluctuate based on supply and demand. Player-run shops (UO-style physical vendor stands on your property or in a shop front) are the primary market infrastructure.

**Currency Sinks:**

- Housing plot costs and upkeep
- Item durability and repair (items degrade; craftspeople are needed to maintain gear, keeping the economy alive)
- Consumable supplies (food, potions, crafting reagents)

**Crafting Quality Variance:**

The same item can be crafted at different quality levels based on skill, material quality, tool quality, and potentially a luck factor. Two swords from two different smiths are not identical. Craftspeople can sign their work. Reputation matters — "I need a Tier 3 sword, who's the best smith on the server?" is a real conversation players will have.

**Recipe Discovery:**

Recipes can be learned from scrolls/books or discovered experimentally. Mastery skills help accelerate recipe comprehension in familiar domains.

---

## 9. Combat System (Detail)

**Action-based, third-person:**

Attacks are player-aimed, not auto-targeted. Dodging, blocking, parrying, and positioning matter. Enemy telegraphing is clear enough that death from a new enemy feels educational, not cheap.

**The Spirit/Death System as Tension:**

Death creates interesting secondary gameplay through the spirit state. Players who die become observers and communicators, creating opportunities for other players to assist (resurrection abilities), and creating a natural incentive not to stay dead — you want to get back in the game.

**PvP Framework (Not Fully Finalized):**

The design leans toward a flagging/zone-based approach:
- Towns and settled areas: Safe zones. No open PvP.
- Wilderness: Increasing risk as you go further from civilization.
- Deep wilderness / dungeon zones: Full tension — the precise PvP consequences here are TBD but follow the spirit of "you chose to go here knowing the risks."
- Murder counts / criminal flagging (UO inspiration) for tracking player-on-player aggression.

---

## 10. Dungeons — Physical Intrusions

Dungeons do not exist as separate instances. They physically emerge in the world.

**Lifecycle:**
1. **Emergence** — Environmental anomalies appear. Strange creatures, unusual flora, atmospheric disturbances.
2. **Manifestation** — A physical structure forms in the world. Other players can see it.
3. **Expansion** — The surrounding area becomes more dangerous. NPCs near the dungeon are affected.
4. **Resolution** — Players locate and destroy the dungeon's core.
5. **Collapse** — The dungeon structure crumbles, leaving a permanent environmental scar.

The world remembers. A grassy valley where a dungeon once collapsed might remain blighted, or show permanent geological changes. History accumulates.

**Creature Hierarchy:**

- **Ambient creatures**: Ecological baseline. Wolves, deer, fish. Part of the living world.
- **Elite creatures**: Persistent, powerful regional entities. They exist as "landmarks" — other players know them by name and location.
- **Champions**: Spawn when a species is killed repeatedly in the same area. The world's existential resistance. They hunt the players responsible.
- **Boss entities**: Unique, persistent entities that are discovered, not unlocked. They exist independently of player progression gates. Encountering one is a genuine world event.

---

## 11. World Simulation — The Persistent Backend

**Ecology:**

Resources grow, deplete, and recover. A forest overhunted by players will show real population decline. Leave it alone long enough and it recovers. Overfarmed soil loses yield. The environment reacts to player activity over time.

**World Seasons:**

The world evolves in phases over its lifetime:
- Season 0: Stability. The world as the Creator intended.
- Season 1: Minor anomalies. Small disruptions from player sparks.
- Season 2: Dungeon emergence begins. The world starts showing real instability.
- Season 3+: Escalating instability. Larger emergent threats. Greater potential for world-altering events.

This creates a long-term arc without requiring a content schedule. The world transforms through its own systemic logic.

**Survival Elements:**

Light survival aspects are woven into normal play: food and hunger affect stamina or effectiveness (not punishing survival-mode starvation), weather affects gameplay subtly (rain, cold, heat), and the day/night cycle has real mechanical implications (visibility, creature behavior, social dynamics in towns).

**Light System:**

Global light driven by time-of-day. Regional overrides for dungeons (always dark), interiors, and caves. Player-carriable light sources (torches, lanterns) with radius-based falloff. This affects visibility, stealth, and atmosphere.

---

## 12. Terrain System

**Discrete Grid, High Fidelity:**

The terrain is a discrete grid — similar in spirit to RollerCoaster Tycoon, not a smooth heightmap. This gives precision, determinism, and efficient diff/patch storage.

Core parameters:
- Tile size: 0.5m × 0.5m
- Height step: 0.25m per unit
- Chunk size: 32 × 32 tiles (16m × 16m per chunk)

Each tile stores four corner heights (integer steps, no floating-point drift), a material ID, and flags. Slopes are governed by step-delta rules: up to 3-step difference is a walkable slope, 4+ is a cliff face with a rendered vertical wall.

**Editing Modes:**

Live Edit: Changes immediately affect the public world, mesh/collision rebuilds instantly, navigation rebuilds on demand (explicit command).

Staged/Instanced Edit: Changes exist in a private overlay. The public world is unchanged until you publish. On publish, the system validates, rebuilds mesh/collision/navigation, and atomically applies the changes to the public world. This is the mode used for player property building.

**Navigation:**

Multiple navmeshes for different agent sizes (human, horse, wagon). Each generates independently per chunk. Navigation is a derived artifact — never directly edited, always rebuilt from terrain changes. This means a wagon physically cannot path through a narrow alley, creating realistic constraints on travel modes.

---

## 13. Travel and Traversal Modes

Multiple movement modes, each with distinct feel:

- **Sneaking**: Quiet, slow, reduces detection radius
- **Walking**: Default pace, no stamina cost
- **Jogging**: Moderate speed, no stamina cost
- **Sprinting**: Fast, burns stamina
- **Mounted (horse)**: Significantly faster, requires trained mount
- **Wagon/cart**: Slow, high cargo capacity, requires wide paths (navigation system enforces this)

Fast travel exists in limited form — fixed-point teleportation gates (UO moongate philosophy) between major settlements, not arbitrary "fast travel to any discovered location." Getting from the gates to your actual destination still requires travel. The world should feel like a place with real geography, not a menu of locations.

---

## 14. Social Infrastructure

**Guilds:** Guild halls (claimable properties), guild banks, guild-level reputation and potentially guild-level skills/perks.

**Reputation:** You can build a name. "The best blacksmith on the server" is a real social title. "That murderer everyone avoids" is also real. Reputation is tracked per character and per server community memory.

**Third Place Design:** The game should be somewhere you log into even when you have nothing to "do." Player-run taverns with minigames, player shops you physically visit, public crafting stations where people gather organically. Campfires. Music skills that play real music. The social space should feel alive without requiring a reason to be there.

**Communication:** Proximity-based local chat, global channels for coordination, spirit chat (accessible only to/from spirits), and music as a social language (real, playable instruments as a skill).

---

## 15. Technical Architecture Summary

The game is built on a **custom C# ECS (Entity Component System)** running inside Godot as the client renderer, with a headless dedicated server using the same ECS kernel.

**Server Architecture:**

The server is sole authority over all world state. Even singleplayer runs a local server that the client connects to via loopback — there is no "offline mode" code path. This eliminates special casing and keeps the multiplayer contract consistent everywhere.

Each server tick follows a deterministic order:
1. Poll inbound network traffic
2. Decode packets into command queues
3. Apply queued commands at the world barrier (pre-simulation)
4. Run gameplay/simulation systems
5. Build replication deltas for outbound
6. Apply any deferred structural changes (post-simulation)
7. Flush outbound packets

This tick-barrier model ensures no out-of-band world mutations — everything goes through the queue, everything applies at the right time, determinism is preserved.

**Network Layer:**

TCP-first baseline (reliable ordered for all gameplay-critical traffic), with channel abstractions in place for future UDP/hybrid enablement for high-frequency streams like movement. Packets are strongly typed structs with compile-time metadata. Dispatch is O(1) via a frozen startup-built table — no reflection in hot paths.

**Replication:**

Interest-based filtering — the server only sends a client data about entities within their area of interest. Shared delta fanout: deltas for a given chunk/region are built once and sent to all clients in that region, not rebuilt per-client. Per-connection replication cursors track what each client has acknowledged. Budget lanes (Critical / State / Cosmetic) control how much bandwidth each connection uses per tick, with explicit overflow policies (defer, coalesce, or drop by lane).

**Client Architecture:**

The client should be split into an engine-agnostic core (network runtime, prediction/reconciliation/interpolation, replicated state apply, semantic output commands) and an engine-specific host/presentation layer (input capture, scene tree orchestration, rendering, animation, audio, VFX, UI/gumps). The core decides what happened; the engine layer decides how it is presented.

Use logical asset IDs (model/animation/VFX/SFX/UI panel IDs) resolved through a client content resolver + layered visual pack stack. This supports user-selectable visual override packs (warn on overlaps, last-enabled wins) and future alternate engine clients without changing protocol/gameplay IDs.

Server-driven UI (gump/menu) flows should use a prefab/schema protocol with a fast prefab path plus dynamic schema fallback. If the client does not have a menu schema/prefab, the server may send the full schema + payload once; the client validates it, builds it dynamically, and caches it under a per-server namespace for future fast opens. Cache keying should use stable server identity (GUID/fingerprint), not only IP:port.

Client app flow should be modeled as states (Boot, MainMenu, Settings, SinglePlayerWorldSelect, MultiplayerServerBrowser, Connecting, InGame, Disconnect/Error), even if implemented inside a single `Game.tscn`. Singleplayer should launch a local server and connect over loopback instead of using a separate offline path.

Two separate ECS worlds — server world (authoritative, full simulation) and client world (projected, local-only). The client handles input sequencing with sequence numbers, local prediction, interpolation of remote entities, and reconciliation against authoritative server corrections. Corrections follow the same world mutation gateway rules — no direct storage hacks.

**Chunk Streaming:**

The world is divided into 16m × 16m chunks. Clients subscribe to their current chunk plus neighbors. On entering a new chunk, the client receives a full packed state dump for that chunk. Ongoing, only dirty deltas are transmitted. The sliding render window (spawning chunks ahead of the player, despawning behind) uses dual positioning — a local coordinate relative to the player's current origin (for rendering precision) and a world coordinate (for server spatial queries and replication matching).

Chunking is layered, not one monolithic system:
- **Spatial chunks (server runtime):** dynamic entity membership + AOI/interest filtering + replication fanout grouping.
- **Terrain chunks (server authoritative data):** terrain tiles, frozen statics/deco/building records, dirty/version tracking, collision/nav rebuild triggers.
- **Render chunks (client runtime):** pooled sliding-window render containers with local-relative and world-absolute chunk coordinates.

Frozen map statics ('/freeze'-style baked objects) are chunk content records by default, not ECS entities. They still load on the server for collision/LOS/nav and on the client for rendering. Promote them to ECS entities only when they need runtime state/logic (doors, containers, destructibles, harvestables, etc.).

Render chunk visuals and actor presentation should resolve assets through the client content resolver/pack stack (logical IDs -> engine assets), not through protocol-embedded engine asset paths.

**Scale Targets:**

- Fast-twitch games (action RPG combat): 100–300 CCU per server
- Survival/crafting tempo: 300–1000 CCU
- ARPG mixed tempo: 500–2000 CCU

These targets are achievable within the current architecture's performance budget with the network layer running well under the 16ms tick budget.

**Testing Approach:**

Phase 1: Fixed island (no chunking, full-world state). Establish baseline correctness, get "holy crap it works with more than 3 people." Stress until broken, back off, soak.

Phase 2: Implement spatial chunking and sliding render windows. A/B comparison against Phase 1 metrics. Expected significant bandwidth reduction (target 80%+ reduction at 100+ CCU).

Current measured Phase 1 island-slice baseline (socket synthetic clients, pre-AOI/pre-chunking, Feb 2026):
- Dense/everyone-active profile (20 Hz movement + chat): ping SLO passes through **56 CCU**, fails at **64 CCU**.
- More realistic profiles (10 Hz movement-only or ~40% active movers/chatters): ping SLO passes through **80 CCU**, fails at **96 CCU**.
- Fresh vs warm server runs produced the same knee; no meaningful uptime drift was observed in these tests.

Recommended implementation order from this baseline:
1. Server spatial chunking + AOI interest filtering
2. Shared delta fanout optimization inside chunk/interest groups
3. Client sliding render windows + terrain/deco chunk streaming

---

## 16. The "Why" — What Makes This Different

Most online RPGs are theme parks: designed experiences you move through and eventually exhaust. This game is designed to be a **place** — somewhere that exists whether you're there or not, that changes because of what you and others do, and that rewards being present rather than being efficient.

The closest analogies in spirit are early Ultima Online (before Trammel) for its systemic player-driven world, Mabinogi for its life-skill depth and tonal warmth, Fable for its whimsy and approachability, and Dark Souls for its tight action feel. But none of those games have the persistent, systemic simulation depth this is aiming for — particularly the idea that entities never truly disappear, that NPCs have genuine lives and needs, and that the world's history accumulates visibly.

The technical infrastructure — the custom ECS, the deterministic tick model, the budget-driven replication system — exists in service of that vision. The world has to actually run, all the time, for all of this to matter.

---


Related working specs also include `Docs/CLIENT_RUNTIME_ARCHITECTURE_SPEC.md`, `Docs/CHUNK_TERRAIN_STATICS_IMPLEMENTATION_GUIDE.md`, and `Docs/UI_SCHEMA_AND_PREFAB_PROTOCOL_SPEC.md`.

*Document synthesized from design conversations, architecture PRDs, terrain system specifications, and network implementation plans. Last updated February 2026.*
