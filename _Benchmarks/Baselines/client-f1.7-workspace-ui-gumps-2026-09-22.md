# F1.7 — Application workspace, Gameplay Frame, and shared UI/gumps

Accepted 2026-09-22. The starting revision was
`adf5c3b547a86a06e1c8427de1e145ebd133d404`. The tested implementation tree was
`55fbd0337077f1c4014a779aef51a789a7d5b899`. The final closure HEAD is the later commit that owns
this immutable evidence and is reported in the handoff because a commit cannot embed its own hash.

F1.0–F1.7 are closed, F1.8 is next, and F1.8+ was not implemented.

## Implemented boundary

The persistent Godot application workspace now owns six finite category roots in this draw order:

| Category | Canvas layer | Current responsibility |
| --- | ---: | --- |
| Workspace | 5 | Dark unused application area around the Gameplay Frame |
| World | 10 | Frame-clipped world-relative overlays |
| HUD | 20 | In-session HUD and system Chat banner |
| Menus/Gumps | 30 | Separate gameplay-gump and shell-menu hosts |
| Debug | 40 | Finite local diagnostic page |
| Context/Tooltip | 50 | Hover tooltip and disclosed-target context menu |

Local and Remote sessions lease the same persistent category hosts and bounded gump manager. The
shell menu host is distinct from the gameplay-gump host, so opening a shell modal suppresses
gameplay interaction and transient Context without destroying modeless gameplay windows.

The gameplay manager has a fixed maximum of 16 windows. It supports simultaneous Inventory and
Server Monitor views, deterministic cascade and bring-to-front order, a 26-pixel drag header,
application-window clamping, exact independent close, and exact session retirement. Local and
Remote compiled semantic UI views use the same native manager rather than parallel ownership
trees.

Right-click on a disclosed world target now opens a first-class Select / Interact / Cancel context
menu. It no longer overloads OrbitCommand stop. Escape and shell/modal input loss remain explicit
stop paths. World input outside the Gameplay Frame is rejected. F11 cycles exactly Off, Client,
Presentation, Content, then Off; it is reserved from server profile remapping.

## Gameplay Frame controls

`GameplayFrameLayout.MaxFit` is finite, integer, half-open, centered, and aspect preserving. The
selected Gameplay Output shape defaults to 1920x1080 and may be overridden for development with
`--gameplay-output=WxH`. Representative exact controls are:

| Application window | Selected output shape | Gameplay Frame | Unused workspace |
| --- | --- | --- | ---: |
| 1920x1080 | 1920x1080 | (0,0,1920,1080) | 0 px² |
| 1920x1080 | 800x600 | (240,0,1440,1080) | 518,400 px² |
| 1920x1080 | 600x800 | (555,0,810,1080) | 1,198,800 px² |
| 3440x1440 | 1920x1080 | (440,0,2560,1440) | 1,267,200 px² |
| 600x800 | 800x600 | (0,175,600,450) | 210,000 px² |

Four dark masks cover the unused region and the World overlay host is clipped to the frame. This is
an intentional transitional main-viewport implementation: it establishes layout, ownership, and
input admission without pretending that the 3D camera or render target has already moved into the
frame.

## Native and lifecycle evidence

The focused native acceptance established:

```text
F1.7 LOCAL UI PASS views=2 windows=2 cascade=(8, 8)/(8, 30) dragged=(8, 8) context=True/True
```

The native S0.8 acceptance independently exercised category ordering, frame masks and input bounds,
two concurrent compiled gumps, shell-modal suppression without destruction, bring-to-front, extreme
drag clamping, resize recovery, independent close, the finite Debug cycle, context retirement, and
session retirement back to zero transient children/windows.

The final mixed lifecycle retained the canonical sequence:

```text
Local -> menu -> Local -> menu -> Remote A -> menu -> Remote B
      -> menu -> Remote A reconnect -> menu -> Local -> menu
```

