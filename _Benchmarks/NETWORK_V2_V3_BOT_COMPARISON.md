# Network V2/V3 bot comparison

Protocol: `ultrasim-network-v2-v3-bot-comparison-v2`.

This is the measurement entrance to N0.10. It reuses the useful experimental
shape of the frozen V2 bot harness—stages, deterministic phased input, fixed
realtime cadence, owner correction, ping, statistics, and JSON evidence—without
copying V2's network implementation into V3.

## Process and topology contract

The coordinator starts V2 and V3 in separate child processes with tiered
compilation and ReadyToRun disabled. Accepted evidence uses both process orders.
The default scale catalog is `32,64,128,256` clients.

The first classification is intentionally `Provisional`:

```text
V2
    one shared server UDP socket
    one client socket/session per bot

V3 N0.9 topology
    one connected client/server UDP pair per bot
    two secure protocol peers per bot

V3 N0.10.1 topology
    one bounded shared server UDP listener
    one client socket and two secure protocol peers per bot
```

This answers how the existing machines handle equivalent observable work. It is
not a direct production-capacity forecast. The N0.10.1 topology deliberately
changes only server-side byte movement and path demultiplexing; protocol,
cryptography, application work, peer capacities, cadence, and semantic oracles
remain unchanged.

## Current admitted workload

`owner_movement` runs at fixed 60 Hz with deterministic phased 20 Hz complete
control input. Inputs stay inside V2's declared `[-100,100]` movement contract.
The comparison found and corrected an old harness defect where a `80..119`
generator silently caused late-cycle V2 inputs to be rejected.

The common canonical contract is:

- ready bot count;
- stable bot/network identity;
- last client-observed authoritative input sequence;
- final authoritative velocity bits.

Position remains in adapter diagnostics but is excluded from the equality hash.
V2 integrates an input when it arrives; the current V3 workload integrates the
latest complete control state every authoritative step. Treating those positions
as identical would manufacture a semantic equivalence neither engine promises.

`spread_mix` and `dense_mix` are not implemented yet. Both adapters reject those
names so an owner-only run cannot be mislabeled as observer WorldState/Event
evidence.

## Metrics

The stable joined schema includes:

- tick, server-pump, client-pump, ping, and input-ack distributions where exposed;
- deadline misses and longest miss streak;
- logical input/owner/WorldState/Event work counts;
- actual V3 UDP datagrams and bytes, exact reliable retransmitted bytes, and
  capacity drops;
- normalized logical application bytes and V3 wire/logical amplification;
- protocol reservation and configured socket-buffer bytes;
- managed allocation, collection counts, and process memory;
- per-surface reliable/independent-unreliable/receive/in-flight occupancy
  capacity, mean, p95, maximum, and 50/75/90/100% pressure ticks;
- owner input-to-authority-ack age in milliseconds and simulation ticks, plus
  per-client ping/input-ack fairness summaries;
- stage process CPU/core utilization for the same-process V3 topology;
- handshake latency, allocation, datagrams, bytes, attempts, and successes;
- UDP send/receive/poll calls, WouldBlock observations, mean/max MTU use;
- secure data/ACK/keepalive packet counts, encrypted/decrypted plaintext, and
  authentication/replay diagnostics;
- population-to-population marginal p95 tick cost in microseconds per added bot;
- exact normalized canonical rows and hash;
- adapter-specific diagnostics.

Every result includes explicit metric-family coverage flags. A false flag means
**not measured by that workload/adapter**, never a measured zero. Frozen V2 does
not expose every V3 counter, but the adapter now preserves every top-level
numeric V2 stage diagnostic under `diagnostics.v2.*`; no ratio is produced from
an unavailable/zero denominator.

The owner-only workload's normalized logical-byte diagnostic counts semantic
messages admitted by the sender, not either protocol's framing: 10 bytes per
complete input sample, 30 bytes per queued owner-state sample, and 4 bytes per
completed ping/pong sample. Applied owner-state counts remain separate, so
coalescing or delivery differences stay visible. V3 wire/logical amplification
includes UDP protocol headers, security, acknowledgements, retransmissions, and
handshake traffic. V2 does not currently expose physical wire bytes, so no
cross-generation byte ratio is claimed.

