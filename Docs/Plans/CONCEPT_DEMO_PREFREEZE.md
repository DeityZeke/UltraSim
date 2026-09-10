# UltraSim Concept Demo — Pre-Freeze Hardening, Load, and Deployment-Policy Plan

**Status:** next campaign. The Concept Demo's required Round-1 feature journey and Client F0.8
product/parity matrix are accepted. Complete this campaign before declaring the Concept Demo frozen
and before beginning the broader post-demo extraction/genericization ecosystem roadmap.

**Suggested repository path:** `Docs/Plans/CONCEPT_DEMO_PREFREEZE.md`

---

# 1. Purpose

The Concept Demo should not be frozen at the instant the last planned feature works.

Once the complete Round 1 experience exists, UltraSim should spend one deliberate campaign treating that exact build as a real multiplayer product:

- run many real protocol clients against one shared authority;
- characterize functional, stress, density, churn, and failure behavior;
- measure actual per-connection and aggregate wire cost;
- decouple render, input, authority, replication, and Network service cadences;
- prove that the current high-performance envelope is a capability, not a correctness dependency;
- expose bounded deployment-performance policy so server operators can trade latency/freshness against CPU and bandwidth;
- test compression, aggregation, cadence, and other bandwidth optimizations only where evidence says they matter;
- run mixed synthetic and native clients under the same authority;
- collect permanent evidence describing both strengths and known limits;
- only then freeze the accepted Concept Demo baseline.

The central rule is:

> **Build and prove the high-performance envelope first; freeze semantic correctness independently from the deployment cadence chosen to run it.**

The current architecture is deliberately being developed under a demanding envelope: approximately 16 ms / 60 Hz-class authority and Network responsiveness, immediate-ish delivery, bounded high-frequency state, and systems engineered as efficiently as practical. That is useful because it keeps an action-game/FPS-like performance ceiling available even though UltraSim's ordinary gameplay may ultimately choose a much cheaper cadence.

The pre-freeze campaign determines how much of that ceiling a deployment actually needs.

---

# 2. Relationship to the existing Concept Demo plan

`CONCEPT_DEMO.md` remains the owner of:

- the human-facing demo journey;
- feature requirements;
- system test markers;
- client/server diagnostic capture;
- crowd/distributed/churn live-test scenarios;
- the population posture:
  - 8–16 functional multiplayer acceptance;
  - 20–50 strong concept-demo concurrency evidence;
  - 50–100 serious stress evidence;
  - 100–200 deliberate failure-characterization territory.

This document does **not** add more gameplay to Round 1.

It adds a phase between:

```text
Concept Demo feature-complete
        |
        v
PRE-FREEZE HARDENING / CHARACTERIZATION
        |
        v
Concept Demo frozen
        |
        v
post-demo extraction / EngineHost / genericization / ecosystem work
```

Do not let this campaign reopen Combat, accounts, persistence scope, WebEngine, public discovery, or other unrelated product work merely because the demo is being stressed.

---

# 3. What “freeze” means

Freezing the Concept Demo means selecting a known exact baseline whose:

- gameplay semantics are accepted;
- dependency/ownership boundaries are accepted;
- local/remote behavior is reproducible;
- exported Online product is physically separated from local authority;
- contentless Online journey is proven;
- multiplayer behavior is proven;
- performance characteristics and failure boundaries are measured;
- known limitations are documented;
- permanent automated evidence can reproduce the important claims.

Freeze does **not** mean:

- every future optimization is forbidden;
- the selected server tick rate can never change;
- the current wire representation is forever immutable;
- every production CCU target is known;
- every future Engine/package must conform to a benchmark written for the demo.

The freeze is a trustworthy reference point from which later extraction and architecture work can prove that behavior was preserved.

---

# 4. Precondition: one real shared multiplayer authority — M0.0 CLOSED

The synthetic-client campaign is most valuable only after multiple external participants actually share one authoritative Game/World.

A harness that starts one isolated authority per bot tests process/session lifecycle but does not answer the important questions about:

- shared AOI;
- public actor fanout;
- local speech fanout;
- Global chat fanout;
- crowd density;
- player-to-player disclosure;
- shared doors/chests/world state;
- per-recipient replication work;
- aggregate server bandwidth;
- contention and queue pressure.

The target topology is:

```text
one authoritative deployment
        |
        +-- real Online client
        +-- real Online client
        +-- bot client
        +-- bot client
        +-- ...
        +-- bot client
```

Every participant must use ordinary externally observable client contracts. No bot receives privileged ECS/Game access merely because it is a test client.

M0.0 satisfies this architectural precondition with three human-shaped participants in one
generation, including a dedicated all-remote composition and a Complete composition whose host
remains packet-free while two exported Online clients join remotely. It proves functional
cardinality, independent control, public actor fanout, spatial Chat, owner-private UI, and
leave/reconnect lifetime. It is not the PF1 bot harness, a high-population result, or the complete
distributed AOI/stress matrix described below.

C0.2 subsequently closes functional Global/System fanout across the same dedicated and
Complete-hosted three-participant shapes. The larger crowd/distributed/concurrent Chat workloads in
this plan remain stress and characterization work rather than being implied by that small canary.

D0.6 subsequently closes the first bounded diagnostic consumer over those same shapes: one
sanitized read-only Server Monitor, local/remote ping indication, and a trusted marker producer that
reuses the accepted System occurrence. Its three-participant evidence is functional scaffolding for
this campaign, not a broad telemetry, load, process-memory, or bandwidth characterization result.

