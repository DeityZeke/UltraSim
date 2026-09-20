# Project Shape and Player Experience

**Status:** CANONICAL broad design direction

Primary references:
- [Movement / Camera / Control](../../Docs/Plans/MOVEMENT.md)
- [Inventory](../../Docs/Plans/INVENTORY.md)
- [Sim Core](../../Docs/Plans/Content/UltraSim_Sim_Core_Reasoning_Decision_Making_Reference_v4.md)
- [Dungeon Core / Dungeon Lab](../../Docs/Plans/Content/UltraSim_Dungeon_Lab_and_Dungeon_Core_Plan_FINAL.md)
- [Mine / Subterranean Excavation](../../Docs/Plans/MINE.md)
- [Deterministic Fate / Timelines / Realities](deterministic-fate-timelines-realities.md)
- [Social Conversation / Information Reveal](social-conversation-and-information-reveal.md)

## Current understanding

UltraSim's broad game shape is:

> **Approachable Sims-like local control over a deep, persistent, autonomous world where the player is mechanically another simulated actor rather than a special species.**

Useful influence shorthand:

```text
Kenshi
    world/faction autonomy and non-heroic player position

The Sims
    local camera, contextual interaction, people/needs/relationships/social intent

Ultima Online / RunUO
    tangible persistent objects, ownership, containers, housing/crafting/world semantics

Majora's Mask
    learnable repeatable Fate when the world is not meaningfully disturbed

UO / OSRS / Kenshi
    broad combat feel: systemic/stat-driven rather than precision-action combat
```

These are influence labels, not requirements to copy their exact mechanics.

## Agreed broad points

- The player character is fundamentally an ordinary simulated actor with a human controller.
- The world continues without the player.
- Camera/control should feel Sims-like: follow/orbit a character, with limited local breakaway rather than unrestricted omniscient map roaming during ordinary character play.
- Movement should remain comparatively simple and grounded; physics supports the world rather than becoming a precision traversal game.
- Interaction is contextual: select a person/object and choose valid actions such as Talk, Use, Examine, Open, Pick Up, Craft, Trade, Attack, etc.
- Social interaction is Sims-like rather than dialogue-tree driven; structured information is surfaced only when meaningful.
- Human multiplayer text chat is separate from NPC reasoning; NPCs do not need a free-text interpreter.
- Inventory should remain readable/item-focused while supporting UltraSim's persistent container semantics; the current Inventory plan owns exact grid, weight, nesting, and viewer policy.
- Crafting, items, economy, combat, Sim Core actors, mines, and dungeon organisms are parts of one coherent world rather than isolated minigames.
- Controls should remain simpler than the simulation beneath them.

## Guardrail

When adding detail, ask:

> **Does this create a meaningful choice, consequence, or simulation distinction?**

If not, prefer abstraction over additional UI/process steps.

Examples:

- meaningful: choose target craft quality;
- usually not meaningful: manually click every hammer strike;
- meaningful: choose a social topic;
- usually not meaningful: require arbitrary free-text NPC interpretation;
- meaningful: finite mine expansion and depletion;
- usually not meaningful: Minecraft-style block-by-block excavation.

## Open details

Exact combat math, camera distances, input bindings, inventory dimensions, carry formulas, and presentation remain owned by their subsystem references and should not be inferred from this overview note.
