# UltraSim Resource Pools Reference

## 1. Purpose

UltraSim resource pools should use one generic layered model rather than treating Health, Stamina, Mana, and future resources as unrelated bespoke systems.

The default model is inspired by layered resource systems where a character can have:

- a real current value;
- restoration already committed but not yet realized;
- temporary impairment that lowers the usable ceiling;
- a theoretical maximum.

The core vocabulary is:

```text
Maximum
Impairment
Current
PendingRestore
```

This is the base shape.

Individual resources configure how damage, spending, impairment, and recovery interact with those layers.

---

## 2. Core Resource Model

Conceptually:

```text
ResourcePool
    Current
    Maximum
    PendingRestore
    Impairment
```

Derived values:

```text
EffectiveMaximum
    = Maximum - Impairment

Potential
    = Current + PendingRestore
      clamped to EffectiveMaximum
```

Example:

```text
Maximum         = 100
Impairment      = 20
Effective Max   = 80

Current         = 35
PendingRestore  = 25

Potential       = 60
```

The actor currently has:

> **35 real resource**

with:

> **25 already committed to restoration**

and:

> **20 capacity currently unavailable because of impairment.**

---

## 3. Maximum

`Maximum` is the theoretical full capacity of the resource.

Examples:

```text
Health Maximum
Stamina Maximum
Mana Maximum
```

Maximum may be derived from:

- Attributes;
- species;
- body;
- equipment;
- effects;
- progression;
- injuries or permanent conditions;
- temporary modifiers.

The Resource Pool itself does not need to own the formula that determines Maximum.

It consumes the resolved value.

---

## 4. Current

`Current` is the amount of resource that is **actually available right now**.

Examples:

```text
Health Current
    actual life/health currently remaining

Stamina Current
    usable exertion capacity now

Mana Current
    usable magical energy now
```

Current is authoritative for immediate use.

Pending restoration is not yet equivalent to Current.

---

## 5. Pending Restore / Restoring Value

`PendingRestore` represents restoration that has already been committed but has not yet become real Current resource.

Example:

```text
Health
    Current = 20
    Maximum = 100

Drink potion:
    Heal 20 over 10 seconds

Result:
    Current        = 20
    PendingRestore = 20
    Potential      = 40
```

Over time:

```text
Current        20 → 21 → 22 → 23...
PendingRestore 20 → 19 → 18 → 17...
```

The Pending value is transferred into Current according to the restoring effect.

This allows the UI to communicate:

> **20 / 100 Health**
>
> with another 20 Health currently restoring.

---

## 6. Potential Resource

`Potential` is useful for UI and reasoning.

Conceptually:

```text
Potential
    = Current + PendingRestore
```

subject to the current recoverable ceiling.

Example:

```text
Health
    Current = 20
    Pending = 20
    Potential = 40
```

The character is **not at 40 Health yet**.

They merely have a path toward 40 if nothing interferes.

This distinction is important because the actor can still die while healing.

---

## 7. Impairment

`Impairment` represents capacity that cannot be restored through ordinary resource restoration.

Examples:

```text
Health
    Injury

Stamina
    Hunger impairment
    Exhaustion impairment

Mana
    optional magical strain / burn / impairment
```

Impairment reduces the effective recoverable ceiling.

Example:

```text
Maximum Health = 100
Injury         = 30

Effective Health Ceiling = 70
```

Ordinary Health restoration cannot exceed 70 until Injury is treated.

---

## 8. Multiple Impairment Sources

A resource may have multiple impairment channels.

Example:

```text
Stamina Maximum = 100

Hunger Impairment     = 20
Exhaustion Impairment = 15

Effective Maximum     = 65
```

Different impairment sources may require different recovery mechanisms.

Example:

```text
Hunger
    food

Exhaustion
    rest / sleep

Injury
    bandaging / medicine / rest / food / healing

Mana Strain
    time / meditation / magical treatment
```

The generic Resource Pool should support impairment composition without assuming every resource uses the same causes.

---

## 9. Resource Layers

The default model therefore has four conceptual layers:

