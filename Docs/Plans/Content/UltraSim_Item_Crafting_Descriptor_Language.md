# UltraSim Item, Crafting, and Descriptor Language

## Purpose

This document defines the current shared language for item identity, generated names, item descriptions, crafting quality, and mutable item properties. The goal is to give weapons, gems, potions, food, armor, tools, containers, and future item families one common semantic system without forcing every category to use the same literal adjectives.

The central rule is simple:

> **Descriptors define what an item fundamentally is. Object properties describe what has happened to that particular instance or what has been layered onto it afterward.**

The descriptor system should therefore be useful for much more than names. It should drive base stats, crafting outcomes, value, appearance, procedural generation, filtering, recipes, appraisal, UI, persistence, and any other system that needs to reason about an item's inherent identity.

---

## 1. Shared Item Grammar

Most item families can be described with the same four structural roles:

1. **Scale / Size** — the item's physical or use magnitude.
2. **Quality / Make** — how well the item was made, formed, refined, or produced.
3. **Characteristic** — the category-specific intrinsic distinction that matters for that item family.
4. **Type / Form** — what the item fundamentally is.

These are **shared semantic roles**, not four hard-coded universal adjective columns. Each item family supplies its own vocabulary and interpretation.

| Category | Scale / Size | Quality / Make | Characteristic | Type / Form |
|---|---|---|---|---|
| **Weapons** | Throwing, One-Handed, Versatile, Two-Handed, Heavy; bow-specific sizes where appropriate | Crude → Masterwork | Style: Plain, Fancy, Ritual/Ceremonial, Elven, Dwarven, etc. | Krys, Dagger, Sword, Axe, Crossbow, Bow, etc. |
| **Gems** | Tiny, Small, Medium, Large, Great | Integrity: Cracked, Chipped, Flawed, Sound, Perfect | Clarity: Clouded, Murky, Clear, Brilliant | Ruby, Sapphire, Diamond, Topaz, Emerald, etc. |
| **Potions** | Tiny/Small/Medium/Large/Giant, or another potion-specific volume ladder | Refinement: Murky → Clouded → Clear → Brilliant → Radiant | Delivery: Gradual, Steady, Rapid | Health, Stamina, Rage, Will, Mana, etc. recovery/effect family |
| **Food** | Bite, Mouthful, Snack, Serving, Meal, Feast/Banquet | Intrinsic cooking/craft quality | Preparation: Raw, Roasted, Baked, Smoked, Spiced, etc. | Bread, Venison, Stew, Pie, Cheese, Beef, etc. |

This gives all item categories the same architecture while allowing the words to remain natural for the thing being described.

---

## 2. Type / Form

**Type / Form answers: "What is this thing?"**

Examples:

- Dagger
- Krys
- Sword
- Ruby
- Healing/Health potion family
- Venison
- Stew
- Plate Cuirass
- Strongbox

Type is not just another modifier. It is the archetype that establishes the base object. Descriptor axes modify or specialize that object.

A useful conceptual representation is:

```text
ItemArchetype / Type
    Dagger
    Ruby
    Health Potion
    Venison

IntrinsicDescriptors
    Scale
    Quality
    Characteristic
```

A category may use compound types where that produces cleaner semantics. For example, `Plate Cuirass` may be a better type than attempting to force `Plate` into an unrelated descriptor axis.

---

## 3. Scale / Size

**Scale / Size is the most reusable descriptor axis.** It means the item's magnitude, handling class, dose, or normal-use size rather than requiring one literal physical measurement across every category.

### Weapons

For weapons, Scale answers roughly:

> **"What physical or handling configuration of this weapon is this?"**

Possible values include:

- Throwing
- One-Handed
- Versatile
- Two-Handed
- Heavy

`One-Handed` is usually the expected/default state and may therefore be **present in data but omitted from the displayed name**.

Examples:

- **Masterwork Ritual Throwing Krys**
- **Crude Fancy Two-Handed Sword**
- **Superior Plain One-Handed Crossbow**

The same axis can use different vocabulary for bows where literal weapon size is more useful:

- Short
- Standard
- Long
- War

Terms such as `Hunting` should only live on Scale if they correspond to a concrete physical/statistical configuration; otherwise they belong to Style/Purpose.

### Gems

For gems, Scale is literal physical size:

