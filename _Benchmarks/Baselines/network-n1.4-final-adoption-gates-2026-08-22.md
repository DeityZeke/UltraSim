# N1.4 final adoption gates — 2026-08-22

This additive fixed-runtime evidence closes representative topology and mixed-lane
measurement, then exercises the intended real shared-socket topology as far as the
accepted in-process host permits. It does not claim a dedicated server, sharded
clients, public listener, or production CCU.

| Workload | Result | Scope |
| --- | ---: | --- |
| Dense-400 semantic total | 1.270 ms p95 | 400 recipients, 1,000 public entities, 2% dirty |
| Representative roaming | 5.975 ms p95 | 152 boundaries, 116 corners, independent regions/headings |
| Staggered fast travel | 7.496 ms p95 | 400 teleports plus ordinary roaming; max 3,566 changes/step |
| Synchronized cardinal | 25.169 ms p95 | Deliberately hostile control |
| Synchronized diagonal | 33.024 ms p95 | Deliberately hostile control |
| Mixed semantic replay | 3.364 ms p95 | 64 recipients; public, owner, Event, action, and proofs |
| Real shared UDP replay | 1.578 ms p95 | 64 connected clients, one native-batched server listener |
| Dense town recovery | 23.664 ms total | 30 chunks x 64 rows, one private snapshot candidate |

The representative roaming run records 33,303 exact interest queries and 14,697
stable-budget fast paths. Reconciliation remains the dominant topology cost, but
the measured p95 is narrowly within the existing 4-6 ms usability range.

Mixed replay applies 3,840 public deltas, owner states, Events, actions, and Event
proofs with zero disclosure violations, no lane starvation beyond three pump
rounds, and zero warmed allocation. Protocol work is 2.848 ms p95 of the 3.364 ms
total.

The real-socket fixture reports 11,596,779 known server bytes, 8,492,288 known
client bytes, 8,388,608 configured listener socket-buffer bytes, and 8,388,608
configured aggregate client socket-buffer bytes. Native send and receive batching
are active. It still owns both server and clients in one process.

The current shared host intentionally has no externally admitted client endpoint.
A genuine dedicated-server/sharded-client run therefore requires the separately
deferred hosted-listener identity, unauthenticated-admission, amplification,
rate-limit, path, and cross-owner packet-memory contracts. The profile decision is
favorable through the current boundary but remains conditional on that explicit
gate. N1.5, N0 deletion, and 2K/4K were not started.