---

# 5. Synthetic Network Bot / Headless Client Harness

## 5.1 Architectural boundary

Create a dedicated headless client harness, with an eventual name such as:

```text
Client.Bot
Tools.NetworkBots
UltraSim.NetworkBots
```

The exact project name is not frozen here.

Its dependency shape should resemble:

```text
Bot Harness
    |
    +-- Client.Core
    +-- Client.Network
    +-- Client.Content where the scenario requires real content/application proof
    +-- public Game protocol/contracts needed by an ordinary client
    +-- canonical Network Core + Hosted Client transport
```

It must **not** depend on:

```text
Godot presentation
Client.Local
Game Composition authority
Server Runtime
server Persistence
server Physics authority
direct ECS mutation
private server test hooks used as gameplay shortcuts
```

The bot should be a cheap non-rendering client, not a server-side simulated player.

The bot product should normally select the ClientOnly physical build of the same
`UltraSim.Network.dll` used by Online. It must not require Hosted Server or
`UltraSim.Network.Testing`; Full remains a compatible development substitute, not
permission for the bot composition to call server-role APIs.

The important path is:

```text
bot behavior
    -> real Client control/profile logic
    -> real Client.Network
    -> real secure Direct Connect/session
    -> real Game protocol
    -> shared authoritative server
```

Not:

```text
bot
    -> call Game movement method directly
```

## 5.2 Real session semantics

At least one correctness mode should exercise the complete ordinary session journey:

```text
deployment trust
    -> authenticated application session
    -> required content authorization
    -> cache/acquisition as configured
    -> fresh ContentSetApplied proof
    -> WorldPack resolve/open/bind where required
    -> guest admission
    -> active playable session
```

For high-population steady-state tests, bots may use prewarmed valid caches so content HTTP acquisition does not dominate the realtime gameplay measurement.

Cold-content stampedes should be a **separate explicit scenario**, because HTTP/content-host traffic is operationally different from ongoing UDP gameplay traffic.

Warm reconnects must still prove a fresh application session and fresh semantic application proof. A cache hit is never session proof.

## 5.3 Drive actual supported control grammars

Bots should not bypass the client control/profile semantics by manufacturing arbitrary normalized Game commands unless a specific low-level protocol test calls for it.

The behavioral harness should be capable of driving the stock client control grammars, including:

```text
OrbitCommand
ThirdPersonDirect
IndependentMoveLook
```

Examples:

```text
Orbit bot
    choose a destination
    generate pointer/orbit-style intent
    walk/run toward it

Third-person bot
    synthetic WASD axes
    synthetic camera basis
    jump / sprint / interact

Independent move/look bot
    locomotion direction independent from view direction
    rotate look while continuing movement
```

The result should prove that different client feel/profiles converge on the same authoritative Game movement semantics.

## 5.4 Seeded deterministic behavior

Pseudo-random bot activity must be deterministic and replayable.

A run should record enough identity to reproduce a failure:

```text
scenario
seed
bot count
bot ID
profile assignment
start/build/config identity
relevant event sequence
```

Example:

```text
--scenario churn
--bots 50
--seed 184729
```

If bot 37 exposes a stale state or crash, the same seed and scenario should make reproduction practical.

Randomness is useful only if it produces repeatable evidence.

## 5.5 Ordinary bot behaviors

Bots should intentionally behave like imperfect players rather than ideal benchmark agents.

Available actions should include:

- idle for varying periods;
- walk/run/sprint;
- change direction rapidly;
- rotate/look independently where the profile permits;
- jump;
- jump repeatedly at legal cadence;
- deliberately walk off floating-island edges;
- fall, land, recover, and immediately move again;
- use telepads;
- use Far Gates;
- cross AOI/chunk boundaries repeatedly;
- approach/read/interact with signs;
- open/close Inventory;
- open/close chest/container UI;
- perform legal Inventory actions;
- pickup/equip/drop/swing where Round 1 permits;
- submit Local speech;
- submit Global chat;
- receive system broadcasts;
- disconnect/reconnect;
- temporarily stall processing where safe;
- resume after a stall;
- change supported client control profile/scenario assignment between sessions.

The cliff/fall behavior is deliberately useful:

```text
grounded
    -> jump
    -> airborne
    -> leave support
    -> long fall
    -> landing / recovery / relocation
    -> fresh input immediately
```

Repeated transition pressure often finds bugs that normal wandering does not.

---

# 6. Bot harness modes

The harness should support several distinct test intents rather than one giant “random bots” switch.

## 6.1 Correctness swarm

Typical population:

```text
8–16 bots
```

Purpose:

- deterministic scripted scenarios;
- exact semantic assertions;
- all supported control grammars;
- movement/jump/fall;
- Chat;
- Inventory/UI;
- gates/telepads;
- reconnect;
- actor enter/leave/re-entry;
- no hidden privileged server path.

This should be suitable for permanent CI/acceptance where runtime permits.

## 6.2 Soak swarm

Typical population:

```text
20–50 bots
```

Purpose:

- seeded pseudo-random behavior;
- sustained world churn;
- memory/high-water stability;
- queue stability;
- no unbounded growth;
- no gradual semantic divergence;
- reconnects over a longer run.

## 6.3 Stress swarm

