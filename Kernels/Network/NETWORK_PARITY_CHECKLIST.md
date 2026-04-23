# Network Parity Checklist

## Purpose

This document tracks what "old network parity" means for the rebuilt stack.

It is a comparison aid and planning guardrail, not a separate rewrite branch.

The goal is not to stop the clean-slate implementation and chase the old stack feature-by-feature.
The goal is to keep building the new stack in the intended architecture, while making sure we know when the new runtime is broad enough for a fair old-vs-new comparison.

## Current Position

The new stack is already past scaffold stage:

- engine-agnostic network kernel exists
- real UDP transport exists
- control-plane handshake/session path exists
- movement input and owner-state round-trip exist
- AOI-backed observer latest-state publication exists
- explicit ordered-event acknowledgement exists
- standalone multi-client ramp harness exists
- widened observer-replication harness exists
- ring-aware AOI latest-state cadence exists
- widened `Realtime + ReliableEvent + Bulk` observer paths exist
- widened `0.40` multi-ring profile now has a passing `64`-client `tick_p95 < 4 ms` run

But the new stack is not yet at old-network parity.

Right now the new harness and the old baseline are still measuring different runtime shapes.

## What Old-Parity Means

At minimum, a fair parity comparison should include:

- all gameplay AOI rings active in the live harness/runtime path
- realistic active movement participation rates:
  - `0.35`
  - `0.40`
- owner movement/correction path
- observer latest-state replication
- at least one real reliable ordered gameplay/event family
- realistic mixed session/control traffic
- dense hotspot profile with a ring-0 crowd case
- benchmark outputs that expose:
  - `tick_avg`
  - `tick_p95`
  - `tick_p99`
  - `tick_max`
  - `ping_avg`
  - `ping_p95`
  - `ping_max`
  - flush/build cost breakdowns comparable to the old reports

## Old Baseline Targets Worth Reproducing

These are the old-style comparison targets, not current claims about the new stack:

- mixed/final-style profile around `~1024 CCU`
- `tick` budget around `~4 ms`
- `ping_p95 <= 50 ms`
- all AOI rings active
- movement participation around `35-40%`

And the dense hotspot stress profile:

- ring-0 crowd around `~300`
- all AOI rings still technically active
- movement participation around `35-40%`
- hotspot failure shape around `~14 ms` tick cost in the old stack

## Major Parity Gaps Today

The most important missing pieces are:

1. Full multi-ring AOI in the live standalone harness by default or by explicit profile
2. Regular use of `0.35` / `0.40` active movement profiles in ramp runs
3. Broader replication families beyond the current first observer-state slice
4. Mature reliable ordered delivery semantics as a real runtime path
Current state:
- highest-contiguous ordered-event ack now exists
- client-side contiguous ordered-event apply now exists
- bounded replay window now exists
- retransmit planning policy now exists
- retransmit execution bridge now exists at the kernel level
- retransmit / resend is not yet wired as a live server runtime loop
5. Snapshot / baseline / recovery path as a real runtime path
Current state:
- full baseline begin/frame/complete packet contracts now exist
- client-side full baseline apply/reset seam now exists
- baseline completion ack now exists
- recovery planning now exists at the kernel level
- full-baseline recovery execution now exists at the kernel bridge level
- send-based per-connection latest-state tracking now exists as the first delta foundation
- incremental delta/baseline advancement does not exist yet
6. Dense-area degradation policy broad enough to compare with the old hotspot behavior
7. Harness/runtime metrics for build/flush/send slices comparable to the old telemetry tables

Additional important gap:

8. Delta/baseline packet flow instead of the current mostly full-state observer payload path

## Important Non-Goal

Do not distort the new design just to mimic the old stack.

Specifically:

- do not reintroduce replay-window-driven late fanout
- do not re-entangle transport, protocol, and replication ownership
- do not force parallel build execution before the runtime semantics are ready for it

If the new architecture reaches the same feature breadth and the numbers differ, that is a valid result.

Parity means comparable scenario breadth and correctness, not architectural imitation.

## Recommended Use

Use this checklist when:

- deciding whether a new benchmark result is directly comparable to the old tables
- deciding whether a missing feature should be implemented now or later
- deciding when to run a true old-vs-new comparison pass

Do not use it as the main day-to-day implementation order.

The day-to-day implementation order should continue to follow:

- real features first
- tests with each slice
- benchmarks with each slice
- broader vertical slices before speculative optimization

## Current Read

The new stack is now broad enough for:

- honest movement-only ceiling measurements
- honest first observer-replication ceiling measurements
- honest client/server socket-backed ramp validation
- first broadened three-lane, three-ring comparisons that are starting to look structurally comparable to old-style load shapes

It is not yet broad enough for:

- direct old "final table" parity claims
- direct old dense-hotspot parity claims
- declaring old test-suite equivalence

That comparison should happen later, once the feature surfaces naturally converge.
