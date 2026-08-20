# Time, Time Scale, Traversal, World Pacing, and NPC Behavior

*A comprehensive design doc covering time systems, traversal, world pacing, and NPC utility-driven behavior.*

---

## 1. Design Goals

You're building a **persistent, systemic, presence-driven world**. Time and traversal must support:

* **Observable life**: Players can watch routines (work, hauling, construction, patrols, socializing).
* **Continuous progress**: The world visibly changes across sessions (walls rising, towns growing).
* **No "zoomie" NPCs**: Movement should look natural, not sped-up to compensate for calendar compression.
* **Functional schedules**: NPCs must be able to commute, work, break, socialize, and sleep within a day.
* **Reasonable for low-hour players**: Someone playing **3–9 hours/week** should still feel seasons/events, not miss everything.
* **Scalable simulation**: Tick-driven, server-authoritative systems should remain deterministic and easy to reason about.
* **Emergent, not scripted**: NPC behavior arises from needs, preferences, and world state — not from hardcoded schedules.

---

## 2. Core Principle: Multi-Scale Time

A single global "time multiplier" is never enough. You're balancing three layers:

### 2.1 Moment-to-moment time

* Day/night cycle, lighting, perceived rhythm, "a day in the life"
* Drives immersion and player experience

### 2.2 Short-term progression

* Construction stages, crop cycles, travel days, local events
* Drives "I can see things happening" across a few sessions

### 2.3 Long-term evolution

* Seasons, years, world shifts, settlement growth arcs
* Drives "history is happening" without feeling chaotic or frozen

**Key rule:** Keep **visual time** (day/night) satisfying, and tune **calendar length** and **system rates** separately.

---

## 3. Recommended Time Model

### 3.1 Atomic unit: the in-game day

A very strong baseline (and proven psychologically) is:

* **1 in-game day = 60 minutes real time**
* This yields: **1 in-game hour = 2.5 minutes real time** (150 seconds)

This is long enough for:

* full observable commutes,
* work blocks,
* social blocks,
* night behaviors,
  without being so slow that nothing changes.

---

## 4. Daylight vs Night: Use Phases, Not a Binary

You were right to shorten "true night" because it's often lower activity and commonly skipped.
But the *fix* isn't just "day vs night" — it's **twilight phases**.

### 4.1 Target lighting/activity structure

**Total day: 60 real minutes**

Use a **2:1 daylight-to-night** feel, but implemented as phases:

| Phase              | In-game time | Purpose                                         | Real-time duration |
| ------------------ | -----------: | ----------------------------------------------- | -----------------: |
| Dawn               |    4:00–6:00 | early risers, guards ending shift, baking, prep |              5 min |
| Daylight (active)  |   6:00–18:00 | main work/social/economy                        |             30 min |
| Evening (twilight) |  18:00–20:00 | closing shop, taverns begin, travel wrap-up     |              5 min |
| Night (true dark)  |   20:00–4:00 | sleep + specialized activity                    |             20 min |
| **Total**          |     24 hours |                                                 |         **60 min** |

This matches the intuitive "midnight is midnight" framing while still compressing the least-interesting portion.

### 4.2 What "night" actually means in a living world

Night should be **lower activity**, not **zero activity**. Give it a role:

* guards patrol
* criminals / smugglers operate
* nocturnal creatures appear
* transport runs quietly
* inns and taverns peak early night
* true quiet window exists (e.g. 2:00–4:00)

---

## 5. Calendar Design: Slow the Calendar, Not the Day

Players who play 3–9 hours/week shouldn't "miss" a season. That's solved by making seasons last **more real-time weeks**, without slowing the day/night loop.

### 5.1 Recommended calendar

**Option A (balanced, strong default):**

* **12 days per month**
* **4 months per season**
* **4 seasons per year**

This yields:

* 1 month = 12 hours real time
* 1 season = 48 hours real time
* 1 year = 192 hours real time

For a player doing ~6 hours/week:

* 1 season ≈ **8 weeks** (feels lived-in)
* 1 year ≈ **32 weeks** (~8 months)

**Why this is good:**

* You get a stable seasonal identity.
* "Spring vibes" aren't a brief blur.
* Town construction can span multiple weeks *within the same season*.

### 5.2 Avoid IRL-anchored calendars

Don't lock "2 weeks IRL = 1 month."
Anchor everything to simulation time and let real-time experience emerge naturally based on play patterns.

---

## 6. NPC Routine Budget (Daily Life)

A 60-minute day supports believable NPC life because an "in-game hour" is long enough to *watch* behavior.

### 6.1 Example schedule template

* 6:00 wake / prep
* 7:00 travel to work
* 8:00–12:00 work block
* 12:00–13:00 lunch/rest
* 13:00–18:00 work block
* 18:00–20:00 market/tavern/social errands
* 20:00–22:00 tavern peak / home return
* 22:00–4:00 sleep