Typical population:

```text
50–100+ bots
```

Purpose:

- characterize throughput and deadline pressure;
- AOI fanout;
- public-state density;
- Chat fanout;
- queue high-water;
- server tick p95/p99/max;
- bandwidth per client and aggregate;
- identify the first meaningful bottleneck.

This is evidence, not a production CCU promise.

## 6.4 Failure-characterization swarm

Typical population:

```text
100–200 bots
```

or higher when cheap enough to characterize a curve.

Purpose:

- determine **how** the system degrades;
- verify bounded rejection/backpressure;
- distinguish CPU saturation, bandwidth saturation, queue pressure, AOI debt, and application failure;
- prove that overload does not silently corrupt semantics.

A failure is useful evidence when its cause and boundary are visible.

---

# 7. Canonical synthetic scenarios

## 7.1 Plaza / town-square convergence

Start distributed, then converge many bots around the Welcome Plaza/Cabin.

```text
50 distributed bots
        |
        v
all navigate toward one small area
        |
        v
jump / chat / interact / change direction
        |
        v
scatter again
```

Exercises:

- worst-case local actor density;
- public transform fanout;
- name/public identity state;
- local Chat fanout;
- Global Chat concurrently;
- dynamic-public bundle density;
- AOI subscriber counts;
- queue pressure;
- native presentation under load when real clients are present.

This is often more important than raw server population because local visibility density drives fanout.

## 7.2 Distributed world

Split clients among:

```text
Central
Sky
Cave
Far Island
```

Exercises:

- AOI benefit;
- separated local speech groups;
- distant retirement/re-entry;
- Global chat crossing spatial separation;
- lower per-client public fanout;
- world residency and structural/presentation boundaries.

## 7.3 Migration wave

Move a large population through the same transition:

```text
Central
    -> Far Gate
    -> Far Island
    -> return
```

Exercises:

- subscription leave/enter;
- public lineage retirement;
- new-area snapshot/delta work;
- retained delivery invalidation;
- client semantic retirement/rebuild;
- traffic burst behavior.

## 7.4 Edge / jump / fall storm

Have many clients repeatedly traverse legal jump/fall routes and intentionally leave support.

Exercises:

- Physics state transitions;
- authoritative owner state;
- public transforms;
- recovery semantics;
- command pressure;
- vertical world edge cases.

## 7.5 Chat storm

Generate high but **legal** Local and Global speech rates.

The purpose is not simply to benchmark spam rejection. It is to characterize normal worst-case retained-fact fanout and application-proof pressure.

Record:

- accepted occurrences;
- recipients;
- bytes;
- queue/high-water;
- latency;
- rejected/rate-limited submissions separately.

## 7.6 UI / Inventory interaction storm

Open/close Inventory and chest views and perform legal bounded actions.

Exercises:

- reliable Event/Control pressure;
- content-backed UI definition reuse;
- owner-private semantic traffic;
- backpressure behavior;
- view teardown.

## 7.7 Reconnect churn

Repeatedly retire and recreate sessions.

Include:

- warm cache reconnect;
- deployment restart;
- fresh application proof;
- fresh admission;
- stale old-session traffic rejection;
- public-world re-entry.

## 7.8 Cold content acquisition wave

Separately launch a bounded set of fresh-cache clients simultaneously.

Measure HTTP/content behavior separately from realtime gameplay:

```text
manifest bytes
blob bytes
cache writes
server/CDN throughput
time to content applied
time to World ready
```

Do not mix these bytes into ordinary “per-player gameplay bandwidth per second.”

---

# 8. Mixed headless + native-client acceptance

Headless bots are ideal for population but cannot prove Godot presentation survives load.

At least one pre-freeze test should run:

```text
shared server
    + 20/50/100 headless bots
    + 1–4 real exported Godot Online clients
```

The native clients prove, under synthetic load:

- movement remains responsive;
- other actors update;
- nameplates/overhead Chat remain correct;
- Inventory UI still opens;
- Chat still applies;
- World presentation does not ghost stale actors;
- render window remains bounded;
- control/UI traffic is not starved by public-state traffic;
- primitive/content presentation still behaves correctly.

The exact exported Online artifact should be the same product composition intended for testers.

---

# 9. First-class wire accounting

Before bandwidth optimization, measure the actual wire.

## 9.1 Required per-connection counters

Capture cumulative and sampled rates for at least:

```text
client -> server UDP bytes
server -> client UDP bytes

data datagrams
acknowledgement datagrams
keepalive datagrams
retransmitted wire bytes

application payload bytes
secure/framing overhead
packet count

bytes/messages by Network lane
    Control
    Input
    OwnerState
    WorldState
    Event
    Bulk
    Disposable

bytes/messages by major Game family
    playable command
    owner state
    public world
    public application proof
    Chat
    UI
    admission/session/content proof
```

Where practical, distinguish:

```text
semantic payload bytes
Network framing bytes
secure datagram overhead
UDP payload bytes
estimated IPv4+UDP wire bytes
```

If IP-layer bytes are estimated rather than observed, label them explicitly.

## 9.2 Content bytes are separate

Report HTTP/content acquisition separately:

```text
gameplay UDP steady-state
vs
cold/warm content HTTP transfer
```

A 700 KiB WorldPack downloaded once must not become “700 KiB/s/player.”

## 9.3 Aggregate + distribution

For every swarm run report:

```text
per-client median
per-client p95
per-client maximum

server ingress total
server egress total

bytes per authority tick
bytes per second
packets per second
```

Per-player averages alone can hide a small number of extremely expensive dense-AOI recipients.

## 9.4 Correlate bandwidth with semantic workload

Record enough semantic counters to explain the bytes:

```text
nearby visible actors
dirty public records
dirty chunks
chunk snapshots/deltas
commands
owner states
Chat occurrences/recipients
UI operations
reconnects
```

The goal is to be able to answer:

> “Why did this player use 68 KiB/s while the median was 14 KiB/s?”

without packet-payload logging.

---

# 10. Current bandwidth observations are motivating baselines, not targets

Before dedicated multiplayer wire instrumentation, first-principles calculations put the current correctness-first/high-cadence remote path roughly in this neighborhood:

```text
idle connected player:
    ~20–25 KiB/s bidirectional

actively moving player in a quiet area:
    ~40–50 KiB/s bidirectional
```

Older authenticated whole-flow Network stress evidence also produced per-recipient rates in the broad tens-of-KiB/s range, depending on workload and cadence.

These figures are useful only as a starting hypothesis.

They should be replaced by actual pre-freeze bot/native measurements.

Two informal external calibration points are worth remembering without turning them into requirements:

```text
RunUO historical/community planning:
    roughly a few KB/s per player, often remembered around 4–5 KB/s

modern generic networking / Mirror-style community guidance:
    low-action: a few KB/s
    RPG/co-op: often ~5–15 KB/s
    high-action/shooter: often tens of KB/s, including ~20–50+ KB/s
```

UltraSim does **not** need to match RunUO's wire cost. It has continuous 3D movement, modern authenticated transport, richer AOI/public state, explicit application proof, and different gameplay goals.

But those comparisons are a useful smell test:

> **If mature UltraSim permanently requires ~50 KiB/s for one player casually walking alone, investigate whether high-cadence correctness-first implementation choices are being mistaken for fundamental game cost.**

No pre-freeze optimization is accepted merely because it makes a comparison chart look better.

---

# 11. Density, not only total CCU, must be a first-class bandwidth axis

Public-state fanout is driven heavily by **mutually visible local population**, not simply total server population.

Think in terms of:

```text
K visible changing actors
x
K interested recipients
```

inside a dense area, rather than assuming one global O(N²) server cost.

Therefore every bandwidth report should distinguish:

```text
100 players scattered
100 players in four clusters
100 players in one plaza
```

A 1,000-player world whose players are distributed can be easier on public fanout than a much smaller event where everyone sees everyone.

This is one of the main reasons the Plaza and Distributed scenarios are both mandatory.

---

# 12. Decouple render, input, authority, replication, and Network cadence

The architecture must not rely on:

```text
1 rendered frame
    =
1 client command
    =
1 authority tick
    =
1 public-state send
    =
1 Network pump
```

The pre-freeze campaign should prove these dimensions can differ.

Relevant clocks/policies include:

```text
Client render frame rate
Client input sampling/submission
Authoritative simulation frequency
Owner-state delivery cadence
Immediate/Near/Far public replication cadence
Network service/pump cadence
diagnostic/monitor cadence
```

A client rendering at 144 FPS should not be required to transmit 144 complete movement commands per second merely because Godot rendered 144 frames.

A server simulating at 60 Hz should not be required to send every public actor at 60 Hz.

A Network pump running faster than authority should not invent new Game truth.

A Network pump running slower than authority must coalesce latest-state debt correctly while retaining non-coalescible facts.

These are already architectural principles; the pre-freeze campaign turns them into product-level evidence.

---

# 13. Server Performance / Delivery Profile

The eventual server should be able to advertise bounded execution/delivery expectations to an ordinary client.

This should be thought of as an authenticated **Performance Profile**, not an arbitrary executable/content package.

Conceptually:

```text
ServerPerformanceProfile/v1

identity / revision / hash

informational server policy
    SimulationFrequencyHz
    NetworkServiceCadence/Hz

hard client admission limits
    MaximumInputHz
    permitted burst/window limits
    protocol/compression requirements if any

client recommendations
    RecommendedInputHz
    expected OwnerState cadence
    expected Immediate/Near/Far public freshness
    interpolation-buffer hint
    other finite stock tuning hints
```

The exact shape is not frozen here.

The important distinction is:

```text
hard server limit
    client must obey
    server must enforce

recommendation/hint
    helps stock client presentation/efficiency
    does not move authority to the client
```

Unlike a control-layout recommendation, a maximum accepted input rate is not a matter of user preference.

The server can say:

```text
MaximumInputHz = 30
RecommendedInputHz = 20
```

and the stock client should automatically schedule its network submission accordingly.

The server still must enforce the maximum against custom/modified clients.

---

# 14. Input-rate enforcement and flood policy

Do not implement an input-rate limit as:

```text
client sends 500 commands/sec
    -> server queues all 500
    -> eventually processes them
```

That converts abuse into memory and latency pressure.

Prefer bounded semantics such as:

```text
continuous/latest movement intent
    rate admitted / coalesced according to contract

important action edges/facts
    separately retained where occurrence matters

excess sustained input
    -> reject/drop/coalesce as defined
    -> increment violation counters
    -> throttle or disconnect according to host policy
```

Repeated gross violation can become operator/security policy:

```text
rate violation
    -> warning/telemetry
    -> disconnect
    -> optional temporary/permanent ban policy
```

But `ban` is not a Network-kernel semantic.

Network exposes bounded delivery/rate facts; Game/host/security policy decides account/session consequences.

The harness should explicitly verify:

- a compliant client stays below advertised limits;
- a fast-rendering client does not accidentally violate them;
- a modified client exceeding the limit cannot force unbounded queues;
- action edges are not lost merely because continuous movement is coalesced;
- overload remains observable and bounded.

---

# 15. Performance-profile matrix

Exact production defaults should be chosen from evidence, not guessed now.

The pre-freeze harness should nevertheless test several qualitatively different profiles.

## 15.1 High-performance envelope

Illustrative shape:

```text
Authority                 ~60 Hz
Network service           high cadence
Client input              up to ~60 Hz
Owner state               high cadence
Immediate public state    high cadence
Near/Far                  progressively lower as policy allows
Compression               none or selective only
```

Purpose:

- preserve the demanding engineering ceiling;
- demonstrate action/FPS-like responsiveness is possible;
- provide the hardest CPU/bandwidth baseline.

## 15.2 Balanced UltraSim profile

Illustrative shape:

```text
Authority                 ~30–40 Hz
Network service           ~30–60 Hz
Client input              ~20–30 Hz
Owner state               ~20–30 Hz
Immediate public          ~20–30 Hz
Near                      ~10–15 Hz
Far                       lower
Compression               selective
```

Purpose:

- likely MMO/simulation-friendly latency;
- lower steady bandwidth;
- retain responsive third-person movement through interpolation.

## 15.3 Bandwidth/CPU-conscious profile

Illustrative shape:

```text
Authority                 ~20 Hz
Network service           ~30 Hz or another independently chosen cadence
Client input              ~10–20 Hz
Owner state               ~20 Hz
Immediate public          ~10–20 Hz
Near                      ~5 Hz
Far                       ~1–2 Hz
Compression               selectively enabled where profitable
```

Purpose:

- community/small-host friendliness;
- prove semantic correctness under a much cheaper deployment policy;
- characterize the visual/latency trade.

These numbers are **test candidates, not frozen defaults**.

---

# 16. Mismatched-cadence correctness matrix

Do not test only matched rates.

Include combinations such as:

```text
Render 144 Hz
Input 30 Hz
Authority 60 Hz
Network ~60 Hz

Render 60 Hz
Input 20 Hz
Authority 20 Hz
Network ~33 Hz

Render 144 Hz
Input 20 Hz
Authority 30 Hz
Network ~60 Hz

Render 60 Hz
Input 30 Hz
Authority 60 Hz
Network ~30 Hz
```

The exact supported frequency set remains governed by Runtime/Network contracts.

Acceptance is semantic:

- movement remains authoritative;
- action edges remain correct;
- owner/public state converges;
- Chat/UI facts remain retained;
- application proofs remain valid;
- reconnect/generation fencing remains correct;
- no hidden one-tick-one-send dependency appears.

Presentation smoothness should be measured separately from correctness.

---

# 17. Bandwidth optimization campaign — evidence first

Do not optimize the wire blindly before accurate accounting exists.

Once measured, evaluate improvements roughly from lowest semantic risk to highest.

## 17.1 Decouple input submission from render FPS

This is likely one of the first practical wins.

Current rendering may run at:

```text
60 / 120 / 144+ FPS
```

without requiring the same number of network commands.

The client should sample/render freely while a bounded input scheduler submits at the server-admitted cadence.

Important action edges must still be represented correctly.

## 17.2 Suppress/coalesce redundant continuous input

If movement intent is unchanged for many frames, investigate whether every sample must become a full command.

Possible shape:

```text
movement state changes
    -> send promptly

movement remains unchanged
    -> bounded periodic refresh / target cadence

button/action edge
    -> retain occurrence correctly
```

Do not implement naive “send only when changed” unless loss/recovery semantics remain provably correct.

## 17.3 Owner-state cadence

Authority may run at 60 Hz without sending a complete owner-state message 60 times per second.

Measure 60/30/20 Hz owner delivery while rendering/interpolating at higher frame rates.

Critical transitions may deserve prompt delivery independent of ordinary transform cadence if gameplay later earns that distinction.

## 17.4 Public AOI tier cadence

Immediate, Near, and Far public state need not have identical freshness.

Measure several policies and record:

```text
bytes
CPU/tick
latency/freshness
visual smoothness
semantic convergence
```

Do not reduce cadence merely to improve a benchmark if the resulting presentation is unacceptable.

## 17.5 Unreliable frame aggregation / packet packing

The Network kernel already has machinery for multiple unreliable frames per datagram, while conservative defaults may currently leave substantial framing overhead.

Measure enabling bounded aggregation.

This can amortize:

```text
secure datagram header
authentication tag
UDP/IP overhead
```

across multiple semantic frames without changing their Game meaning.

Acceptance requires:

- no harmful extra wait merely to fill a packet;
- no changed lane semantics;
- no new head-of-line behavior;
- bounded work;
- measurable wire benefit.

## 17.6 Avoid redundant self-public movement where safe

The controlled client already receives owner-private authoritative movement state.

Investigate whether the same recipient needs its own public transform repeated through the public AOI path at the same cadence.

Do not remove:

- public identity/lifetime;
- descriptor/prefab/body semantics;
- interactions that genuinely depend on public projection.

