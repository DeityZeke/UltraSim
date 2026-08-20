# UltraSim Server Runtime Template

This folder contains source-controlled package and data examples for server
runtime roots.

Startup creates the runtime root and an empty `scripts/` folder. It does not copy
these scripts automatically. Operator-authored server code belongs under
runtime `scripts/`; generated compiler output belongs under runtime
`cache/scripts/`.
