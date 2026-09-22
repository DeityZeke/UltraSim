# UltraSim Sim Core Reasoning, Attention, Knowledge, and Decision-Making Reference

## 1. Purpose

UltraSim needs a reasoning system capable of driving citizens, animals, monsters, guards, merchants, workers, companions, combatants, and other autonomous actors through the same broad architecture while allowing very different depth, personality, knowledge, and behavior.

The system must support both:

- slow, broad decisions such as where to travel, what work to do, or whether to eat;
- fast reactions such as dodging an incoming attack, fleeing a threat, or reacting to a sudden sound.

It must also scale to very large populations.

The central design principle is:

> **Actors should not continuously reconsider everything. Something changes, the actor notices or experiences it, and only then does the relevant slice of reasoning run.**

Most behavior should therefore be **signal-driven, event-driven, persistent, bounded, and context-specific**.

---


## 2. Runtime State Boundaries

The conceptual model is rich, but the runtime implementation should remain compact.

A hard architectural distinction should exist between:

```text
Actor State
    persistent

Current Intent
    persistent

Personal Memory
    sparse persistent semantic claims / durable references

Reasoning Working Set
    transient

World Records / History
    shared
```

### Actor State

Persistent actor-local state includes things such as:

- Needs;
- stats/resources;
- current condition;
- relationships;
- stable personality/profile references;
- sparse personal overrides.

### Current Intent

The active Intent is persistent because it represents ongoing commitment.

It may survive:

- multiple ticks;
- temporary subgoals;
- pauses;
- save/load;
- short interruptions.

### Personal Memory

Personal memory should remain sparse.

It stores exceptional actor-specific information such as:

- personally salient events;
- relationship-changing events;
- direct observations that matter uniquely to this actor;
- compact semantic claims and, where useful, references to durable Episode/Ledger identities.

Personal Memory must **not** retain raw offsets into rolling history arenas or other transient storage.

It should not duplicate broad world history.

### Reasoning Working Set

Reasoning data should usually be transient and compact.

Examples:

- current candidate list;
- context vectors;
- temporary utility scores;
- current Threat Projections;
- temporary Knowledge Results;
- temporary attention impulses;
- domain prediction state.

These should generally be:

- stack/local data;
- pooled structs;
- fixed arrays;
- arena allocations;
- other cheap transient representations.

Avoid hanging heavyweight managed object graphs off every actor.

### World Records / History

Long-form history belongs in shared pooled storage.

Actors query it as needed through the knowledge system.

The detailed storage/compaction architecture is intentionally deferred to the dedicated pooled-history design.

---

## 3. The Three Primary Reasoning Triggers

Most reasoning can be reduced to three trigger sources.

| Trigger | Meaning | Example |
|---|---|---|
| **Need → Reaction** | Internal state becomes decision-relevant | Hungry, tired, lonely, injured |
| **Perception → Reaction** | Something in the world is noticed | Apple on ground, rustling bush, arguing couple |
| **Effect → Reaction** | Something already happened to the actor | Took damage, got poisoned, was insulted |

Conceptually:

```text
NEED
    "Something inside me changed."

PERCEPTION
    "I noticed something outside me."

EFFECT
    "Something happened to me."
```

All three feed the same downstream decision machinery.

---

## 4. Common Reasoning Pipeline

```text
Need / Perception / Effect
        ↓
Attention / Relevance
        ↓
Knowledge Query
        ↓
Interpretation / Prediction
        ↓
Generate Plausible Outcomes / Reactions
        ↓
Desire
        -
Friction
        ↓
Feasibility / Urgency
        ↓
Compare Against Current Intent
        ↓
Ignore / Acknowledge / Act
        ↓
Persistent Intent / Immediate Reaction
```

The system should avoid expensive all-purpose planning whenever possible.

A trigger should select a **small reasoning template** that touches only the variables relevant to that problem.

---

## 5. Persistent Intent

Actors should usually have a persistent current Intent.

Examples:

```text
TravelTo(Westmarch)
WorkField
Sleep
Eat
GuardGate
Hunt
Socialize
Fight
Flee
Protect(Person)
```

An Intent is not a one-tick command.

It represents:

> **What am I currently trying to accomplish?**

The actor continues pursuing it until something meaningful changes.

Possible interruption reasons include:

- threat appears;
- target disappears;
- task completes;
- path becomes impossible;
- health changes dramatically;
- new high-priority Need appears;
- a more important Perception steals attention;
- a critical Effect occurs.

This is important for both realism and performance.

Actors should not repeatedly ask:

> "Should I still walk to Westmarch?"

every frame.

They should walk until something gives them a reason to reconsider.

---

## 6. Outcomes vs. Action Primitives

The reasoning layer should choose **desired outcomes**, not micromanage every animation or movement step.

Examples of outcomes:

```text
Reach(Location)
Eat(Food)
Rest
Work(Job)
Trade(Person)
Defend(Self)
Escape(Threat)
Attack(Target)
Investigate(Event)
Socialize(Person)
Hunt(Target)
FindFood
Protect(Person)
```

Examples of lower-level action primitives:

```text
Move
Face
Equip
Mount
Dismount
Open
Close
Attack
Block
Parry
Use
Speak
PickUp
Drop
```

The outcome layer asks:

> **What do I want to accomplish?**

The execution layer asks:

> **What actions are required to accomplish it?**

This separation prevents the reasoning system from scoring every footstep.

---

## 7. Strategy Templates Instead of Full General Planning

Most outcomes should have a small number of known strategy templates.

Example:

```text
TravelTo(Target)

if already mounted:
    RideTo

else if horse nearby and riding is worthwhile:
    RetrieveHorse
    Mount
    RideTo
    Dismount / SecureHorse

else:
    WalkTo
```

Example:

```text
AcquireFood

if edible item in inventory:
    Eat

else if known owned food nearby:
    Fetch
    Eat

else if vendor known and affordable:
    Travel
    Buy
    Eat

else if capable of hunting:
    Hunt

else if capable of foraging:
    Forage

else if desperate:
    Beg / Steal / Search
```