V3 uses an explicit owner-workload capacity profile for executable 256-client
evidence. Each artifact also records the current default protocol reservation
estimate per client and total. The benchmark profile is evidence, not yet a
production default change.

Application Event scale diagnostics live in production snapshots rather than a
deep timing mode: records examined/skipped, registered consumers, retained high
water, and minimum-cursor scan entries. The future mixed workload will use these
to decide whether the simple global Event ledger needs participant-local indexing.

### Metric levels and remaining gates

The harness deliberately uses three evidence levels:

```text
always-on counters
    bytes, packet kinds, occupancy/high-water, pressure, drops,
    retransmission bytes, syscall counts, reservation, deadline misses

normal timings
    tick/server/client wall time, ping, owner input ACK,
    handshake, combined-process CPU

deep diagnostic mode
    opt-in causal layer sweeps and coarse pump/application phase timing;
    future crypto/codec/assembly phase CPU and per-lane timing,
    GC pause events, worker/shard busy and handoff time
```

`owner_movement` can honestly measure owner-input freshness and per-client
fairness. It cannot measure WorldState age by Immediate/Near/Far tier,
application-Event publish-to-apply latency, interest amplification, baseline
recovery, slow-recipient debt, or traversal edge/relation work. Those are frozen
as required metrics for the workloads that own the semantics:

| Future profile | Required additional evidence |
| --- | --- |
| `spread_mix` / `dense_mix` | WorldState age by tier; canonical changes → semantic constructions → recipient copies → bundles → datagrams/bytes; worst-recipient freshness. |
| `traversal` | region edges added/removed, relations examined/changed, retier work, interest CPU. |
| `generation_resync` | baseline capture, records/segments/bytes/retries, first new-epoch owner/input latency, all-clients-current duration. |
| `slow_client` | debt age, retained bytes, retries, occupancy, nonviable/escalation counts, pathological-recipient CPU spillover. |
| `join_storm` | joins/second, handshake CPU/allocation/bytes, established reservation and physical memory per join. |

P95 packet-fill distribution, GC pause duration, LOH/pinned pressure, and
server-only CPU require targeted instrumentation rather than inference from the
current combined process. They remain explicit N0.10 follow-ups. Worker/shard
metrics remain inapplicable until a measured checkpoint actually adds workers.

## Running

```bash
dotnet run --project _Tests/UltraSim.Network.Comparison.Tests -c Release

DOTNET_TieredCompilation=0 DOTNET_ReadyToRun=0 \
dotnet run --project _Benchmarks/UltraSim.Network.Comparison -c Release -- \
  --clients=32,64,128,256 \
  --stage-ticks=120 \
  --settle-ticks=30 \
  --v3-topology=shared-listener \
  --output-dir=_Benchmarks/results/network-v2-v3-bot-local
```

Development smoke runs may pass `--clients=4 --single-order`. `--use-existing`
rejoins already-created adapter artifacts at their expected scale/order paths.
`--v3-topology=connected-pairs` retains the N0.10 entry control.
The V3 adapter-only `--shared-socket-io=platform-batch` enables the opt-in
shared-listener native-send experiment; scalar remains the comparison default.
An immutable accepted baseline requires the full matrix, semantic tests, both
orders, stable machine conditions, and review under the repository benchmark
policy.

The first accepted provisional matrix is
[`network-v2-v3-bot-n010-entry-2026-08-15.json`](Baselines/network-v2-v3-bot-n010-entry-2026-08-15.json).
It preserves stage-start cold work; the single reported deadline miss in each
V3 stage is therefore not presented as a steady-state overload cliff.

The first matrix also records a stable V3-only retransmission inflection:
`2`, `3–5`, and `6–11` retransmissions at `32/64/128` bots becomes `426–430`
at 256, while
bounded queues remain below 50% occupied and capacity drops stay zero. N0.10's
first shared-listener/session-pump experiment was intended to determine whether
removing serial per-pair traversal removed that timeout/ACK pressure before any
congestion or capacity policy was retuned.

## N0.10.1 shared-listener result

