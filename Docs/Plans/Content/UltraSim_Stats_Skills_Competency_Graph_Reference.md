# UltraSim Stats, Attributes, Skills, and Competency Graph Reference

## 1. Core Philosophy

UltraSim should use a **use-based progression system** in the spirit of UO and Kenshi.

Characters improve by actually performing relevant actions rather than primarily spending abstract level-up points.

The progression model is best understood as a **Competency Graph**, not a flat list of dozens or hundreds of independent skill bars.

At the highest level:

```text
Attributes
    ↓
General Aptitudes
    ↓
Domain Masteries
    ↓
Specialty Skills
    ↓
Knowledge / Techniques / Recipes
    ↓
Familiarity
    ↓
Contextual Derived Capability
```

The core goal is:

> **Characters can become deeply specialized without requiring hundreds of unrelated permanent skill bars.**

The system should support:

- narrow specialists;
- broad generalists;
- transferable learning;
- meaningful sideways progression;
- use-based growth;
- contextual competence;
- readable UI despite deep simulation.

---

## 2. Attribute Categories

Attributes are broad underlying capacities rather than learned professions.

They should train slowly through use and influence many systems.

They do **not** replace learned competence.

A strong person is not automatically a good blacksmith.

A highly intelligent person is not automatically a good mage, physician, or engineer.

A lucky person is not automatically competent.

The current preferred structure is three broad categories:

```text
BODY
    Strength
    Dexterity
    Endurance

MIND
    Intelligence
    Wisdom
    Willpower

SPIRIT
    Luck
    Presence
    Attunement
```

The categories describe the **center of gravity** of each stat.

They are not hard mechanical fences.

A Mind stat may contribute to a physical result.

A Body stat may contribute to Mana.

A Spirit stat may contribute to resistance or crafting.

The important rule is:

> **Each attribute should have one clear primary identity, several sensible secondary effects, and possibly small cross-category contributions.**

Avoid making every attribute contribute to everything.

---

# BODY

## 3. Strength — Lift & Power

Strength represents:

- lifting capability;
- raw physical force;
- load handling;
- ability to control physically demanding tools and weapons.

Likely major effects:

```text
Carry / Lift Capacity      LARGE
Weapon Handling            LARGE
Physical Force             LARGE
```

Likely secondary effects:

```text
Melee Damage / Impact      MODERATE
Shove / Grapple            MODERATE
Health                     SMALL
Stamina                    SMALL
```

Strength should not become a universal melee damage multiplier.

A character with extreme Strength using a dagger should not automatically deal several times normal damage simply because Strength is high.

Instead, Strength should primarily affect:

- whether the weapon is physically manageable;
- how efficiently the user can accelerate it;
- recoil / recovery;
- leverage;
- impact;
- stamina cost when handling heavy equipment.

### Strength and Equipment Requirements

Item requirements should reflect the actual object.

Example:

```text
Two-Handed Battle Axe
    Recommended Strength: 40
```

A better material should not automatically require more Strength merely because it is higher-tier.

Example:

```text
Iron Battle Axe
    Strength Req: 40

Steel Battle Axe
    Strength Req: ~40–42 depending on mass/design

Mithril Battle Axe
    Strength Req: potentially LOWER if the material is lighter
```

This supports sideways progression.

Material progression should come from physical trade-offs rather than arbitrary level scaling.

---

## 4. Dexterity — Speed & Control

Dexterity represents:

- coordination;
- speed;
- bodily control;
- fine physical execution;
- accuracy.

Likely major effects:

```text
Physical Control           LARGE
Attack Rating / Accuracy   LARGE
Action Handling            MODERATE
Movement / Agility         MODERATE
```

Likely secondary effects:

```text
Recovery Speed             SMALL/MODERATE
Stamina                    SMALL
Carry Efficiency           SMALL
Craft Precision            SMALL
```

Dexterity should not replace weapon skill.

Conceptually:

```text
Attack Effectiveness
    mostly Specialty Skill
    + Dexterity
    + small Intelligence contribution
    + equipment handling
    + situation
```

Dexterity answers:

> **Can I physically put the action where I intend?**

Skill answers:

> **Do I know how to perform this exact action well?**

---

## 5. Endurance — Efficiency & Rejuvenation

Endurance represents:

- sustained exertion;
- physical efficiency;
- recovery;
- ability to continue working under load.

Likely major effects:

```text
Stamina Cost Reduction     LARGE
Stamina Recovery           LARGE
Physical Recovery          LARGE
```

Likely secondary effects:

```text
Maximum Stamina            MODERATE
Health                     MODERATE
Health Recovery            MODERATE
Mana                       SMALL
Fatigue Resistance         MODERATE
```

A high-Endurance character should not merely have a larger Stamina pool.

They should accomplish **more work per point of Stamina**.

Example:

```text
Battle Axe Swing

Low Endurance:
    14 Stamina

High Endurance:
     9 Stamina
```

This supports sideways progression better than simply inflating resource bars.

---

# MIND

## 6. Intelligence — Capacity & Complexity

Intelligence represents:

- mental capacity;
- abstract understanding;
- ability to handle complex systems;
- ability to reason through sophisticated processes.

Likely major effects:

```text
Mana Capacity              LARGE
Complex Spell Access       LARGE
Complex Craft Capability   LARGE
Formula / Process Capacity MODERATE/LARGE
```

Likely secondary effects:

```text
Attack Rating              SMALL
Craft Precision            SMALL/MODERATE
Analysis                   MODERATE
```

Intelligence should not primarily mean "learn faster."

Instead, it should answer:

> **How much complexity can this character understand, hold, and execute?**

Examples:

- simple forging may require little Intelligence;
- advanced alloying may require more;
- ordinary Fireball may have modest complexity;
- teleportation may require much greater complexity;
- advanced enchantment may require very high complexity.

A low-Intelligence character can still become excellent at a practiced craft through repeated use.

They may simply struggle with highly complex processes or advanced theoretical work.

---

## 7. Wisdom — Insight & Learning

Wisdom represents:

- learning from observation;
- pattern recognition;
- inference;
- reverse engineering;
- understanding from examples.

Likely major effects:

```text
Learning Transfer          LARGE
Study / Reverse Engineering LARGE
Knowledge Discovery        LARGE
```

Likely secondary effects:

```text
Appraisal                  MODERATE
Danger Recognition         SMALL/MODERATE
Skill Learning Speed       MODERATE
```

Wisdom should be especially relevant when learning from existing objects or examples.

Example:

```text
Fancy Dagger
    ↓ Study
Wisdom + relevant Craft Skill
    ↓
possible discovery:
    Fancy Style
    construction method
    recipe / pattern
    material use
```

A low-Wisdom character may need explicit instruction.

A high-Wisdom expert may be able to examine something and understand how it was made.

Wisdom should not become a mandatory "XP stat."

Its strongest advantage should be:

> **unlocking more ways to learn and infer**

rather than only applying a universal XP multiplier.

---

## 8. Willpower — Focus & Mental Endurance

Willpower represents:

- concentration;
- mental persistence;
- focus under stress;
- resistance to interruption;
- ability to continue mentally taxing actions.

Likely major effects:

```text
Mental Efficiency          LARGE
Concentration              LARGE
Mental Resistance          LARGE
```

Likely secondary effects:

```text
Mana Cost Efficiency       MODERATE
Mana Recovery              MODERATE
Stamina                    SMALL
Fear Resistance            MODERATE
Pain / Interruption Resist MODERATE
```

Willpower and Intelligence should create useful contrasts.

Example:

```text
High Intelligence / Low Willpower
    knows complex magic
    large Mana capacity
    poor efficiency
    easily interrupted

Lower Intelligence / High Willpower
    smaller complexity ceiling
    extremely stable
    efficient
    difficult to interrupt
```

---

# SPIRIT

## 9. Luck — Favorable Uncertainty

Luck represents:

- favorable variance;
- margins of uncertainty;
- avoidance of bad breaks.

Luck should **not** control random loot quality or drop rate.

UltraSim does not need "magic find" style loot RNG.

Instead, Luck should affect uncertain processes.

Likely uses:

```text
Crafting
    nudge borderline outcomes
    reduce severe failure
    slightly improve quality preservation
    slightly improve exceptional-success chance

Skill Checks
    improve close calls
    reduce catastrophic mishaps

World Interaction
    tool barely does not break
    unstable ledge holds
    trap narrowly misses
```

The rule should be:

> **Luck changes the edges of uncertainty; it does not replace competence.**

A novice crafter with enormous Luck should not outperform a master.

Conceptually:

```text
Skill
    determines center of expected outcome

Luck
    nudges outcome margins
```

