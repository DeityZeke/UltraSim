# F1.12-R3 — Framework HTTP Endpoint / Kestrel retirement closure

**Result: F1.12 CLOSED; Gate 0 CLOSED. P1 Framework Control Plane NEXT.**
P2–P11 and Gate 1 are PLANNED. **PF6 PLANNED / NOT READY / NOT STARTED; many-player testing NOT READY.**
This is the narrow correctness-driven extraction, not commencement of P1 or a general module,
Discovery, Web, account, TLS or W8/W9/W10 campaign.

## Provenance

- Clean starting `main`: `efdd43e13be2ecd8323d36d78e8aa20b220f0cea`; exactly the requested reference.
- Endpoint/bridge/provider/composition implementation: `cc3d2c3f19b1a03cdfc966f7b36d04c563fb32df`.
- Real HTTP fixture migration and W7 setup barrier: `4fa5197af91ea2943ebf39ba01d39c95613239ae`.
- Startup/cleanup failure retention: `65efff932649780f2e2209c1df05cf1c516d7fce`.
- Exact sword-reintroduction test checkpoint: `a0bf158111c04d3ac196bfcec4031f409141ef33`.
- Atomic active-owner accounting / **tested integrated implementation**:
  `cc937185ce630fdb707e51bdee6372f550ff77b0`.
- Evidence commit resolves without a self-referential SHA:
  `git log -1 --format=%H -- _Benchmarks/Baselines/client-f1.12-product-dogfood-pf6-readiness-2026-09-26.r3.md`.
  Later roadmap-only closure is separate; the full canonical pass is bound to the implementation above.

Release, SDK 8.0.131, .NET/ASP.NET Core 8.0.31; CachyOS Linux 7.2.6-1-cachyos,
Ryzen 7800X3D, 32 GiB, RTX 4070 Ti SUPER / NVIDIA 615.71.09, Godot 4.7.1 Mono
`a13da4feb`. Native capture used X11/OpenGL Compatibility. No affinity control, separate
performance rating or many-player capacity claim. An existing editor, IDE activity and two old
unowned server processes were left alone.

System dotnet lacked ASP.NET Core. The official 8.0.31 Linux x64 runtime was installed only into
task-owned `.output/f112-r3-runtime`; its SDK/packs point to the existing installation. No system
runtime was overwritten. Dedicated hosting now requires ASP.NET Core 8. Complete ships the
official pinned Linux x64 runtime assemblies privately because Godot's running CoreCLR cannot
attach an additional shared framework. Other export platforms need their matching runtime pack.

## F112-R3-01 — affected backend retired, not its historical bug disproved

The [original R1 failure](client-f1.12-product-dogfood-pf6-readiness-2026-09-26.r1/controls/f112-r1-release.log)
is unchanged, SHA-256 `e10313032f0ea4ac56f3ddb4a266829cd09a750ef10a8b6b20c90184486d07e1`:

```text
NullReferenceException
NameValueCollection.GetKey → HttpListenerResponse.FormatHeaders / SendHeaders
→ HttpResponseStream.GetHeaders / DisposeCore → HttpConnection.Close
→ HttpEndPointListener.Close → HttpEndPointManager.RemoveEndPoint
→ HttpListener.Close / Stop → StaticContentHttpHost.Dispose
```

The [R2 investigation](client-f1.12-product-dogfood-pf6-readiness-2026-09-26.r2.md) remains valid:
an abandoned cancellation wrapper and competing application closers were repaired, but those
repairs could not fence framework-owned undisclosed connections/header mutation. Its controlled
overlap was not a deterministic reproduction of the literal historical formatter NRE.

**Production no longer uses managed HttpListener for HTTP endpoint hosting.** Architecture
verification scans active Server/Framework/Bridges/Client/Game/Kernels sources and rejects its
return. No NRE suppression, production reflection or private-runtime lock was introduced.
The old listener scheduler/hooks were removed; R1/R2 immutable evidence and git history remain.

## Actual ownership and composition

| Owner | Implemented responsibility |
| --- | --- |
| `Framework/Http/Endpoint` | BCL-only request/response/exchange contracts, frozen method/path ownership, finite instance composition, independent service queues/workers/deadlines, conservation snapshots, retirement |
| `Bridges/Http.Kestrel` | Only Framework + `Microsoft.AspNetCore.App`; explicit component Start/Stop/Dispose, HTTP/1.1 IPv4 binding, neutral native exchange, actual write/completion and idempotent Abort |
| `Server/Hosting/StaticContentHttpService` | Copied immutable bytes, exact authorized routes and content headers; no listener or scheduler |
| `Server/ConceptDemo` | Explicit provider + endpoint + bridge composition for dedicated and Complete-hosted deployment |