The accepted follow-up is
[`network-v2-v3-bot-n0101-shared-listener-2026-08-15.json`](Baselines/network-v2-v3-bot-n0101-shared-listener-2026-08-15.json).
All eight scale/order runs again reached exact canonical owner state. The first
implementation exposed an important aggregate-capacity error: a listener serving
64 sessions inherited one connection's 16 KiB kernel socket-buffer request,
causing unreported OS drops, hundreds of retransmissions, and state that could
not converge inside the admitted settle window. The accepted implementation
gives the shared socket the same total configured send/receive capacity that the
replaced per-session server sockets collectively owned. This is topology
consolidation, not a capacity increase. A permanent 64-session initial-flight
test protects the aggregate burst contract.

Paired tick-p95 observations were:

| Bots | V2 | V3 connected-pairs entry | V3 shared listener |
| ---: | ---: | ---: | ---: |
| 32 | 0.244–0.257 ms | 0.718–0.800 ms | 0.691–0.705 ms |
| 64 | 0.487–0.492 ms | 1.449–1.558 ms | 1.369–1.579 ms |
| 128 | 0.946–1.136 ms | 2.750–2.818 ms | 2.692–2.977 ms |
| 256 | 1.829–1.858 ms | 5.469–5.613 ms | 5.112–5.229 ms |

The V3 least-squares slope across the paired midpoints improved from about
21.2 to 19.8 microseconds per bot, roughly 6.9%. The simple 4 ms marker moves
only from about 184 to 194 bots. At 256, retransmissions fell from 426–430 to
210–331, ping p95 improved from about 20.6 to 20.2–20.4 ms, and
wire/logical amplification moved from about 7.51x to 7.47–7.48x. These are useful
but modest changes. The experiment therefore rejects the stronger hypothesis
that per-session server sockets explain most of V3's roughly 3x CPU slope.

The shared listener is retained because it establishes the correct bounded
one-listener/many-session ownership model and removes redundant server sockets.
It is not presented as a large CPU optimization. Its fixed per-session demux
rings increase reported managed payload reservation at 256 from 5.04 MiB to
6.51 MiB while configured aggregate socket-buffer bytes remain 16 MiB. The next
specialization must preserve low warmed allocation while reducing per-connection
reservation; further CPU work requires profiling the still-dominant per-peer
protocol/application path rather than guessing at socket topology.

## N0.10.2 peer-cost attribution

The V3 adapter has an explicitly non-comparable causal mode:

```bash
DOTNET_TieredCompilation=0 DOTNET_ReadyToRun=0 \
dotnet run --project _Benchmarks/UltraSim.Network.V3ComparisonAdapter -c Release -- \
  --clients=256 \
  --stage-ticks=120 \
  --settle-ticks=30 \
  --topology=shared-listener \
  --diagnostic-suite \
  --output=_Benchmarks/results/network-v3-peer-cost-attribution.json
```

It keeps all 256 secure connections and reservations alive while varying either
the active-client fraction or one application layer. Every case warms its exact
branch before measurement, snapshots cumulative wire counters at the timed
boundary, and runs in both forward and reverse case order. Coarse phase timers
are enabled only here.

The first 256-connection observation is preserved as
[`network-v3-peer-cost-attribution-2026-08-15.json`](Baselines/network-v3-peer-cost-attribution-2026-08-15.json):

| Case | Active | Tick p95 mean | Server p95 mean | Client p95 mean | CPU cores mean | Timed wire bytes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| established idle | 0 | 0.684 ms | 0.466 ms | 0.224 ms | 0.145 | 0 |
| full, 10% active | 26 | 1.182 ms | 0.823 ms | 0.376 ms | 0.150 | 0.762 MB |
| full, 35% active | 90 | 2.361 ms | 1.646 ms | 0.747 ms | 0.205 | 2.638 MB |
| Control ping | 256 | 0.816 ms | 0.572 ms | 0.251 ms | 0.152 | 0.133 MB |
| Input only | 256 | 1.867 ms | 1.310 ms | 0.581 ms | 0.200 | 1.403 MB |
| Input + owner state | 256 | 5.100 ms | 3.517 ms | 1.619 ms | 0.362 | 7.432 MB |
| full vertical | 256 | 4.975 ms | 3.432 ms | 1.575 ms | 0.360 | 7.504 MB |

