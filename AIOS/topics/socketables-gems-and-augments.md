# Socketables, Gems, and Augment Crafting

**Status:** TENTATIVE / explicitly preferred current direction

Primary reference:
- [Items / Crafting / Descriptor Language](../../Docs/Plans/Content/UltraSim_Item_Crafting_Descriptor_Language.md)

Related:
- [Magic](../../Docs/Plans/Content/UltraSim_Magic_Elements_Reification_Semantics_v2.md)
- [Materials and Processing](materials-and-processing.md)

This note captures later socket/gem/Augment discussion that is not yet promoted into the current item reference.

## Agreed / preferred structure

### Sockets are simple fittings

Current preferred rule:

- no Small/Medium/Large socket classes;
- one socket accepts one compatible socketable object;
- a small gem, large gem, metal inset/nugget, rune-like object, relic fragment, or Augment still consumes one socket;
- the host item's form/size/construction determines plausible socket count rather than occupant size.

Illustrative direction:

```text
Ring
    usually 1 socket
    rare 2
    extraordinary/gaudy masterpiece may reach 3

larger weapons / shields / ceremonial objects
    can plausibly support more
```

Exact counts by item family remain open.

### Raw gems remain directly socketable

A raw gem should be useful without Augment Crafting.

Direct raw-gem behavior is intentionally:

- simple;
- predictable;
- host-context-sensitive.

Illustrative Ruby semantics:

```text
Ruby in weapon
    Fire expression

Ruby in armor
    Strength/stat expression

Ruby in shield
    Fire resistance

Ruby in jewelry
    Health/resource expression
```

Exact host/effect tables are not frozen.

### Augment Crafting is the intentional intermediary

A material can instead be crafted into a persistent **Augment**.

```text
Raw material(s)
    ↓
Augment Crafting recipe / intention
    ↓
specific Augment
    ↓
socket into host
```

The Augment resolves broad material potential into one intentional effect.

Unlike a raw gem's context-sensitive expression, the current preferred direction is that the completed Augment carries its selected effect across compatible host types.

Examples under discussion:

```text
Ruby
    → Fire Augment
    → Strength Augment
    → Health / Blood Augment
    → Rage-related Augment

Ruby + advanced reagents
    → Vampirism Augment

Gold
    → Fortune / Gold Find / Magic Find direction

Silver
    → Purity / anti-undead / curse-resistance direction

suitable metal
    → Durability Augment
```

Exact recipes/effects are not frozen.

Augments may be presented visually as medallions, badges, seals, inlays, iconography, signet-like fittings, or similar host-appropriate objects rather than requiring one literal physical shape at every scale.

## Gem readability

The preferred initial gem system favors a small static/readable vocabulary over exhaustive gemology.

Desired initial color families:

```text
Red
Orange
Yellow
Green
Blue
Purple
Black
White
Grey
```

The initial direction is one obvious readable gem identity per color family, with **Red = Ruby** fixed for v1 rather than supporting edge-case red sapphires/black rubies/etc.

Exact non-red species mapping is not yet fully frozen.

## Gem affinity package

A gem type may have a small static semantic affinity package rather than one hard-coded socket effect.

Current example:

```text
Ruby
    Color: Red
    Attribute: Strength
    Resource/Body: Health / Blood
    Element: Fire
    Emotion/Concept: Rage
```

Context or Augment Crafting chooses which face is expressed.

The exact affinity table for all gem types remains open.

## Gem descriptors

Gem identity should continue using the shared item-descriptor architecture.

Current useful axes include:

```text
Size
Clarity
Integrity / Quality
Type
```

Example:

> **Small Perfect Ruby**

or:

> **Large Brilliant Perfect Ruby**

The canonical item reference owns the descriptor-vs-ObjectProperty boundary and generated-name grammar.

## Raw material vs Augment

Current preferred conceptual split:

```text
RAW GEM / MATERIAL
    immediately usable
    simple
    predictable
    context-sensitive

AUGMENT
    crafted
    intentional
    effect-specific
    flexible across compatible socket hosts
    can combine gems, metals, reagents, and techniques
```

## Ideas still under exploration

- whether Augment creation sacrifices some raw-material potency in exchange for flexibility;
- exact Augment crafting Skill/Domain placement;
- whether raw non-gem materials can be socketed directly in ordinary equipment or primarily serve as Augment inputs;
- Augment removal/recovery rules;
- exact gem species for Orange/Yellow/Green/Blue/Purple/Black/White/Grey;
- exact affinity/effect table;
- exact socket counts and rarity curves by item family.
