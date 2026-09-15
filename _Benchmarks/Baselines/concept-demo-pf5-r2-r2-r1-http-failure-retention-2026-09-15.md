# Concept Demo PF5-R2-R2-R1 — HTTP Worker-Failure Retention

**Outcome: accepted. The original shutdown-race repair remains valid and the remaining
error-retention defect is closed.**

## Lineage

| Item | Commit |
| --- | --- |
| Review starting HEAD | `1808aeae17e727b86b4a14a81607995828d4e80b` |
| PF5-R2 replication semantic candidate | `992fc541460f18534da9036c73cb711277de11ce` |
| HTTP failure-retention repair / integrated correctness candidate | `89f26f8bbaa1003b15ddf52d61d6ce2567694b74` |

The worktree was clean at the review start. No Game/Replication or Network production file changed.

## Independent reproduction

Two regressions were written against the unchanged starting host before its join logic was
modified:

1. `BeforeAcceptWait` threw `ObjectDisposedException` while the host was still `Running`. The
   accept task faulted, but `Dispose` returned successfully because join-time exception-type
   filtering misclassified the pre-shutdown failure.
2. Two response workers were released together and threw distinct worker-tagged exceptions.
   Joining `Task.WhenAll` through `GetAwaiter().GetResult()` retained only one observed exception.

Cleanup completed in both probes. These were error-retention defects, not reproductions of the
already-repaired listener/response shutdown races.

## Repair

Expected shutdown outcomes remain contained at the operation where shutdown makes them expected.
`TryJoin` no longer classifies worker failures by exception type. It now retains all flattened
constituent failures from the task being joined, and response workers are joined individually
rather than through one `Task.WhenAll` await.

Consequently:

- an exception raised before `Stopping` remains observable even when its type can also occur during
  ordinary shutdown;
- every independently faulted response worker is retained;
- cleanup still completes before the failure set is published;
- concurrent and later `Dispose` callers observe the same retained exception instance/failure set;
- normal expected shutdown remains non-throwing because those outcomes are handled at their
  originating accept, response, listener, or cleanup boundary.

No broad worker-join exception suppression remains.

## Permanent regressions

- `StaticContentHttpHostShutdownPreservesPreStoppingAcceptFault`
- `StaticContentHttpHostShutdownPreservesEveryResponseWorkerFault`

The second test also proves simultaneous disposers and a later repeated disposer receive the same
complete two-worker aggregate. Both tests assert complete host retirement.

The complete Server Hosting suite has 23 tests. Three consecutive post-repair focused runs passed;
each includes the maintained 128-cycle shutdown stress, for 384 clean stress cycles in this
follow-up (in addition to prior closure stress).

Canonical `./verify.sh --configuration=Release` passed three consecutive times. Every pass
included the Server Hosting suite, physical composition checks, Contentless Online/content
acquisition, actual Godot exports, the PF1 mixed native/bot swarm, F0.7 lifecycle, and F0.8 final
composition parity, and ended with `PASS repository verification`.