Forward/reverse full means were 4.799/4.832 ms. Of that mean, the forward
protocol pump consumed about 3.342 ms and the return pump about 1.442 ms; all
application ingress, authority integration, owner encoding/queueing, and client
apply loops together consumed about 0.032 ms. The two pumps therefore account
for over 99% of this benchmark's timed work. Established-idle mean was about
0.645 ms, only ~13% of the full total; active packet processing, not idle peer
housekeeping, owns most of the remaining slope.

The layer delta is equally clear. Input adds about 1.106 ms above idle. Adding
owner publication adds another ~3.132 ms, approximately 75% of the full active
increment. The 0/10/35/100% sweep remains close to linear, so the result still
does not indicate hidden pairwise work.

Most importantly, the Input+Owner case emitted 48,640 unreliable Data packets,
zero reliable fragments, and exactly 48,640 transport ACK packets. The current
protocol acknowledges every admitted Data packet even though unreliable
messages own no retained sender record for that ACK to retire. Full traffic
similarly produced about 49.6K Data packets and 48.8K ACK packets while only
about 1.18K reliable fragments existed. That makes **delivery-ACK suppression
for unreliable lanes, while preserving authenticated replay state**, the first
measured CPU/wire refinement to test.

Reservation specialization remains N0.10.2a's independent memory requirement.
The general profile still estimates roughly 3.39 GB for both peers across 256
connections. It should not be judged primarily by CPU gain. N0.10.2b now has a
narrow CPU hypothesis: remove semantically unused unreliable ACK emission and
remeasure the same full, input-only, and input+owner cases before attempting
generic batching, crypto tuning, or workers.

### N0.10.2b unreliable-ACK result

The narrow correction preserves authenticated replay and admitted-delivery
tracking for every Data packet, but only an admitted reliable fragment is now
ACK-eliciting. Reliable data deferred by receive/reassembly backpressure remains
unacknowledged and retained for retry under a fresh packet number. Pure
unreliable traffic intentionally creates no transport RTT sample because it owns
no sender flight record from which an RTT sample could be derived.

The paired causal suite is preserved as
[`network-v3-peer-cost-unreliable-ack-suppression-2026-08-15.json`](Baselines/network-v3-peer-cost-unreliable-ack-suppression-2026-08-15.json).
All case hashes match across forward/reverse order. Means across the two orders
show the expected activity-dependent effect:

| Case | Tick p95 before | Tick p95 after | Change |
| --- | ---: | ---: | ---: |
| established idle | 0.684 ms | 0.688 ms | +0.6% |
| full, 10% active | 1.182 ms | 0.920 ms | -22.2% |
| full, 35% active | 2.361 ms | 1.482 ms | -37.2% |
| Control ping | 0.816 ms | 0.816 ms | +0.04% |
| Input only | 1.867 ms | 1.247 ms | -33.2% |
| Input + owner | 5.100 ms | 2.790 ms | -45.3% |
| full vertical | 4.975 ms | 2.868 ms | -42.4% |

The idle and reliable-Control controls remain unchanged while the improvement
grows with unreliable traffic. Packet/wire means for the three causal cases are:

| Case | Tick p95 before | Tick p95 after | Change | ACK packets before -> after | Wire bytes before -> after | Send calls before -> after |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Input only | 1.867 ms | 1.247 ms | -33.2% | 10,240 -> 0 | 1.403 MB -> 0.788 MB | 20,480 -> 10,240 |
| Input + owner | 5.100 ms | 2.790 ms | -45.3% | 48,640 -> 0 | 7.432 MB -> 4.513 MB | 97,280 -> 48,640 |
| full vertical | 4.975 ms | 2.868 ms | -42.4% | 48,806 -> 1,194.5 | 7.504 MB -> 4.542 MB | 98,432.5 -> 49,733 |

The packet-level mechanism matches the hypothesis exactly. Input and
Input+Owner keep their 10,240 and 48,640 Data packets while eliminating every
transport ACK. Full traffic retains about 1,194.5 ACKs for about 1,194.5 reliable
fragment sends. Encrypted/decrypted plaintext falls from 0.502/0.502 MB to
0.338/0.338 MB for Input, from 3.151/3.151 MB to 2.373/2.373 MB for Input+Owner,
and from about 3.173/3.173 MB to 2.353/2.353 MB for Full. Receive calls fall with
send calls.