`StaticContentHttpHost` remains only a compatibility composition facade. F03b real loopback HTTP
acquisition fixtures now explicitly compose the same endpoint/service/bridge. There is no fake
source or direct-file replacement for those HTTP controls. Duplicate service IDs or method/path
owners fail before workers/socket startup. Native RawTarget avoids authorizing normalized aliases.

The service active slot includes handler execution **and the real Body.WriteAsync/CompleteAsync**.
Cancellation/timeout aborts the exchange and awaits its actual operation; no abandoned waiter
stands in for completion. Native-write IOException is handled only at that write boundary;
unexpected handler/dispatch/cleanup failures remain retained. A service faults closed after an
unexpected handler failure, with one finite retained failure slot per worker. Bridge dispatch
failures use a finite connection-sized store with explicit overflow failure.

Retirement: close service admission → cancel application work → supported backend Stop → join
real service workers → verify stopped backend/zero native requests → dispose backend/CTS → publish
Disposed. Concurrent/repeated callers receive the same complete retained failure. Locks protect
transitions, never handler I/O or joins. Unretired ownership produces Faulted/failure, **not** a
successful disposal beneath live work. Handlers must honor cancellation; self-disposal is unsupported.

The pinned [Kestrel Stop implementation](https://github.com/dotnet/aspnetcore/blob/v8.0.31/src/Servers/Kestrel/Core/src/Internal/KestrelServerImpl.cs)
delegates to supported transport shutdown. Its
[transport manager](https://github.com/dotnet/aspnetcore/blob/v8.0.31/src/Servers/Kestrel/Core/src/Internal/Infrastructure/TransportManager.cs)
unbinds and awaits accept ownership before connection closure/abort and transport disposal.
Assurance combines that supported lifecycle, zero observable ownership, late old-socket EOF/frozen
snapshot controls and immediate exact-port reuse. **Private callbacks are not individually
instrumented, and stress is not a mathematical proof of all framework internals.**

## Static Content parity and independent budgets

Defaults remain **4,096 resources / 768 MiB / 4 active / 8 queued / 30 s**.
GET exact canonical lowercase configured path/no query returns 200, exact copied bytes,
`application/vnd.ultrasim.content-package`, exact Content-Length and
`public, immutable, max-age=31536000`. Wrong method/query/unknown route return 404; queue overflow
returns 503 without admitted work. Original caller-buffer mutation cannot change served bytes.

Real socket saturation holds Content at **2 active + 2 queued**, confirms overflow **503**, then
gets **200** from the test-only status/proof-like service on that same endpoint/port while Content
remains saturated. Its independent budget is 1 active/1 queued. This is not Discovery or a registry.
Backend limits are separately finite: 1,024 connections, zero body, 8 KiB/32 headers, 2 KiB request
line, 5 s headers/30 s keep-alive; native graceful-stop allowance 100 ms, Framework failure deadlines
5 s for backend and worker phases. Failure does not authorize abandonment.

## Failures found during verification — retained, not waived

1. W7 preflight failed twice at the existing stale-proof bound (11 versus ≤4). Owner admission did
   not imply the initial chunk baseline was proved before the fixture's manual relocation. The
   fixture now awaits applied proof, zero outstanding world deliveries and no snapshot transfer
   **before** testing traversal. The bound and conservation assertions are unchanged. The original
   R2 control passed separately; that pass did not explain away current failures. Its first parallel
   build hit generated-file sharing, then serial build/full control passed; both logs are preserved.
2. **First canonical Release failed** in M00 line 320, not HTTP hosting. A focused repeat reproduced
   it on attempt 8: same sword identity `8:1`, source still present, client had processed one Remove.
   The reintroduction predicate could accept the old positive metadata; waiting for the pending
   removal proof then made its later presence assertion fail. The checkpoint now requires a newer
   revision for **each recipient**, not just presence. Five independent repaired M00 controls and
   final canonical pass succeed. Game/Client presentation production code is unchanged.
3. Startup cleanup could hide the original startup failure. A controlled backend supplies distinct
   startup/disposal errors; the repaired constructor retains both and still attempts later cleanup.
   Restoring the old catch makes its new regression fail. Real occupied-port startup failure leaves
   the original endpoint serving exact bytes, then allows immediate fresh binding.
4. Active-owner increment used a lock while decrement was atomic. Both now use atomic updates;
   1,024 concurrent successful native responses reconcile to zero active/queued owners and exactly
   1,024 admissions/completions. This is a diagnostic conservation repair, not a policy optimization.

See the [preserved controls](client-f1.12-product-dogfood-pf6-readiness-2026-09-26.r3/controls/).
Canonical run 1 failed (exit 134); run 2 was deliberately stopped for the counter repair (exit 143),
not counted as a pass; **run 3 passed uninterrupted on `cc937185`**. No passing retry is substituted
for the investigated failure or used to weaken a gate.

## Final verification

| Gate on tested implementation | Result |
| --- | --- |
| Three independent Release Hosting processes | 18 top-level tests + 11 R3 subcontrols each; PASS |
| Shutdown stress | 3 × 1,024 before canonical, plus 1,024 in canonical; zero unexpected failures |
| Same-port controls | 3 × 16 before canonical, plus 16 in canonical; no sleep/port change |
| Lifecycle states | no request, full/partial request, disconnect, keep-alive/close, stalled write, timeout, gated handler, queued work, single/concurrent/repeated disposal; PASS |
| Topologies | 1/1, 2/2, 3/4, 8/128 workers/queue; actual production default 4/8 exercised |
| Unexpected failures | independent pre-stop/during-stop service errors; independent real bridge dispatch errors; backend failure; startup+cleanup errors; retained/aggregated/repeated identically |
| Retired gate | Disposed, stopped backend, zero native requests, retired workers, zero active/queued service work, disposed CTS; admitted = completed + aborted + faulted |
| Architecture-only | four gates PASS |
| Canonical Release | PASS, exit 0; [raw log](client-f1.12-product-dogfood-pf6-readiness-2026-09-26.r3/controls/release-3.log) SHA-256 `b790bb8cfaf9c8e8397bd0348158879471649fc896272e4829fef55115c09309` |
| Actual contentless Online | cold/warm/profile change PASS; same world/content identity; warm refetch zero |
| Actual exported Complete hosting | 2 exported remote clients; packet-free local path; 3 humans/client; clean detach PASS |
| Export closure | SinglePlayer 22 UltraSim assemblies/no Network; Online 13/ClientOnly/no authority; Complete 31, including Framework/Bridge and private Kestrel; PASS |
| Mixed native lifetime / bots | 8-lifetime Local/Remote journey, exact profile reuse, no residue; PF1/PF2 impairment/accounting PASS |
| Short native Cabin | 12 bounded actual Gameplay Output captures, including installed→primitive same-session; unchanged application/control epochs, clean retirement; PASS |

The [native evidence](client-f1.12-product-dogfood-pf6-readiness-2026-09-26.r3/native/cabin/evidence.json)
records 2 actors (1 animated/1 primitive) becoming 2 primitive actors, without replacing semantic
identity. All 12 PNGs are retained. It used existing device preferences (render radius 3 and visible
vegetation), not a new performance workload; no human art/feel score is assigned. R1's full visual,
pointer/tier and two memory-plateau campaigns are carried forward unchanged, not repeated. Its
obsolete unbounded capture trigger remains unknown; the current bounded path passed again.

Settings v6, Game manifest v33, transport r2, WorldPack/v1, WorldStructuralManifest/v1, worldview/v2
and participant cap 64 remain unchanged. Experimental grass stays device-owned/default Off/next
entry, discoverable beside Show Advanced. No current Category-1/blocking Category-2 finding from
this checkpoint remains. Motion/camera/UI/Chat/art/readability feel remains human evaluation work.

## Reproduction and workspace

With an installed ASP.NET Core 8 runtime, ordinary commands are sufficient. This environment used:

```bash
.output/f112-r3-runtime/dotnet run --project _Tests/UltraSim.Server.Hosting.Tests/UltraSim.Server.Hosting.Tests.csproj -c Release
.output/f112-r3-runtime/dotnet run --project _Tests/UltraSim.Network.Application.Tests/UltraSim.Network.Application.Tests.csproj -c Release -- --m0-0-only
PATH="$PWD/.output/f112-r3-runtime:$PATH" DOTNET_ROOT="$PWD/.output/f112-r3-runtime" bash ./verify.sh --architecture-only
PATH="$PWD/.output/f112-r3-runtime:$PATH" DOTNET_ROOT="$PWD/.output/f112-r3-runtime" bash ./verify.sh --configuration=Release
```

Native control used a SHA-identical copy of the canonically verified Complete export
(`b42d017c95c87b61ae00cb1b3328597f13e4b072391c3275374986eb30f8092a` executable),
`--f1-12-cabin-preview --expect-edition=Complete --f1-12-head=cc937185ce630fdb707e51bdee6372f550ff77b0`
plus isolated save/cache/evidence arguments and an external 90 s process bound. Its relative
arguments resolve beneath the export directory; the archived outputs are copied without rewriting.

All checkpoint changes are committed separately from two unrelated user-created untracked files:
`Docs/Plans/UltraSim_Pre_Many_Person_Live_Testing_Roadmap.md` and
`Docs/Plans/UltraSim_Web_Discovery_Registry_Portal_Ecosystem.md`. They were read for context but
not edited, staged or removed. **Tracked checkpoint work is clean; the overall worktree is not
literally clean because those unrelated files are preserved.** This is not a hidden gate failure.
