# Network V2/V3 bot comparison

Protocol: `ultrasim-network-v2-v3-bot-comparison-v1`.

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
```

This answers how the existing machines handle equivalent observable work. It is
not a direct prediction for the future N0.10 shared listener/shard topology.

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
- actual V3 UDP datagrams and bytes, retransmission and capacity drops;
- normalized logical application bytes and V3 wire/logical amplification;
- protocol reservation and configured socket-buffer bytes;
- managed allocation, collection counts, and process memory;
- reliable/unreliable/receive/in-flight high water and pressure ticks;
- exact normalized canonical rows and hash;
- adapter-specific diagnostics.

Frozen V2 does not expose every V3 counter. Missing measurements remain zero and
are called out in Markdown; no ratio is produced from a zero denominator.

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

## Running

```bash
dotnet run --project _Tests/UltraSim.Network.Comparison.Tests -c Release

DOTNET_TieredCompilation=0 DOTNET_ReadyToRun=0 \
dotnet run --project _Benchmarks/UltraSim.Network.Comparison -c Release -- \
  --clients=32,64,128,256 \
  --stage-ticks=120 \
  --settle-ticks=30 \
  --output-dir=_Benchmarks/results/network-v2-v3-bot-local
```

Development smoke runs may pass `--clients=4 --single-order`. `--use-existing`
rejoins already-created adapter artifacts at their expected scale/order paths.
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
first shared-listener/session-pump experiment must determine whether removing
serial per-pair traversal removes that timeout/ACK pressure before any
congestion or capacity policy is retuned.