| Layer | Meaning |
|---|---|
| **Maximum** | Theoretical full capacity |
| **Impairment** | Temporarily unavailable capacity requiring special recovery |
| **Current** | Real usable amount now |
| **Pending Restore** | Recovery already committed but not yet realized |

Example:

```text
Maximum = 100
Impairment = 20

Current = 35
PendingRestore = 25
```

means:

```text
Theoretical Max = 100
Effective Max   = 80
Current Now     = 35
Potential       = 60
```

---

# HEALTH

## 10. Health Default Model

UltraSim Health should use:

```text
Health
    Current
    Maximum
    PendingRestore
    Injury
```

Optional additional state such as:

- bleeding;
- burning;
- poison;
- disease;
- fractures;
- localized wounds;

should generally live in their own Effect/Injury systems rather than being packed directly into the scalar Health pool.

---

## 11. Health Restoration

Ordinary healing restores:

```text
PendingRestore
    ↓ over time
Current
```

Example:

```text
Current = 20
Pending = 20
Max = 100

Potion:
    20 Health over 10 sec
```

The character gradually climbs from 20 toward 40.

---

## 12. Damage Routing

Damage should be configurable by **routing semantics**.

The default UltraSim behavior may be:

```text
Ordinary Damage
    hits PendingRestore first
    then Current

Critical / Severe Damage
    may bypass PendingRestore
    and hit Current directly
```

Example:

```text
Current = 20
Pending = 20
Potential = 40
```

Take 10 ordinary damage:

```text
Current = 20
Pending = 10
Potential = 30
```

The blow destroys part of the healing that was on the way.

---

## 13. Critical Damage Can Kill Through Healing

Example:

```text
Current = 20
Pending = 20
```

Take 25 severe damage routed directly to Current:

```text
Current = 0
Pending = irrelevant
```

The actor can die while still having pending healing.

This preserves the distinction:

> **Pending restoration is not actual Health yet.**

---

## 14. Injury Is Not Ordinary Missing Health

Injury should reduce the amount of Health ordinary healing can restore.

Example:

```text
Maximum Health = 100
Injury         = 30

Effective Health Ceiling = 70
```

Then:

```text
Current        = 45
PendingRestore = 50
```

does not allow ordinary healing to reach 95.

It can only reach:

```text
70 / 100
```

until the Injury is addressed.

---

## 15. Injury Recovery

Injury may require special recovery.

Possible sources:

```text
Bandages
    stop bleeding
    modest Injury treatment

Rest
    slow Injury recovery

Food
    supports natural Injury recovery

Medicine
    stronger Injury treatment

Chivalric / Holy Healing
    potentially excellent Injury recovery

Specialized treatment
    severe wounds
    fractures
    deep trauma
```

The exact treatment model belongs to the Injury/Medicine system.

The Resource Pool only needs to understand that some capacity is impaired.

---

# STAMINA

## 16. Stamina Default Model

UltraSim Stamina should use:

```text
Stamina
    Current
    Maximum
    PendingRestore

    HungerImpairment
    ExhaustionImpairment
```

Other impairment sources can be added later if useful.

---

## 17. Stamina Spending

Ordinary activity spends Current Stamina.

Examples:

```text
Running
Attacking
Blocking
Dodging
Climbing
Heavy labor
Dragging
Swimming
```

Stamina expenditure should normally hit Current directly.

---

## 18. Stamina Pending Restore

Stamina restoration can also be gradual.

Example:

```text
Current = 30
PendingRestore = 20
```

A potion, food effect, rest effect, or recovery process may move Pending into Current over time.

---

## 19. Hunger and Exhaustion

Hunger and Exhaustion are not simply:

```text
Stamina -= X
```

They represent reduced capability to restore/use the full pool.

Example:

```text
Maximum Stamina = 100

Hunger Impairment     = 20
Exhaustion Impairment = 15

Effective Maximum     = 65
```

Meaning:

> even if the character stops moving and regenerates normally, ordinary recovery cannot push Stamina above 65 until those impairment causes are treated.

---

## 20. Hunger vs. Exhaustion Semantics

Useful distinction:

```text
Stamina Loss
    "I used energy."

Exhaustion
    "My body is progressively less capable of continuing exertion."

Hunger
    "I lack the fuel required to regain full physical capability."
```