- Tiny
- Small
- Medium
- Large
- Great

### Potions

For potions, Scale is dose/container magnitude:

- Tiny
- Small
- Medium
- Large
- Giant

The exact potion ladder can remain category-specific.

### Food

For food, Scale is **not literal dimensions**. It answers:

> **"How much hunger does one normal consumption replenish?"**

Example ladder:

- Bite
- Mouthful
- Snack
- Serving
- Meal
- Feast
- Banquet

A physically small but calorically dense ration could count as a `Meal`, while a physically large but low-nutrition food could count as only a `Serving`.

If an item contains multiple consumptions, that should be separate from Scale:

```text
Scale = Serving
UsesRemaining = 4
```

A pot of stew can therefore contain four Servings instead of abusing `Large` to mean both container size and hunger restoration.

---

## 4. Quality / Make

**Quality / Make answers: "How well was this item inherently made, formed, refined, or produced?"**

The vocabulary is category-specific, but the axis is normally **ordinal**, making it especially valuable to crafting.

### Weapons and ordinary crafted items

Current weapon/craft ladder:

1. Crude
2. Rough
3. Functional
4. Normal
5. Fine
6. Superior
7. Exquisite
8. Masterwork

Examples:

- **Crude Fancy Two-Handed Sword**
- **Fine Plain Dagger**
- **Masterwork Ritual Throwing Krys**

### Gems

For gems, the quality-like axis is better understood as **Integrity** rather than craftsmanship:

1. Cracked
2. Chipped
3. Flawed
4. Sound
5. Perfect

This remains orthogonal to Clarity.

`Perfect` means structurally flawless; it does not mean the gem must also be `Brilliant` in clarity.

### Potions

Potion quality is best represented as **Refinement**:

1. Murky
2. Clouded
3. Clear
4. Brilliant
5. Radiant

This avoids the semantic collision produced by combinations such as `Perfect Pure Potion`, where two different data fields sound like they are saying the same thing.

### Food

Food can have an intrinsic quality ladder representing how well it was prepared or produced. Exact vocabulary can be tuned later, but this is distinct from freshness.

Possible vocabulary:

- Poor
- Common
- Good
- Fine
- Excellent
- Exquisite

**Fresh / Stale / Moldy / Rotten is not Quality.** Freshness changes over time and therefore belongs to mutable object state.

---

## 5. Characteristic

The Characteristic slot is intentionally category-specific. It captures the second important inherent distinction after Quality without pretending that Style, Clarity, Delivery, and Preparation are the same concept.

### Weapons: Style

Examples:

- Plain
- Fancy
- Ritual
- Ceremonial
- Elven
- Dwarven
- Other cultural, aesthetic, or construction traditions

Style may affect appearance, value, construction biases, stat profiles, recipe requirements, or other base behavior.

### Gems: Clarity

Examples:

- Clouded
- Murky
- Clear
- Brilliant

Clarity and Integrity are deliberately separate:

> **Large Brilliant Perfect Ruby**

- Large = Scale
- Brilliant = Clarity
- Perfect = Integrity
- Ruby = Type

Every word answers a different question.

### Potions: Delivery

Delivery describes the potion's effect-over-time profile:

- Gradual
- Steady
- Rapid

Examples:

- Gradual: smaller immediate effect plus a longer recovery tail
- Steady: balanced immediate and over-time recovery
- Rapid: strong immediate effect plus a short recovery tail

Exact numbers are balance data, not naming semantics, but examples might look like:

```text
Gradual Health Recovery
    10 HP immediately
    +1 HP/sec for 10 sec

Steady Health Recovery
    30 HP immediately
    +5 HP/sec for 5 sec

Rapid Health Recovery
    50 HP immediately
    +10 HP/sec for 2 sec
```

### Food: Preparation

Examples:

- Raw
- Boiled
- Roasted
- Baked
- Smoked
- Spiced

Preparation is inherent to the food item and can reasonably affect base hunger restoration, secondary effects, value, shelf life, appearance, or taste-related systems.

---

## 6. Identity Descriptors vs. Object Properties

This is the most important semantic boundary in the system.

### Identity descriptors

Identity descriptors define the item's base identity and should generally remain stable for the life of that object.

They may determine:

- Base stats
- Base value
- Visual presentation
- Crafting semantics
- Procedural generation
- Generated name
- Recipe requirements
- Appraisal/filtering categories

Examples:

```text
Type    = Krys
Scale   = Throwing
Quality = Masterwork
Style   = Ritual
```

The item is therefore a:

> **Masterwork Ritual Throwing Krys**

### Object properties

Object properties describe mutable state or modifiers layered onto the item instance afterward.

Examples:

- Current / maximum durability
- Current freshness
- Remaining uses
- Poison charges
- Sockets
- Enchantments
- +Fire Damage
- +Strength
- Temporary blessings/curses
- Player-added modifications
- Recharge state
- Other D2/UO-style property-list attributes

Example:

```text
Masterwork Ceremonial Dagger

Damage        8–13
Speed         2.1
Durability    17 / 80

+3 Fire Damage
+2 Strength
Poisoned (4)
```

Repairing that dagger back to full durability does **not** change its canonical identity or generated name.

The same rule applies to food:

```text
Fine Meal of Spiced Venison
Freshness: Stale
```

Later it may become:

```text
Freshness: Moldy
```

without turning `Moldy` into part of the permanent item identity.

### Practical classification test

Ask:

> **If I repair, recharge, dispel, poison, socket, enchant, damage, age, or partially consume the existing object, does this field change?**

If yes, it is probably an **ObjectProperty**.

Ask:

> **If I destroyed this object and recreated an equivalent one, would I need to reproduce this characteristic for it to be considered the same kind of item?**

If yes, it is probably an **identity descriptor**.

---

## 7. Naming Language

The descriptor system and the displayed English name are related but are **not the same thing**.

The data model should preserve the authoritative descriptors. A category-specific name formatter decides:

- Which descriptors appear
- Their display order
- Whether default values are omitted
- Whether a compact or detailed phrase is used
- How grammar changes between item families

This prevents the data schema from dictating awkward English.

### Default omission

Expected/default descriptors can exist in data without being printed.

Example:

```text
Scale = OneHanded
Quality = Masterwork
Style = Plain
Type = Dagger
```

can display as:

> **Masterwork Plain Dagger**

rather than:

> One-Handed Masterwork Plain Dagger

The exceptional case should announce itself:

> **Masterwork Ritual Throwing Krys**

> **Crude Fancy Two-Handed Sword**

This preserves fast recognition while keeping the underlying data explicit.

---

## 8. Compact and Detailed Names

Some categories, especially potions, become semantically precise but excessively verbose if every relationship is spelled out.

The solution is to generate **multiple representations from the same descriptors**.

Example descriptor set:

```text
Size       = Giant
Refinement = Murky
Delivery   = Gradual
Resource   = Stamina
```

### Compact / Display Name

> **Giant Murky Gradual Stamina Potion**

### Detailed / Full Name

> **Giant Murky Potion of Gradual Stamina Recovery**

Both names represent the same underlying item.

A client setting can expose the choice:

```text
Show Detailed Item Names: Off
```

Default: **Off**.

The compact name is preferable for:

- Inventory grids
- Loot lists
- Vendor lists
- Quick tooltips
- Overhead labels
- Dense UI

The detailed form is useful for:

- Examination views
- Crafting details
- Logs
- Generated prose
- Accessibility/preferences
- Players who want maximum semantic detail

General rule:

> **Descriptors define the item. Name formatters decide how much of that information to spell out.**

---

## 9. Potion Language

Potions exposed an important naming problem: phrases such as `Potion of Gradual Stamina` are technically understandable but semantically incomplete. A potion does not "do stamina"; it causes **Stamina Recovery**.

The detailed potion grammar is therefore:

> **[Size] [Refinement] Potion of [Delivery] [Resource] Recovery**

Examples:

- **Giant Murky Potion of Gradual Health Recovery**
- **Giant Murky Potion of Gradual Stamina Recovery**
- **Small Radiant Potion of Rapid Rage Recovery**
- **Medium Clear Potion of Steady Will Recovery**

The compact grammar is:

> **[Size] [Refinement] [Delivery] [Resource] Potion**

Examples:

- **Giant Murky Gradual Health Potion**
- **Giant Murky Gradual Stamina Potion**
- **Small Radiant Rapid Rage Potion**
- **Medium Clear Steady Will Potion**

