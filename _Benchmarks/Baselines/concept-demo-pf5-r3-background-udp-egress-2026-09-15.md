# PF5-R3 — bounded background UDP egress

Status: **CLOSED — Outcome A; `BackgroundPlatformBatch` selected.**

## Identity and scope

- Starting HEAD: `ed04e08310fc79b6eadc9cd185dcdc5ab6abd758`
- Experimental implementation: `8ce6d5f7` (`Add bounded background UDP egress experiment`)
- Selected-policy production commit: `f2940afa18d69bb83df291215dcb6903938ee3c2`
- PF5-R2 replication semantic candidate: `992fc541460f18534da9036c73cb711277de11ce`
- Prior integrated correctness candidate: `89f26f8bbaa1003b15ddf52d61d6ce2567694b74`
- New integrated correctness candidate for PF6: `f2940afa18d69bb83df291215dcb6903938ee3c2`
- Game manifest: v29, unchanged.
- Network transport revision: 2, unchanged.

PF5-R3 changes physical ownership only after a complete encrypted datagram exists. Game,
`ChunkPublication`, AOI, cadence, message 129/148, proof, recipient knowledge, reliability,
packet selection, PacketNumber, SecurityEpoch, encryption, aggregation, and wire bytes retain
their prior owners and formats.

## Selected ownership

The old production default performed scalar socket submission on the listener owner. The selected
default copies each finalized established-session datagram into one fixed-capacity SPSC slab per
shared listener. One persistent sender drains it with Linux `sendmmsg`; other platforms use scalar
submission with identical background ownership. Admission/handshake responses remain inline.

`DatagramSendStatus.Accepted` means the bounded listener storage owns a stable ciphertext copy. It
does not mean kernel acceptance or remote delivery. A full queue refuses before peer send state is
committed. The worker owns neither protocol nor Game state. A queued item retains endpoint and
path generation; retirement/rebind advances that generation and stale work is discarded.

The default 64-session Concept Demo shape reserves 2,048 datagram slots: 3,014,656 payload bytes
and 3,097,864 total background-egress bytes. The rated synthetic capacities scale only because the
benchmark explicitly raises listener capacity; this does not raise the product participant cap.

## Rated current-path matrix

Machine: Linux, .NET 8.0.29, 16 logical CPUs, 1 GHz Stopwatch. Each row uses 1,000 authoritative
entities, 20 dirty source rows/tick across eight chunks, 60 warmup ticks, 270 measured ticks,
selected 1/2/4 cadence, message-129 bundling, selective 148 LZ4 at 384/32, cap-two CrossLane
aggregation, current shared prepared-latest-state publications, secure loopback UDP, client apply,
and exact proof. Heavy Game/stage probes were off. `Remote p95/p99` is the per-tick server-owned
remote budget (shared publication preparation + recipient wakeup + Game replication + Network +
listener submission). Process CPU is the matched in-process server-plus-clients window and includes
the sender.

| R | Mode | Remote p95 ms | Remote p99 ms | Process CPU ms | Sender active wall ms | Queue HW | Queue p99 ms | Proof processing p95 ms | UDP datagrams | Send syscalls |
|--:|:--|--:|--:|--:|--:|--:|--:|--:|--:|--:|
|64|Inline Scalar|1.444|1.470|1,360|0|0|0|6.340|34,930|17,465|
|64|Inline Batched|1.404|1.428|1,360|0|0|0|6.284|34,930|546|
|64|Background Scalar|1.333|1.371|1,400|73.4|56|0.252|8.154|34,930|17,465|
|64|Background Batched|1.275|1.305|1,390|57.6|64|0.217|6.996|34,930|878|
|128|Inline Scalar|2.801|2.858|2,790|0|0|0|12.920|69,644|34,822|
|128|Inline Batched|2.726|2.803|2,780|0|0|0|13.452|69,644|1,089|
|128|Background Scalar|2.524|2.661|2,860|143.1|104|0.498|16.383|69,644|34,822|
|128|Background Batched|2.486|2.565|2,830|112.9|118|0.388|12.564|69,644|1,389|
|192|Inline Scalar|4.467|4.655|4,410|0|0|0|24.026|104,608|52,208|
|192|Inline Batched|4.055|4.233|4,240|0|0|0|20.832|104,608|1,632|
|192|Background Scalar|3.770|4.034|4,410|189.6|153|1.049|23.225|104,608|52,208|
|192|Background Batched|3.642|3.813|4,290|154.6|159|0.505|19.322|104,608|2,187|
|256|Inline Scalar|5.641|5.918|5,810|0|0|0|32.767|139,582|69,663|
|256|Inline Batched|5.458|5.604|5,760|0|0|0|26.965|139,582|2,178|
|256|Background Scalar|4.982|5.124|5,840|265.8|201|0.852|30.077|139,582|69,663|
|256|Background Batched|4.901|5.142|5,810|205.3|201|0.610|27.056|139,582|2,604|
|300|Inline Scalar|6.872|7.329|7,040|0|0|0|32.767|163,796|81,598|
|300|Inline Batched|6.429|6.608|6,810|0|0|0|30.967|163,796|2,721|
|300|Background Scalar|5.794|5.976|6,890|307.6|229|0.989|32.767|163,796|81,598|
|300|Background Batched|5.691|5.967|6,820|245.6|226|0.722|30.162|163,796|2,968|

