# Server Scripts

Server scripts are trusted, operator-owned C# source files compiled at startup.

Scripts can register commands, UI panels, archetypes, prefabs, and later gameplay
rule providers through stable server facades. They should not write generated
compiler output into this tree.

Use top-level folders for loose scripts and small content groups. Use
`Packages/` for complete systems that should stay together as one installable
unit. For example, a future Knives-style chat system should live under
`Packages/Chat/` with its own channels, commands, UI panels, rules, and data.

Default platform packages that are still server-authored also live under
`Packages/`. `Packages/Admission/` contains the default login and character-flow
panel builders plus stable admission event names. Servers register or replace
those steps through the script admission registry instead of modifying kernel
code.

`Mobiles/` contains entity-backed mobile archetypes and prefabs. The stock
template registers a wide `BaseMobile -> BaseHumanoid -> Human` archetype chain
and the `mobiles/human` prefab. Sex and reproduction state live on the
`BaseMobile` component set, so human, animal, and creature systems can filter
that data without requiring separate male/female archetypes by default.