This assumes commutes and errands are usually **≤ 1–2 in-game hours**, not half the day.

**Important:** This schedule is not hardcoded. It is the *emergent output* of the NPC behavior system described in Section 8. An NPC follows this pattern under normal conditions because their needs and preferences naturally produce it — not because it was scripted.

---

## 7. Traversal Design: Time-Based Scale, Natural Speeds

### 7.1 The critical rule

> **Crossing a settlement should take hours at most, not a majority of the day.**

If NPCs spend 7+ in-game hours crossing town, they'll appear broken and unproductive.

### 7.2 Natural movement speeds

* **Elderly / slow:** 1.2 m/s
* **Average walk:** 1.4–1.6 m/s
* **Purposeful walk:** 1.8 m/s
* **Urgent:** 2.2–2.8 m/s (rare)
* **Jog (avoid as default):** ~3.0 m/s

**Recommended baseline NPC walk: 1.6 m/s**, with variation by role and personality.

### 7.3 Conversion: distance per in-game hour

With 1 in-game hour = 150 real seconds:

* Distance per in-game hour = 1.6 × 150 = **240 meters**

---

## 8. Settlement Size Targets (Fable/Bowerstone style)

### 8.1 Target diameters

* **Village:** 150–300 m across
* **Small town:** 300–600 m across
* **Large town (upper bound):** 600–800 m max

### 8.2 What that means in time

At 1.6 m/s:

* 400m cross-town ≈ 250s real ≈ 4m10s real = **~1.6 in-game hours**

That's excellent — commute feels meaningful without dominating the day.

---

## 9. Commute and Locality Rules

### 9.1 Most NPCs should be local

* Most NPCs live, work, and socialize within **one district** or two adjacent districts.
* Typical daily routine radius: **0.5–1.5 in-game hours** of travel per trip.

**Technical chunking implication:** districts should be modeled as a coarse layer above the chunk grid (districts contain many chunks). NPC locality/AoI should use chunk membership for runtime queries/replication, while district-level systems drive scheduling, economy, and event modifiers.

### 9.2 Only special roles roam far

* couriers, merchants, guards on patrol routes, transport workers, travelers

This keeps towns legible and consistent while making the world feel connected.

---

## 10. Construction and World vs. Player Parity

### 10.1 Stage-based construction for everything

Nothing should appear instantly fully-built.

**Example house build pipeline:**

* Blueprint placement: instant
* Foundation: +1 day
* Frame: +3 days
* Walls: +6 days
* Finished: +12 days

### 10.2 Acceleration via participation

* Base time: 12 days (12 hours real time)
* Each worker reduces time by a % (diminishing returns)
* Cap reduction at ~60%

So:

* Solo: 12 days
* Coordinated effort: ~5–7 days

Players get the joy of watching progress while maintaining parity between player-built and world-built structures.

---

## 11. Regional Travel and Caravan Camping

### 11.1 Travel hours per day

* Normal travel day: **6–8 hours**
* Hard travel day: **10 hours**
* Absolute max: **12 hours**

### 11.2 Caravan speeds

* Light caravan: **1.2–1.4 m/s**
* Heavy caravan: **1.0–1.2 m/s**

### 11.3 Distance per in-game hour (caravan)

At 1.2 m/s: 1.2 × 150 = **180 meters per in-game hour**

### 11.4 Distance per travel day

* Normal 8-hour day: 180 × 8 = **~1,440 meters (1.4 km)**
* Hard 10-hour day: 180 × 10 = **~1,800 meters (1.8 km)**

### 11.5 Camping thresholds

Camping triggers when:

* `travelHoursUsed >= maxTravelHours`
* remaining distance exceeds what's completable in remaining travel budget
* safety/weather risk rises above NPC risk tolerance

The "4+ hours remaining" scenario: at 1.2 m/s, 4 in-game hours = ~720 meters remaining. NPCs camp because they hit their day's limit with 700m+ still to go.

Camping beyond **~1.5–2.2 km** journeys (depending on caravan weight and urgency) is common.

---

## 12. NPC Behavior: Utility AI, Not Scripted Schedules

NPCs do not follow scripted daily timetables. They behave like The Sims: they sleep when tired, eat when hungry, socialize when bored, and work to earn money. Their choices are driven by **need urgency**, **personal preferences**, and **world state** — producing emergent, believable behavior without a single hardcoded routine.

---

### 12.1 Need Meters

Every NPC has a small set of continuously depleting and filling meters:

| Need | Depletes when... | Filled by... |
|------|-----------------|--------------|
| Hunger | Time passes | Eating at home, tavern, market |
| Fatigue | Active / working | Sleeping, resting |
| Social | Isolated | Talking, shared spaces, taverns |
| Financial security | Spending money | Working, selling |
| Safety / comfort | Nearby danger | Moving away from threat, familiar spaces |