Coarse phase evidence also localizes the recovery. Input forward/return pump
means fall from 1.246/0.493 ms to 0.960/0.209 ms. Input+Owner falls from
3.390/1.462 ms to 2.461/0.211 ms, and Full from 3.342/1.442 ms to
2.464/0.242 ms. Application work is unchanged. Reliable retransmission remains
small and noisy rather than being the cause: Full averages 146 -> 165.5 packets
and 10,074 -> 11,419.5 bytes. Input+Owner input-ack p95 improves from
20.196 ms / 1.212 ticks to 19.242 ms / 1.155 ticks; Full improves from
20.131 ms / 1.208 ticks to 19.285 ms / 1.157 ticks. Input-only has no owner-state
application proof, so its zero-valued ACK-age fields remain explicitly
unmeasured, not zero-latency evidence.

The accepted post-change V2/V3 matrix is
[`network-v2-v3-bot-n0102-unreliable-ack-suppression-2026-08-15.json`](Baselines/network-v2-v3-bot-n0102-unreliable-ack-suppression-2026-08-15.json):

| Bots | V2 tick p95 | V3 tick p95 | V3/V2 |
| ---: | ---: | ---: | ---: |
| 32 | 0.255-0.265 ms | 0.374-0.389 ms | 1.41-1.53x |
| 64 | 0.486-0.503 ms | 0.725-0.741 ms | 1.47-1.49x |
| 128 | 0.925-0.935 ms | 1.437-1.459 ms | 1.54-1.58x |
| 256 | 1.786-1.872 ms | 2.809-2.813 ms | 1.50-1.58x |

All eight canonical comparisons match. V3 marginal p95 cost is now
10.98, 11.18, and 10.65 microseconds per added client across the three scale
steps, versus 7.33, 6.81, and 7.02 for V2. The simple local 4 ms marker moves
from roughly 194 clients after the listener experiment to roughly 368 clients;
this remains a diagnostic interpolation, not a production capacity forecast.
At 256, wire/logical amplification falls from about 7.47-7.48x to
4.39-4.56x while warmed timed allocation remains about 43 KiB. Mean datagram
fill rises only to about 6.2%, with one send call per datagram, so small-packet
assembly/batching is now a measured candidate for the remaining CPU/wire gap.
Reservation specialization remains independently required for memory viability.
Neither observation earns workers, sharding, compression, or capacity increases.

### N0.10.3 batch-opportunity census

Low datagram fill does not itself prove same-connection packet assembly can help.
The deep diagnostic therefore performs a cold capacity scan immediately before
each peer-direction pump and reports already-ready unreliable frames, exact
frame plaintext, 2+/3+/4+ opportunities, maximum ready frames, and immediate
order-preserving greedy datagrams. It neither mutates queues nor waits for work;
its scan cost means census timings are diagnostic rather than a replacement
performance baseline.

The accepted census is
[`network-v3-peer-cost-batch-opportunity-census-2026-08-15.json`](Baselines/network-v3-peer-cost-batch-opportunity-census-2026-08-15.json).
Each case inspected 230,400 peer-direction emit opportunities. Results:

| Case | Nonempty opportunities | Ready frames observed | Maximum ready | 2+ / 3+ / 4+ | Greedy datagrams |
| --- | ---: | ---: | ---: | ---: | ---: |
| idle | 0 | 0 | 0 | 0 / 0 / 0 | 0 |
| full, 10% active | 4,940 | 4,940 | 1 | 0 / 0 / 0 | 4,940 |
| full, 35% active | 17,100 | 17,100 | 1 | 0 / 0 / 0 | 17,100 |
| Control | 0 | 0 | 0 | 0 / 0 / 0 | 0 |
| Input | 10,240 | 10,240 | 1 | 0 / 0 / 0 | 10,240 |
| Input + owner | 48,640 | 48,640 | 1 | 0 / 0 / 0 | 48,640 |
| Full | 52,805-56,286 | 52,805-56,286 | 1 | 0 / 0 / 0 | same as ready |