The system may still become sophisticated, but it should search over a **small set of meaningful strategies**, not arbitrary combinations of all possible actions.

---

## 8. Need → Reaction

Needs are internal conditions that become strong enough to matter.

Examples:

```text
Hunger
Thirst
Fatigue
Pain
Safety
Social Need
Comfort
Shelter
Duty
Curiosity
```

A Need should not automatically produce an action.

Example:

```text
Hungry Signal
    ↓
Do I care enough yet?
    ↓
Ignore temporarily?
Eat carried food?
Find food?
Finish current task first?
```

The same Hunger level can produce different behavior because of:

- personality;
- current Intent;
- urgency;
- available food;
- risk;
- distance;
- social rules;
- knowledge;
- expected future hunger.

---

## 9. Perception → Reaction

Perception covers external things that may catch notice.

Examples:

```text
Apple on ground
Cat darts past
Bush rustles
People arguing
Window shatters
Bandit raises sword
Smoke visible on horizon
```

Perception should not mean:

> "Every visible thing receives full reasoning."

Instead, world stimuli compete for **Attention**.

---

## 10. Effect → Reaction

Effects represent things that have already happened to the actor.

Examples:

```text
TookDamage
WasShoved
GotPoisoned
ReceivedPayment
WasInsulted
WasHealed
LostItem
ShieldBroke
```

Effects immediately update actor state and may also trigger new reasoning.

Example:

```text
TookHit
    ↓
Health decreases
Pain increases
Balance may worsen
Confidence may change
Threat becomes highly salient
    ↓
Reevaluate current combat response
```

An Effect may coexist with ongoing Perceptions.

Example:

```text
I took a hit
AND
the attacker is preparing another swing
```

The first changes current state.

The second predicts future danger.

Both matter.

---

## 11. Attention

Attention sits between raw perception and deeper reasoning.

```text
World stimulus
    ↓
Perceptible?
    ↓
Notice score
    ↓
Peripheral awareness
    ↓
Focused attention
    ↓
Detailed observation
```

Actors should generally reason deeply about:

- one primary focus;
- a very small number of peripheral concerns;
- not everything around them.

Possible conceptual structure:

```text
Primary Focus
    1 item

Peripheral Attention
    2–4 items

Ambient Awareness
    not individually stored in depth
```

---

### Hard Attention Bounds

Attention must remain explicitly bounded.

A practical invariant is:

```text
PrimaryFocus
    exactly 0 or 1

PeripheralSlots
    small fixed count

RecentAttentionImpulseRing
    bounded fixed-capacity buffer

AmbientAwareness
    implicit / sampled
```

The Attention system must never quietly grow into an unbounded per-actor list.

If new stimuli exceed capacity, they compete by attention score and decay; low-value entries are evicted or never promoted.

## 12. Attention Impulses and Decay

Events generate temporary Attention impulses.

Example:

```text
Cat darts past

Motion             +40
Suddenness         +30
Threat               0
Personal relevance   0

Result:
    moderate attention spike
    very fast decay
```

Example:

```text
Couple arguing loudly

Sound              +45
Sustained activity +30
Social interest    +20

If Nosey:
    additional large bias

If actor knows them:
    relationship bias
```

Example:

```text
Window suddenly shatters

Loudness            very high
Suddenness          very high
Unexpectedness      very high
Potential hostility high

Result:
    sharp attention spike
    likely steals focus
```

A person watching an argument may immediately turn toward the broken window.

The arguing people themselves may also pause and look.

---

## 13. Attention Decay

Attention should fall over time unless reinforced.

Examples:

```text
Cat ran past
    high initial spike
    very fast decay

Argument
    medium/high spike
    slow decay while continuing

Bandit aiming at me
    huge spike
    little/no decay while threat persists

Friend crying
    relationship keeps attention high

Apple beside road
    small spike
    decays quickly after decision to ignore
```

Attention is therefore not just a static priority.

It is a temporary competition between things that currently matter.

---

## 14. Personality Modifies Attention

Different actors should notice different things.

Examples of cached attention biases:

```text
Nosey
    SocialDramaAttention +high

Cowardly
    PotentialThreatAttention +high

Hunter
    AnimalMovementAttention +high

Guard
    ViolenceAttention +very high

Merchant
    TheftAttention +high
```

This lets personality influence behavior before decision-making even begins.

Two actors may inhabit the same world but consciously register different subsets of it.

---

## 15. Perception Is a Derived Capability Family

Perception should not be collapsed into one simple number.

Useful conceptual dimensions include:

| Capability | Meaning |
|---|---|
| **Range** | How far something can potentially be perceived |
| **Field / Arc** | Width of effective awareness |
| **Acuity** | Detail that can be resolved |
| **Notice** | Ease of entering attention |
| **Detection** | Ability to notice hidden/subtle things |
| **Identification** | Ability to understand what was perceived |

These may be derived from:

```text
Species / senses
Facing
Lighting
Weather
Distance
Movement
Wisdom
Intelligence
Relevant Skill
Fatigue
Fear
Injury
Familiarity
Equipment
```

This preserves detailed observation without requiring another giant permanent stat sheet.

---

## 16. Seeing Is Not the Same as Noticing

Example:

> A tree branch lies on the ground 12 meters to the actor's left.

The perception system may determine:

```text
Potentially visible:
    yes

Peripheral:
    yes

Acuity:
    enough to identify as branch
```

But the actor may still fail to consciously care about it.

A woodsman may notice because:

- useful fuel;
- useful tool material;
- familiarity with forest resources.

A noble may barely register it.

For the player, a noticed pickupable object may be represented visually as an item sparkle or similar cue.

---

## 17. Observation

Once something earns enough Attention, the actor may perform a deeper Observation.

Observation asks:

> **What can I actually determine about this focused thing?**

Example:

```text
Bush rustles 50m ahead
```

Initial observation:

```text
Localized foliage movement
Cause unknown
```

Focused observation may examine:

```text
Movement pattern
Wind
Sound
Silhouette
Local wildlife
Recent history
Known threats
```

The result remains actor-relative and uncertain.

---

## 18. Prediction

Prediction is a short-term interpretation of focused Observation.