Luck integrates particularly well with the crafting-quality degradation model.

If a craft is close to failure, Luck may:

- turn failure into success;
- reduce a two-step quality loss to one;
- preserve more of the intended result.

---

## 10. Presence — Metaphysical Weight of Self

Presence represents:

- spiritual solidity;
- metaphysical "weight";
- resistance to supernatural displacement or overwriting.

Likely uses:

```text
Possession Resistance
Curse Resistance
Binding Resistance
Forced Summoning Resistance
Soul Manipulation Resistance
Death / Spirit Effects
```

Presence differs from Willpower.

```text
Willpower:
    "My mind refuses."

Presence:
    "My existence refuses."
```

Presence may also contribute to:

- strength of spiritual bindings;
- soul-linked magic;
- resisting forced teleportation;
- surviving metaphysical pressure.

It should not simply become another Health stat.

---

## 11. Attunement — Supernatural Connection & Resonance

Attunement represents:

- sensitivity to magical forces;
- supernatural resonance;
- ability to connect with magical phenomena.

Likely uses:

```text
Magic Detection
Spirit Interaction
Ritual Sensitivity
Magical Item Affinity
Enchantment Interaction
```

Possible secondary effects:

```text
Mana                     SMALL/MODERATE
Ritual Efficiency         SMALL/MODERATE
Unusual Tradition Access  CONTEXTUAL
```

Attunement should not become "Intelligence 2" or a generic magic-damage stat.

It is about:

> **connection and resonance, not raw output.**

---

## 12. Spirit States and Resources Are Separate

Not every metaphysical concept belongs in the core attribute set.

Important examples:

```text
Piety / Grace
Karma
Corruption
Favor
```

These are better treated as states/resources rather than attributes.

### Piety / Grace

Piety is a consumable resource associated with Chivalry/Holy traditions.

It is gained through:

- prayer;
- deeds;
- service;
- donations;
- vows;
- religious/chivalric practice.

It should not be a trainable core attribute.

### Karma

Karma is better understood as a changing metaphysical state caused by behavior.

It may be bipolar or otherwise contextual.

Example:

```text
Negative ←──── 0 ────→ Positive
```

Karma is not necessarily a simplistic good/evil score.

It may instead represent accumulated metaphysical residue.

---

# ATTRIBUTE PROGRESSION

## 13. Attributes Train Through Use

Attributes should follow the same use-based philosophy as skills.

They grow when the character performs actions that meaningfully stress or exercise them.

Examples:

```text
Strength
    heavy lifting
    dragging
    pushing
    heavy weapons
    overcoming physical resistance

Dexterity
    precise attacks
    aiming
    dodging
    fast tools
    fine bodily control

Endurance
    sustained exertion
    long combat
    running
    repeated stamina use
    physical recovery

Intelligence
    complex crafts
    difficult theory
    complex spells
    analysis

Wisdom
    studying examples
    reverse engineering
    experimentation
    inference

Willpower
    concentration
    channeling
    resisting fear/control
    sustained mental strain

Luck
    should probably grow slowly, if at all, through uncertain exposure or special systems

Presence
    spiritual conflict
    curse resistance
    binding
    metaphysical pressure

Attunement
    repeated magical/spiritual exposure
    ritual work
    magical perception
```

Exact training rules remain tuning.

The important principle is:

> **The cause of the task determines what was trained.**

Avoid hardcoding:

```text
Sword Swing = +Strength XP
```

Instead, the action should expose the kind of exertion actually occurring.

---

## 14. Soft Requirements Instead of Binary Gates

Where possible, item and task requirements should be **soft thresholds** rather than hard equip locks.

Example:

```text
Two-Handed Battle Axe
Recommended Strength: 40
```

Possible handling:

```text
40+ Strength
    normal use

35 Strength
    somewhat tiring

30 Strength
    slow / stamina-heavy

20 Strength
    extremely awkward

10 Strength
    nearly unusable
```

This produces stories and progression rather than arbitrary walls.

The character may be able to attempt difficult equipment early, but suffer realistic consequences.

---

# COMPETENCY GRAPH

## 15. General Aptitudes

General Aptitudes are extremely broad transferable competencies.

The clearest example is:

```text
Crafting Aptitude
```

This represents:

- understanding tools;
- measuring;
- tolerances;
- planning;
- material economy;
- recognizing mistakes;
- learning unfamiliar processes.

