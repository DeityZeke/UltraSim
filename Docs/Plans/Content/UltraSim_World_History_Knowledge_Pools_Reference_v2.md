# UltraSim World History, Knowledge Pools, Episodes, and Memory Reference

## 1. Purpose

UltraSim needs long-lived history without one unbounded event log and without giving every actor a giant private memory database.

The intended architecture is a **bounded hierarchy of domain-owned byte pools, durable ledgers, temporary Episode storage, semantic compaction, and actor-relative information access**.

The core separation is:

```text
History
    = authoritative temporal truth

Information
    = actor-relative interpretation/access to truth

Working Knowledge
    = short-lived actor-local claim

Personal Memory
    = sparse promoted actor-relative memory
```

The history subsystem records what actually happened. The reasoning system decides what an actor could know, how deeply they know it, and whether that knowledge matters to a current decision.

---

## 2. Core Architecture

```text
AUTHORITATIVE WORLD OCCURRENCE
            │
            ▼
     History Admission
            │
     semantic routing
            │
       ┌────┴─────────────────────────────┐
       │                                  │
       ▼                                  ▼
Permanent / Durable Truth           Rolling History Pool
                                       │
                                       ▼
                                   RICH TIER
                              recent / high resolution
                                       │
                                compaction / demotion
                                       ▼
                                  SUMMARY TIER
                                       │
                                compaction / demotion
                                       ▼
                                   TREND TIER
                              old / coarse / cheap

                     abnormal high-information period
                                       │
                                       ▼
                                EPISODE ARENA
                          bounded high-detail segments
                                       │
                          finalize Part 1 / Part 2 / ...
                                       │
                             incremental compaction
                                       ▼
                              Finalized Episode
                                       │
                                       └────► normal history
```

Separately:

```text
Historical Truth
        ↓
Accessibility / Exposure
        ↓
Relevance
        ↓
Interest
        ↓
Salience
        ↓
Actor-relative Knowledge Result
        ↓
Working Knowledge
        ↓
optional Personal Memory promotion
```

---

## 3. History Admission and Perception Are Parallel

A world occurrence may feed History and actor experience independently.

```text
World Occurrence
    ├── History Admission / Projection
    └── Perception / Observation
```

History admission answers:

> **Is this occurrence worth preserving as authoritative world history?**

Perception answers:

> **Could this nearby actor sense/observe what happened?**

An event may be too trivial for any durable History pool while still mattering strongly to an individual actor.

Example:

```text
Child drops an apple
    no SettlementHistory record

Mother nearby
    perceives it
    reacts
    may form Working Knowledge or Personal Memory
```

History is not a prerequisite for experience.

---

## 4. History Is Not One Giant Log

History is **domain-owned** rather than one universal `HistoricalEvent` collection.

Examples:

```text
Weather / Environment
Settlement
Organization
Market / Economy
Trade Route
Crime / Safety
Health / Disease
Government / Authority
```

Each domain owns its record codec, temporal semantics, compaction rules, query policy, Episode policy, and retention policy.

A Weather observation may require one byte. An Organization event may require several bytes. The architecture therefore budgets **bytes, not records**.

---

## 5. History Pool Definitions

A logical definition may contain:

```text
HistoryPoolDefinition
    Id
    Domain

    RichBudgetBytes
    SummaryBudgetBytes
    TrendBudgetBytes

    RichCodec
    SummaryCodec
    TrendCodec

    DemotionPolicy
    EpisodePolicy
    QueryPolicy
    RetentionPolicy
    PinnedPolicy
```

A runtime pool instance should remain tiny:

```text
HistoryPoolInstance
    DefinitionId
    Owner / ScopeId

    RichHandle
    SummaryHandle
    TrendHandle

    ActiveEpisodeHandle?
    Flags
```

Avoid per-event managed objects, normal `List<>` growth, and routine GC churn.

---

### Pinning Never Breaks the Budget

A `PinnedPolicy` may change retention priority, but it must never make a bounded rolling pool unbounded.

If information is important enough that it cannot be safely demoted/evicted within the pool budget, the policy should instead:

- promote it into a durable ledger;
- promote it into a finalized Episode / durable historical landmark;
- retain a compact durable summary;
- otherwise move it to an appropriate bounded durable representation.

> **Pinning changes where/how information is retained; it never disables the byte budget.**

## 6. Pools Are Scoped and Earned

Definitions are shared. Instances exist only where a real scope needs one.

```text
OrganizationHistory:
    MiningGuild
    BakersGuild

SettlementHistory:
    Greyhaven
    Blackmere

TradeRouteHistory:
    NorthRoad
```