It asks:

> **Given what I currently observe and know, what is likely to happen next?**

Prediction should normally follow focus.

The actor should not generate future projections for every nearby object.

Examples:

```text
Focused bandit
    sword arm drawing back

Prediction:
    probable swing
    probable target = me
    short time to impact
```

Outside combat:

```text
Focused bush
    unusual movement

Prediction:
    wind?
    animal?
    ambush?
```

Prediction is useful both inside and outside combat.

---

### Prediction Is Domain-Specific

Do not build one giant universal Prediction Engine.

Prefer a shared conceptual interface with small domain-specific evaluators such as:

```text
CombatPrediction
SocialPrediction
TravelPrediction
TaskPrediction
AnimalBehaviorPrediction
```

Each domain should:

- consume only relevant observations/knowledge;
- generate only a small bounded set of likely outcomes;
- expose confidence/severity/timing where useful.

This preserves the unified reasoning model without creating an expensive general-purpose future simulator.

## 19. Threat Projection

Combat uses Prediction at high frequency.

A useful temporary structure is:

```text
ThreatProjection
    Source
    ApparentAction
    ApparentTarget
    TimeToImpact
    Direction / Arc
    Reach
    EstimatedSeverity
    Confidence
```

The data should represent **what the actor believes**, not omniscient truth.

A master swordsman may identify:

> heavy overhead swing, ~400 ms to impact.

A frightened civilian may only understand:

> large axe coming at me now.

---

## 20. Combat Reaction

Combat is not a separate decision architecture.

It is the same reasoning system operating with:

- very high urgency;
- tiny candidate sets;
- focused Prediction;
- fast event updates.

Example:

```text
Incoming Swing
    ↓
Possible Responses

Block
Parry
Dodge
Evade
Counter
Brace
Tank
Flee
```

Each response still uses:

```text
Desire
Friction
Feasibility
Urgency
```

---

## 21. Combat Uses Both Consequence and Prediction

Example:

```text
I took a hit
AND
the enemy is preparing another swing
```

Observed consequence:

```text
Health lower
Pain higher
Possible bleeding
Balance damaged
Confidence changed
```

Projected consequence:

```text
Second attack incoming
```

The next response is evaluated using the updated state.

No hardcoded rule such as:

```text
if hit:
    flee
```

is required.

---

## 22. Feints and Misreading

Because combat Prediction is based on Observation, actors can be fooled.

A feint creates a convincing false Threat Projection.

Prediction quality may depend on:

```text
Relevant Combat Skill
Domain Mastery
Dexterity
Intelligence
Wisdom
Familiarity with opponent
Familiarity with weapon
Visibility
Fatigue
Fear
```

This is preferable to a hidden modifier such as:

```text
Feint = -20 Defense
```

The actor is actually misinterpreting the opponent.

---

## 23. World-Derived Knowledge Is Mostly Resolved From Pooled History

> **Hard invariant: actors reason from believed records, not simulation truth.**


Most **world-derived knowledge** should be resolved from shared history pools rather than copied wholesale into every actor.

Examples of shared authoritative sources include:

```text
WeatherHistory
CrimeHistory
MarketHistory
PoliticalHistory
LocalEvents
Deaths
Births
Harvests
Wars
MonsterSightings
PropertyEvents
```

Actors should not each carry full duplicated histories.

Instead, an actor receives bounded actor-relative knowledge through legitimate access, observation, communication, sparse personal memory, and history queries.

History remains authoritative truth. Knowledge remains actor-relative belief.

---

## 24. Knowledge Query Dimensions

Knowledge derived from shared history is first gated by **Accessibility / Exposure**:

> **Could this actor legitimately have access to this information at all?**

Only after that gate passes do the three main actor-relative dimensions apply:

| Dimension | Question | Main effect |
|---|---|---|
| **Relevance** | "How deeply would this actor know/query this subject?" | Query depth |
| **Interest / Care** | "How much do I care about this?" | Granularity/detail |
| **Salience / Impact** | "Did this particular event matter to me?" | Retention/priority |

Returned knowledge can also contain:

```text
Confidence
Age
Precision
Source
```

Confidence is useful metadata but does not necessarily need to be one of the three main query dimensions.

---

## 25. Relevance Controls Depth

A farmer may have deep Weather relevance.

A noble may not.

Same pooled history:

```text
WeatherHistory
```

Farmer query:

```text
Relevance = very high
    deeper history
    more precise chronology
```

Noble query:

```text
Relevance = low
    shallow/general recollection
```

The system does not need separate copied Weather histories for each actor.

---

## 26. Interest Controls Granularity

Interest determines how much detail matters to the actor.

A farmer may care about:

- timing of rain;
- duration;
- soil condition;
- field effects.

A noble may care only that:

> it rained recently.

---

## 27. Salience Preserves Personal Events

Low-interest subjects may still contain personally important events.

Example:

```text
Heavy rain
    6 days ago

Personal consequence:
    cancelled planned journey

Salience:
    high
```

The noble may therefore remember:

> "It rained last Tuesday and ruined my trip."

They still do not gain the farmer's complete Weather history.

---

## 28. Sparse Personal Memory

Most actors should store only exceptional personal memories or modifiers.

Examples:

```text
Roger punched me yesterday
My barn burned down
Alice saved my life
Rain cancelled my trip
I saw a dragon north of town
```

These sparse records can strongly influence later queries.

Most ordinary world history remains pooled.

---

## 29. Confidence and Imperfect Knowledge

Actors should decide based on what they believe, not omniscient truth.

Possible result:

```text
KnowledgeResult
    Value
    Age
    Confidence
    Source
    Precision
```

Example:

```text
Personally observed:
    "North field flooded yesterday"
    Confidence = high

Heard from traveler:
    "Bridge may be flooded"
    Confidence = medium

Rumor:
    "Entire valley is underwater"
    Confidence = low
```

This allows actors to make reasonable but incorrect decisions.

---

## 30. Knowledge Modifies Desire and Friction

Knowledge is not a separate decision system.

It supplies modifiers to the current decision.

Example:

> "I want to travel to Westmarch."