It ended with `boundaries=clean`, `sessionResidue=none`, exact profile-decision reuse, and no stale
gumps, tooltip/context state, focus, or native session UI ownership. The F1.0 semantic/resource
guardrail remained:

```text
local_chunks=49/25
local_batches=64/7
remote_cold_bytes=733198
remote_warm_reused=1/3
remote_resources=1/2213268
retirement=clean
```

## Intentional differences from the F1.0 pre-rework baseline

- Workspace and Context/Tooltip are now real persistent category roots.
- Local Inventory and Local/Remote server UI moved from ad-hoc HUD ownership into
  Menus/GameplayGumps.
- Local hover tooltip moved from World UI and Remote hover tooltip moved from HUD into Context.
- The system Chat banner is explicitly HUD-owned.
- Shell menus and gameplay gumps use different hosts.
- World pointer admission is frame-aware, and ContextMenu is no longer synonymous with stop.

Game outcomes, semantic identities, Inventory/Chat/UI authority, public actor state, renderer
chunk/batch/resource counts, authenticated Remote application proof, and content activation remain
unchanged. These routing changes are the intended F1.7 architecture delta.

## Verification and reproducibility

Host: CachyOS Linux `7.2.6-1-cachyos` x86_64; AMD Ryzen 7 7800X3D (8 cores / 16 logical
processors); .NET SDK 8.0.131, runtime 8.0.31; Godot
`4.7.1.stable.mono.official.a13da4feb`; Release configuration.

Commands included:

```sh
./verify.sh --architecture-only
./verify.sh --f1-7-only --configuration=Release
./verify.sh --configuration=Release
```

Engine-neutral Client tests passed 32/32. Architecture verification passed. The F1.7 focused gate
passed its managed frame/settings tests, native S0.8 workspace/gump acceptance, builds/import, and
the real Local F1.7 journey. One uninterrupted final canonical Release run on
`55fbd0337077f1c4014a779aef51a789a7d5b899` exited zero with `PASS repository verification`.

The canonical run covered all managed suites; S0.8/T0.8/T0.10 Godot acceptance; all Local D0/F0
native smokes; SinglePlayer, Online, and Complete exports; cold/warm Contentless Online; Official
installed/absent/restored; PF1/PF2 exported swarms; F0.7 mixed lifecycle; F1.0 guardrails; final
composition parity; and `git diff --check`.

Non-authoritative attempts are retained transparently. They exposed two legacy smoke assertions
that still assumed direct centered Inventory/sign children; both were corrected to assert the real
managed-gump ownership. One M0.0 timing attempt and one PF1 mixed-chaos attempt were isolated flakes
whose exact gates subsequently passed. Godot 4.7 could also return success after an internal managed
publish failure while leaving an incomplete export. Export verification now validates the executable
and managed dependency product, clears only the exact partial ExportRelease product, retries once,
and fails closed if still incomplete. The final canonical run exercised that retry and passed.

Implementation commits:

- `1cba62693acf192d45641ecf995bc855fe8aa051` — implement F1.7 client workspace and gump composition;
- `85d5b8d7444d191f6d18bfcbec004a1a22af15df` — align the legacy Inventory smoke with managed gumps;
- `2b1f2c209eb8acd2d1e4ef1006235c6fd7692d88` — align the sign smoke with the gameplay-gump host;
- `55fbd0337077f1c4014a779aef51a789a7d5b899` — fail closed and retry incomplete Godot exports.

## Deliberately deferred

F1.8 remains responsible for declarative camera/control rig expansion, frame-aware camera projection,
crop, and visibility-envelope policy. F1.8A remains responsible for prediction/reconciliation. F1.9
remains responsible for a real Gameplay Frame render target/SubViewport and independent output
resolution. No camera crop, SubViewport, prediction, interpolation, direct RenderingServer rewrite,
content-scope change, atlas change, or authority/Network policy change was included in F1.7.