General Aptitudes may be better presented as coarse ranks rather than ordinary skill bars.

Example UI:

```text
Skilled Crafter
Master Craftsman
```

Their major purpose is **learning transfer**.

They should not automatically make the character good at every related specialty.

---

## 16. Domain Masteries

Domain Masteries are broad bodies of related knowledge and practice.

Examples:

```text
Metalworking
Woodworking
Agriculture
Husbandry
Medicine
Textiles
Cooking
Alchemy
Survival
Combat
```

A Domain Mastery answers:

> **How much does this character understand this broad field?**

It can help with:

- learning related specialties;
- recognizing mistakes;
- interpreting unfamiliar processes;
- some direct task performance.

But it should contribute less to immediate performance than actual Specialty Skill.

---

## 17. Specialty Skills

Specialty Skills are where most conventional numbered progression lives.

Examples:

```text
Metalworking
    Bladesmithing
    Armorsmithing
    Shieldmaking
    Farriery
    Fine Metalwork

Woodworking
    Carpentry
    Joinery
    Bowmaking
    Wheelwrighting

Combat
    Blades
    Heavy Arms
    Polearms
    Ranged
    Defense
    Unarmed
```

This is where values such as:

```text
Bladesmithing 125
```

make sense.

A character can therefore be:

> Legendary Bladesmith  
> Expert Metalworker  
> Mediocre Armorsmith

without contradiction.

---

## 18. Domain Mastery Is Not Just Upward-Trickled XP

A Domain Mastery should not simply be:

```text
Bladesmithing XP
    ↓
Metalworking XP
    ↓
Crafting XP
```

That creates awkward results such as:

```text
Bladesmithing 125
Metalworking 47
Crafting 19
```

Instead, Domain Mastery should reflect some combination of:

- accumulated domain experience;
- specialty depth;
- specialty breadth;
- demonstrated competence.

Example:

```text
Bladesmithing    125
Armorsmithing      3
Shieldmaking       5
Fine Metalwork     8
```

could reasonably produce:

> Expert Metalworker

because the character is exceptionally deep but narrow.

Whereas:

```text
Bladesmithing     90
Armorsmithing     83
Shieldmaking      76
Farriery          72
Fine Metalwork    85
```

could produce:

> Grandmaster Metalworker

because the character has both depth and breadth.

Domain Mastery is therefore describing a different thing from a Specialty level.

---

## 19. The Competency Graph Is a DAG, Not Necessarily a Tree

A Specialty may belong to multiple Domains.

Example:

```text
Metalworking ──┐
               ├── Shieldmaking
Woodworking ───┘
```

Likewise:

```text
Wagon Construction
    Woodworking
    Metalworking
    Wheelwright / Vehicle Specialty
```

The graph can therefore be a web.

The hard structural rule is:

> **No cycles.**

The graph should remain a DAG.

---

## 20. Tasks Determine What Is Actually Practiced

The graph does not automatically say:

> Shieldmaking always trains Metalworking and Woodworking equally.

The recipe/action/task declares the competencies genuinely used.

Example:

```text
Wooden Round Shield

Shieldmaking    high
Woodworking     high
Metalworking    low
```

versus:

```text
Steel Shield

Shieldmaking    high
Metalworking    high
Woodworking     low
```

This is more flexible than hardcoding every craft relationship into the graph.

---

## 21. Broad Mastery Accelerates Learning Downward

One of the central mechanics is **transfer learning**.

Example:

```text
Metalworking
    Grandmaster

Armorsmithing
    0
```

This character is not automatically a master armorer.

However, they should learn Armorsmithing much faster than someone who has never worked metal.

They already understand:

- heat;
- metal behavior;
- tools;
- forging;
- deformation;
- failure;
- general smithing technique.

Conceptually:

```text
same-domain transfer
    strong

general aptitude transfer
    weaker

unrelated-domain transfer
    little or none
```

---

## 22. Transfer Diminishes as the Specialty Catches Up

Transfer should be strongest when the specialty is far below the relevant mastery.

Example:

```text
Armorsmithing 5
    huge transfer bonus

Armorsmithing 40
    moderate transfer bonus

Armorsmithing 70
    small transfer bonus

Armorsmithing near mastery
    little/no transfer bonus
```

The broad mastery helps the character **catch up**.

It should not multiply progression indefinitely.

---