Knowledge may increase Desire:

```text
Good market prices
Friend lives there
Festival happening
Job available
Needed item sold there
```

Knowledge may increase Friction:

```text
Recent bandit sighting
Traveler killed on west road
Wolf attacks
Heavy rain
Muddy road
Damaged bridge
```

The same shared records can produce different actor-relative beliefs.

---

## 31. Desire

Desire answers:

> **Why would I want this outcome?**

Sources include:

```text
Needs
Preferences
Personality
Relationships
Duty
Goals
Current Intent
Expected reward
Knowledge / Working Knowledge
Personal Memory
Traits
```

Example:

```text
Pick Up Apple

Desire:
    Hunger
    Expected hunger soon
    Likes apples
    Hoarder tendency
    Food scarcity knowledge
```

Desire should not contain every negative factor.

Those belong primarily to Friction.

---

## 32. Friction

Friction is a first-class decision concept.

It answers:

> **What makes pursuing this outcome inconvenient, costly, dangerous, uncertain, disruptive, or difficult?**

Useful Friction categories include:

```text
Distance
Time
Transition
Physical Effort
Resource Cost
Danger
Social Cost
Legal Cost
Uncertainty
Opportunity Cost
Psychological Cost
```

This prevents technically optimal behavior that looks absurd.

---

### Friction Channels

Internally, Friction should remain decomposable for scoring and debugging.

A small fixed vocabulary is preferable to arbitrary dynamic labels.

Current useful channels:

```text
Distance
Time
Transition
Effort
Resource
Danger
Social
Legal
Uncertainty
Opportunity
Psychological
```

Example:

```text
TravelTo(Westmarch)

Desire:
    +120

Friction:
    Distance       20
    Danger         35
    Time           15
    Transition      5
    Social          0
```

The final decision may use a combined score, but the channels should remain inspectable.

## 33. Travel Friction Example

Bad AI:

```text
Tavern is 22m away
Horse is faster
Therefore:
    mount horse
    ride to tavern
```

Correct reasoning:

```text
Walk To Tavern

Friction:
    short walking distance
```

versus:

```text
Ride To Tavern

Friction:
    retrieve horse
    mount
    crowded town
    short-distance inefficiency
    dismount
    tether / secure horse
```

Walking wins.

---

## 34. Friction Scales With Context

Useful context biases:

```text
ShortDistance
    strongly disfavors riding

InsideTown
    disfavors riding

CrowdedArea
    disfavors riding

BadHorseTerrain
    disfavors riding

AlreadyMounted
    greatly reduces transition friction

HeavyLoad
    favors riding

LongDistance
    favors riding

UrgentTravel
    favors faster travel
```

These should usually be weights/curves, not hard rules.

---

## 35. Risk Is Friction

Threat evaluation is largely a form of anticipated Friction.

Example:

```text
Outcome:
    Fight Bandit

Desire:
    self-defense
    protect family
    aggression
    hatred of bandits

Friction:
    bandit looks strong
    large weapon
    I am injured
    three enemies
    low combat experience
```

"Does he look tough?" is not negative Desire.

It is anticipated Danger Friction.

---

## 36. Personality Changes Perceived Friction

Different actors can perceive the same obstacle differently.

Examples:

```text
Courageous
    danger friction reduced

Cowardly
    danger friction increased

Lazy
    physical effort friction increased

Greedy
    spending-money friction increased

Reckless
    danger friction reduced

Patient
    time friction reduced

Proper noble
    social embarrassment friction increased

Shameless
    social embarrassment friction reduced
```

Personality changes the decision landscape rather than creating large bespoke behavior trees.

---

## 37. Feasibility

Feasibility answers:

> **Can I actually do this?**

Examples:

```text
Do I have the weapon?
Can I reach the target?
Can I afford the purchase?
Can I carry the object?
Is there room to dodge?
Do I know the route?
Can I cast the spell?
```

Impossible options should be discarded early.

---

## 38. Urgency

Urgency answers:

> **How much does delay matter?**

Examples:

```text
Hungry
    moderate urgency

Starving
    high urgency

Apple beside path
    low urgency

Sword incoming
    extreme urgency

House on fire
    extreme urgency
```

High urgency can justify accepting much greater Friction.

---

## 39. Switching Friction

Changing Intent has a cost.

This is critical to prevent behavioral thrashing.

Example:

```text
Current Intent:
    TravelToTown

New possibility:
    Apple 20m off trail
```

The apple must beat:

```text
CurrentIntentValue
+ SwitchingFriction
```

Otherwise the actor keeps walking.

Urgent threats can trivially overcome Switching Friction.

Minor opportunities usually cannot.

---

## 40. Ignore / Acknowledge / Act

A signal can resolve into three broad outcomes:

```text
IGNORE
    not worth processing further

ACKNOWLEDGE
    update knowledge / awareness
    do not change current behavior

ACT
    insert subgoal or interrupt current Intent
```

Example:

```text
Distant wolf seen
    acknowledge

Wolf approaches closely
    act
```

Example:

```text
Apple seen while full
    acknowledge location

Later hungry
    knowledge query may recall apple
```

---


## 41. Intent Commitment Lifecycle

Intent should have an explicit lifecycle rather than behaving as an ad hoc score winner.

A useful conceptual lifecycle is:

```text
Candidate
    ↓
Selected
    ↓
Committed
    ↓
Executing
    ├── Subgoal
    ├── Paused
    ├── Interrupted
    ├── Failed
    └── Completed
        ↓
Resume / Replan / Finish
```

### Commitment

Once an Intent wins, it becomes **Committed**.

Commitment means:

- the actor stops continuously competing it against trivial alternatives;
- Switching Friction now protects it;
- execution begins.

### Subgoal

A Subgoal supports the current Intent without replacing it.

### Pause

A temporary condition may pause execution without abandoning the Intent.

### Interrupt

A high-priority event may suspend the current Intent and push a temporary replacement.

The original Intent may later resume.

### Failure

Failure should produce a bounded reconsideration:

```text
strategy failed
    ↓
try alternate strategy?
    ↓
reassess feasibility/friction?
    ↓
abandon Intent only if needed
```