Need urgency scales non-linearly — a mildly hungry NPC gives food low priority; a very hungry NPC will drop almost everything to find food. This produces the "I have to eat *now*" behavior without scripting it.

---

### 12.2 Preference Maps

Each NPC has a **weighted preference map** for locations, people, activities, and categories of experience. The "favorite shop" isn't hardcoded — it's simply the highest-weighted option in the "eat lunch" decision at that moment.

Weights shift from multiple sources:

* **Personal experience** — "I had a good meal there" → small positive bump
* **Trait alignment** — a shy NPC weights crowded spaces negatively
* **Environmental conditions** — "monsters spotted nearby" → safety weight for that region drops
* **Social influence** — "everyone I know avoids that area" → mild negative drift
* **Recency / novelty** — going to the same place many days in a row accrues a small penalty; variety gets a small bonus

Weights are not permanent. They **decay toward a baseline** over time (see Section 12.6).

---

### 12.3 Trait System

Traits are persistent personality parameters that act as **multipliers on how an NPC interprets weight changes**.

Example traits and their effects:

| Trait | Effect on behavior |
|-------|--------------------|
| Brave | Risk weight from nearby danger is a *positive* modifier — they want to investigate |
| Cowardly | Same risk weight is strongly negative — they avoid the area entirely |
| Sociable | Social need depletes faster; taverns and crowded spaces weighted higher |
| Introverted | Social need depletes slowly; prefers quieter spots |
| Greedy | Financial security need spikes harder; risk near potential profit is tolerated |
| Curious | Novel locations/experiences get a positive weight bump |
| Habitual | Novelty penalty is weak; they resist changing established routines |

Traits can shift gradually over time based on repeated experiences — a coward who has several safe encounters with low-level danger might slowly become less fearful. This is slow and bounded, not dramatic.

---

### 12.4 Environmental Weight Modifiers

The world itself produces modifiers that all NPCs pick up and filter through their own traits.

**Examples:**

* **Dungeon emergence nearby** → regional risk weight increases. Cowardly NPCs avoid the area; brave or greedy NPCs are drawn toward it.
* **Monsters spotted on the road** → travel-route safety weight drops; NPCs find alternate routes or delay travel.
* **A new tavern opens** → positive novelty weight on that location for a period.
* **A player builds a workshop in a district** → foot traffic to that district changes; nearby vendors see adjusted demand.
* **A crime happens in a district** → safety weight for that area drops for nearby NPCs who witnessed or heard about it.

The dungeon example you described works exactly this way: it doesn't trigger any scripted "flee" behavior. It raises the risk weight of that region. Each NPC's own traits determine whether that's repellent or attractive. An NPC's favorite lunch spot might fall inside the affected radius, so they check alternatives — their second-favorite, or if weights are equal, a random choice between comparable options.

---

### 12.5 Decision Architecture: Infrequent Evaluation, Committed Execution

NPCs do not re-evaluate their entire preference map every tick. That would be expensive and produce jittery behavior. Instead:

**Evaluation triggers:**
* A need crosses an urgency threshold
* A current goal completes
* A significant environmental change occurs nearby (dungeon appears, safety weight shifts, etc.)
* A high-priority interrupt fires (see below)

**Between evaluations:** The NPC commits to their current goal and executes it — pathing, acting, waiting. They do not second-guess.

**High-priority interrupts** can preempt a current goal:
* Immediate physical danger
* Critical need (starvation-level hunger)
* Major environmental event in close proximity

This separation between the **decision layer** (runs infrequently, produces a goal) and the **execution layer** (runs continuously, pursues the goal) keeps the system performant and behavior readable.

---

### 12.6 Memory Decay

Weight changes are not permanent. Every shift decays toward a neutral baseline over time, proportional to the intensity of the original event.

* A mildly unpleasant experience at a shop → weight recovers in a few days
* Witnessing violence nearby → weight recovers over a week or more
* A dungeon collapses → risk weight in that region gradually normalizes as the threat recedes

This prevents the world from accumulating bizarre behavior from edge-case events. An NPC disrupted by a player encounter gradually drifts back to their normal patterns if nothing reinforces the change. Persistent behavioral shifts require persistent reinforcement — which is exactly how personality actually works.

---

### 12.7 Seeded Determinism

The behavior system is **deterministic under identical conditions** but produces different outcomes when inputs differ.

Each NPC has their own **independent per-NPC RNG stream**, seeded from their stable identity. This means:

* Start a new world with no interference → NPCs do the same things every time
* A player talks to an NPC and delays them → that NPC's RNG state diverges; their choices downstream change
* Other NPCs are unaffected directly — they only react to how the *world state* changes as a result