## 23. Learning Transfer and Immediate Performance Are Separate

This is a major guardrail.

Broad mastery may strongly affect:

> **how quickly you learn**

while only weakly affecting:

> **how good you are right now**

Example conceptual craft weighting:

```text
Armorsmithing       DOMINANT
Metalworking        SECONDARY
Crafting Aptitude   SMALL
Attributes          CONTEXTUAL
Tools / Materials   CONTEXTUAL
```

This prevents:

> master swordsmith = automatic master armorer

while still allowing expertise to transfer naturally.

---

# KNOWLEDGE LAYERS

## 24. Knowledge Is Separate From Skill

Someone can be highly skilled but not know a particular process.

Examples:

```text
Krys geometry
Plate armor construction
Dwarven joinery
Wheat cultivation
Glassworking
```

Knowledge may:

- unlock a process;
- remove an ignorance penalty;
- expose an option;
- allow a recipe;
- enable recognition.

Skill answers:

> **How good are you at doing this kind of work?**

Knowledge answers:

> **Do you actually know this thing or process?**

---

## 25. Techniques Are Separate From Knowledge and Skill

Techniques describe special methods.

Examples:

```text
Differential Hardening
Blood Quenching
Advanced Irrigation
Special Rune Engraving
```

Techniques may:

- reduce difficulty;
- increase quality ceiling;
- change material retention;
- alter side effects;
- unlock special outputs;
- modify workflow.

Techniques answer:

> **What special method can you apply?**

---

## 26. Recipes / Patterns Specify Outputs

Recipes are specific output definitions.

Examples:

```text
Ceremonial Krys
Westmarch Breastplate
Healing Draught
Specific Bread Recipe
```

Recipes may specify:

- ingredients;
- form;
- process;
- competencies used;
- required Knowledge;
- target descriptors;
- tools;
- expected difficulty.

Recipes are not Skills.

---

## 27. Familiarity Is Lightweight Specific Experience

Familiarity handles experience too narrow to deserve a full Specialty Skill.

Examples:

```text
this recipe
this forge
this animal breed
this crop
this material
this weapon form
this fishing location
```

Familiarity should remain sparse and inexpensive.

Avoid turning it into dozens of permanent visible bars:

```text
Iron Familiarity 72
Steel Familiarity 63
Oak Familiarity 58
...
```

The system can store lightweight familiarity internally while presenting it only when relevant.

---

## 28. Profession Is Separate From Capability

A profession is a role or job.

Examples:

```text
Blacksmith
Farmer
Physician
Merchant
Guard
```

Profession should not be mistaken for one universal Skill.

A Blacksmith may actually rely on:

```text
Metalworking
Bladesmithing
Armorsmithing
Trade
Strength
Endurance
Tools
Techniques
Recipes
```

Profession answers:

> **What does this character do?**

Competency answers:

> **What can this character actually do well?**

---

# DERIVED CAPABILITY

## 29. Derived Capability Is Contextual

The game can calculate many detailed outcomes without storing them as independent Skills.

Examples:

```text
Expected Craft Quality
Work Speed
Material Waste
Harvest Efficiency
Treatment Success
Attack Accuracy
Carry Capacity
Spell Stability
Learning Rate
```

Derived capability may depend on:

```text
Attributes
General Aptitudes
Domain Mastery
Specialty Skills
Knowledge
Techniques
Recipes
Familiarity
Tools
Materials
Current condition
Environment
Luck
```

This is how the system creates depth without giant permanent stat sheets.

---

## 30. Multiple Attributes Can Contribute to the Same Outcome

Attribute categories should not remain mechanically isolated.

Examples:

### Attack Rating

```text
Weapon Skill    LARGE
Dexterity       MODERATE
Intelligence    SMALL
Equipment       CONTEXTUAL
State           CONTEXTUAL
```

Dexterity contributes physical coordination.

Intelligence may contribute:

- spacing;
- prediction;
- anatomy;
- timing;
- tactical understanding.

### Stamina

```text
Endurance       LARGE
Strength        SMALL
Dexterity       SMALL
Willpower       SMALL
```

Each contribution has a different meaning:

```text
Strength
    exertion is a smaller fraction of capability

Dexterity
    less wasted movement

Endurance
    sustained physical capacity

Willpower
    ability to push through fatigue
```

### Mana

```text
Intelligence    LARGE
Willpower       MODERATE
Attunement      SMALL/MODERATE
Endurance       SMALL
```