Full has more ready observations than unique unreliable sends because reliable
pacing can leave one unreliable frame pending across more than one inspected
pump. That does not create a multi-frame opportunity. Every nonempty observation
still contains exactly one frame.

The current owner workload therefore does **not** earn an unreliable multi-frame
wire format. Its small datagrams arise from one ready frame per connection and
direction, so the next CPU evidence should distinguish selection, frame
encode/bookkeeping, authenticated secure framing, endpoint send, socket/demux,
secure receive, and frame admission. Cross-connection syscall batching may later
be relevant, while a future `spread_mix`/`dense_mix` WorldState workload may
independently earn same-connection frame assembly. Neither should be inferred
from mean MTU fill alone.

### N0.10.4 protocol-stage attribution

The next opt-in run times only coarse peer stages. Timestamp overhead is visible
(especially across 512 idle peer directions), so it is diagnostic attribution,
not a replacement p95 baseline. The accepted artifact is
[`network-v3-peer-stage-attribution-2026-08-15.json`](Baselines/network-v3-peer-stage-attribution-2026-08-15.json).

Subtracting the paired established-idle scan/poll floor gives this Input+Owner
breakdown across 48,640 Data packets:

| Stage | Total | Approx. per Data packet |
| --- | ---: | ---: |
| select/schedule | 2.842 ms | 0.058 us |
| frame encode | 2.637 ms | 0.054 us |
| secure frame + AES-GCM send | 32.718 ms | 0.673 us |
| endpoint send | 139.875 ms | 2.876 us |
| endpoint receive above idle | 48.589 ms | 0.999 us |
| secure parse + AES-GCM receive | 32.666 ms | 0.672 us |
| frame decode/admission | 4.153 ms | 0.085 us |

Endpoint send/receive accounts for about 69% of the active attributed peer time;
secure framing/AES accounts for about 24%. Selection plus frame codec/admission is
about 3.6%, with the remainder in bounded bookkeeping/residual work. Input-only
and Full show the same ordering. This rejects ready-head restructuring as the
next large owner-workload recovery.

The measured CPU candidate is now **cross-connection socket I/O batching at the
shared listener**, especially the server-to-client OwnerState direction. That is
not the rejected same-connection multi-frame format: packet identities, AEAD,
replay, and semantics remain independent while one owner may submit/receive
several already-built datagrams in fewer OS calls. It must first define a bounded,
portable ownership/fallback contract and preserve latency, backpressure, and
failure attribution. Linux-only evidence cannot silently become the portable
Network contract. Reservation specialization remains independently required.

### N0.10.5 shared-listener native send experiment

The first socket-batching experiment preserves one independently framed,
encrypted, numbered, replay-protected datagram per peer operation. On supported
Linux hosts, the shared listener admits those datagrams into one fixed outbound
ring and submits already-ready prefixes through `sendmmsg`; unsupported hosts and
handshake traffic retain the scalar `SendTo` path. No send waits for a fuller
batch. A returned native prefix is removed exactly, an unsent suffix remains
owned by the bounded listener, and endpoint retirement cannot send a stale queued
path. The mode is opt-in because its additional fixed reservation belongs in the
separate connection-memory specialization.

Permanent tests cover an eight-recipient native batch, one-datagram outbound-ring
pressure with later peer work remaining pending, independent payload/recipient
delivery, and 0 B warmed allocation. The diagnostic artifacts are:

- [`network-v3-shared-listener-scalar-split-attribution-2026-08-15.json`](Baselines/network-v3-shared-listener-scalar-split-attribution-2026-08-15.json)
- [`network-v3-shared-listener-native-send-batch-2026-08-15.json`](Baselines/network-v3-shared-listener-native-send-batch-2026-08-15.json)

| 256-client case | Scalar tick p95 | Native-send tick p95 | Listener sends | Native batch | Result |
| --- | ---: | ---: | ---: | ---: | ---: |
| Input | 1.547 ms | 1.565 ms | 0 -> 0 | n/a | unchanged/no server sends |
| Input + Owner | 3.186 ms | 2.979 ms | 38,400 -> 600 | 64.0 datagrams/call | 6.5% lower |
| Full | 3.325 ms | 3.039 ms | 38,067 -> 846 | 45.5 datagrams/call | 8.6% lower |

