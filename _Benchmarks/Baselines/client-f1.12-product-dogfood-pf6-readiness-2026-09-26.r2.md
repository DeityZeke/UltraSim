# F1.12-R2 — HTTP ownership hardening and unresolved runtime teardown

**Disposition: incomplete correctness closure / NOT READY FOR PF6.** Application-owned accept
and response-closer defects are repaired. A controlled probe still demonstrates framework-owned
header mutation during endpoint teardown, outside application context ownership. Passing retries
do not close F112-R1-06. No NRE is suppressed; no replacement HTTP server is adopted.

This extends the immutable [R1 report](client-f1.12-product-dogfood-pf6-readiness-2026-09-26.r1.md).
The obsolete unbounded Cabin capture harness's unknown original trigger is **not** another PF6
blocker. Current bounded R1 capture, pointer/tier and memory assurance are carried forward.

## Lineage and changes

Starting clean main: `9877eed960f7857d151bfe566a346ab912c67765`. No intervening commit or unrelated
worktree edit was present. Ownership repair/test commit:
`b2902449dab8edbb80f6840f85f2d0cfa078c2d0`. Grass discoverability/native regression commit:
`ca48ca05b0a988f861610c59d9b56f511f437140` (tested implementation).
Final evidence commit is the commit adding this report; resolve with
`git log -1 --format=%H -- _Benchmarks/Baselines/client-f1.12-product-dogfood-pf6-readiness-2026-09-26.r2.md`.
It is not a self-referential invented SHA. The task handoff names it exactly.

Production files changed: `Server/Hosting/StaticContentHttpHost.cs`,
`Client/Godot/Shell/ClientRoot.cs`. Tests/diagnostics:
`_Tests/UltraSim.Server.Hosting.Tests/Program.cs`, `Client/Godot/Shell/ClientRoot.F112.cs`.
Current Client/Completion/Pre-freeze/Godot documentation is qualified, not marked closed.

Release; .NET SDK 8.0.131, runtime 8.0.31, Linux/CachyOS; same workstation as R1 (7800X3D,
RTX 4070 Ti SUPER, Godot 4.7.1 Mono). Desktop/editor activity was not isolated. Focused test
processes were independent. No benchmark or new memory campaign is claimed.

## F112-R2-01 — runtime teardown remains a blocker

P1 / high confidence: the managed runtime permits unsafe concurrent header access during teardown.
Confidence in the **exact historical NRE instruction interleaving** remains limited: that individual
collection failure has not been made deterministic. These are different claims.

The original stack/runtime/canonical provenance remain in
[the unchanged failed R1 log](client-f1.12-product-dogfood-pf6-readiness-2026-09-26.r1/controls/f112-r1-release.log).
Immediate failing chain:

```text
NameValueCollection.GetKey
HttpListenerResponse.FormatHeaders / SendHeaders
HttpResponseStream.GetHeaders / DisposeCore
HttpConnection.Close
HttpEndPointListener.Close
HttpEndPointManager.RemoveEndPoint / RemoveListener
HttpListener.Close / Stop
StaticContentHttpHost.TryCleanup / Dispose
StaticContentHttpHostShutdownStressIsBounded
```

The aggregate in that log is from concurrent Dispose callers; it is not evidence of two distinct
host cleanup failures. The exact cycle was not logged.

Before production edits, increasing the original stress to 1,024 cycles additionally terminated
the process with an unhandled framework callback NRE in
`HttpEndPointListener.UnbindContext → HttpConnection.BeginReadRequest → ProcessAccept → OnAccept`.
The [preserved terminal excerpt](client-f1.12-product-dogfood-pf6-readiness-2026-09-26.r2/controls/pre-repair-process-failure.txt)
is explicitly an excerpt, not a fabricated canonical log or second FormatHeaders reproduction.

### Deterministic runtime control