This is desirable as long as one attribute remains the primary source.

---

# LEARNING AND XP

## 31. XP Is Task-Budgeted, Not Recursively Multiplied

Avoid recursive XP multiplication such as:

```text
+100 Bladesmithing
+50 Metalworking
+25 Crafting
```

from one action and then hybrid tasks multiplying those paths again.

Instead, an activity has a bounded **Learning Potential**.

Example:

```text
Task Learning = 100

Bladesmithing   65
Metalworking    25
Crafting        10
```

Exact allocation remains tuning.

The task declares where the learning genuinely happened.

---

## 32. Project Learning Potential

For crafting and similar long-form tasks:

```text
Project Learning Potential
=
Complexity
× Relative Challenge
× Novelty
```

The exact formula remains tuning.

The important rule is:

> **A project has a finite learning budget.**

---

## 33. Failure Teaches, But Cannot Be Farmed Infinitely

Failure can reveal mistakes and therefore teach.

However, failed attempts should consume part of the project's finite learning potential.

Example:

```text
Total Project Learning Potential = 100

Failure 1
    +15

Failure 2
    +10

Completion
    remaining 75
```

Completion does not then award another full 100.

Repeated identical failures should teach progressively less.

This prevents intentionally impossible work from becoming an infinite XP exploit.

---

## 34. Clean Execution Can Award Bonus Learning

Difficult work performed correctly should be more learning-efficient than intentionally failing.

A rough principle:

```text
70% success chance
≈ meaningful challenge gap

clean first-pass success
→ bounded bonus learning
```

The bonus must have a cap.

Extremely tiny success chances should not generate absurd XP jackpots.

The exact numeric model remains tuning.

---

## 35. Extremely Difficult Does Not Automatically Mean Impossible

Eligibility is checked before success chance.

If an action is impossible because of:

```text
missing furnace
unknown required technique
wrong material
physically impossible geometry
missing prerequisite knowledge
```

there is no roll.

But if the action is genuinely possible and simply extremely difficult, a tiny success floor may be desirable.

Example:

```text
~0.01%
```

This permits rare historical freak successes.

The exact floor remains tuning.

---

# ITEM AND EQUIPMENT MODIFIERS

## 36. Item Bonuses Modify Effective Capability

Items may provide temporary/equipment-bound bonuses such as:

```text
+4 Bladesmithing
+6 Medicine
+Strength
+Willpower
```

These increase effective capability while active.

They do not permanently raise learned Skill or Attribute values.

This distinction should remain visible internally:

```text
Learned Skill
+ Item Modifier
+ State Modifier
= Effective Skill
```

---

# UI AND PRESENTATION

## 37. UI Should Collapse the Complexity

The player should not open a sheet containing hundreds of bars.

Default view can show broad summaries.

Example:

```text
BODY
    Strength       42
    Dexterity      31
    Endurance      55

MIND
    Intelligence   47
    Wisdom         61
    Willpower      38

SPIRIT
    Luck           22
    Presence       35
    Attunement     40

CRAFTING
    Master Craftsman

Metalworking
    Grandmaster

Woodworking
    Apprentice

Medicine
    Skilled
```

Expanding Metalworking:

```text
Bladesmithing    125
Armorsmithing     23
Shieldmaking      44
Fine Metalwork    61
```

Knowledge, Techniques, Recipes, and Familiarity should have their own focused views.

The simulation can remain deep while the interface remains readable.

---

## 38. Attribute UI Should Show Meaning, Not Just Numbers

Each attribute should have a short semantic description.

Example:

```text
Strength
Lift & Power

Dexterity
Speed & Control

Endurance
Efficiency & Rejuvenation
```

Likewise:

```text
Intelligence
Capacity & Complexity

Wisdom
Insight & Learning

Willpower
Focus & Mental Endurance
```

and:

```text
Luck
Favorable Uncertainty

Presence
Metaphysical Weight

Attunement
Supernatural Resonance
```

This helps players understand why an attribute affects multiple systems.

---

# GUARDRAILS

## 39. Sideways Progression Is a Core Requirement

Progression should not automatically mean:

> newer item = larger requirements = larger numbers = obsolete older content

Examples:

- Mithril may reduce weight instead of increasing Strength requirement.
- A Fine dagger may be better made without becoming a "level 70 dagger."
- A master can improve efficiency, reliability, options, and specialization rather than simply multiplying damage.