This is currently the preferred default display language.

Other potion families such as poison, cure, transformation, resistance, etc. can use the same descriptor architecture but may need their own category-specific naming grammar rather than being forced through the word `Recovery`.

---

## 10. Example Names by Category

### Weapons

1. **Masterwork Ritual Throwing Krys**
2. **Crude Fancy Two-Handed Sword**
3. **Superior Plain One-Handed Crossbow**

`One-Handed` should normally be omitted when it is the expected/default scale for that weapon family, but it can be shown where the distinction is mechanically meaningful.

### Gems

1. **Tiny Clouded Chipped Sapphire**
2. **Large Brilliant Perfect Ruby**
3. **Great Clear Sound Emerald**

### Potions — Compact

1. **Giant Murky Gradual Health Potion**
2. **Small Radiant Rapid Rage Potion**
3. **Medium Clear Steady Will Potion**

### Potions — Detailed

1. **Giant Murky Potion of Gradual Health Recovery**
2. **Small Radiant Potion of Rapid Rage Recovery**
3. **Medium Clear Potion of Steady Will Recovery**

### Food

1. **Fine Snack of Smoked Venison**
2. **Common Serving of Roasted Potatoes**
3. **Exquisite Meal of Spiced Beef**

Category-specific grammar may reorder tokens when that creates more natural language. The authoritative descriptor order in storage does not need to be the printed word order.

---

## 11. Crafting Semantics

The descriptor vocabulary is not merely cosmetic. It gives crafting a common language.

A recipe or crafting attempt can define:

- The intended Type/Form
- The intended Scale/Size
- The intended Characteristic
- The intended or achievable Quality/Make
- Materials and other requirements

The craft then produces an item whose descriptors are mechanically meaningful.

### Intended result vs. final result

Example:

```text
Recipe / Intent
    Type       = Health Potion
    Size       = Giant
    Delivery   = Rapid
    Refinement = Brilliant
```

The crafter is attempting to make a **Brilliant** potion. Failures do not necessarily mean "no item exists." Instead, a failure can reduce the appropriate quality-like descriptor.

---

## 12. Craft Failure = Quality Reduction

The current preferred generic crafting rule is:

> **A crafting failure reduces the item's quality-like descriptor, and quality can never fail below the worst valid value.**

This gives every category the same failure semantics while preserving category-specific vocabulary.

### Potion example

```text
Brilliant
    ↓ failure
Radiant
    ↓ failure
Clear
    ↓ failure
Clouded
    ↓ failure
Murky
    ↓ failure
Murky
```

Once the floor is reached, further quality failures clamp at `Murky` rather than inventing invalid qualities below the defined range.

Narratively:

> "I started out making a brilliant potion, failed several stages, and ended up with a murky potion."

The final item is still valid and understandable:

> **Giant Murky Gradual Health Potion**

### Weapon example

```text
Masterwork
    ↓
Exquisite
    ↓
Superior
    ↓
Fine
    ↓
Normal
    ↓
Functional
    ↓
Rough
    ↓
Crude
    ↓
Crude
```

A smith can therefore attempt a Masterwork result and still produce a usable but poor object rather than always receiving a binary success/failure outcome.

### Gem example

If cutting or finishing a gem uses its Integrity ladder as the craft-sensitive axis:

```text
Perfect → Sound → Flawed → Chipped → Cracked
```

The exact operations that can reduce Integrity can be defined by gem-cutting rules rather than assumed globally.

### Food example

Cooking quality can use the same ordinal degradation rule once the food-quality vocabulary is finalized.

Freshness is excluded because it is mutable post-craft state, not craft quality.

---

## 13. Quality Floors and Other Failure Effects

Quality degradation is bounded:

```text
FinalQuality = max(WorstQuality, IntendedQuality - FailureSteps)
```

This is a semantic rule, not a requirement that the implementation literally store integer subtraction.

Reaching the quality floor does **not** prevent a craft from having other failure consequences if a specific crafting system needs them. Examples could include:

- Wasted ingredients
- Reduced output quantity
- Tool wear
- Longer craft time
- Lost secondary properties
- Catastrophic recipe-specific failure

Those are separate effects. The important rule is that the quality descriptor itself never moves outside its defined ladder.

---

## 14. Derived Ratings Should Not Become Identity Names