Only suppress redundant recipient self-echo if the client semantic model remains coherent.

## 17.7 Compact repeated fencing only if evidence justifies it

Current messages deliberately repeat strong explicit identities/fences:

```text
application session
control session
generation
sequence
public identity
```

This correctness-first representation is valuable.

Later, if measurements show meaningful savings, consider connection-local compact identity/tokens or other canonical wire compaction.

Rules:

```text
DO NOT REMOVE FENCING TO SAVE BYTES WITHOUT AN EQUIVALENT PROOF DOMAIN.

WIRE COMPACTION MAY CHANGE REPRESENTATION.
IT MAY NOT WEAKEN SESSION / GENERATION / IDENTITY SEMANTICS.
```

## 17.8 Quantization/field compaction only where useful

Public chunk records already use compact chunk-local coordinates in several places.

Additional float/integer/rotation compaction should be driven by measured traffic and gameplay precision needs, not generic networking folklore.

---

# 18. Compression campaign

Compression is deployment policy, not a universal default.

The trade is:

```text
compression enabled
    lower wire bytes
    higher encode/decode CPU
    potentially slightly later send/application

compression disabled
    higher wire bytes
    lower processing cost
    simpler/faster path
```

## 18.1 Do not compress tiny realtime messages blindly

Compressing a small command or owner-state record may save little or even expand it once compression framing is included.

Compression is more promising for:

- large WorldState bundles;
- large snapshots;
- large UI/state payloads;
- Bulk data where compression is not already handled by the content artifact;
- other payloads above an evidence-backed threshold.

## 18.2 Selective, finite codecs

If a codec is accepted:

- codec identity/version is finite and explicit;
- compressed size and decompressed size are bounded;
- decompression rejects malformed/oversized output before publication;
- no unbounded temporary allocation;
- compression occurs before authenticated encryption;
- do not create unsafe compression-oracle mixtures of secrets and attacker-controlled reflection;
- unsupported mandatory codec policy fails explicitly rather than silently misdecoding.

The exact codec is not frozen here.

A cheap codec such as LZ4-like behavior may make sense for latency-sensitive state; other codecs may make sense for colder/Bulk surfaces. Evidence decides.

## 18.3 Measure total cost, not bytes alone

For every candidate record:

```text
wire bytes
compression ratio
encode CPU
decode CPU
server tick p95/p99
client apply cost
latency/freshness
allocation
```

A 20% bandwidth win is not automatically good if it burns the server's critical tick budget.

---

# 19. Public-state scaling and AOI evidence

The selected public replication architecture must be tested under actual multiplayer actor density.

Measure at least:

```text
few dirty actors / many recipients
many dirty actors / few recipients
many dirty actors / many recipients
cross-chunk movement
cluster entry/exit
distributed populations
```

Capture:

- records per delta;
- bytes per dirty record;
- chunk headers amortized per record;
- proofs per delivery/bundle;
- snapshot recovery;
- history misses;
- interest queries/reconciliation;
- subscriber counts;
- per-recipient visible actor count;
- server fanout work.

The pre-freeze goal is not to prove one replication profile is optimal for every future semantic class.

It is to know how the **selected current-game profile** behaves under the real Concept Demo workload.

---

# 20. Loss, jitter, reorder, and failure behavior

Use the existing `UltraSim.Network.Testing` deterministic impairment machinery where it can
exercise the same production protocol semantics. It drives the canonical Network Core but must not
become a dependency of the shipped bot or Online product.

Test combinations of:

- loss;
- duplication;
- reorder;
- delay/jitter;
- reconnect;
- stale proof;
- delayed client processing;
- queue/capacity pressure.

For realtime public/latest state:

```text
loss
    -> old intermediate state may disappear
    -> current truth must eventually converge
```

For Chat/UI/application facts:

```text
loss/backpressure
    -> occurrence semantics must remain retained/bounded
    -> no silent conversion into latest-state behavior
```

For overload:

```text
capacity reached
    -> explicit bounded status / backpressure / disconnect as defined
    -> no silent semantic mutation
    -> no emergency unbounded allocation
```

---

# 21. Abuse-oriented rate tests

The bot harness should have an intentionally non-compliant mode.

Examples:

```text
input above MaximumInputHz
Chat above admitted rate
UI action spam
rapid reconnect attempts
stale session command replay
```

The goal is not to build a full anti-cheat or moderation system.

It is to prove:

- work remains bounded;
- queues remain bounded;
- valid clients retain service where expected;
- violation counters are observable;
- disconnect policy is deterministic;
- Game/host policy, not Network-kernel internals, owns punitive decisions.

---

# 22. Metrics captured for every important swarm run

## Server

```text
population
session/admission counts
authority tick mean/p50/p95/p99/max
late/deadline counts
process CPU
process memory
GC counts/pauses where useful

commands
    received
    admitted
    coalesced/rejected/rate-limited
    stale/duplicate

Physics/game
    jumps
    falls
    landings/recoveries
    interactions

public replication
    visible/subscribed counts
    dirty chunks/records
    deltas/snapshots
    proof debt
    history recovery
    bytes

Network
    ingress/egress
    packets/sec
    bytes/sec
    lane/message family
    ACK
    retransmit
    queue/high-water
    loss/replay/capacity counters

Chat
    submissions
    accepted occurrences
    recipient fanout
    rejected/rate-limited
    bytes

UI/Inventory
    open/action/apply counts
    backpressure
```