### Completion

Completion resolves the Intent and returns the actor to broader decision-making.

This lifecycle should persist cleanly across save/load and LOD changes.

---

## 42. Subgoals vs. Interrupts

Not every action requires replacing the current Intent.

Example:

```text
TravelToVillage
    ↓
See apple 2m from path
    ↓
Subgoal:
    PickUpApple
    ↓
Resume TravelToVillage
```

But:

```text
TravelToVillage
    ↓
Bandits attack
    ↓
Interrupt:
    SurviveThreat
```

The system should preserve interrupted Intents where appropriate.

---

## 43. Decision Templates

Most signals should map to a small fixed Decision Template.

Example:

```text
EdibleObjectObserved
    Ignore
    PickUp
    EatNow
    Remember
```

Example:

```text
ThreatObserved
    Ignore
    Observe
    Fight
    Flee
    Hide
    CallHelp
    WarnOthers
```

A template should read only a small number of relevant features.

---

## 44. Small Context Vectors

For performance, common decision types should evaluate small fixed context vectors.

Example:

```text
PickUpFood Context

HungerNow          0..255
ExpectedHunger     0..255
FoodPreference    -255..255
DistanceCost       0..255
HoardingBias      -255..255
CautionBias       -255..255
OwnershipRisk      0..255
CarryAvailability  0..255
```

Then only a few plausible responses are scored.

This is much cheaper than a fully generic planner inspecting hundreds of variables.

---

## 45. Weight Representation

UltraSim already has a compact **bipolar byte table** concept.

```text
MagnitudeBytes[index] : byte      // 0..255
SignBits[index]       : bit       // positive / negative
```

Semantic range:

```text
-255 ... -1, 0, +1 ... +255
```

Because:

```text
+0 == -0
```

there are 512 raw encodings but only 511 ordinary numeric values.

The duplicate zero representation can therefore be reserved as a special semantic state/flag where useful.

A current possible convention is to use that duplicate-zero state to distinguish a normalized/off interpretation from a magnitude/on interpretation without adding another storage array.

Exact encoding semantics should remain centralized and explicit.

---

## 46. Weight Storage Cost

The bipolar table costs approximately:

```text
1 byte magnitude
+ 1 sign bit
≈ 1.125 bytes per stored value
```

This is suitable for large numbers of:

- personality weights;
- preference weights;
- disposition weights;
- decision biases;
- other signed scalar modifiers.

Arithmetic can widen to normal integers during evaluation.

Persistent storage remains compact.

---

## 47. Not Every Value Needs to Be Bipolar

Use bipolar values when opposing direction matters.

Examples:

```text
Hate <-> Love
Avoid <-> Seek
Distrust <-> Trust
Cowardice <-> Courage
Dislike <-> Preference
```

Use ordinary magnitudes when negative meaning is unnecessary.

Examples:

```text
Relevance
Granularity
Salience
Confidence
Urgency
Distance Cost
Threat Magnitude
```

---

## 48. Personality Is Mostly Stable and Cacheable

Personality generally changes rarely.

Examples:

```text
Aggression
Courage
Curiosity
Sociability
Patience
Greed
Hoarding
Caution
Nosiness
```

These values can be cached or precombined into useful response-bias profiles.

Decision evaluation should avoid repeatedly rebuilding personality state.

---

## 49. Shared Defaults + Sparse Overrides

The largest memory savings should come from **shared profiles**, not increasingly exotic bit packing.

Useful sources:

```text
Species Defaults
Culture Defaults
Profession Defaults
Personality Profile
Traits
Sparse Individual Overrides
```

Example:

```text
Human
    baseline weights

Rural Culture
    weather relevance +
    agriculture relevance +

Farmer Profession
    weather relevance ++
    crop relevance ++
    market relevance +

Individual Overrides
    horses preference high
    rain dislike
    Roger salience high
```

The individual actor should store only what truly differs.

---

## 50. Candidate Generation Must Be Aggressive

Scoring speed is not enough if every actor evaluates thousands of world entities.

Candidate generation should narrow options before utility evaluation.

Example:

```text
Intent:
    Eat

Candidate sources:
    edible inventory items
    remembered nearby food
    known vendors
    nearby forage targets
```

Then score perhaps:

```text
3–12 candidates
```

not every edible object in the world.

Useful infrastructure includes:

- spatial indexes;
- ownership indexes;
- relationship caches;
- perception results;
- known-location caches;
- job indexes;
- route/path caches.

---

## 51. Reconsider Only When Relevant State Becomes Dirty

A core scalability rule:

> **Actors should reconsider only when decision-relevant state changes enough to matter.**

Possible dirtying events:

```text
Hunger threshold crossed
Threat appeared
Target disappeared
Task failed
Weather changed significantly
Job completed
Relationship changed
Inventory changed
New knowledge arrived
Damage received
Current path failed
```

Most actors should spend most of their time simply continuing their current Intent.

---

## 52. Broad vs. Fast Decision Cadence

Not all reasoning occurs at the same rate.

| Layer | Typical role |
|---|---|
| **Strategic / Life** | What should I broadly be doing? |
| **Tactical** | How should I accomplish the current goal? |
| **Reactive** | What must I respond to immediately? |

Strategic reasoning may run every second, every several seconds, or on schedule/Need changes.

Tactical reasoning runs when context changes or a plan step fails.

Reactive reasoning is mostly event-driven and bounded to a small response set.

---

### Decision Scheduling and Work Budgets

Signals should enqueue bounded reasoning work rather than executing arbitrary amounts immediately.

A world-level scheduler should eventually consider:

- signal priority;
- actor simulation LOD;
- urgency;
- whether the actor is already queued;
- per-tick/per-frame reasoning budgets;
- starvation prevention for low-priority work.

Reactive combat work may receive very high priority.

Background reconsideration can be deferred safely.

The scheduler should merge/reduce redundant dirty signals where possible so one actor does not perform multiple equivalent reconsiderations in the same step.

## 53. Combat Should Not Run Full Reasoning Every Frame

While combat is active, actors may maintain:

```text
Current Combat Intent
Current Target(s)
Active Threat Projections
```

Events update that state:

```text
EnemyWindupStarted
EnemyChangedTarget
ProjectileDetected
AttackCancelled
AttackImpact
ShieldBroken
AllyDown
TargetRetreated
```

Only meaningful changes trigger reevaluation.

---

## 54. Decision LOD

Different actors can reason at different fidelity depending on simulation relevance.

```text
Tier 0 — Active
    full perception
    reactive combat
    detailed tactical reasoning

Tier 1 — Nearby
    normal utility decisions
    simplified execution

Tier 2 — Background
    coarse Intent selection
    abstract movement/work

Tier 3 — Dormant / Strategic
    schedule/event simulation
    no moment-to-moment action reasoning
```

The same Intent can exist across tiers.

Example:

```text
High Fidelity:
    GoToBakery
        pathfind
        open door
        enter
        buy bread

Background:
    GoToBakery
        after estimated duration:
            actor location = bakery
            money -= cost
            bread += 1
```

Same intent.

Different resolution.

### Cross-System Resolution Invariant

Decision LOD must preserve the cross-system simulation-time/resolution contract defined in [Simulation Time / Resolution / Control Authority](../../../AIOS/canonical/simulation-time-resolution-and-control.md).

Background/Dormant processing is not permission to invent unrelated "fake NPC" rules. When the starting state, definitions, ordered external inputs, and deterministic Fate are the same, safe fine/coarse resolution should preserve the same meaningful authoritative outcome.

A causally sealed Intent or interaction may be advanced headlessly or collapsed into an equivalent larger state transition. If an external actor/event could observe or change the outcome, advancement must stop at that next causal boundary and materialize/escalate as needed.

Decision authority is orthogonal to LOD: an actor may be Direct, Autonomous+Intervention, Command/Queue, or governed through higher-level policy while still moving between Active/Nearby/Background/Dormant resolution.

---

## 55. Animals and Monsters Use the Same Architecture

A bear does not need a separate reasoning engine.

It uses the same system with different:

- Needs;
- available Intents;
- personality weights;
- perception;
- skills/stats;
- knowledge depth;
- candidate generators.

Example:

```text
Bear Needs
    hunger
    safety
    territory
    rest
    offspring protection

Preferences
    fish +
    berries +
    humans -

Knowledge
    fishing spot A recently poor
    fishing spot B historically good
```

The bear can make meaningful decisions without carrying a human-scale social model.

---

## 56. Example: Apple Beside the Road

Current Intent:

```text
TravelTo(Village)
```

Perception:

```text
Apple observed
Distance = 2m
```

Relevant questions:

```text
Am I hungry?
Will I be hungry soon?
Do I like apples?
Am I a hoarder?
Am I cautious?
Does it belong to someone?
Do I have inventory space?
How far out of my way is it?
```

Possible result:

```text
PickUpApple
    Desire high
    Friction low
    inserted as subgoal
```

If the apple is 80m away:

```text
Distance Friction rises sharply
    ↓
Ignore / acknowledge instead
```

---

## 57. Example: Traveling to Westmarch

Current desire:

```text
TravelTo(Westmarch)
```

Knowledge query asks only decision-relevant history.

Possible Desire modifiers:

```text
festival happening
good market prices
friend lives there
job available
```

Possible Friction modifiers:

```text
recent bandit attack
recent death on road
wolf sightings
heavy rain
mud
bridge damage
```

A cautious actor may heavily weight these risks.

A reckless actor may know the same facts and discount them.

A poorly informed actor may walk into danger because their knowledge is stale.

---

## 58. Example: Threat From Bandits

Perception:

```text
Hostile actor observed
```

Observation:

```text
muscular
large axe
armor
three allies
possibly noticed me
```

Prediction:

```text
likely attack
```

Response candidates:

```text
Fight
Flee
Hide
Evade
CallForHelp
WarnOthers
Surrender
```

Evaluation uses Desire, Friction, Feasibility, and Urgency.

A veteran and a civilian may react very differently.

---

## 59. Example: Incoming Attack

Focused target:

```text
Bandit
```

Observation:

```text
weapon drawing back
```

Threat projection:

```text
likely swing
target = me
short time to impact
high severity
```

Candidate responses:

```text
Parry
Block
Dodge
Evade
Counter
Brace
Flee
```

The actor chooses only among plausible immediate reactions.

No global life planning is required.

---

## 60. Example: Took Hit + Another Swing Incoming

Effect:

```text
TookHit
```

State updates:

```text
Health lower
Pain higher
Balance worse
Confidence changed
```

Perception:

```text
attacker preparing another swing
```

Prediction:

```text
second attack incoming
```

The next reaction uses both.

The actor may switch from fighting to fleeing without any hardcoded "if hit then flee" rule.

---

## 61. Example: Rustling Bush

Perception:

```text
Bush moving 50m ahead
```

Attention rises because:

```text
unexpected motion
possible animal
possible threat
```

Focused observation may query:

```text
wind
movement pattern
sound
silhouette
local wildlife
recent attacks
```

Prediction:

```text
wind?
small animal?
large animal?
ambush?
```

Responses:

```text
Ignore
Observe longer
Approach
Avoid
Ready weapon
Warn companion
```

---

## 62. Example: Attention Stealing

Current focus:

```text
Loud argument
```

A window suddenly shatters.

New stimulus has:

```text
high loudness
high suddenness
high unexpectedness
high hostility potential
```

The new Attention score overtakes the argument.

Focus switches.

The arguing participants may also stop arguing and look.

No scripted scene is required.

---

## 63. Deterministic Ordering and Tie-Breaking

UltraSim's reasoning must remain deterministic under identical authoritative state and inputs.

Never rely on:

```text
unordered container iteration
first candidate encountered
unstable hash ordering
thread scheduling order
```

for decision outcomes.

Candidate generation should produce or normalize into a stable order.

Equal-scoring outcomes should use deterministic tie-breaking such as:

```text
Stable Candidate ID
Intent Type ID
Actor-specific deterministic seed
Authoritative Fate/RNG input
```