Some useful information is better treated as a derived rating than an identity descriptor.

An example considered was weapon danger/lethality:

- Hazardous
- Deadly
- Catastrophic

This is useful information, but it produces poor canonical names:

> `Catastrophic Masterwork Normal Dagger`

More importantly, lethality may change when modifiers, enchantments, damage bonuses, skills, or balance patches alter the output.

Therefore a threat/danger label is better represented as a derived UI/stat classification:

```text
Damage: 18–27
Threat: Deadly
```

rather than becoming part of the permanent generated item identity.

General rule:

> **If a label is mostly a summary of other stats, prefer deriving it for UI instead of storing it as an identity descriptor.**

---

## 15. Suggested Data Shape

The shared system should avoid rigid columns such as:

```text
Quality
Style
Material
Thing4
```

A more flexible representation is:

```text
ItemType / ItemArchetype

DescriptorAxis
DescriptorValue
```

An item type declares which axes it supports and how they are interpreted.

Conceptually:

```text
Dagger
    Scale
    CraftQuality
    Style

Ruby
    Size
    Integrity
    Clarity

HealthPotion
    Size
    Refinement
    Delivery

Venison
    SatietyScale
    FoodQuality
    Preparation
```

The implementation can still optimize this however is appropriate; the important part is the semantic separation.

### Useful descriptor metadata

A descriptor/value definition may eventually need metadata such as:

```text
DescriptorValue
    Axis
    Value
    OrdinalRank?          // for ordered ladders
    CompactToken
    DetailedPhrase?
    SortOrder
    ShowInName
    OmitWhenDefault
    AffectsBaseStats
    AffectsValue
    AffectsVisuals
```

Not every descriptor must be ordinal. `Masterwork` is naturally ranked; `Elven` and `Dwarven` are not.

Likewise, not every mechanically meaningful descriptor must always be printed.

---

## 16. Naming Is a Formatter, Not Storage

Each item family should own a naming grammar.

The formatter can decide:

- Compact vs. detailed mode
- Adjective ordering
- Prepositions such as `of`
- Whether a default descriptor is hidden
- Whether a descriptor becomes a phrase instead of a token
- Singular/plural behavior if necessary
- Whether a category has a special canonical word order

Example:

```text
Stored descriptors
    Size       = Giant
    Refinement = Murky
    Delivery   = Gradual
    Resource   = Stamina

Compact formatter
    Giant Murky Gradual Stamina Potion

Detailed formatter
    Giant Murky Potion of Gradual Stamina Recovery
```

This prevents awkward English from feeding back into the data model.

---

## 17. Current Category Summary

### Weapons

**Grammar:**

```text
[Quality] [Style] [Scale] [Type]
```

Scale defaults may be omitted.

Examples:

- **Masterwork Ritual Throwing Krys**
- **Crude Fancy Two-Handed Sword**
- **Superior Plain Crossbow** (`One-Handed` stored but omitted if default)

### Gems

**Grammar:**

```text
[Size] [Clarity] [Integrity] [Type]
```

Examples:

- **Tiny Clouded Chipped Sapphire**
- **Large Brilliant Perfect Ruby**
- **Great Clear Sound Emerald**

### Potions

**Compact grammar:**

```text
[Size] [Refinement] [Delivery] [Resource] Potion
```

**Detailed recovery grammar:**

```text
[Size] [Refinement] Potion of [Delivery] [Resource] Recovery
```

Examples:

- **Giant Murky Gradual Health Potion**
- **Small Radiant Rapid Rage Potion**
- **Medium Clear Steady Will Potion**

### Food

**Grammar is category-specific and may reorder tokens for natural English.**

Data roles:

```text
Scale       = hunger restored per consumption
Quality     = intrinsic preparation/craft quality
Preparation = cooked form/style
Type        = base food
```

Examples:

- **Fine Snack of Smoked Venison**
- **Common Serving of Roasted Potatoes**
- **Exquisite Meal of Spiced Beef**

Freshness is an ObjectProperty and is not part of the permanent generated name.

---

## 18. Extension to Other Item Families

The same architecture should extend naturally without requiring every family to use exactly the same words.

Possible examples:

### Armor

```text
Scale/Weight  = Light / Standard / Heavy
Quality       = Crude → Masterwork
Characteristic= Style / cultural construction
Type          = Plate Cuirass / Chain Coif / Leather Jerkin
```