## Per client

```text
profile/control grammar
render FPS if native
input submission rate
RTT
command->owner-state latency where measurable
UDP bytes up/down
public-state bytes
Chat/UI bytes
retransmits
semantic rejects
current visible actors
current area
reconnect count
```

## Scenario output

Every permanent evidence artifact should include:

```text
commit/build
scenario
seed
population
server performance profile
client profile distribution
duration / measured interval
warm/cold state
network impairment
hardware/OS when relevant
result/verdict
```

---

# 23. Performance versus bandwidth scorecard

The pre-freeze campaign should eventually be able to generate a table shaped like:

| Deployment profile | Authority | Input | Public cadence | Compression | Population/topology | Server p95 | Avg client up | Avg client down | Server egress |
| --- | ---: | ---: | --- | --- | --- | ---: | ---: | ---: | ---: |
| High Performance | measured | measured | measured | policy | scenario | measured | measured | measured | measured |
| Balanced | measured | measured | measured | policy | scenario | measured | measured | measured | measured |
| Bandwidth Conscious | measured | measured | measured | policy | scenario | measured | measured | measured | measured |

Do not fill the final table from estimates.

The point is to let a future server operator understand the real trade:

```text
lower latency / fresher public state
    <-> more CPU + bandwidth

lower bandwidth / cheaper host
    <-> more interpolation + less freshness
```

---

# 24. Human live test after synthetic characterization

Bots should find obvious scaling/lifecycle problems before asking humans to spend time on them.

After the synthetic campaign is healthy, run the existing human Concept Demo journey with external testers.

Use the existing diagnostic/test-marker model to compare real behavior against bot workloads.

Human evidence is valuable for things bots do not judge well:

- perceived movement responsiveness;
- acceptable interpolation;
- UI responsiveness;
- Chat feel;
- visual crowd behavior;
- animation/state presentation;
- whether 20/30 Hz profiles actually feel different;
- whether lower-bandwidth settings still feel appropriate for the game.

Do not optimize solely from machine counters if the result feels bad to play.

---

# 25. Choose demo/server defaults only after evidence

The project can continue to build against a demanding high-performance envelope without requiring every deployment to use it.

Before freeze, select and document:

- Concept Demo default authority frequency;
- Concept Demo Network service policy;
- stock client input cadence behavior;
- public AOI cadence policy;
- whether unreliable aggregation is enabled;
- compression default/threshold if any;
- ServerPerformanceProfile defaults/limits;
- interpolation recommendations;
- flood/rate policy.

Keep the distinction:

```text
supported high-performance capability
    !=
recommended ordinary UltraSim deployment default
```

---

# 26. Pre-freeze checkpoints

The names below are local planning labels, not automatically subsystem milestone IDs.

## PF0 — feature-complete freeze candidate

Entry:

- all required Concept Demo Round 1 features complete;
- shared multiplayer authority complete;
- exported Online client complete;
- normal human acceptance journey passes.

Output:

- exact baseline commit to stress;
- no new gameplay scope admitted during the campaign without explicit reopening.

## PF1 — synthetic client harness

Close when:

- real external headless clients can connect to one shared deployment;
- multiple control grammars work;
- deterministic seeded movement/jump/fall/Chat/UI/reconnect scenarios exist;
- no privileged server/Game shortcut is used.

## PF2 — wire accounting

Close when:

- actual per-connection UDP bytes in/out are measured;
- major lanes/message families are attributable;
- HTTP content bytes are separate;
- aggregate + p50/p95/max per-client reporting exists;
- permanent baseline artifact records quiet/moving/crowd cases.

## PF3 — cadence/performance policy

Close when:

- render FPS and input submission cadence are independent;
- server advertises bounded hard/recommended performance policy as required;
- server enforces maximum input rate;
- mismatched authority/Network/input/render rates pass semantic correctness.

## PF4 — measured bandwidth/CPU optimization

Evaluate and accept only evidence-backed changes such as:

- input cadence/coalescing;
- owner-state cadence;
- public tier cadence;
- unreliable frame aggregation;
- redundant self-public suppression;
- safe wire compaction;
- selective compression.

Close when chosen changes have permanent before/after evidence and no semantic regression.

## PF5 — swarm/soak/failure matrix

Close when:

- crowd, distributed, migration, fall, Chat, UI, reconnect, and impairment runs are reproducible;
- 8–16 functional and higher stress ranges are characterized;
- mixed bot + native exported-client load passes at the chosen functional target;
- known failure cliffs are documented rather than hidden.

## PF6 — external human validation

Close when:

- real testers run the accepted journey;
- diagnostic reports/test markers can be correlated;
- selected performance defaults feel acceptable;
- major synthetic-vs-human discrepancies are understood.

## PF7 — Concept Demo freeze

Close when the final exact tree passes:

- full canonical verifier;
- product/dependency architecture gates;
- exported Online inspection;
- cold/warm native remote acceptance;
- multiplayer functional suite;
- bot correctness suite;
- chosen stress/soak evidence;
- bandwidth/performance baselines;
- known-limit document;
- clean worktree.

Then tag/record the exact frozen Concept Demo baseline and begin the separate post-demo ecosystem/extraction roadmap.

---

# 27. Freeze evidence package

The freeze should retain a small set of permanent artifacts sufficient to answer:

```text
What exact build was frozen?
What did the real exported client contain?
How many real/synthetic clients were tested?
At what authority/input/Network/public cadences?
What was server tick p95/p99?
What was per-client bandwidth?
What was server aggregate ingress/egress?
How did crowd vs distributed populations differ?
What happened under loss/reconnect?
What were the first known bottlenecks/failure boundaries?
Which optimizations were selected and why?
Which tempting optimizations were rejected and why?
```

Do not require every experimental run to remain forever.

Retain the decisive baselines and comparison artifacts.

---

# 28. Non-goals

This pre-freeze campaign does **not** require:

- a production CCU guarantee;
- multi-host load balancing;
- live connection migration between servers;
- cloud orchestration;
- CDN architecture;
- public server-directory infrastructure;
- WebEngine;
- a generalized anti-cheat system;
- account-level banning infrastructure;
- a universal compression abstraction;
- one compression codec for all semantics;
- rewriting movement around client prediction;
- matching RunUO's historical bandwidth number;
- matching Mirror or another framework's bandwidth number;
- switching public replication architecture merely because one benchmark favors another profile;
- turning the bot harness into a generic framework before UltraSim itself uses it;
- premature extraction of Network/Game/Client libraries before the frozen baseline exists.

---

# 29. Permanent architectural rules reinforced by this campaign

```text
HIGH PERFORMANCE IS A CAPABILITY.
IT IS NOT A REQUIRED DEPLOYMENT POLICY.

SEMANTIC CORRECTNESS MUST SURVIVE LOWER CADENCE.

RENDER FPS != INPUT SEND RATE.
INPUT SEND RATE != AUTHORITY RATE.
AUTHORITY RATE != NETWORK PUMP RATE.
AUTHORITY RATE != PUBLIC REPLICATION RATE.

LATEST STATE MAY COALESCE.
FACTS / EVENTS MAY NOT SILENTLY DISAPPEAR.

SERVER-ADVERTISED RATE LIMITS ARE ENFORCED SERVER-SIDE.
CLIENT COMPLIANCE IS AN OPTIMIZATION, NOT A SECURITY BOUNDARY.

BANDWIDTH IS MEASURED PER DIRECTION AND PER SEMANTIC FAMILY.
CONTENT DOWNLOAD IS NOT STEADY-STATE GAMEPLAY BANDWIDTH.

LOCAL PLAYER DENSITY IS A FIRST-CLASS LOAD AXIS.
TOTAL CCU ALONE IS NOT A USEFUL REPLICATION MODEL.

COMPRESSION IS SELECTIVE DEPLOYMENT POLICY.
IT MUST NEVER CREATE UNBOUNDED DECOMPRESSION OR WEAKEN SECURITY.

WIRE COMPACTION MAY CHANGE REPRESENTATION.
IT MAY NOT WEAKEN SESSION / GENERATION / APPLICATION-PROOF SEMANTICS.

BOT CLIENTS USE THE REAL CLIENT/NETWORK CONTRACT.
THEY DO NOT BECOME PRIVILEGED SERVER ACTORS.

A STRESS FAILURE IS EVIDENCE.
DO NOT RELABEL IT AS FUNCTIONAL SUCCESS OR HIDE THE CLIFF.

FREEZE ONLY AFTER THE REAL PRODUCT HAS BEEN CHARACTERIZED,
NOT MERELY AFTER THE LAST FEATURE COMPILES.
```

---

# 30. Recommended execution order

```text
1. Keep Client F0.5's compiled-UI source convergence closed.
2. Keep M0.0 shared multi-participant authority closed.
3. Keep Inventory I0.12's persistent rolled world-item vertical closed.
4. Keep I0.13 typed Inspection Properties and content-sized shared tooltip closure closed.
5. Keep C0.2 routed Global and authority System occurrence closure closed.
6. Keep D0.6 read-only Server Monitor/ping/test-marker closure closed.
7. Keep the completed Concept Demo Round 1 feature journey and F0.7/F0.8 matrices closed.
8. Select one exact feature-complete pre-freeze candidate. **NEXT**
9. Build the real headless Network bot client.
10. Add first-class wire/per-message accounting.
11. Establish high-performance quiet/moving/crowd baselines.
12. Decouple render FPS from input submission cadence.
13. Define/advertise/enforce the bounded Server Performance Profile.
14. Run mismatched-cadence correctness matrix.
15. Evaluate input/owner/public cadence reductions.
16. Evaluate unreliable frame aggregation.
17. Measure dense vs distributed AOI/fanout.
18. Evaluate selective compression only where payload shape justifies it.
19. Evaluate deeper wire compaction only if remaining measurements justify the complexity.
20. Run correctness, soak, stress, migration, Chat, UI, fall, reconnect, and impairment swarms.
21. Run mixed headless + exported Godot clients.
22. Run external human Concept Demo validation.
23. Choose/document the default deployment-performance policy.
24. Record decisive performance/bandwidth/failure evidence.
25. Full exact-tree verifier and export closure.
26. Freeze/tag the Concept Demo baseline.
27. Begin the separate extraction/genericization/ecosystem roadmap.
```

The intended outcome is not merely a demo that works.

It is a **known reference product** whose semantics, deployment tradeoffs, wire cost, scaling shape, and failure boundaries are understood well enough that subsequent extraction can distinguish a real regression from an intentional architectural change.
