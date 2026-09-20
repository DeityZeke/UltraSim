# Materials and Processing

**Status:** EXPLORING / current preferred direction

## Core model

Recipes normally request a **Material Family**, while the actual item records the **specific Material Type** used.

Examples:

```text
Material Family
    Metal
    Wood
    Leather
    Stone
    Clay/Ceramic
    Glass
    Textile
    Bone/Organic
```

A recipe might require:

```text
Two-Handed Axe
    Metal
    Wood
    Leather
```

and accept Steel/Oak/Leather, Bronze/Ash/Leather, Mithril/Yew/etc. unless the process genuinely requires a specific material.

Material rarity should produce physical/crafting trade-offs rather than automatic tier inflation.

## Starter material palette

Metals: Bronze, Iron, Steel, Silver, Gold, Platinum, one or more fantasy metals such as Mithril.

Woods: Generic Wood, Ash, Oak, Yew, Willow, Maple, Birch, Pine, Mahogany. Not every botanical species needs bespoke lumber; unimportant trees can yield Generic Wood.

Other useful families include Leather/Hide, Stone, Clay/Ceramic, Glass, Cloth/Linen/Wool/Silk, Bone/Horn/Chitin/Shell.

## Production classes

```text
DIRECT
    source/ore -> material

ALLOY
    material + material -> new material

REFINED
    material + additive/process -> new material state

EXOTIC
    material + unusual world resource/technique
```

Do not require every metal to use a RuneScape-style two-input bar recipe. Alloying/refinement should be common enough to matter but only exist when semantically useful.

## Mithril

Current concept:

Mithril is not merely a blue ore tier. It is a difficult material state roughly analogous to steelmaking plus magical actualization.

```text
Iron
+ Carbon
+ small Silver component (preferred current idea; Platinum remains possible)
+ extreme magical saturation/binder
    ↓
Mithril
```

Possible artificer catalyst: a highly magical reagent such as **Moon Dew**.

Alternative high-end production: one extremely powerful caster or multiple casters continuously pour raw magic into the melt throughout bonding.

The magic must be present while the structure forms; finished Steel cannot simply be enchanted afterward into equivalent Mithril.

Natural Mithril is exceedingly rare because nature must accidentally recreate the process:

```text
deep Iron deposit
+ overlapping Silver/precious-metal deposit
+ suitable carbon/mineral conditions
+ extreme ambient magic
+ geological heat/pressure/time
    ↓
natural Mithril pocket
```

## Bloodwood

**Bloodwood** is fantasy wood transformed by prolonged blood-rich conditions.

Current concept:

- trees grown on blood-soaked battlefields can gradually become deep blood-red Bloodwood;
- it can be cultivated deliberately by supplying blood as part of its growth inputs;
- Bloodwood has a natural affinity for Blood Magic;
- possible effects include cheaper/easier Blood Magic control, better stabilization, or the ability to hold a small blood substrate;
- it should be a niche material, not simply Oak +10.

This creates world-history semantics: an old Bloodwood grove may imply a major past battle or deliberate Blood Mage cultivation.

## Hides and leather

Raw hides retain creature identity because species matters for trophy/fur outcomes:

```text
Bear Hide
Wolf Hide
Deer Hide
...
```

Processing should usually collapse them into a smaller functional material vocabulary rather than creating BearLeather/WolfLeather/DeerLeather as separate mechanical classes forever.

Possible outputs:

```text
Leather
Supple Leather
Tough Leather
Hardened Leather
Magic-Resistant Leather
Furred Hide / Pelt
```

Example:

```text
Bear Hide
    preserve fur -> Bear Pelt
    tan/dehair  -> Tough Leather
```

Cheap provenance may survive processing:

```text
MaterialType = ToughLeather
Origin       = Bear
NaturalColor = DarkBrown
```

Color/provenance can matter for appearance without requiring a full new mechanical material definition.

## Resource drops

Resource drops should be data-driven and replaceable/extensible.

A tree may initially produce Generic Wood, then later gain a specific wood/blossom/branch output without changing the harvesting system.

The same principle applies to creatures and hides.