The system should favor:

- trade-offs;
- specialization;
- handling differences;
- efficiency;
- reliability;
- material properties;
- context.

---

## 40. Avoid Single-Stat Builds Becoming Mandatory

No attribute should be the obvious answer to an entire playstyle.

Examples:

Strength should not equal:

```text
all melee damage
all carry
all health
all armor
```

Intelligence should not equal:

```text
all magic damage
all mana
all learning
all crafting
```

Luck should not equal:

```text
better loot
better crafting
better crits
better everything
```

Attributes should overlap enough to create multiple viable character shapes.

---

## 41. Do Not Add a Skill Unless It Needs Repeated Numeric Practice

Whenever a new Skill is proposed, ask:

> **Does this need its own repeatedly-practiced numeric progression?**

Or is it actually:

- a Domain;
- Knowledge;
- a Technique;
- a Recipe;
- Familiarity;
- a Profession;
- a Trait;
- an Attribute;
- a Derived Stat?

This is the primary guardrail against spreadsheet creep.

---

# CURRENT REFERENCE MODEL

## 42. Current Attribute Summary

| Category | Attribute | Core Identity |
|---|---|---|
| **Body** | Strength | Lift & Power |
| **Body** | Dexterity | Speed & Control |
| **Body** | Endurance | Efficiency & Rejuvenation |
| **Mind** | Intelligence | Capacity & Complexity |
| **Mind** | Wisdom | Insight & Learning |
| **Mind** | Willpower | Focus & Mental Endurance |
| **Spirit** | Luck | Favorable Uncertainty |
| **Spirit** | Presence | Metaphysical Weight of Self |
| **Spirit** | Attunement | Supernatural Connection / Resonance |

These are the current preferred attributes.

Older exploratory candidates such as separate Agility or Perception are not currently part of the canonical set unless a later mechanic reveals a genuine missing role.

---

## 43. Current Competency Graph Summary

```text
                        ATTRIBUTES
         Body / Mind / Spirit broad capacities
                             │
                             ▼
                    GENERAL APTITUDES
                  broad transferable ability
                             │
             ┌───────────────┴───────────────┐
             ▼                               ▼
       DOMAIN MASTERY                  DOMAIN MASTERY
        Metalworking                    Woodworking
         │       │                         │
         │       └────────┐                │
         ▼                ▼                ▼
  Bladesmithing      Shieldmaking      Carpentry
  Specialty Skill      ▲               Specialty
                       │
                 multiple parents

             ↓
Knowledge / Techniques / Recipes

             ↓
Familiarity

             ↓
Contextual Derived Capability

Attributes
+ Aptitude
+ Domain Mastery
+ Specialty Skill
+ Knowledge
+ Technique
+ Recipe
+ Familiarity
+ Tools
+ Materials
+ State
+ Environment
+ Luck
```

---

## 44. Core Philosophy

The current intended progression system can be summarized as:

> **Specific practice makes you good at the specific thing.**

> **Broader mastery makes you better at understanding and learning related things.**

> **Attributes determine broad capacity, not profession-level competence.**

> **Knowledge determines what you know how to attempt.**

> **Techniques determine special ways you know how to perform it.**

> **Recipes define specific outputs.**

> **Familiarity captures narrow repeated experience without creating another full Skill.**

> **Context determines what you are capable of right now.**

> **Progression should primarily increase options, efficiency, reliability, and specialization rather than force constant vertical numerical escalation.**

---

## 45. Open Questions / Future Tuning

The structure is stable enough to build on, but several areas remain deliberately numeric/tuning questions:

- final Attribute ranges and caps;
- how quickly Attributes train relative to Skills;
- whether Luck trains normally or changes through rarer systems;
- exact Domain Mastery calculation from breadth/depth/experience;
- exact transfer-learning curves;
- exact Learning Potential formula;
- clean-success bonus caps;
- tiny-success floor;
- soft requirement penalty curves;
- resource formulas for Health, Stamina, Mana, and other consumables;
- exact Attribute contribution weights;
- how Presence and Attunement interact with specific magical traditions;
- UI rank names for Aptitudes and Domain Masteries;
- whether some Attributes can temporarily exceed normal caps through magic/items;
- whether extreme debuffs can drive effective Attributes below zero.

These should be tuned from gameplay and simulation needs rather than over-specified before implementation.
