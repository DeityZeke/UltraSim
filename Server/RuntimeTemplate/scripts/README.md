# Server Scripts

Server scripts are trusted, operator-owned C# source files compiled at startup.

Scripts can register commands, UI panels, archetypes, prefabs, and later gameplay
rule providers through stable server facades. They should not write generated
compiler output into this tree.