`ManagedHttpListenerTeardownCanOverlapUnregisteredHeaderMutation` uses a real listener, socket,
partial request and its real framework response. Test-only reflection installs an observing
WebHeaderCollection subclass. GetKey gates the **actual Stop/Abort header formatter**; then the
client completes the real request after the listener prefix was removed. The runtime's own
failed BindContext path executes SendError, setting Content-Type from a different thread while
the formatter is still inside GetHeaders. No application accept ever receives this context.
The probe injects no exception, fake request callback or synthetic header mutation.

Four controls pass their overlap assertion: Stop and Abort, each with Connection: keep-alive
and Connection: close. [Raw control log](client-f1.12-product-dogfood-pf6-readiness-2026-09-26.r2/controls/framework-teardown-control.log).
**The probe proves the unsafe overlap, not a deterministic literal NRE.** It is an explicit,
pinned managed-runtime investigation, excluded from the ordinary portable product test list.

The installed runtime's [endpoint Close](https://github.com/dotnet/runtime/blob/v8.0.31/src/libraries/System.Net.HttpListener/src/System/Net/Managed/HttpEndPointListener.cs)
closes unregistered connections after removing the prefix. The concurrent
[read callback](https://github.com/dotnet/runtime/blob/v8.0.31/src/libraries/System.Net.HttpListener/src/System/Net/Managed/HttpConnection.cs)
can fail BindContext and prepare an error response. That preparation modifies headers outside
the response formatter's lock. [GetHeaders](https://github.com/dotnet/runtime/blob/v8.0.31/src/libraries/System.Net.HttpListener/src/System/Net/Managed/HttpResponseStream.Managed.cs)
locks other formatter calls, not ContentType's setter. Force-close still obtains/closes a response
stream and may format headers. Both listener Stop and Abort traverse endpoint removal before
listener cleanup. Merely choosing Abort instead of Stop does not remove this demonstrated race.

This is a concrete mechanism compatible with the preserved FormatHeaders stack; it does not
establish which historical header setter won that particular race. Public host ownership gates
cannot serialize a context that the framework has not disclosed. The additional callback failure
also cannot be caught or aggregated by Dispose. A supported runtime fix/workaround or a separately
approved host replacement needs investigation. Production reflection/private runtime locking,
blanket suppression, disabling keep-alive, and declaring stress passes sufficient are not adopted.

## Focused application repairs

1. **Actual accept ownership.** Cancelling GetContextAsync().WaitAsync(token) completed its wrapper
   while the underlying accept remained live and could subsequently produce a context. The public
   API control demonstrates exactly that. RunAcceptAsync now directly awaits and observes each
   actual accept. Shutdown finishes listener teardown and joins this owner; a context completed
   during stopping waits for teardown before application rejection. Started/completed accept
   counts reconcile at the retired snapshot. Restoring WaitAsync fails the focused owner regression.
2. **Single application closer.** Shutdown used to Abort responses during initialization, and both
   shutdown and the worker could close the same response. Bounded per-worker states now distinguish
   initialization, immutable headers/write and claimed closer. Initializers observe cancellation;
   shutdown can claim only write-ready responses. Compare/exchange gives Close/Abort one owner.
   Restoring initialization-time abort fails an independent actual-operation counter assertion.
3. **No global lock across I/O.** Ownership gates cover publication/claims only. Response abort,
   rejection, listener teardown and joining occur outside them. An accept publication fence retires
   normal-service queue/rejection work before teardown. Pending rejection failures are captured
   individually, so one unexpected failure does not abandon the remaining queue.

Previous ordering: stopping → cancellation → queue completion → Abort every active context →
join responses → drain → Stop/Close → join wrapper accept → drain → CTS disposal.

New ordering: stopping/stop admission → cancellation/queue completion → claim write-ready aborts →
join response owners → drain → join any accept publication/rejection section → Stop/Close → release
teardown fence → join **actual** accept owner → final drain → fence/CTS disposal → Disposed.
This improves application ownership; **it is not claimed to fence every framework callback**.

Expected lifecycle OCE/HttpListenerException/ODE remain localized. InvalidOperationException
handling remains limited to listener-stop/response-termination boundaries. Unexpected errors,
including NRE, remain failures. Joining inspects every task's aggregate failures; repeated and
concurrent Dispose preserve the same retained exception set. No first-error-only policy is added.

## Verification and limits

Three independent Release hosting processes: **27/27 each**, with 1,024 stress cycles each
(3,072 cycles), plus each process's 16 same-port reuse controls. Gate coverage includes no client,
full request before accept begins, accepted/not queued, initialization, headers-before-write,
write-completed-before-close, disconnection, stalled write, queued response, completed keep-alive,
single/concurrent disposal, 1-worker/queue-1 and 3-worker/queue-4. Existing timeout, overflow,
two-worker independent download, fault aggregation and pre-stopping accept-fault tests remain.
The new actual-accept and initializing-response regressions each fail their targeted mutant.
Detailed per-framework socket callback lifecycle observability is **not** claimed.

One uninterrupted canonical Release also passes on `ca48ca05`, including a fourth 27/27 hosting
invocation (total **4,096 stress cycles** and **64 same-port reuse cycles** across four processes).
The canonical log records actual cold/warm Contentless Online acquisition, changed profile,
Complete-hosted two-Remote content/retirement, mixed Local/Realm A/Realm B/reconnect lifetimes,
PF1 mixed native/bot impairment and PF2 exact physical accounting. Final Complete Debug/editor
assembly was rebuilt. Architecture-only passes separately. No second full canonical run is
claimed: more green retries would not close the outstanding framework mechanism.

Native Complete export Cabin regression passes: **12 PNGs** through the unchanged maintained
preview (eight installed views/environments and four same-session primitive views), X11/OpenGL,
1920×1080. Application/control epochs remain 2/1 and semantic truth remains equal across the
presentation-only transition; exact persistent-root retirement passes. The
[Cabin evidence](client-f1.12-product-dogfood-pf6-readiness-2026-09-26.r2/cabin/evidence.json)
retains draw observations, image/pixel hashes, before/after and final boundary. This was a correctness
regression, not a matched visual-quality or performance rating; it overlapped the nonrated canonical
exported-client campaign and retained the loaded device settings. Full R1 images/memory are unchanged.

Successful retired snapshots require State=Disposed, joined accept/response tasks, queue=0,
active responses=0, closed listener, retired CTS, and started accepts=completed accepts. These
application diagnostics do not establish retirement of the runtime's private socket callbacks.

Commands and logs are under
[R2 controls](client-f1.12-product-dogfood-pf6-readiness-2026-09-26.r2/controls).
Architecture, canonical Release and native results are recorded in the
[machine companion](client-f1.12-product-dogfood-pf6-readiness-2026-09-26.r2.json).
A green canonical run cannot dispose the controlled runtime issue.

Reproduce:

```bash
dotnet run --project _Tests/UltraSim.Server.Hosting.Tests/UltraSim.Server.Hosting.Tests.csproj -c Release
dotnet run --project _Tests/UltraSim.Server.Hosting.Tests/UltraSim.Server.Hosting.Tests.csproj -c Release -- --test=ManagedHttpListenerTeardownCanOverlapUnregisteredHeaderMutation
bash ./verify.sh --architecture-only
bash ./verify.sh --configuration=Release
```

## Grass preference and carried-forward assurance

The existing checkbox was below the entire Advanced dimensions panel. It is now immediately
beneath Show Advanced, explicitly labeled **Experimental Vegetation / grass (next world entry)**.
The maintained actual Settings acceptance checks tree visibility, sibling placement, label and
Simple-mode hiding. This is a discoverability repair, not proof that every user report had the
same cause (an old loaded editor assembly could also conceal newer controls). Rebuild the Complete
editor assembly before testing. Device ownership, settings v6, persistence, default Off and next
world entry semantics are unchanged.

R1 pointer/tier/nameplate, two 8-lifetime memory controls and current bounded capture remain canonical
assurance; they are not overwritten or rerated. No rendering/actor/vegetation system, content
authorization, route, bytes, hash, CAS, Game/transport, AOI, capacity or world topology changes.
Settings 6; Game manifest 33; transport 2; WorldPack 1; structural manifest 1; worldview 2;
participant cap 64. W8/W9/W10 and PF6 remain unstarted.

## Release decision

F112-R1-06 remains open, with F112-R2-01 recording the application repairs and remaining runtime
mechanism. One current Category-1 shutdown blocker remains; no independent new blocking Category-2
client behavior was identified. **F1.12 ACTIVE / NOT READY FOR PF6.** This checkpoint does not claim
that a minimal public-API lifecycle repair has made the managed listener safe. The next decision is
how to repair the framework-owned teardown contract without suppression or private runtime hooks.

## Requested closure ledger (request order)

1. Starting HEAD: `9877eed960f7857d151bfe566a346ab912c67765`.
2. Tested implementation: `ca48ca05b0a988f861610c59d9b56f511f437140`.
3. Final evidence HEAD: resolve this report's adding commit; exact SHA in handoff.
4. Commits: `b2902449` ownership/tests; `ca48ca05` grass; this evidence/documentation commit.
5. Files: two production files, two test/diagnostic files, four current docs, linked R2 artifacts.
6. Original HTTP failure: preserved FormatHeaders/GetKey NRE during canonical R1 stress.
7. Deterministic reproduction: abandoned wrapper and initializer-abort defects reproduced; four
   framework header-overlap controls pass. Literal historical NRE is not deterministic.
8. Exact historical root cause: not fully isolated; compatible framework race identified, not closed.
9. Framework behavior: prefix/endpoint teardown can format an unregistered response while its
   read callback sets Content-Type; Stop and Abort both permit it.
10. Application flaws: cancellable wrapper abandoned actual accept; multiple response closers.
11. Prior ordering: cancelled wrapper, unconditionally aborted active responses, Stop/Close, join.
12. Repaired ordering: stop admission, cancellation, single ready-response closer, retire publication,
    teardown, release stopping-context fence, observe/join actual accept, finish disposal.
13. Accept ownership: actual operation observed; retired started/completed counts equal.
14. Response ownership: bounded worker-slot states and atomic closer claims.
15. Abort/Close: one application closer; initialization never externally aborted; framework race open.
16. Concurrent Dispose: gates, stress and same retained worker-failure set pass.
17. Keep-alive: original normal and stress controls retained; runtime overlap still demonstrable.
18. Stalled response: timeout, overflow/active-write termination and independent download pass.
19. Worker faults: pre-Stopping accept and unexpected handler exceptions preserved.
20. Aggregation: both independent response-worker faults survive repeated/concurrent Dispose.
21. Stress/processes: 4×1,024 stress and 4×16 same-port controls; one separate four-case runtime probe.
22. Retired snapshot: exact application gate passes; private runtime callback retirement unproven.
23. Real content: canonical authenticated remote cold/warm and profile-change controls pass.
24. Complete-hosted: real exported local authority/two exported Remotes, detach and world survival pass.
25. Cabin: installed + same-session primitive, 12 bounded captures, exact retirement pass.
26. Pointer/tier: R1 retained; existing canonical native/product controls green.
27. Memory: R1 two 8-lifetime campaigns carried forward; no new plateau claim.
28. Vegetation: device-owned, v6/default-Off/next-entry; discoverable Advanced grass label regression passes.
29. Formats: settings 6, Game 33, transport 2, WorldPack 1, structural manifest 1, worldview 2; cap 64.
30. Architecture: PASS (also rerun after evidence/docs).
31. Canonical Release: one uninterrupted PASS on tested implementation; log retained, no waiver.
32. Category-1: one HTTP shutdown blocker remains (F112-R1-06 / linked F112-R2-01).
33. Blocking Category-2: none independently identified; mechanical feel remains human validation.
34. W8/W9/W10: NOT STARTED.
35. F1.12: ACTIVE; R2 correctness closure incomplete.
36. PF6: **NOT READY FOR PF6**, NOT STARTED.
37. Worktree: clean at evidence handoff; no unrelated edits overwritten or included.
