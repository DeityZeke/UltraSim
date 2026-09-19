# F1.0 pre-rework client baseline — 2026-09-19

F1.0 is closed on tested commit `29ae5aa732f9ce38a253f4010636ca0f01093061`
(starting HEAD `1345a4ca391818bac02c896e6d4f2deee63c4ddf`). This checkpoint added
diagnostic guardrails only: no production Game, Network, content policy, UI architecture, or
presentation semantics changed, and no F1.1+ implementation began.

The machine-readable companion is
`client-f1.0-pre-rework-baseline-2026-09-19.json`.

## Environment and verification

- Release configuration on CachyOS Linux, AMD Ryzen 7 7800X3D (16 logical CPUs).
- .NET SDK 8.0.131, runtime 8.0.31, Godot 4.7.1 Mono.
- Complete owns the coordinated F0.7 lifecycle; physical SinglePlayer, Online, and Complete
  products were separately built, inspected, exported, and executed by the canonical verifier.
- `./verify.sh --architecture-only`: pass.
- direct T0.10 native Godot acceptance: pass.
- `ULTRASIM_F10_EVIDENCE_OUTPUT=/tmp/ultrasim-f1.0-client-baseline.raw.json ./verify.sh`:
  pass, ending with `PASS repository verification`.

The canonical run included contentless exported Online cold/warm acquisition, declarative profile
change/reuse, exported Complete local hosting, two active exported Online clients, an eight-bot
mixed-chaos swarm, accounting gates, and the F0.7 mixed lifecycle. No required environmental gate
was unavailable.

## Coordinated baseline

The F0.7 evidence journey was:

```text
Local -> menu -> Local -> menu
      -> Remote A -> menu
      -> Remote B -> menu
      -> Remote A reconnect -> menu
      -> Local -> menu
```

All six retirement boundaries returned to the same persistent root ownership:

| Boundary fact | Count/state |
| --- | ---: |
| Controller children | 0 |
| Content-catalog children | 0 |
| World / World UI children | 4 / 0 |
| HUD children | 1 |
| Menus / Debug children | 1 / 1 |
| Menu-host children | 2 |
| Session active / server profile active | false / false |
| Focused control | Button |

Persistent cache contents are deliberately not required to become empty. After the remote
journeys, six immutable blobs (7,669,834 bytes), six realm manifests, and two activations remain;
the next session opens with zero live blob/catalog pins. Transient native presentation, active
profile ownership, focus, and session children retire at every boundary.

### Local Concept Demo

The representative radius-two Local session had 162 authored, 49 semantic-resident, and 25
rendered chunks with nine known public identities. Presentation owned 177 active nodes and 99
resources: 28 terrain surfaces, 64 static batches, seven public batches, and three fallback groups.
Two actors were active (one primitive, one content-backed animated actor). The ordinary active UI
snapshot had three World-UI children, three HUD-host children, no open semantic view, and gameplay
input enabled. The retirement canary then opened Inventory and focused Chat before proving cleanup.

Application session epochs were 2, 4, and 6 across the three Local entries. The current local
profile remained OrbitCommand/Orbit at 14 m and 70 degrees with the exact physical binding
signature retained in the JSON.

### Remote cold content

Remote A began with an empty cache, fetched three required packages/six files (7,669,834 bytes),
then held six blobs, three manifests, one activation, seven live blob pins, and one live catalog
pin. Its active native presentation had nine render chunks, 81 nodes, 51 resources, 12 terrain
surfaces, 32 static batches, seven public batches, and 39 fallback groups. Two actors were active:
one primitive and one animated/content-backed. One retained actor resource held 2,213,268 bytes.

Remote B used a distinct authenticated realm over the same process cache. Its package acquisition
remained realm-specific, while CAS installation reported all six blobs reused. This is current
behavior, not the future F1.1 content-scope model.

### Remote warm reconnect

Remote A reconnect used a fresh application session epoch, remembered the exact realm profile,
reused all three packages and six files, fetched zero files/bytes, and recreated one live actor
resource from the retained immutable content. It inherited no prior live native presentation.

## Guardrails added

- `GodotWorldRendererSnapshot` now reports maintained terrain, static-batch, public-batch, and
  fallback-group counts without scanning the arbitrary scene tree.
- Local and Remote snapshots now expose their existing semantic/cache, renderer, actor,
  actor-resource, UI-ownership, focus, and lifecycle state in one coordinated capture.
- Root session-boundary snapshots include Menus/menu-host ownership and focused control type.
- F0.7 can emit bounded structured evidence and the canonical verifier asserts cold/warm content,
  fresh lifetime, semantic/native ownership, and exact clean retirement.
- T0.10 asserts that its native renderer census is internally meaningful.

## Intentionally preserved pre-F1 mismatches

- Local Inventory remains under HUD; Local tooltip remains under World UI.
- Remote server UI, Chat, system banner, and tooltip remain under one HUD tree.
- No final Context/Tooltip layer or multi-window/gump manager exists.
- ContextMenu input also remains OrbitCommand stop/cancel.
- high-quality Local X/Y actors remain `res://Assets`/export-packaged.
- Online actor packages remain required by current Concept Demo activation.
- no V3 general atlas pipeline exists.
- Local and Remote native presentation composition remain duplicated and drifted.

The V2 client archive was used only as design-history context. Its persistent layer convention and
its distinction between persistent caches and live session/presentation ownership informed the
baseline; no archived code or architecture was restored.

F1.0 has no open closure blocker. F1.1 remains planned and was not started.