Any randomized variation must use the project's deterministic authoritative RNG/fate system.

Rule:

> **Same actor state + same perceived inputs + same knowledge + same authoritative random input = same decision.**

This requirement applies to:

- candidate ordering;
- equal-score ties;
- attention eviction;
- strategy selection;
- prediction alternatives;
- background LOD resolution.

---

### Definition / Codec Versioning

Deterministic behavior across saved worlds requires versioned rules.

Persist or otherwise bind saves to stable identifiers such as:

```text
DecisionDefinitionVersion
ReasoningRulesHash
HistoryDefinitionVersion
CodecVersion
DemotionPolicyVersion
QueryPolicyVersion
```

Changing a codec, compaction rule, query policy, candidate definition, or scoring rule can change the future of an existing world even when the raw save data is unchanged.

Migration between versions must therefore be explicit rather than silently reinterpreting old state under new deterministic rules.

## 64. Core Data Philosophy

The reasoning system should prefer:

- compact numeric IDs;
- contiguous arrays;
- pooled history;
- shared profiles;
- sparse overrides;
- bounded candidate sets;
- cached stable weights;
- event-triggered reevaluation;
- persistent Intents;
- lightweight temporary projections.

Avoid:

- per-actor giant object graphs;
- string-based lookup in hot paths;
- scanning global entity lists during decisions;
- rebuilding personality every evaluation;
- full GOAP search for ordinary behavior;
- every-frame global reasoning.

---

## 65. Decision Snapshot

For hot-path reasoning, useful dynamic values can be gathered into a compact snapshot.

Example:

```text
DecisionSnapshot

Hunger
Fatigue
HealthRatio
StaminaRatio
ThreatLevel
CurrentSafety
WealthPressure
SocialNeed
CurrentIntent
CurrentFocus
Urgency
```

Stable personality and profile data should remain cached/shared.

Knowledge should generally be queried on demand.

---

## 66. Knowledge Should Not Be Preloaded Into Every Snapshot

Do not attach a giant:

```text
Knowledge[]
```

to every decision evaluation.

Instead expose a bounded query interface:

```text
QueryKnowledge(
    Actor,
    Subject,
    Context
)
```

The result is synthesized from:

```text
Pooled History
Relevance
Interest
Salience
Confidence
Personal Overrides
```

and returns only the information needed for the current decision.

---

## 67. Possible Intent / Decision Contract

A future implementation may formalize concepts such as:

```text
IntentDefinition
    Preconditions
    CandidateGenerator
    RelevantFeatures
    UtilityEvaluator
    RequiredCapabilities
    StrategySet
    ExpectedEffects
    InterruptPolicy
```

This is conceptual rather than frozen API design.

Each decision type should know:

- what candidates are plausible;
- what data it needs;
- how to score them;
- how to execute the winner;
- what can interrupt it.

---

## 68. Desirable Scoring Shape

A conceptual score can be thought of as:

```text
Attractiveness
≈
Desire
- Friction
adjusted by:
    Feasibility
    Urgency
    Confidence
    Current Intent / Switching Cost
```

This does **not** require one universal literal formula.

Different templates may use:

- weighted sums;
- response curves;
- thresholds;
- multiplicative gates;
- normalized comparisons.

The semantic layers matter more than one fixed mathematical expression.

---

## 69. Pooled History Subsystem Contract

The reasoning system depends on a dedicated long-history subsystem, but does **not** own that subsystem's storage details.

```text
Authoritative World Occurrence
        ↓
History Admission / Projection
        ↓
Domain History Pool
    Rich → Summary → Trend
        │
        ├── Durable Ledger where exact permanence is required
        └── Episode Arena during exceptional periods
                ↓
        Historical Accessibility
                ↓
        Actor Knowledge Query
                ↓
Bounded actor-relative KnowledgeResult / Claim
```

### Experience Does Not Require History Admission

Perception/experience and History admission are parallel consequences of a world occurrence.

```text
World Occurrence
    ├── History Admission / Projection
    └── Perception / Observation
```

An occurrence does **not** need to be admitted into a durable world-history pool for nearby actors to perceive it, learn from it, or form a personal memory.

Example:

```text
Child drops an apple
    not historically important enough for SettlementHistory

Mother standing nearby
    sees it
    may react
    may remember it if personally salient
```

History is therefore not a prerequisite for lived experience.

### History vs. Information vs. Memory

Keep the boundary strict:

```text
History
    authoritative temporal truth

Information / Claims
    actor-relative interpretation or communication

Working Knowledge
    short-lived actor-local claims

Personal Memory
    sparse persistent actor-relative memory
```

A rumor does not become History merely because somebody believes or repeats it. The fact that a rumor or accusation was made may itself be authoritative history, but that is a different occurrence.

### Accessibility Comes Before Relevance

```text
Accessibility / Exposure
    "Could this actor legitimately possess this information?"
        ↓
Relevance
    "How deeply would this actor know/query this subject?"
        ↓
Interest
    "How much detail matters to this actor?"
        ↓
Salience
    "Did this particular event matter enough to persist?"
```

This prevents strong interest from becoming accidental omniscience.

### Bounded Query Contract

```text
QueryKnowledge(
    actor,
    subject/domain,
    context,
    requested depth/detail
)
    -> bounded actor-relative KnowledgeResult
```

Normal hot-path reasoning must not scan centuries of history. The reasoning layer should not care whether the answer came from a recent Rich record, Summary, Trend, active Episode Part, finalized Episode, durable ledger, communicated claim, or sparse personal memory.

### Episode Parts / Chapters

An Episode is **logically continuous but physically bounded**.

When the current Episode segment reaches capacity:

```text
Active Episode — Part 1
    becomes full
        ↓
Finalize Part 1 as a partial Episode record
        ↓
Submit/link Part 1 into normal history
        ↓
Open Episode — Part 2
        ↓
Continue
```

and later:

```text
Great Fever
    Part 1
    Part 2
    Part 3
    ...
```

Each Part is independently bounded, deterministically ordered, and associated with the same logical Episode. Parts may still compact internally while active.

When the overall Episode ends, a final Episode summary may reference or subsume all Parts.

