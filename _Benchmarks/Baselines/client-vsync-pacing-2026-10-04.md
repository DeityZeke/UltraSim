# Exported-client presentation pacing characterization — 2026-10-04

Status: completed narrow characterization; no product VSync/default/renderer change.

Implementation commits: `475d14cc723b6378017d86b5cb93801ecf25a3f9` (effective pacing diagnostics) and `b3a1e28fcfa96a0c31bd1d533325dd621b017897` (opt-in ten-second study window). The matched export and evidence use `b3a1e28fcfa96a0c31bd1d533325dd621b017897`. Configuration: Release, exported Single Player, Godot 4.7.1 Mono, Official art, Concept Demo, vegetation Off, fixed idle position, 1920×1080 output, eight-second warmup, hidden F11/Performance page, ten-second focused measurement. No code optimization was attempted.

## Matched product result

| Effective launch mode | VSync reported | Average FPS | Average / P95 / P99 / maximum wall-frame interval | Process CPU / 10 s | Samples |
| --- | --- | ---: | --- | ---: | ---: |
| Project/default | Enabled | 144.02 | 6.943 / 7.697 / 8.480 / 15.910 ms | 2,100 ms | 1,440 |
| Godot `--disable-vsync` | Disabled | 1,256.31 | 0.796 / 1.229 / 1.344 / 9.079 ms | 10,320 ms | 12,563 |

Both controls reported 143.83 Hz display refresh, `Engine.MaxFps=0`, `DisplayServer.GetName()=X11`, rendering method `gl_compatibility`, driver `opengl3`, adapter NVIDIA GeForce RTX 4070 Ti SUPER, 1920×1080 output, 49 rendered chunks, 293 renderer nodes, 143 renderer resources, two detailed actors, zero batched actors, zero vegetation patches/blades, and zero pending builds/retirements. Semantic resident chunks were 81; player position and renderer census stayed unchanged between measurement endpoints. The JSON records `PacingAtStart` and `PacingAtEnd`, which matched in each completed run.

Host context: `XDG_SESSION_TYPE=wayland`, KDE Plasma (`XDG_CURRENT_DESKTOP=KDE`), `kwin_wayland --xwayland` and Xwayland `:0` observed. `kscreen-doctor -o` reported DP-1 at 2560×1440 @ 143.86 Hz, scale 1, VRR Never. NVIDIA driver 615.71.09 reported display active. Godot's exported product selected its X11 display server under that Wayland desktop session. No desktop, compositor, driver, or OS synchronization setting was changed.

The default average of 144.02 FPS matches reported display refresh within normal sampling/clock variation. Disabling Godot VSync on the *same binary and workload* raised the observed loop rate to 1,256.31 FPS (8.72× default) while consuming about 4.91× process CPU time. This establishes that Godot's default VSync presentation pacing explains the earlier ~144 FPS warmed-idle observation on this setup; it was not a ~144 FPS CPU/GPU throughput ceiling or a tight external compositor/driver cap under the disabled control. The uncapped frame interval is **not** an isolated GPU render-time or delivered-display-frame metric; CPU/GPU decomposition and tear/latency quality were not measured. The diagnostic setter control was unnecessary under the requested stop condition.

## Commands and retained controls

Export: `Client/Godot/export-client.sh SinglePlayer` (passed, Official packages included). For each final control, the exported executable was run with `ULTRASIM_COMMIT=b3a1e28fcfa96a0c31bd1d533325dd621b017897`, `--client-hitch-study=idle`, `--client-hitch-hide-debug`, `--client-hitch-window-seconds=10`, `--p9-no-vegetation`, and its absolute `--client-hitch-evidence` directory. The disabled control additionally put Godot `--disable-vsync` before the `--` user-argument separator. Both exited 0 with `CLIENT HITCH STUDY PASS`.

- [Default ten-second context](client-vsync-pacing-2026-10-04/controls/default-10s/idle.context.json) and [raw frame snapshot](client-vsync-pacing-2026-10-04/controls/default-10s/idle.frames.json)
- [Disabled ten-second context](client-vsync-pacing-2026-10-04/controls/cli-disabled-10s/idle.context.json) and [raw frame snapshot](client-vsync-pacing-2026-10-04/controls/cli-disabled-10s/idle.frames.json)

An initial default 30-second control passed: 143.85 average FPS, 6.952 ms average, 7.651 ms P95, 7.755 ms P99, 15.999 ms maximum, 6,550 ms CPU. Its [context](client-vsync-pacing-2026-10-04/controls/default/idle.context.json) and [frames](client-vsync-pacing-2026-10-04/controls/default/idle.frames.json) are retained. An initial 31-second disabled attempt reached 39,414 total frames, so the fixed 16,384-frame ring held only its final 12.94 seconds; the existing complete-window check rejected that capture (exit 1), appropriately. Its [partial raw snapshot](client-vsync-pacing-2026-10-04/controls/cli-disabled/idle.frames.json) is retained, not treated as a matched full-window result. The opt-in ten-second window allowed complete, equivalent controls without changing the sampler ring, wall-frame semantics, or product FPS policy.

Focused verification: `dotnet build Client/Godot/UltraSim.Client.Application.Godot.csproj -c Release --no-restore -v:q` passed (0 warnings/errors); `_Tests/UltraSim.S0.Godot.Tests` Release build passed (0 warnings/errors); native `S08HeadlessAcceptance.tscn` passed on the final implementation tree, including new Performance-page/snapshot pacing assertions. `./verify.sh --architecture-only` passed all four checks. The exported Single Player `--client-performance-smoke` passed (`CLIENT PERFORMANCE HUD PASS`). Both matched study processes exited 0. The full canonical Release verifier was not run for this narrow diagnostic change; prior canonical status is unchanged.