Do not allocate `every actor × every possible domain`.

A pool exists because a real owner/scope has legitimate historical questions that need answering.

---

## 7. Admission Rule

Before adding a new pool, ask:

> **Who will ask this history a question?**

Then:

```text
Who queries it?
What decision does the query affect?
What temporal resolution is required?
Why can't an existing history owner answer it?
What happens after detailed retention is gone?
```

If there is no good answer, do not create the pool.

This guards against unnecessary domains such as `ChairHistory` or `StreetSweepingHistory` merely because those systems produce activity.

---

## 8. Physical Storage: Shared Slabs / Arenas

Logical pool budgets should preferably be backed by shared domain/tier storage rather than one tiny allocation per pool.

```text
WeatherRichSlab
WeatherSummarySlab
WeatherTrendSlab

TradeRouteRichSlab
TradeRouteSummarySlab
TradeRouteTrendSlab
```

Pool instances hold compact handles/ranges into these slabs.

Possible allocation classes might be:

```text
64 B
128 B
256 B
512 B
1024 B
```

Exact sizes remain implementation tuning.

The rule is:

> **Logical retention budgets are per pool; physical storage should be page/slab based and allocation-efficient.**

---

## 9. Rich / Summary / Trend

Ordinary rolling history ages through loss of resolution:

```text
RICH
    recent
    precise
    expensive

SUMMARY
    older
    aggregated

TREND
    old
    coarse
    cheap
```

History should not primarily age by `exists → deleted`.

Instead:

```text
high detail
    ↓
semantic summary
    ↓
long-period characterization
```

---

## 10. Compaction Is Semantic Compression

Compaction is domain-aware, not generic compression.

### Weather

```text
hourly observations
    ↓
daily summary
    ↓
seasonal characterization
```

### Trade Route

```text
individual attacks / closures / delays
    ↓
period reliability / danger summary
    ↓
long-term route reputation
```

### Market

```text
individual price / supply / shortage observations
    ↓
daily or weekly state
    ↓
economic trend
```

Example:

```text
Day 412:
    four attacks on North Road
Day 413:
    one attack
Day 415:
    road closure
```

may later become:

```text
NorthRoad
Period 410–420

Reliability = Poor
Violence    = High
```

The meaning survives while detail disappears.

---

## 11. Degradation Means Loss of Resolution

> **History degradation means losing temporal/detail resolution, not randomly corrupting authoritative truth.**

Normal history:

```text
exact event
    ↓
bucketed period summary
    ↓
trend / characterization
```

Under pressure, the system may lose:

> Mary became symptomatic at exactly 14:17.

while preserving:

> 37 cases that day, 8 deaths, east district worst affected.

and later:

> Great Fever — 914 cases, 126 deaths, 43-day duration.

---

## 12. Incremental Deterministic Compaction

Prefer compaction on writes/bucket rollover rather than periodic global scans.

```text
Append Rich Record
    ↓
Would exceed Rich budget?
    ↓ yes
Fold oldest Rich group into Summary
    ↓
Would exceed Summary budget?
    ↓ yes
Fold oldest Summary group into Trend
```

For fixed-cadence domains:

```text
hour changes
    finalize hourly bucket

day changes
    finalize daily summary

season changes
    finalize seasonal trend
```

The owning domain codec performs the semantic fold.

Compaction must be deterministic under identical authoritative inputs.

---

## 13. History Routing / Projection

A world occurrence should not become one giant generic event copied everywhere.

Example:

```text
Mining Guild caravan attacked on North Road
```

may project into:

```text
TradeRoute:NorthRoad
    Attack
    Severity
    Delay

Organization:MiningGuild
    ShipmentLoss
    Severity

Settlement:Greyhaven
    RoadSafetyIncident
    Region=North
```

Each projection uses the domain's own compact encoding.

---

## 14. Permanent Ledgers

Some truths should not undergo ordinary semantic degradation.

Examples:

```text
Person Lifespan
    PersonId
    BirthTime
    DeathTime

Lineage
    PersonId
    ParentA
    ParentB
```

Potentially also:

```text
marriages / partnerships
title changes
major office transitions
major ownership transitions
```

"Permanent" means **logically durable truth**, not permanently hot RAM.

---

## 15. Hot / Cold Durable Truth

Durable ledgers can separate:

```text
Hot
    recent / active pages

Cold
    packed immutable persistence pages

Indexes
    disposable / rebuildable / demotable
```

Exact canonical truth may survive indefinitely while expensive query structures do not.

---

## 16. Temporal Indexes Are Not Canonical Truth

Example mortality indexing:

```text
Recent:
    exact people who died today

Older:
    daily compact roster

Older:
    monthly mortality

Older:
    yearly mortality
```

The exact lifespan ledger remains authoritative. The temporal index becomes cheaper/coarser with age.

---

## 17. Episodes

Episodes solve **bursty high-information history**.

Normal life may fit comfortably in a tiny HealthHistory pool. A plague will not.

Rather than permanently sizing all pools for crisis conditions:

```text
Normal Pool
    ↓
Exceptional information pressure detected
    ↓
Lease bounded Episode storage
    ↓
Capture richer detail
    ↓
Compact incrementally while active
    ↓
Finalize Episode Part(s)
    ↓
Produce final Episode summary
    ↓
Return compact result to normal history
```

Episode-worthy periods may include:

```text
Plague
Siege
Famine
Riot
Major fire
Trade collapse
Political crisis
War
```

---

## 18. Episodes Augment Normal History

An active Episode should normally sit alongside the ordinary rolling pool.

During plague:

```text
HealthHistory
    disease incidence = extreme
    mortality = high

Active Plague Episode
    district spread
    daily cases
    important deaths
    interventions
    turning points
```

The ordinary pool remains valid even if richer Episode storage becomes unavailable.

Queries must avoid double-counting the Episode and its ordinary-domain projection.

---

## 19. Episode Parts / Chapters / Pages

An Episode is **logical**, not physically single-record.

If an active bounded Episode segment fills:

```text
Great Fever — Part 1
    full
        ↓
Finalize Part 1
        ↓
Submit/link Part 1 into history
        ↓
Open Great Fever — Part 2
        ↓
Continue recording
```

Then:

```text
Great Fever
    Part 1
    Part 2
    Part 3
    ...
```

Each Part is:

- bounded;
- independently finalizable;
- deterministically ordered;
- linked to the same logical Episode;
- free to compact internally while active.

This is the normal answer to a long Episode exceeding one segment's storage budget.

The Episode is effectively a **book made of bounded chapters/pages/logs**.

---

## 20. Episode Segments Can Compact While Active

A long-running Part need not retain maximum detail until it fills.

```text
Today
    detailed occurrences

This week
    grouped events

Older portion of current Part
    coarser internal summaries
```

This preserves bounded memory even within one Part.

---

## 21. Finalized Episode

When the overall Episode ends, produce a durable compact historical object.

Conceptually:

```text
Episode
    EpisodeId
    Kind
    Start
    End
    Duration

    Peak
    Cases
    Deaths

    MajorLocations
    MajorActors
    ImportantSubEvents
    Consequences

    Parts[]
```

The final Episode can reference or semantically subsume its Parts.

Bulky active storage can then be released.

---

### Retention of Episode Parts After Finalization

Episode Parts are not automatically permanent merely because they were once active.

After the logical Episode is finalized, retention should be **policy-driven**:

```text
Final Episode Summary
    durable historical meaning

Episode Parts
    important / pinned parts -> cold durable retention
    ordinary parts           -> normal demotion / compaction
    redundant parts          -> discardable after safe semantic absorption
```

The final Episode must preserve the historical meaning required by its policy before any ordinary Part becomes discardable.

This prevents centuries of long crises from accumulating every historical chapter forever while still allowing especially important Parts—major battles, turning points, famous deaths, decisive discoveries—to survive as durable cold records.

Pinning a Part still obeys the global bounded-storage rule: long-lived importance must be represented in an appropriate durable format, not by making an active arena unbounded.

## 22. Episode Identity and Part Identity

A historically important logical Episode should receive a stable identity.

```text
EpisodeId = GreatFeverOfGreyhaven

Parts:
    EpisodeId + PartIndex 1
    EpisodeId + PartIndex 2
    EpisodeId + PartIndex 3
```

Part ordering must be deterministic.

Long-lived actor memory may safely reference the Episode ID rather than transient arena offsets.

---

## 23. Episode Arena Budget

The Episode Arena remains globally bounded.

Multiple simultaneous crises may compete:

```text
12 city fires
3 wars
2 plagues
```

The system must never fall back to unbounded emergency allocation.

If a preferred new Part lease is unavailable, the implementation may try a smaller segment profile or fall back to ordinary history until richer storage becomes available.

Exact arbitration remains intentionally open until proof-pool implementation.

---

## 24. Working Knowledge

Shared History is not directly "in an actor's head."

The first actor-local layer is Working Knowledge.

```text
WorkingClaim
    Claim
    AcquiredAt
    ExpiresAt
    Relevance
    Confidence
    Source?
```

Example:

> North Market is dangerous lately.