These should remain separate enough that:

- rest can solve Exhaustion;
- food can solve Hunger;
- ordinary Stamina restoration may solve neither.

---

# MANA

## 21. Mana Default Model

Mana can use the same structure:

```text
Mana
    Current
    Maximum
    PendingRestore
```

By default it may have no impairment channels.

Later systems may introduce:

```text
Mana Strain
Mana Burn
Arcane Exhaustion
Spiritual Impairment
```

without changing the base Resource Pool architecture.

---

## 22. Mana Spending

Casting normally spends Current Mana.

Mana restoration effects can fill PendingRestore and transfer into Current over time.

The exact cost model belongs to the magic system.

---

# GENERIC CONFIGURATION

## 23. Resource Pools Should Be Data-Configured

UltraSim should provide default resource semantics without forcing them permanently on every server/game configuration.

Conceptually:

```text
ResourceDefinition
    Id
    MaximumRule
    RestoreRule
    ImpairmentChannels
    SpendingPolicy
    DamageRoutingPolicy
    ClampPolicy
    Death / Empty Behavior
```

This is conceptual rather than frozen API design.

---

## 24. Example Health Configuration

```text
Health

Current
Maximum
PendingRestore
Impairment:
    Injury

DamageRouting:
    Ordinary
        PendingRestore first
        then Current

    Critical
        Current first / bypass PendingRestore

Restore:
    Pending -> Current over time

Ceiling:
    Maximum - Injury
```

---

## 25. Example Stamina Configuration

```text
Stamina

Current
Maximum
PendingRestore

Impairment:
    Hunger
    Exhaustion

Spending:
    Current

Restore:
    Pending -> Current

Ceiling:
    Maximum
    - Hunger
    - Exhaustion
```

---

## 26. Example Mana Configuration

```text
Mana

Current
Maximum
PendingRestore

Impairment:
    none by default

Spending:
    Current

Restore:
    Pending -> Current
```

---

## 27. Routing Should Be Explicit

Do not hardcode one universal damage/spend rule into the Resource Pool.

Different operations can define routes such as:

```text
PendingFirst
CurrentFirst
CurrentOnly
PendingOnly
ImpairmentFirst
Split
Custom
```

Examples:

```text
Ordinary Health Damage
    PendingFirst

Critical Health Damage
    CurrentFirst

Stamina Expenditure
    CurrentOnly
```

The exact set can be simplified during implementation.

The important semantic rule is:

> **An effect specifies which resource layer it acts on.**

---

## 28. Pending Restore Can Be Lost

Pending restoration should not necessarily be guaranteed.

Depending on resource rules, it may be:

- consumed by incoming damage;
- interrupted;
- dispelled;
- cancelled by death;
- reduced by poison;
- limited by impairment.

This makes the restoring layer mechanically meaningful rather than purely cosmetic UI.

---

## 29. Multiple Restore Sources

Pending restoration may come from several simultaneous sources.

Examples:

```text
Health Potion
Food regeneration
Bandage
Spell
Natural recovery
```

The implementation may store:

- one aggregate PendingRestore value;
- multiple queued restoration effects;
- or both.

A useful split is:

```text
ResourcePool
    aggregate current/pending totals

RestoreQueue / Effects
    source-specific rates/durations
```

This keeps hot resource queries cheap while preserving effect semantics.

---

## 30. Restore Queue

A restore effect may conceptually contain:

```text
RestoreEffect
    ResourceId
    RemainingAmount
    Rate
    Duration
    Source
    Routing / Restrictions
```

Example:

```text
Healing Potion
    Remaining = 20
    Duration = 10 sec
    Rate = 2/sec
```

The pool exposes the aggregate:

```text
PendingRestore = sum(active remaining restore)
```

Exact storage should be compact and bounded.

---

## 31. Clamping

Resource changes should use explicit clamping.

Examples:

```text
Current
    0 .. EffectiveMaximum

PendingRestore
    cannot promise recovery beyond the configured ceiling

Impairment
    0 .. Maximum
```

If Maximum changes dynamically, Current/Pending/Impairment need deterministic reconciliation rules.

---

## 32. Effective Maximum Changes

