# N1.4 remaining-adoption matrix — 2026-08-22

This fixed-runtime observation uses the accepted compact N1.4 recipient and dirty
scheduler. It separates server-semantic work from deterministic secure transport;
it is not a controlled full-stack N0/N1 ratio or a socket/CCU result.

| Workload | Result | Scope |
| --- | ---: | --- |
| Dense-400 semantic total | 1.272 ms p95 | 400 recipients, 1,000 public entities, 2% dirty, no protocol/client CPU |
| Dense-400 schedule/query | 1.248 ms p95 | 205,008 due publication queries over 120 measured steps |
| Distributed 100 mm movement | 6.553 ms p95 | 400 recipients; reconciliation plus scheduler topology mutation |
| Synchronized cardinal jump | 25.490 ms p95 | Deliberately hostile 400-recipient transition |
| Synchronized diagonal jump | 33.615 ms p95 | Deliberately hostile 400-recipient transition |
| Secure protocol replay | 2.386 ms p95 | 64 recipients; framing, security, packetization, client apply, proof; no sockets |
| Dense town recovery | 29.082 ms total | 30 chunks x 64 rows through one private snapshot candidate |

Every warmed timed region records zero caller-thread allocation. Dense semantic
storage is 59,097,920 bytes across scheduler, recipients, and publication. The
secure replay reserves 149,916,596 bytes across its real per-connection test
fixtures; this is bounded fixture reservation, not process RSS or a recommended
connection profile.

The dense ordinary path is encouraging. One snapshot candidate was sufficient for
the measured dense entry, so no transfer-concurrency mechanism was added. Topology
remains the adoption gate: sustained distributed movement is slightly outside the
existing 4-6 ms usability range and synchronized crossings are intentionally much
worse. Mixed-private integration, socket replay, and sharded end-to-end clients
remain required before selecting N1 over N0 for the current game. N1.5 and 2K/4K
were not started.
