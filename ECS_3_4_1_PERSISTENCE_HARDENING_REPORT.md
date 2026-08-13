# ECS 3.4.1 Persistence Adversarial Hardening Report

Date: 2026-08-12  
Profile: `ecs-3.4.1-persistence-correctness-hardening`  
Decision: accepted as ECS 3.4.1

## Executive result

The adversarial findings were reproducible and justified a correctness revision.
ECS 3.4.1 retains the 3.4 persistence architecture and artifact format while
closing publication, memory-bound, recovery, multi-writer, catalog, generator,
and canonical-decoding proof holes.

All 3.4.0 benchmark snapshot digests and byte counts remain identical. The full
V2/V3 adapter passes in both process orders with 103 comparable rows, zero state
or contract mismatches, nine explicitly unsupported rows, and zero timed V3
managed allocation. The permanent 141-row V3 benchmark also reports zero managed
allocation in every timed row.

## Verified findings and fixes

| Audit finding | Verification | 3.4.1 resolution |
| --- | --- | --- |
| publication callback can be retried after throwing | reproduced | publication is marked attempted before host code; retry and coordinator reclamation are forbidden once ownership transfer begins |
| temporary limit omits real preflight ownership | reproduced with a 92-byte limit | a conservative artifact/table/section/decode estimate is checked before the immutable clone; the public estimate now reports that bound |
| malformed/unsafe `CURRENT` defeats compatible recovery | reproduced | compatible enumeration treats malformed pointer data as corruption and continues; strict mode rejects it |
| zero-width value codec captures but cannot restore | reproduced | current and historical value codecs require positive encoded width at registration |
| component key incorrectly inherits 256-byte prefab limit | reproduced at 257 characters | component keys use the container's `ushort` UTF-8 bound; prefab keys retain their narrower authoring contract |
| prepared participant key is unchecked | reproduced | returned prepared state must match its registered participant key or is disposed/rejected |
| 8 GiB configured artifact is impossible for `byte[]` | confirmed | limits and participant contracts explicitly cap array-backed artifacts at `Array.MaxLength`; server defaults are honest |
| compatible recovery catches arbitrary exceptions | confirmed | fallback is limited to recognized data/compatibility failures; implementation, OOM, authorization, and general I/O failures surface |
| concurrent coordinators can delete/overwrite each other | reproduced by inspection and concurrent fixture | one cross-process root lease covers cleanup, forward-sequence proof, capture, and publication; `CURRENT` cannot move backward |
| static persistence members generate invalid instance access | reproduced | static fields/properties are excluded; generated source compiles and strict boolean decoding is also verified |

## Additional hardening

- Durable IDs are assigned during successful entity creation in persistence-enabled
  worlds, not as a side effect of a later fallible capture. Exhaustion rollback is
  cold and out-of-line. Focused and two-order controls verify no retained creation
  regression in ordinary non-persistent worlds.
- Save failures clean their private pending generation. A process-killed writer
  deliberately leaves pending state, which the next exclusive writer removes.
- Manifest directory sequence, ECS snapshot sequence, participant capture key,
  and prepared participant key are cross-validated.
- Alias and drop declarations cannot contradict current IDs, each other, or form
  alias chains through historical identities.
- The snapshot schema hint is now surfaced as a bounded compatibility diagnostic.
- `RequiredArchetypes` reports unique resolved current archetypes after historical
  shapes converge, rather than the saved-shape count.
- Save-side SHA-256 is streamed through the injected filesystem adapter. Load owns
  one bounded artifact array and hashes that same array.
- Container parsing now requires canonical physical directory order, contiguous
  shared column/enable ordinals, zero alignment padding, zero reserved bytes, and
  strict generated boolean bytes (`0` or `1`).
- The unused kernel-level restore recovery-mode option was removed. Save-set
  generation recovery remains the explicit server coordinator policy.

## Crash and concurrency evidence

Existing fault injection still covers every named durable boundary. New tests add:

- actual child-process termination after the pending directory was durably
  flushed;
- recovery of the last complete generation afterward;
- cleanup by the next exclusive writer;
- simultaneous coordinators where a newer sequence owns the lease and an older
  waiter is rejected rather than repointing `CURRENT` backward;
- malformed `CURRENT` compatible fallback and strict rejection;
- arbitrary participant implementation exceptions not being hidden by fallback.

This is process-termination evidence, not a claim that CI can emulate removal of
power from every filesystem/controller combination. Platform durability still
depends on the supplied file/directory-fsync adapter.

## Scale benchmark

The 3.4.1 scale artifact uses 21 iterations at both sizes. This corrects the
original evidence weakness where p95 over three or five samples was effectively
the maximum.

| Population | Capture mean / p95 | Preflight mean / p95 | Restore mean / p95 | Sum means | Pause mean / p95 | Logical encode | Artifact |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 100K | 51.71 / 92.73 ms | 27.96 / 29.44 ms | 30.88 / 64.53 ms | 110.55 ms | 21.42 / 59.91 ms | 2.064 GiB/s | 11,613,472 B |
| 1M | 477.91 / 530.74 ms | 278.34 / 312.67 ms | 279.83 / 309.80 ms | 1,036.08 ms | 167.22 / 170.54 ms | 1.635 GiB/s | 116,125,968 B |

The longer run does not reproduce 3.4.0's 2.144 GiB/s 1M encode result; it is
reported as variance rather than preserved as a headline. Conversely, preflight
and restore remain near or faster than the accepted milestone values. Both
canonical digests are exactly unchanged:

- 100K: `19A3CA4E274D1921CD42A80FF50985379440BD7866DE22EADAAAA2F0BF267A98`
- 1M: `BE6DB26C49E5AC605D17F3B341812C2182790E254F4E4C778EE8794D7017870E`

The restore-plan temporary estimate rises intentionally from about 2.8/28.0 MB
to 38.8/388.4 MB at 100K/1M. That is not new allocation; it is honest accounting
for proportional ownership that 3.4.0 failed to include.

## Validation matrix

- complete ECS runtime/primitive, scheduler, lifecycle, content, and persistence
  suites in Debug and Release;
- 17 focused persistence tests, including the new adversarial format/catalog/
  memory/identity cases;
- 28 generator tests;
- save-set transaction suite including child-process kill and concurrent writers;
- 12 comparison-core tests;
- 18 bitmap experiment tests;
- 141-row permanent V3 benchmark, all zero timed managed allocation;
- isolated V2-first and V3-first 15-iteration comparison runs against 3.4.0;
- `git diff --check`.

## Remaining limitations

The audit's proportional-copy observation is valid but is not a correctness patch.
Capture, final artifact construction, immutable parsing, decoded columns, and a
simultaneous staging world still create substantial cold-path allocation and GC
pressure. ECS 3.4.1 accounts and bounds that design; it does not pretend to have
implemented a segmented/streaming snapshot format.

Likewise, publication callback failure is now safe from retry, but arbitrary host
code cannot be rolled back by the coordinator. Once publication begins, the host
owns any participant state it took and must treat a thrown callback as terminal.

Future persistence work should measure a segmented/streaming container, pooled
large buffers, incremental capture, retention/operator policy, and real
cross-participant relationship adapters. Those are feature/performance milestones,
not reasons to weaken this hardening checkpoint.

