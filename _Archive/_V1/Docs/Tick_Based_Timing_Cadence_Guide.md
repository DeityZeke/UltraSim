# Tick-Based Timing, Cadence, and Load Spreading — Design Reference

This document captures principles and concrete mechanisms for tick-based simulation
timing, system cadence, phase staggering, striping/amortization, and timer-driven
entities in a high-scale ECS server.

---

## 1) Base Simulation Model: Fixed-Step Ticks

### 1.1 Base Tick

- Authoritative simulation advances in **fixed steps**:
  - `dt = 10ms (0.01s)`
- This corresponds to **100 simulation ticks per second**.
- Rendering/client framerate is **independent** of the simulation tick.

### 1.2 Accumulator Pattern (wall-clock → ticks)

At the top of the world loop:

1. Add real time to an accumulator: `accum += deltaSeconds`.
2. While `accum >= 0.01f`:
   - run one simulation step with **constant dt = 0.01f**
   - increment `worldTick++`
   - subtract `0.01f` from `accum`

Only this layer is time-based. **All gameplay logic uses `worldTick`.**

### 1.3 Spiral-of-death guard

To prevent backlog spikes:

- Cap ticks per frame: `maxTicksPerFrame` (e.g., 3–8).
- If backlog persists, carry the remainder or apply optional time dilation.

This prevents one hitch from cascading into long stalls.

### 1.4 Why fixed-step?

- Deterministic cadence (no drift from frame hitches)
- Easy reasoning in ticks instead of seconds
- Clean multi-rate scheduling (`worldTick % N == 0`)
- Natural basis for striping and amortization

---

## 2) System Cadence (Multi-Rate Scheduling)

Each system declares a **period in ticks**:

| Real time | Ticks (@10ms) | Meaning                       |
| --------- | ------------- | ----------------------------- |
| 10ms      | 1             | Every tick (movement core)    |
| 30ms      | 3             | Chunk/region updates          |
| 50ms      | 5             | Perception / nearby awareness |
| 100ms     | 10            | Light AI checks               |
| 250ms     | 25            | AI decisions                  |
| 1s        | 100           | Territory/economy             |
| 5s        | 500           | Crops / slow decay            |

A system runs when:

```
(worldTick - StartOffset) % PeriodTicks == 0
```

This keeps scheduling deterministic and avoids time drift.

---

## 3) Phase Staggering (Load Spreading Across Ticks)

### 3.1 Motivation

If many systems share the same cadence (e.g., 4 systems at Period=2), running them all
on the same ticks creates spikes.

**Phase staggering** spreads them across nearby ticks without changing their true period.

### 3.2 Core idea: per-system start offset

Each system gets a **StartOffsetTicks** chosen at startup.

The system runs at:

```
StartOffset + k * PeriodTicks   (for k = 0,1,2,...)
```

So systems with the same period fire on different ticks, but still at the correct interval.

### 3.3 How to assign offsets (recommended)

For each `(PeriodTicks, Stage)` bucket:

1. Collect all systems with that period and stage.
2. Choose a **MaxStagger** (default 10 for non-critical systems).
3. Let `PhaseCount = min(PeriodTicks, MaxStagger)`.
4. Assign offsets round-robin:
   - `StartOffset = i % PhaseCount`.

This guarantees:

- Even distribution
- Determinism
- No extreme spreading for very large periods

### 3.4 Critical vs non-critical stages

Systems should declare a **Stage**, for example:

- Movement (critical)
- Combat (critical)
- Perception
- AI
- WorldSim (crops, decay, territory)
- Persistence / housekeeping

Rules:

- **Critical stages**: `MaxStagger = 0` (no staggering) or very small (1–2 ticks)
- **Non-critical stages**: `MaxStagger = 5–10` is typical

This prevents subtle latency between tightly coupled systems.

---

## 4) Striping / Amortization (Time-Slicing Large Workloads)

When a system must conceptually touch a very large population (e.g., millions of
items), **stripe** the work across ticks.

### 4.1 Basic striping

For a stripe count `S`:

- Each tick processes only entities where:

```
stripe = worldTick % S
```

Over `S` ticks, all entities are processed once.

### 4.2 Choosing S

Typical choices:

