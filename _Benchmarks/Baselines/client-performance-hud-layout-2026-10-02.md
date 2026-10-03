# Client Performance HUD layout control — 2026-10-02

This is focused F1.12 UI evidence, not a frame-rate rating or a canonical Release-verifier result.

## Provenance

- Starting HEAD: `6b697779343dd42a19386a7b573c5c354f984431`.
- Tested implementation: `15603e9c681728e5f66da7ebce9416ec8ceaf49d`.
- Product: exported Linux Single Player, Godot `4.7.1.stable.mono.official.a13da4feb`, OpenGL 3.3, NVIDIA GeForce RTX 4070 Ti SUPER, 1920×1080.
- Capture command: `UltraSimSinglePlayer.x86_64 --resolution 1920x1080 -- --client-performance-layout-capture=<absolute.png>`.
- [Performance with Help closed](client-performance-hud-layout-2026-10-02.png), SHA-256 `5e058cae4ef3e6b8cb31c34d19c5d959a96d5505744302fde0d4fc1045337e8d`.
- [Performance with Help open](client-performance-hud-layout-2026-10-02.help.png), SHA-256 `cd92d42faa41e6ec579326d8c8aa501e80b919b636824b5fc3140ba5f2afa8a2`.

Both images are 1920×1080 actual exported-window readbacks. The capture asserts the Help and Performance rectangles do not intersect. The panels have opaque backgrounds; the ordinary Local diagnostic block hides while F11 diagnostics are open. Help, including the profile buttons, is session-owned on the Debug layer and initially closed. The graph is taller and retains its 8.33/16.67/33.33 ms guides. Peaks above 50 ms are clipped **only in the drawing**; statistics and raw JSON keep the actual duration. Both low-FPS displays now include their exact selected frame counts without changing their definitions.

The capture harness performs native `ForceDraw`/readback, which itself may add an eligible hitch. Values visible in these images are therefore **not** a performance comparison or vegetation result.

## Focused verification

- `dotnet run --project _Tests/UltraSim.Client.Tests/UltraSim.Client.Tests.csproj -c Release --no-restore`: 37/37 passed. The sampler's zero-allocation warmed hot-path check and exact low-tail count assertions passed.
- `dotnet build _Tests/UltraSim.S0.Godot.Tests/UltraSim.S0.Godot.Tests.csproj -c Debug -p:Optimize=true --no-restore`: passed, zero warnings/errors.
- Godot headless `res://S08HeadlessAcceptance.tscn`: `S0.8 / I0.11 GODOT ACCEPTANCE PASS`. Added checks for opaque panels, taller graph, drag bounds, Help toggle/profile action, Debug-layer/session ownership, and strong-reference native style retirement.
- `./verify.sh --architecture-only`: all four checks passed.
- `Client/Godot/export-client.sh SinglePlayer|Online|Complete`: all three exports passed from the tested implementation commit. Complete restore emitted the pre-existing `NU1505` duplicate `PackageDownload` warning, without an export failure.
- Each exported edition with `--client-performance-smoke`: passed.
- Exported Single Player with `--d0-0-smoke` and `--f1-7-ui-smoke`: passed on the same runtime implementation during development.
- Exported Single Player 1920×1080 layout capture: passed, including Help-open non-overlap and clean exit.

The full `./verify.sh --configuration=Release` was **not** run for this UI-only pass. Its prior variable PF1/W7/Online status is not converted into a pass by these focused controls. No sampler eligibility, percentile/low definition, raw capture format, renderer, vegetation, physics, or Network behavior changed.