### Tools

```text
Scale         = Light / Standard / Heavy / Precision, as appropriate
Quality       = Crude → Masterwork
Characteristic= Style / specialization
Type          = Hatchet / Pickaxe / Chisel
```

### Containers

```text
Scale         = Tiny / Small / Medium / Large / Great
Quality       = Crude → Masterwork
Characteristic= Style / construction
Type          = Pouch / Chest / Strongbox
```

These are extensions of the grammar, not finalized vocabularies.

---

## 19. Design Rules to Preserve

1. **One common descriptor architecture; category-specific vocabularies.**
2. **Type/Form defines what the object is.**
3. **Scale/Size defines magnitude, handling class, dose, or use size.**
4. **Quality/Make is intrinsic and normally ordinal.**
5. **Characteristic is the category-specific inherent distinction such as Style, Clarity, Delivery, or Preparation.**
6. **Mutable state belongs in ObjectProperties, not permanent item names.**
7. **Repairing or aging an item should not rewrite its canonical identity.**
8. **Derived summaries such as danger/lethality should remain derived unless there is a strong reason otherwise.**
9. **Default descriptors may be stored but omitted from names.**
10. **Naming grammar is category-specific and independent of storage order.**
11. **Compact and detailed names may be generated from the same descriptor set.**
12. **`Show Detailed Item Names` defaults to false.**
13. **Craft failures reduce the appropriate quality-like axis and clamp at the category's worst valid quality.**
14. **The descriptor language should carry mechanical semantics, not exist only for flavor text.**
15. **Do not invent extra descriptor axes merely to fill four slots; use the common shape only where the semantics remain useful and orthogonal.**

---

## 20. Current Working Vocabulary

### Weapon Quality

```text
Crude
Rough
Functional
Normal
Fine
Superior
Exquisite
Masterwork
```

### Weapon Style — examples, not exhaustive

```text
Plain
Fancy
Ritual
Ceremonial
Elven
Dwarven
```

### Weapon Scale — examples, category-dependent

```text
Throwing
One-Handed
Versatile
Two-Handed
Heavy
```

Bow families may instead use a size-oriented scale such as `Short`, `Standard`, `Long`, or `War` where appropriate.

### Gem Size

```text
Tiny
Small
Medium
Large
Great
```

### Gem Clarity

```text
Clouded
Murky
Clear
Brilliant
```

### Gem Integrity

```text
Cracked
Chipped
Flawed
Sound
Perfect
```

### Potion Size — current examples

```text
Tiny
Small
Medium
Large
Giant
```

### Potion Refinement

```text
Murky
Clouded
Clear
Brilliant
Radiant
```

### Potion Delivery

```text
Gradual
Steady
Rapid
```

### Potion Resource / Recovery Types — examples

```text
Health
Stamina
Mana
Rage
Will
```

### Food Scale

```text
Bite
Mouthful
Snack
Serving
Meal
Feast
Banquet
```

### Food Preparation — examples

```text
Raw
Boiled
Roasted
Baked
Smoked
Spiced
```

### Food Freshness — mutable ObjectProperty, not identity

```text
Fresh
Aged
Stale
Spoiling
Moldy
Rotten
```

Exact ladders can be tuned later without changing the architecture.

---

## 21. Final Model

The current model can be summarized as:

```text
ITEM IDENTITY
    Type / Form
    Scale / Size
    Quality / Make
    Characteristic

        ↓ generates

    Base Stats
    Base Value
    Base Appearance
    Craft Semantics
    Compact Name
    Detailed Name

ITEM INSTANCE STATE / OBJECT PROPERTIES
    Durability
    Freshness
    Charges
    Remaining Uses
    Enchantments
    Sockets
    Poison
    Bonus Stats
    Temporary or added modifiers
```

And crafting follows:

```text
Intended Item
    ↓ crafting process
Success / Failure Events
    ↓
Quality-like descriptor degrades as needed
    ↓
Clamp at worst valid quality
    ↓
Final valid item with the same shared descriptor language
```

This gives UltraSim a single item language that is readable by players and reusable by crafting, loot generation, stats, UI, persistence, vendors, recipes, and future item systems without forcing every object in the world to sound like it came from the same adjective table.
