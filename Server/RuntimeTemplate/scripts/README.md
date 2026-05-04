# Server Scripts

Server scripts are trusted, operator-owned C# source files compiled at startup.

Scripts can register commands, UI panels, archetypes, prefabs, and later gameplay
rule providers through stable server facades. They should not write generated
compiler output into this tree.

Use top-level folders for loose scripts and small content groups. Use
`Packages/` for complete systems that should stay together as one installable
unit. For example, a future Knives-style chat system should live under
`Packages/Chat/` with its own channels, commands, UI panels, rules, and data.

Default platform packages that are still server-authored can ship separately as
installable packages. `Packages/Admission/` is the stock login and character-flow
package source used by tests and package builds; it is not copied into a fresh
runtime root automatically.

`Mobiles/` can contain entity-backed mobile archetypes and prefabs. The stock
mobile package registers a wide `BaseMobile -> BaseHumanoid -> Human` archetype
chain and the `mobiles/human` prefab. Sex and reproduction state live on the
`BaseMobile` component set, so human, animal, and creature systems can filter
that data without requiring separate male/female archetypes by default.