Bob may route around North Market for a while without permanently remembering who told him, where, and at exactly what time.

---

## 25. Personal Memory Promotion

Working Knowledge is promoted only when long-term retention is earned.

Possible promotion reasons:

```text
personally involved
highly salient
repeated
altered an important decision
emotionally significant
important to goals/role
relationship-changing
```

```text
Information received
        ↓
Working Knowledge
        ↓
Worth preserving?
        ↓ yes
Personal Memory
```

Personal Memory is a sparse sidecar, not a full private historical database.

---

## 26. Stable References Across Compaction

Never persist raw rolling-arena offsets as long-lived actor-memory identities.

A promoted Personal Memory should normally contain its own compact semantic claim:

```text
Subject = Roger
Relation = SavedMe
Context = NorthMarketFire
ApproxTime = ...
Confidence = ...
```

Optionally:

```text
EpisodeId = NorthMarketFireEpisode
```

Stable IDs should generally be reserved for durable ledgers, finalized Episodes, major historical landmarks, or other genuinely persistent records.

---

## 27. Accessibility / Exposure

History knows truth. Actors receive only information they could legitimately access.

Possible access sources:

```text
Local / physically present
Direct witness
Public information
Organization membership
Profession
Official role
Conversation
Written record
Rumor
Family / relationship
```

Accessibility is a gate before Relevance/Interest/Salience.

A farmer's strong Weather relevance does not grant knowledge of a continent they have never visited or heard about.

---

## 28. Relevance / Interest / Salience

These three dimensions operate **only after Accessibility / Exposure has passed**.

After access is legitimate:

```text
Relevance
    controls depth

Interest / Care
    controls granularity

Salience
    preserves particular actor-important events
```

These are the bridge between shared authoritative History and actor-relative usable knowledge.

---

## 29. Historical Truth vs. Claims

Maintain an absolute boundary.

Alice tells Bob:

> Harold murdered the baker.

This does **not** create authoritative CrimeHistory saying Harold did it.

It creates:

```text
Bob WorkingClaim
    Harold murdered baker
    Source = Alice
    Confidence = ...
```

Alice may be lying or mistaken.

The authoritative historical fact might instead be:

```text
Alice publicly accused Harold
```

if that accusation itself is worth recording.

---

## 30. Information Transfer

Communication transfers claims, not History.

```text
Actor A Knowledge
    ↓
Claim generation
    ↓
Speech / writing / message
    ↓
Actor B receives claim
    ↓
B evaluates source / confidence / relevance
    ↓
Working Knowledge
```

This supports rumor, propaganda, lies, misunderstanding, information drift, and reputation without corrupting canonical History.

---

## 31. World-History vs Personal-Memory Degradation

These are different subsystems.

### World-History Degradation

Authoritative loss of temporal/detail resolution:

```text
4 exact robberies
    ↓
robbery incidence elevated
    ↓
North Market was dangerous that season
```

### Personal-Memory Degradation

Actor-relative interpretation/generalization:

```text
I watched Harold stop a mugging at 14:32
    ↓
Harold stopped a mugging once
    ↓
Harold protects people
    ↓
Harold is brave
```

History compression remains authoritative. Memory compression becomes actor belief.

---

## 32. Hot Decision Queries Must Stay Bounded

The Sim Core may ask:

> What do I know about travel danger to Westmarch?

The knowledge layer should return a bounded result such as:

```text
BanditRisk       180
AnimalRisk        70
RoadCondition    110
Confidence       190
RelevantEvents     2
```

It should not scan or decompress centuries of records during a hot decision.

Deep historical research is a different query class.

---

## 33. Anti-Bloat Ladder

> **Not every state change is historical.**  
> **Not every historical fact becomes information.**  
> **Not every piece of information becomes somebody's memory.**  
> **Not every memory is worth communicating.**

This is one of the subsystem's foundational invariants.

---

## 34. Starter Proof Pools

The preferred first proof set is:

```text
Weather
Lifespan / Demographics
Organization
Trade Route
Disease Episode
```

### Weather

Tests dense fixed cadence, tiny encoding, and aggressive time summarization.

### Lifespan / Demographics

Tests durable exact facts, hot/cold storage, and temporal indexes.

### Organization

Tests irregular scoped events, semantic demotion, and access by affiliation.

### Trade Route

Tests cross-domain routing, economic/safety summaries, and decision-facing risk queries.

### Disease Episode

Tests burst pressure, Episode Arena, Part rollover, incremental compaction, and final Episode summarization.

---

## 35. History Manager Concept

