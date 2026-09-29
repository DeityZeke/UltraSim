# P5 — User/Admin/Developer control surfaces (2026-09-29)

Status: **CLOSED for the representative P5 contract.** P6, P7, Gate 1 and PF6 have not started.

## Provenance and verification

- Starting clean `main`: `15e185c5f46965d869628c06818659e91963d945`.
- Implementation: `c5ab587d` (generic client viewer, Settings provider, neutral visual roles, typed image seam, native regressions).
- Export-gate promotion: `6b9264aa`; exact closure ordering: `bc0f9131b4ba512ad6b67693ed4d0d94ec645249`.
- Typed image binding view-instance fence: `016f32a7e1b614e8f01c9acccfcc45b4e54cb24e`; its focused native regression passed. Uninterrupted canonical Release passes on both `bc0f9131` and the final tested implementation `016f32a7`. Platform: CachyOS Linux `7.2.6-1-cachyos`, AMD Ryzen 7 7800X3D (8 cores/16 threads), Godot Mono 4.7.1, .NET SDK 8.0.131 and .NET/ASP.NET Core runtimes 8.0.31. The user-local matching SDK/runtime was installed in `/tmp/ultrasim-p5-dotnet-8.0.131` because system `dotnet` lacked `Microsoft.AspNetCore.App`; no repository runtime dependency changed.
- Focused Release: Control Plane 21/21, UI kernel 29/29, Client Core 36/36, Local client-session 24/24. Godot F1.10/native ownership and the new management smoke passed. `./verify.sh --architecture-only` and `PATH=/tmp/ultrasim-p5-dotnet-8.0.131:$PATH DOTNET_ROOT=/tmp/ultrasim-p5-dotnet-8.0.131 ./verify.sh --configuration=Release` passed. Canonical included exact SinglePlayer/Online/Complete exports, contentless Online, Complete hosting, native retirement, Direct Connect and bot controls.
- Post-verifier fresh SinglePlayer export plus five consecutive installed Official-content warm launches: 5/5 clean, exact package/plan fingerprints unchanged, actor realization 2 (1 animated, 1 primitive), blobs 5, live pins 9/2 each launch.
- Earlier full-run stops are retained as findings, not passes: system ASP.NET runtime missing (environment); old export allowlist still forbade the promoted `Client.ControlPlane` assembly; then an expected-assembly list was not in ordinal order. The final view-instance-fenced implementation passed without a verifier change after the gate-only commits.

## Control census and ownership

| Class | P5 surface and owner | Boundary |
| --- | --- | --- |
| User | F10 generic viewer over `ClientSettingsControlProvider`: UI scale, volume preference, pointer sensitivity, invert Y, control grammar, camera profile/distance/FOV, render distance, vegetation and display mode; bounded bindings table and rebind action. | Existing Client settings document, resolver, file store and display consumer still own validation, persistence and application. Server recommendations never acquire write authority. |
| Developer | Discoverable read-only F10 diagnostics toggle over the client Settings and Godot adapter publications. Adapter reports edition, lifecycle/session, UI layers/open gumps, world-root children and internal output dimensions. | No server-claimed Developer capability and no per-tick polling. |
| Admin/Operator | Generic viewer exercised with a trusted `LocalOperator` principal over adapter diagnostics; immutable implementation selections render disabled and cannot be written. | This is a viewer/authorization proof, **not** the P7 remote operator console or a grant of server command authority. |
| Test | Existing CLI/native acceptance hooks and TestAutomation principals remain test-only. | No benchmark or fault-injection toggle was promoted to User settings. |
| Cold configuration | Physical adapter implementation selection remains fixed/read-only and composition-owned. | A settings edit cannot swap Godot, input, runtime or Audio implementations. |

F4 Server Monitor remains the existing authority-authored, sanitized read-only Game UI for Local and Online. No authenticated Control Plane operator transport exists yet, so duplicating or relabeling F4 as an Admin write surface would misstate ownership. P7 owns remote operator routing and any warranted F4 management projection. The advanced F10 display form remains alongside the new generic User section to preserve exact existing behavior and the accepted native settings controls; this is an intentional coexistence, not a second settings store. P6 owns D2/List Inventory preference and portrait/cache policy; P5 publishes only an explicit read-only placeholder.

## Generic viewer and neutral presentation

The cold viewer renders provider-published Bool, Option, bounded Int64/Float64, Text, Action arguments, diagnostics and collections without knowing the provider type. It groups category paths in tabs, shows requested/effective/pending/provenance/apply mode and disabled reason, and pages collections at 32 rows (provider storage remains bounded). Read-only principals cannot stage drafts or see enabled Apply; actions lacking invoke capability are disabled. Confirmation is a second gesture pinned to the provider generation **and revision**, so changed owner state rejects the stale decision. P1 ECS's existing `ECS/Systems` table remains the representative generic proof; no ECS reflection or scheduler UI ownership moved to the viewer.

Neutral authored visual roles now support natural/fit/fill/stretch/tile image modes, opacity, nine independent frame pieces with separate edge/center Stretch/Tile and logical margins, deterministic button-state fallback and image-backed toggles. The native fixture resolves a silver-frame role, tile, button and toggle through borrowed texture wrappers, then proves explicit resolver retirement with a retained `NativeInstance == 0` reference. A bounded `UiSemanticImageBindings` attaches the existing P4 `SemanticPresentationReference` to exact compiled image element IDs and is fenced by both the view instance and compiled definition handle; the Godot resolver receives that typed identity and owns/retains its texture. This is a presentation seam, **not** new content authorization, a new item ID, a protocol change, a full art library or P6's dynamic Inventory portrait/cache.

## Intentional P5 limits and next work

- The representative visual catalog is a native fixture, not shipped silver/gold/stone art. P9/P10 own production art and polish.
- Existing Game UI text/visual protocol and F4 authority semantics are unchanged; no new Admin network capability is implied.
- Volume remains a persisted preference with no Audio consumer until P8. Vegetation remains device-owned/default-Off/next-world-entry.
- Settings' detailed Advanced window/frame/output controls remain in their validated legacy F10 form; generic F10 covers representative existing settings but does not replace that form or alter the resolver.
- The typed image binding is static for one compiled view; dynamic P6 item portraits, derived 3D caches and atlas policy have not started.
- The prior native shutdown repair/two-frame deletion-queue boundary is unchanged; the new P5 texture wrapper is explicitly owned and retired.

P6 Inventory D2/List viewers is next. This record does not assert P7 operator product, P8 Audio, P9 art, Gate 1 or PF6 readiness.
