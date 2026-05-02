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
panel builders plus stable admission event names. Servers can replace those
steps through the replacement registry instead of modifying kernel code.