- `S = 10` → full sweep in 100ms
- `S = 20` → full sweep in 200ms
- `S = 100` → full sweep in 1s

This gives a clean tradeoff between freshness and CPU budget.

### 4.3 Stable striping assignment

Stripe assignment should be stable per entity (e.g., hash of entity id), not based on
iteration order, to avoid thrash when entity order changes.

---

## 5) Deadline / Freshness Caps (Bounded Staleness)

Instead of run-every-tick, systems can declare:

- `PeriodTicks` — desired cadence
- `MaxStalenessTicks` — worst allowed delay

Example tiers (@10ms base tick):

| System      | Period | Max Staleness | Notes                    |
| ----------- | ------ | ------------- | ------------------------ |
| Movement    | 1      | 1             | Must be fresh every tick |
| Combat      | 1–2    | 2–3           | Very tight               |
| Perception  | 10     | 20            | ≤ 200ms stale            |
| AI decision | 25     | 30–40         | ~250–400ms               |
| Crops       | 500    | N/A           | Slow sim OK              |

Scheduler should monitor violations for profiling/alerts.

---

## 6) Catch-Up Policy (Overdue Ticks)

When a system is overdue:

- **Fast-forward**: apply multiple updates until caught up (bounded).
- **Single step**: run once and reschedule (lateness is accepted).

Define the policy per system to avoid inconsistent behavior.

---

## 7) Plant as an Entity + Timer (ECS-native growth model)

### 7.1 Components

Typical plant archetype:

- `PlantTag` (marker)
- `GrowTimer`
- `PlantState` (stage, health, hydration, yield)
- Optional: `Position` / `ChunkCoord`

Example timer:

```
struct GrowTimer
{
    public int NextGrowTick; // worldTick when this plant should grow
}
```

### 7.2 Scheduling growth

On planting:

```
NextGrowTick = worldTick + GrowthPeriodTicks + (Hash(entityId) % JitterTicks);
```

This introduces deterministic jitter, preventing synchronized cascades.

### 7.3 Growth system loop (simple scan)

Each tick:

1. Iterate all plants (Span-based, SoA, parallel-friendly).
2. If `worldTick >= NextGrowTick`, apply growth.
3. Set `NextGrowTick += GrowthPeriodTicks`.

If catch-up is allowed, repeat step 3 in a loop until `NextGrowTick > worldTick`.

### 7.4 Alternatives

If plant count becomes enormous:

- **Striped scan**: process only 1/N of plants per tick.
- **Timing wheel / buckets**: process only plants due this tick.

Both integrate cleanly with tick-based timing.

---

## 8) Avoiding Visible Cascades

To prevent waves of updates:

### Option A — Jittered per-entity timers (recommended)

Each plant schedules its next update time with deterministic offset. Result: smooth
behavior, no spikes.

### Option B — Bounded sweep

If you prefer full sweeps:

- Run the system at a cadence (e.g., every 5s),
- Cap per-tick work so the sweep finishes within 200–500ms.

---

## 9) Combining Cadence, Stagger, and Striping

A typical non-critical system might have:

- Period = 25 ticks (250ms)
- MaxStagger = 5
- StripeCount = 20

Meaning:

- It fires roughly every 250ms,
- Starts at an offset in the first 5 ticks of that window,
- Processes only 1/20th of its workload per tick.

This yields smooth CPU usage, no visible cascades, and bounded staleness.

---

## 10) Networking Cadence (Server)

Snapshot/delta sends should run at their own cadence (e.g., 50–100ms) and align to
ticks for consistency.

- Input → applied at tick boundary
- Snapshot → reflects a specific tick id
- Client interpolation → uses tick ids for smoothing

---

## 11) Tick Budget and Profiling

Define a per-tick CPU budget (server side) and log overruns:

- Example budget: 2–4 ms per tick
- Track median/95th per system and per stage
- Alert on repeated budget violations

---

## 12) Summary of Best Practices

1. Fixed-step base tick (10ms).
2. Schedule everything by `worldTick`, not wall time.
3. Use phase staggering to smooth load.
4. Use striping for huge populations.
5. Prefer per-entity timers with jitter over big sweeps.
6. Reserve strict freshness caps for gameplay-critical systems.
7. Keep the 10ms path extremely narrow.
8. Cap ticks per frame to avoid spiral-of-death.