Example:

```text
Maximum = 100
Current = 90

Temporary debuff:
    Maximum becomes 70
```

The resource definition should specify whether:

- Current clamps immediately to 70;
- excess becomes Pending/overflow;
- excess is remembered and restored when the debuff ends;
- another custom behavior applies.

This should be resource-configurable.

---

## 33. Resource Empty Semantics

Zero does not have to mean the same thing for every resource.

Examples:

```text
Health = 0
    death / incapacitation / critical state

Stamina = 0
    exhausted / unable to perform costly actions

Mana = 0
    cannot cast mana-costing spells
```

The Resource Pool provides the scalar state.

Higher systems define consequences.

---

## 34. Resource Reasoning Integration

The Sim Core can reason about:

```text
Current
Potential
EffectiveMaximum
Impairment
RestoreRate
```

Example:

```text
Health Current = 20
Pending = 40

Actor knows:
    actual Health is dangerously low
    large healing is incoming
```

This may affect:

- Fight vs. Flee;
- whether another potion is needed;
- whether to rest;
- whether to risk a critical attack.

---

## 35. UI Presentation

The resource bar should be able to distinguish:

```text
Current
Pending Restore
Impairment
Missing capacity
```

Example:

```text
Health 20 / 100

20 Current
20 Restoring
30 Injury
30 ordinary missing Health
```

The exact visual language is a UI concern.

The data model should expose all layers clearly.

---

## 36. Why This Model Is Useful

The layered Resource Pool solves several problems at once:

- gradual healing without pretending it already happened;
- damage interfering with healing;
- death while healing;
- Injury requiring special treatment;
- Hunger/Exhaustion reducing Stamina ceiling;
- configurable resource-specific behavior;
- common infrastructure for Health/Stamina/Mana;
- future resources without redesigning the entire system.

---

## 37. Core Vocabulary

**Maximum**  
The theoretical full capacity of the resource.

**Effective Maximum**  
The portion currently accessible after impairment.

**Current**  
The amount actually available now.

**Pending Restore / Restoring**  
Recovery already committed but not yet transferred into Current.

**Potential**  
Current + Pending Restore, bounded by the current recoverable ceiling.

**Impairment**  
Capacity unavailable to ordinary restoration and requiring some special recovery path.

**Restore Effect**  
A source-specific process transferring Pending restoration into Current.

**Routing**  
The rule determining which resource layer an incoming effect changes first.

---

## 38. Core Rules

1. **Current is real usable resource now.**
2. **Pending Restore is promised/active restoration, not actual Current yet.**
3. **Potential is useful information but does not prevent death or resource failure by itself.**
4. **Impairment reduces the ordinary recoverable ceiling.**
5. **Different impairment sources may require different recovery mechanisms.**
6. **Damage/spending routes are resource-configurable.**
7. **Ordinary Health damage may consume Pending Restore before Current.**
8. **Critical/severe Health damage may bypass Pending Restore and hit Current directly.**
9. **Injury is distinct from ordinary missing Health.**
10. **Hunger and Exhaustion are distinct from ordinary Stamina expenditure.**
11. **Mana uses the same infrastructure even if its default configuration is simpler.**
12. **Resource zero semantics belong to the owning gameplay system.**
13. **The engine should provide UltraSim defaults without preventing alternate configurations.**
14. **The layered model should remain generic enough to support future resources.**

---

## 39. Open Implementation / Tuning Questions

The semantic model is considered settled enough to build on.

Implementation/tuning questions remain:

- exact integer widths for each resource;
- whether all pools use the same numeric type;
- how restore effects are stored/aggregated;
- restore tick cadence;
- fractional restore accumulation;
- damage routing enum/policy shape;
- how multiple impairment channels compose;
- how temporary Maximum modifiers reconcile Current/Pending;
- whether over-restoration can be banked;
- exact Injury recovery rules;
- exact Hunger/Exhaustion progression;
- death/incapacitation thresholds;
- network replication granularity;
- deterministic ordering of simultaneous damage/restore events;
- UI representation of Current/Pending/Impairment.

These should be resolved from implementation and gameplay requirements rather than by over-specifying the generic pool now.
