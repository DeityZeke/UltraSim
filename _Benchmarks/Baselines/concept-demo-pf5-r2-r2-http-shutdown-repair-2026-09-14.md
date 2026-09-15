# Concept Demo PF5-R2-R2 Phase A — HTTP Shutdown Repair

**Outcome: Phase A accepted. PF6 may resume on integrated production candidate
`389fb3a0de274fcba0c9d4f64e686b94818f9c71`.**

## Lineage

| Item | Commit |
| --- | --- |
| Starting HEAD | `8472038eb01e39f10a78e200f85d19f5b7e37ad4` |
| PF5-R2 replication semantic candidate | `992fc541460f18534da9036c73cb711277de11ce` |
| HTTP shutdown production repair / integrated candidate | `389fb3a0de274fcba0c9d4f64e686b94818f9c71` |

No replication production file or semantic changed. The integrated candidate is the tree containing
the accepted replication architecture from `992fc541...` and this later hosting correction.

## Reproduction and cause

The unchanged host reproduced both reported shutdown failures. A response submitted or being
initialized could throw `InvalidOperationException` into a response worker while disposal awaited
that worker. Separately, the accept loop could pass its cancellation check just before listener
shutdown, causing `GetContextAsync` to throw `ObjectDisposedException` (or the runtime's exact
not-started `InvalidOperationException`) into the accept task. Because the former disposal path set
its single disposed flag before joining workers, either fault could bypass the remaining cleanup and
make a later `Dispose` return without finishing it.

The first repair ordering was rejected during verification: a response close raced
`HttpListener.Stop()` inside the runtime and exposed a `NullReferenceException` in
`HttpListenerResponse.FormatHeaders`. The accepted repair does not suppress that exception. It
prevents the concurrency: response ownership is quiesced before listener stop/close, and accepted
contexts are serialized against listener retirement.

## Repaired lifecycle

`StaticContentHttpHost` now has an explicit host-local `Running -> Stopping -> Disposed` lifecycle.
One disposer owns shutdown; concurrent disposers wait for that owner and receive the same retained
unexpected failure, if any. Shutdown performs, in order:

1. publish `Stopping`, cancel service, and complete the bounded pending writer;
2. abort active response contexts and join every response worker;
3. reject/retire queued contexts;
4. serialize listener stop/close against post-accept context ownership;
5. join the accept worker and drain any final accepted context;
6. dispose the cancellation source;
7. publish `Disposed` and release concurrent disposers.

Every cleanup stage is attempted even when an owned worker reports an unexpected fault. Expected
shutdown exceptions are contained only at their exact lifecycle boundary:

- accept wait while stopping: `OperationCanceledException`, `HttpListenerException`,
  `ObjectDisposedException`, and the verified listener-not-started `InvalidOperationException`;
- response initialization/close while stopping: the verified submitted/closed-response failures;
- listener stop/close: `HttpListenerException` and `ObjectDisposedException`.

Unexpected worker exceptions are retained, rethrown only after cleanup completes, and repeated by
later `Dispose` calls. There is no blanket `catch (Exception) { }` shutdown policy.

## Deterministic verification

The maintained Server Hosting suite now covers blocked accept, the pre-accept cancellation window,
post-accept ownership, response initialization, body submission, post-write completion, completed
response, client disconnect, queued response retirement, an injected response-worker fault,
repeated disposal, and concurrent disposal. Every successful disposal asserts:

```text
accept worker complete
all response workers complete
pending queue empty
active response count = 0
listener retired
cancellation source retired
second Dispose harmless
```

The focused suite contains 21 tests. Its targeted shutdown stress executes 128 cycles per run;
four consecutive focused executions completed the HTTP tests, totaling 512 clean targeted cycles.
(A fifth whole hosting-suite repetition reached an unrelated pre-existing timing assertion after the
HTTP tests; it is not counted as HTTP failure evidence.)

After the final production change, canonical `./verify.sh --configuration=Release` passed three
consecutive times. Each pass included the Server Hosting suite, Contentless Online acquisition,
all physical client/server compositions, actual Godot exports, F0.7 mixed-session lifecycle, and
F0.8 final composition parity. Each ended with `PASS repository verification` and `git diff
--check` clean.

## Files changed by the repair

- `Server/Hosting/StaticContentHttpHost.cs`
- `_Tests/UltraSim.Server.Hosting.Tests/Program.cs`

The repair commit contains no benchmark/scaling changes. Phase B evidence is maintained separately.