```text
HISTORY MANAGER

Shared / cold:
    HistoryPoolDefinitions
    domain codecs
    demotion policies
    Episode policies
    query policies

Runtime:
    PoolInstances
    shared fixed byte arenas/slabs
    permanent-ledger hot pages
    EpisodeArena
    temporal indexes

Operations:
    AdmitOccurrence
    RouteOccurrence
    ProjectOccurrence
    AppendRich
    Compact / Demote
    LeaseEpisodePart
    FinalizeEpisodePart
    ContinueEpisode
    FinalizeEpisode
    ReleaseEpisode
    Query
```

This is a semantic architecture, not a frozen API.

---

## 36. Deterministic Requirements

Given identical authoritative occurrences and configuration, history output must be reproducible.

Stable deterministic rules are required for:

- admission order;
- routing/projection order;
- pool write order;
- compaction;
- bucketing;
- pinned-record handling;
- Episode Part numbering;
- Episode lease fallback;
- index generation;
- query tie-breaking.

Never let unordered iteration or thread scheduling become historical truth.

---

### Codec / Schema / Policy Versioning

History determinism depends on the exact interpretation rules used by the save.

Persist or bind durable state to explicit versions/hashes such as:

```text
HistoryDefinitionVersion
RichCodecVersion
SummaryCodecVersion
TrendCodecVersion
DemotionPolicyVersion
EpisodePolicyVersion
QueryPolicyVersion
RulesHash
```

Changing one of these may change:

- encoded bytes;
- compaction output;
- Episode boundaries;
- query results;
- actor decisions downstream.

Old saves must therefore be migrated explicitly or continue using compatible historical semantics. They must not be silently reinterpreted under new deterministic policies.

## 37. Persistence Philosophy

Long history naturally contains hot and cold information.

Prefer:

```text
Hot
    active/recent pages in memory

Cold
    packed immutable persistence pages

Indexes
    rebuildable / disposable / demotable
```

The exact backend format remains implementation work.

---

## 38. Current Open Engineering Questions

The semantic architecture is stable enough to proceed.

Implementation questions intentionally left open include:

- exact arena page/chunk layout;
- variable-length record encoding;
- wrap behavior;
- fragmentation policy;
- shared slab allocation strategy;
- per-instance vs shared tier arenas;
- exact compaction thresholds;
- pinned-record semantics;
- Episode lease arbitration;
- Episode Part size profiles;
- persistence/page format;
- handle/offset widths;
- query cursor representation;
- concurrency/commit semantics;
- cold-page indexing;
- state hashing for deterministic Fate;
- exact information-access indexing.

These should be resolved by implementing and benchmarking the starter proof pools rather than over-specifying them abstractly.

---

## 39. Current Core Rules

1. **History is authoritative truth; actor knowledge is not.**
2. **World Occurrence → History Admission and World Occurrence → Perception are parallel paths. History is not required for experience.**
3. **History storage is domain-owned and byte-budgeted.**
4. **Rolling history ages through semantic compaction, not simple deletion.**
5. **Rich → Summary → Trend represents decreasing temporal/detail resolution.**
6. **Some facts belong in durable ledgers and do not undergo ordinary semantic degradation.**
7. **Logical permanent truth does not imply permanently hot RAM.**
8. **Temporal indexes are derived query structures, not canonical truth.**
9. **Exceptional periods use bounded Episode storage.**
10. **Episodes may span multiple deterministic Parts/chapters/pages.**
11. **A full Episode Part is finalized and submitted before the next Part begins.**
12. **Active Episode Parts may compact internally while still ongoing.**
13. **After final Episode creation, Part retention is policy-driven: important Parts may survive cold; ordinary Parts may demote or disappear after their meaning is safely absorbed.**
14. **Episodes augment normal history rather than replacing baseline truth.**
15. **Pool instances exist only for real scopes that need them.**
16. **Occurrences route into compact domain projections rather than universal event objects.**
17. **Pinning never defeats a byte budget; important information is promoted to an appropriate durable representation.**
18. **Accessibility gates pooled-history knowledge before Relevance/Interest/Salience.**
19. **Communication transfers claims, not authoritative History.**
20. **Working Knowledge is temporary; Personal Memory is earned and sparse.**
21. **Long-lived memory must not reference transient arena offsets.**
22. **World-history degradation and personal-memory degradation are separate.**
23. **Hot Sim Core knowledge queries must remain bounded.**
24. **All admission, compaction, Episode segmentation, query behavior, and tie-breaking must be deterministic.**
25. **History/codec/query semantics are versioned; old saves are never silently reinterpreted under incompatible deterministic rules.**
26. **If richer storage is unavailable, fidelity may fall; memory bounds must not be violated.**