This is the preferred answer to an Episode outliving one segment's storage budget: **close the current chapter and continue in another bounded chapter**, rather than allocate an unbounded buffer.

If the global Episode Arena cannot provide another segment, the implementation may fall back to a smaller segment profile or ordinary rolling history. Exact lease arbitration remains an implementation question.

### Stable Long-Lived References

Rolling arena offsets must not become permanent actor-memory identities. Long-lived references should be earned by durable objects such as finalized Episode IDs, ledger IDs, or major historical landmarks.

Promoted Personal Memory should normally carry its own compact semantic claim and may optionally reference one of those durable identities.

### Physical Storage Guidance

Logical per-pool byte budgets should preferably be backed by shared domain slabs/pages rather than thousands of independent tiny allocations.

Compaction should be incremental, semantic, deterministic, and preferably triggered by writes or bucket rollover rather than world-wide scans.

The dedicated **World History / Knowledge Pools** reference owns the detailed storage, compaction, Episode, ledger, and information-transfer architecture.

### Anti-Bloat Ladder

> **Not every state change is historical.**  
> **Not every historical fact becomes information.**  
> **Not every piece of information becomes somebody's memory.**  
> **Not every memory is worth communicating.**

This is a foundational Sim Core invariant.

---

## 70. Current Core Vocabulary

**Need**  
An internal state that may demand action.

**Perception**  
A potentially observable external stimulus.

**Effect**  
Something that has already happened to the actor.

**Attention**  
How strongly a stimulus competes for conscious processing.

**Focus**  
The thing currently receiving detailed observation/reasoning.

**Observation**  
Actor-relative interpretation of focused perceptual data.

**Prediction**  
Short-term estimate of what the focused thing may do next.

**Threat Projection**  
A combat/high-risk prediction containing likely action, target, timing, severity, and confidence.

**Knowledge Query**  
A bounded actor-relative lookup against pooled history.

**Relevance**  
How deeply the actor is likely to know a subject.

**Interest / Care**  
How much detail/granularity matters to the actor.

**Salience**  
How strongly a particular event remains personally important.

**Confidence**  
How trustworthy the actor believes returned knowledge to be.

**Desire**  
Reasons the actor wants an outcome.

**Friction**  
Costs, risks, inconvenience, uncertainty, disruption, and barriers associated with pursuing an outcome.

**Feasibility**  
Whether the actor can actually perform the option.

**Urgency**  
How much delay matters.

**Switching Friction**  
The cost of abandoning or suspending the current Intent.

**Intent**  
A persistent goal/outcome the actor is currently pursuing.

**Subgoal**  
A temporary nested task that supports the current Intent.

**Interrupt**  
A higher-priority event that temporarily or permanently replaces the current Intent.

---

## 71. Core Rules

1. **Most reasoning begins with Need, Perception, or Effect.**
2. **Actors should not deeply analyze everything around them.**
3. **Attention determines what earns deeper reasoning.**
4. **Prediction normally applies only to focused subjects.**
5. **Knowledge is mostly pooled and queried, not duplicated per actor.**
6. **Actors reason from belief, not omniscient world state.**
7. **Desire explains why an outcome is attractive.**
8. **Friction explains why an outcome is costly, risky, inconvenient, or difficult.**
9. **Personality can modify both Desire and perceived Friction.**
10. **Knowledge/history can modify both Desire and Friction.**
11. **Impossible outcomes should be rejected by Feasibility before expensive scoring.**
12. **Urgency determines how much Friction an actor is willing to tolerate.**
13. **Changing Intent has Switching Friction.**
14. **Decisions should produce persistent Intent, not one-frame commands.**
15. **Subgoals should preserve broader Intents where possible.**
16. **Combat uses the same architecture with short-horizon Prediction and extreme Urgency.**
17. **Stable personality/preference data should be cached/shared.**
18. **Candidate generation should aggressively narrow the world before scoring.**
19. **Reconsideration should occur when decision-relevant state changes, not constantly.**
20. **Decision fidelity should scale with simulation relevance.**

---

## 72. Open Questions / Future Implementation Work

The detailed history-storage subsystem is specified separately in the **World History / Knowledge Pools reference**.

The conceptual model is now fairly complete, but implementation details remain open:

- exact Attention score formula;
- exact Attention decay model;
- size of Primary/Peripheral Attention sets;
- how often ambient perception is sampled;
- exact Perception derived-stat formulas;
- how Observation precision scales with Skills/Stats;
- exact Threat Projection representation;
- how feints and uncertainty are encoded;
- exact bipolar-byte special-zero convention;
- how profile layers are combined/cached;
- exact knowledge-pool indexing scheme;
- how Relevance affects query depth;
- how Interest affects result granularity;
- how Salience creates sparse actor-specific memory;
- exact Confidence decay/propagation;
- exact Desire/Friction scaling functions;
- Friction category vocabulary;
- how Switching Friction is tuned;
- how Intents persist across save/load;
- how nested subgoals are represented;
- execution-strategy API;
- event dirtying and wake-up queues;
- per-LOD reasoning cadence;
- combat-specific event batching;
- SIMD/vectorization opportunities for score evaluation;
- deterministic ordering and deterministic RNG for equally scored outcomes;
- debug tooling for explaining **why** an actor chose an action.

A future implementation should prioritize **explainability**.

The Decision Inspector should ideally expose:

- why each candidate existed;
- why a candidate was filtered by Feasibility;
- which Need/Perception/Effect triggered reconsideration;
- what Attention entry held focus;
- which Knowledge Results were consulted;
- which Desire weights helped;
- which Friction channels hurt;
- what Switching Friction protected the current Intent;
- what deterministic tie-breaker was used if scores matched.

For any meaningful decision, developer tooling should ideally be able to show something like:

```text
Actor chose: WalkToTavern

RideToTavern rejected because:
    ShortDistance Friction      +80
    RetrieveHorse Friction      +35
    TownRiding Friction         +50
    Dismount/Tether Friction    +20

WalkToTavern:
    Distance Friction            +8

Result:
    Walk
```

The system can be deep internally, but it must remain debuggable.