At 350, inline batched p95/p99 was 7.535/7.998 ms; background batched was
6.712/6.869 ms with 270 queued datagrams high-water and 0.788 ms p99 residence.

Against inline batching, selected background batching reduces p95 by 9.19%, 8.78%, 10.19%,
10.20%, and 11.48% at 64/128/192/256/300. Corresponding p99 reductions are 8.62%, 8.46%,
9.92%, 8.25%, and 9.70%. Matched-window process CPU changes by +2.21%, +1.80%, +1.18%,
+0.87%, and +0.15%. The first sampled point over 4 ms moves from 192 for both inline modes to
256 for both background modes. These are synthetic rated recipients, not CCU.

Proof-processing p95 comes from a separate bounded Detailed pass and is the delivery-queued to
proof-processed histogram upper bound; 32.767 ms is that histogram's terminal bucket. The rated
CPU rows keep Detailed Game attribution disabled. Every population/mode has the same semantic digest, successful publication count, proof count,
Game-message count, and bundle-entry count. Secure Data/UDP datagram counts are identical within
each population. Payload bytes vary by at most 0.34% because asynchronous proof arrival changes
otherwise-valid latest-state range/bundle composition; the Game/wire format and final truth are
unchanged.

## Freshness, pressure, and dedicated-process evidence

A separate Detailed 192 pass recorded 414,720/414,720 applied proof entries in every mode.
Background batched matched inline batched at a two-authority-step source-to-proof p95 upper bound,
11 outstanding deliveries high-water, zero snapshot fallback, and zero Rebase. Its
queued-to-proof processing p95 was 19.592 ms versus 19.857 ms inline batched. Detailed timing is
not used as the rated CPU result.

The equal-long-window burst control emits 80 rows every fourth tick instead of 20 each tick. At
192, inline-batched remote p95/p99 was 6.698/6.933 ms; background batched was 6.010/6.322 ms.
The selected queue reached 268 datagrams, p99 0.862 ms, then drained to zero with no refusal or
send failure. Logical work remained 368,640 publications/proofs and 63,234 Game messages.

The external H0.5 control uses one dedicated server process plus 64 independent client processes,
eight connection-owner threads, ordinary Hosted admission, path rebind, secure traffic, Game
apply, and proof. All modes completed 64 applications, 3,904 chunk proofs, 3,840 event proofs,
64 rebinds, and clean retirement. Selected background batching measured 1,102.35 ms server
transport work and 1,390 ms server-process CPU, versus 1,224.60 ms and 1,450 ms inline batching.
Its queue high-water was 262 and no refusal/failure occurred. Local scheduler/process noise makes
this a composition control, not the rated curve.

A real-loopback small-send-buffer attempt requested a 1,472-byte server send buffer (Linux raised
the aggregate socket total to its kernel minimum). At 192 it produced no `WouldBlock` or
`NoBufferSpace`; the queue peaked at 383, p99 residence was 1.049 ms, and it drained. This host
could not force genuine kernel backpressure. The deterministic sender hook therefore remains the
portable pressure gate: capacity one accepts one datagram, refuses the second without advancing
PacketNumber or consuming reliable work, exposes nonzero oldest age, and drains after release.

The scripted native tests observe requests 3,3,2 across `WouldBlock`, one-prefix partial success,
and suffix retry, plus requests 2,2 across `NoBufferSpace` and later success without losing the
queued work. A deliberate physical reliable-send failure is retained by reliable flight and
retransmits successfully. Security-epoch FIFO, path rebind, retirement, worker-fault propagation,
bounded drain, repeated dispose, and warmed zero producer allocation are permanent tests.

Queue timestamps/counters are included in every rated row. A 192 background-batched run with the
detailed timestamps disabled measured 3.781 ms p95 versus 3.642 ms with them enabled; the disabled
control was 3.8% slower, so no positive instrumentation tax was distinguishable from run noise.

The rated listener requested an 8 MiB socket buffer and Linux reported 16 MiB after its normal
doubling. Sender-thread CPU time was not available from the managed runtime; the table therefore
reports worker-owned service wall time, while matched process CPU contains both producer and sender
work. No sender-wall percentile is added to a main-thread percentile because the paths overlap.

## Disposition

Outcome A is accepted. `BackgroundPlatformBatch` is the selected production default; scalar
background is the portable execution fallback when native batching is unavailable. PF5-R2's
replication candidate remains its semantic attribution point. PF6 must use/reconfirm the new
integrated production candidate `f2940afa18d69bb83df291215dcb6903938ee3c2`.

PF5-R3 does not authorize moving encryption, scheduling, proof, or Game work off-owner. A later
performance checkpoint is warranted only by new external-product evidence, not by a desire to add
more worker stages.

## Verification closure

The PF5-R3 focused verifier passed on the selected default. The complete canonical Release verifier
then passed at source/evidence commit `352e414ae9ad9e606f66786077dbd5b9caa26953`, including all
Network/replication tests, ServerOnly/ClientOnly/Full physical composition, Contentless Online
cold/warm, SinglePlayer/Online/Complete exported products, the exported PF1/PF2 mixed swarms,
Complete-hosted parity, and F0.7 lifecycle retirement. The complete Network suite also passed three
additional consecutive shutdown/retirement stress runs. A final test-only child adds deterministic
`NoBufferSpace` retention coverage and repeats the focused PF5-R3 gate; it does not change
production code.