Input+Owner's server socket-submit time falls from approximately 110.8 ms to
85.0 ms across the measured stage, while peer-side bounded queue admission adds
about 3.7 ms. Client socket send time remains about 28.3 ms and shared-listener
receive time about 7.6-7.8 ms. There were no partial native submissions or native
WouldBlock results; native batch p50/p95/max was 64 for the two server-output
cases. A normal, probes-disabled 256-client Full smoke measured 2.683 ms versus
the prior scalar 2.821 ms observation, consistent with the diagnostic result.

The experiment therefore earns the bounded native-send seam but rejects the
stronger prediction that syscall-count collapse alone would remove most of the
remaining CPU gap. `sendmmsg` amortizes the managed/kernel transition while the
kernel still performs per-datagram UDP work. Native receive batching is not the
next broad optimization by inspection: server receive syscall time is only about
8 ms in these stages. Per-connection reservation specialization remains the next
independent obligation; any further CPU work must remeasure the residual rather
than infer it from syscall counts.

### N0.10.6 manifest-aware payload reservation

This memory specialization changes only physical payload reservation. The global
16 KiB maximum remains a safety bound, while concrete arrays follow the immutable
manifest and lane fragmentation contract. Message counts and transactional
failure semantics do not change.

Permanent tests prove that independent WorldState storage is capped at one exact
datagram, a byte beyond it is rejected, unused descriptors and non-reliable lanes
reserve no reassembly region, fragmented Bulk still reassembles, retained-byte
counters transition exactly, and warmed delivery remains 0 B.

The accepted artifact is
[`network-v3-manifest-payload-reservation-2026-08-15.json`](Baselines/network-v3-manifest-payload-reservation-2026-08-15.json):

| 256-client observation | Result |
| --- | ---: |
| known Network reservation | 6.21 MiB |
| known reservation/client | 25.4 KiB |
| manifest-aware default two-peer estimate/client | 303.0 KiB |
| old conservative count-only estimate/client | about 12.6 MiB |
| retained payload capacity/client in this profile | 596 B |
| population retained-byte mean / p95 / max | 901 / 1,011 / 1,070 B |
| largest retained message | 30 B |
| byte-pressure ticks >= 50/75/90/100% | 0 / 0 / 0 / 0 |
| byte-capacity failures | 0 |
| peak managed heap / private bytes | 14.6 / 82.9 MiB |

The run's 3.295 ms tick p95 is recorded but is not a paired CPU claim; this is a
memory/evidence checkpoint. `owner_movement` contains no large Bulk or baseline
traffic and cannot select eventual byte budgets. The same telemetry must be
collected for `dense_mix`, `generation_resync`, `slow_client`, and UI/Bulk. Only
then should a bounded shared page/size-class store replace reliable/received
fixed strides if the remaining reservation and high water justify it.

### Cadence-decoupling acceptance

N0.10 must prove that transport cadence and authoritative publication cadence are
independent without becoming independent application-work producers:

> Additional Network pumps without additional authoritative publication may
> advance transport state but may not create duplicate application-semantic debt.

Conversely, multiple authoritative steps between Network publications may
coalesce intermediate latest-state facts, but must retain required Event/Control
facts and the newest complete Owner/WorldState truth. Metrics must be reported per
wall-clock second, per authoritative step, and per Network pump, including
`network_pumps_per_authoritative_step`, captures per step, publications per
capture, queue/debt high water, and state/input/Event age in both simulation ticks
and milliseconds.

The focused matrix is `60/60`, `60/120`, `60/240`, `30/60`, `30/120`, `20/120`,
and `120/60` Hz plus opt-in `60->30->60` and `60->20->60` cadence transitions at
120 Hz Network pumping. Separate 250 ms authority-stall and Network-stall cases
must report peak occupancy/debt, retransmission and wire bursts, recovery time,
and maximum client state age. Normal performance development remains fixed 60 Hz,
1x playback, cadence adaptation off; this matrix tests correctness and liveness,
not an escape hatch for missing the 60 Hz budget.
