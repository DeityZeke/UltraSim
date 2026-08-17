# Sparse 10 m confirmation and capacity evidence

- Workload: `sparse_observer_movement`
- Geometry confirmation: four fresh-process alternating 16→10 / 10→16 pairs at
  1,024 and 4,096 clients, probes off, unchanged legacy capacities
- Attribution confirmation: two alternating probe-bearing pairs
- Capacity confirmation: four fresh-process alternating legacy128/current10 pairs
  at both populations, fixed 10 m geometry
- Runtime: Release, tiered compilation and ReadyToRun disabled

## Decision

The 10 m direction survived every probes-off geometry pair. Mean integrated p95
fell 19.843→17.180 ms at 1,024 (-13.4%) and 105.157→84.693 ms at 4,096
(-19.5%). At 4,096 it also removed 131,796 maintained relations, 5.51 million
WorldState records, 212,153 bundles, and about 258 MiB of wire per run. Input ACK
p95 improved 344.81→278.84 ms. All runs converged with zero capacity drops and
zero nonviable recipients.

The independent deep rows agree: server-role p95 fell 11.660→9.947 ms at 1,024
and 60.287→49.552 ms at 4,096. These probe-bearing values explain the direction;
the probes-off rows decide acceptance.

The exact-tier × maintained-tier matrix shows that exact Near is always maintained
Near. The 10 m profile does not create a new urgency approximation: at 4,096 it
reduces exact-Far-as-Near relations from 96,980 to 81,084 and coarse-only Near/Far
relations from 198,556 to 66,760. Faster Far freshness is therefore the result of
lower admitted work, not a hidden tier-policy relaxation.

## Separate capacity experiment

For this benchmark profile, the 39.5 m Far radius uses a steady 9×9 envelope.
A diagonal one-cell crossing temporarily owns the prior 81 cells plus a 17-cell
entering row/column union: 98 cells. The accepted profile adds 14 explicit
transient slots, giving 112 relation/known/active slots, while a baseline captures
only current scope and receives 14 slots of headroom (95 records).

At 4,096, the measured relation-count high-water was 213,815 globally; the
largest client knowledge and active sets were 72 and 64. The derived profile had
zero drops/nonviable recipients and identical convergence. It lowers known
Network + Game/Replication reservation from 932.89 to 899.17 MiB (-33.72 MiB,
-3.6%). Its 87.642 ms mean p95 versus 86.793 ms for legacy128 is a small unpaired-
noise-sized regression, not a performance claim; the capacity checkpoint is
accepted for memory honesty.

These bounds belong only to the admitted sparse benchmark policy/density and its
bounded movement/recovery window. The legacy128 profile remains selectable, and
other region sizes automatically retain it. No general GameInterest default or
the reference 192 m Far policy changed.

## Protocol recensus

A fresh deep run under accepted 10 m/current10 reports 53.509 ms server-role p95:
36.703 ms protocol/listener, 10.660 ms interest update, 5.062 ms WorldState
scheduling, and 1.950 ms application ingress. These p95 values are attribution
signals and are not additive or directly comparable with probes-off acceptance.

The smaller honest workload emitted 1,715,155 Data packets and only 24,816 ACK
packets. Mean datagram fill was 23.29%; native listener sends averaged 61.57
datagrams/call. Unlike `owner_movement`, 570,187 of 1,761,380 unreliable-bearing
emit inspections (32.37%) found at least two compatible ready frames, with a
maximum of seven and 1,267,303 co-batchable frames observed. Protocol remains the
largest server phase, and this evidence earns a narrow no-wait unreliable-only
same-peer multi-frame assembly experiment. It does not earn scheduler work,
wider queues, workers, or a new AOI representation.

Machine-readable evidence: [JSON](network-v3-sparse-10m-confirmation-capacity-2026-08-17.json).