Player interactions affect individual NPC RNG streams. Those NPCs then affect world state (the spot is now taken, the route is now crowded). Other NPCs react to world state changes. This is the right level of coupling — it produces butterfly-effect emergence without making every NPC globally dependent on every other NPC's RNG.

**Practical implication:** "Restart a fresh world and don't interfere — NPCs do the exact same thing" is achievable and correct. "Introduce a player" produces deterministic divergence from that player's specific interventions.

---

### 12.8 The Spot-Stealing Cascade (Emergence in Practice)

The full chain you described:

1. Player talks to NPC → NPC is delayed
2. NPC arrives late → usual table is taken
3. NPC sits elsewhere → new experience at new location
4. New location gets a small positive weight bump
5. Over time, NPC's "favorite spot" gradually drifts

This emerges entirely from the system with no scripted triggers. Players affect NPC lives in ways that compound and persist — not because any of it was authored, but because needs, weights, memory, and decay interact continuously.

---

### 12.9 Disrupted Schedules During World Events

When significant world events occur, NPC schedules should visibly respond:

* **Dungeon emergence near a settlement** → patrol routes shift, work blocks shorten, NPCs avoid the region at night
* **Severe weather** → outdoor activities weighted lower, indoor social spaces weighted higher
* **A road becomes unsafe** → caravan routes recalculate, merchants delay travel

These are not scripted reactions. They emerge from the same weight-modifier system. The dungeon raises risk weights in an area; NPCs with outdoor-heavy routines find their preferred options downweighted; they naturally shift toward alternatives. The world event doesn't need to "know about" NPC schedules — it just changes the terrain of weights that NPCs navigate.

The NPC AI doc (future) should define which event types produce which weight modifiers, and at what radius and decay rate.

---

## 13. Implementation Notes (Tick-Driven, Server-Authoritative)

### 13.1 Deterministic time source

Single authoritative world clock:
* `WorldTimeSeconds` (or ticks)
* Derive: current in-game time (hour/min), day index, month/season/year

### 13.2 Use curves and thresholds

* Lighting is a curve across phases (dawn/dusk), not toggles
* NPC need urgency scales non-linearly (not linear depletion → linear priority)
* Weight decay uses a curve, not a fixed daily subtraction

### 13.3 Movement and path distance in meters

Treat units as meters for sanity:
* speeds are meters/sec
* distances come from nav paths
* time estimates: `distance / speed`

### 13.4 Travel budget model

Per agent per day:
* `travelHoursUsed`
* `maxTravelHours` (role-based)
* `fatigue`
* `riskTolerance`

Camping decision:
* if `travelHoursUsed >= maxTravelHours` → camp
* if `timeToDestination > remainingAllowedTravelTime` → camp
* if weather/risk high → camp early

### 13.5 NPC evaluation cadence

NPC decision evaluation should not run every tick. Recommended approach:
* Evaluate on need-threshold crossing, goal completion, or nearby environmental change
* Between evaluations, commit to current goal and execute
* High-priority interrupts (danger, critical need) can preempt at any time
* Per-NPC RNG stream, seeded from stable identity, for all probabilistic choices

Runtime scheduling note (server): combine cadence with spatial chunking/AoI so distant NPCs can run lower-frequency evaluations while nearby/visible NPCs keep tighter cadence and interrupt responsiveness.

---

## 14. Final Recommended Constants

### Time

* **1 in-game day = 60 minutes real**
* **1 in-game hour = 150 seconds real**

### Lighting/activity phases

* Dawn: 4:00–6:00
* Day: 6:00–18:00
* Evening: 18:00–20:00
* Night: 20:00–4:00

### Calendar

* **12 days / month**
* **4 months / season**
* **4 seasons / year**
* Season ≈ 48 real hours (ideal for low-hour players)

### Movement

* NPC average walk: **1.6 m/s**
* Caravan: **1.2 m/s** (heavy: 1.0–1.2)

### Settlement sizes

* Village: **150–300 m diameter**
* Town: **300–600 m**
* Large town max: **600–800 m**

### Travel / camping

* Travel day: **6–8 hours normal**
* Hard travel: **10 hours**
* Absolute max: **12 hours**
* Camping common beyond **~1.5–2.2 km** journeys

---

## 15. What This Achieves

With these settings, your world will feel like:

* NPCs live believable daily lives driven by genuine needs, not scripts
* NPC behavior is deterministic under identical conditions, emergent under player or world influence
* Towns grow in a way you can *watch*
* Construction has weight and continuity
* Travel feels meaningful (including camps)
* Low-hour players still "live inside" seasons and events
* World events — dungeons, weather, crime — ripple through NPC behavior naturally
* Players affect the world in ways that compound over time without any authored trigger
* Everything stays consistent with a deterministic tick-driven server
