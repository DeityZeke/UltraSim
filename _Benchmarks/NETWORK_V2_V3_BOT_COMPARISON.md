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

deep diagnostic mode (not enabled yet)
    crypto/codec/assembly phase CPU, per-lane timing,
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
