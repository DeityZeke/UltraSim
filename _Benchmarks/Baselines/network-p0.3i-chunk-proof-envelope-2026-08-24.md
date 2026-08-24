# P0.3I bounded chunk application-proof envelopes

Date: 2026-08-24

Implementation: `41b403dc36c17c0dbab25ea3aaa5c54db9ca6221`

Before control: `b2c9081cbd511fe1ea0c055a0f0ef5e6b09f9eca`

## Result

P0.3I is accepted as a mechanical whole-flow improvement for both chunk-semantic
profiles. It changes neither chunk proof granularity nor recipient knowledge. A
single bounded Game message now carries several independently fenced chunk proof
entries for one application session and publication epoch.

The default 1,405-byte nonfragmenting application-message limit admits 40 entries:

```text
header       17 B
entry        34 B
40 entries  1,377 B
```

The client clamps its retained envelope scratch to the actual peer limit. Message
130, `ultrasim.game.chunk-applied-envelope`, is on independent-unreliable
`WorldState`. The canonical Game manifest is version 13, generated protocol version
11, with fingerprint `5993274912390600944`. The former one-proof message remains
decodable for compatibility; current chunk clients emit envelopes.

## Semantics retained

- `SENT != KNOWN`: each entry retains its exact delivery ID, chunk, kind, revision,
  application epoch, and publication epoch.
- The envelope is not an atomic semantic proof. Servers validate and apply each
  entry independently.
- A rejected, stale, duplicate, or unrelated entry cannot advance another entry and
  cannot prevent a valid sibling from being considered.
- Lost envelopes recover through the existing latest-state delivery retry. A client
  that receives repeated current truth regenerates the exact application proof.
- Source replacement, session replacement, subscription leave/re-entry, delivery
  timeout, and snapshot transaction semantics are unchanged.
- Encoding and hostile decoding are bounded and allocation-free after warmup. The
  decoder validates the complete envelope before writing caller-provided rows.

## Causal whole-flow A/B

Both sides use 30 warmup ticks, 90 measured ticks, deterministic workload/order,
real shared UDP sockets, canonical Game codecs, protocol selection/framing,
AES-GCM, native send/receive batching, client semantic application, reverse proof,
and server proof interpretation. Chunk profiles retain cap-eight unreliable-frame
aggregation. Protocol/listener stage probes are enabled in this table.

| Workload | Profile | Proof entries | Proof messages before | Proof messages after | Tick p95 before | Tick p95 after | Server p95 before | Server p95 after |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Dense-400 | ChunkPublication | 147,132 | 147,132 | 36,400 | 32.020 ms | 30.593 ms | 7.429 ms | 6.935 ms |
| Dense-400 | DirtyChunkSpatialFanout | 147,132 | 147,132 | 36,400 | 40.506 ms | 39.405 ms | 15.808 ms | 15.181 ms |
| Sparse-512 | ChunkPublication | 272,617 | 272,617 | 40,829 | 55.326 ms | 52.191 ms | 18.336 ms | 17.907 ms |
| Sparse-512 | DirtyChunkSpatialFanout | 363,961 | 363,961 | 46,889 | 68.663 ms | 65.315 ms | 29.674 ms | 28.756 ms |

Proof Game-message count falls 75.3% in both Dense-400 rows, 85.0% in sparse
ChunkPublication, and 87.1% in sparse DirtyChunk. The proof-entry count and semantic
digests are unchanged.

| Workload | Profile | Unreliable frames before | After | Data datagrams before | After | Game payload before | After | Wire before | After |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Dense-400 | ChunkPublication | 294,264 | 183,532 | 80,495 | 80,495 | 48.05 MiB | 46.39 MiB | 57.90 MiB | 53.81 MiB |
| Dense-400 | DirtyChunkSpatialFanout | 186,405 | 75,673 | 75,673 | 75,673 | 45.76 MiB | 44.10 MiB | 53.04 MiB | 48.95 MiB |
| Sparse-512 | ChunkPublication | 545,214 | 313,426 | 107,900 | 96,300 | 53.78 MiB | 50.28 MiB | 70.29 MiB | 61.22 MiB |
| Sparse-512 | DirtyChunkSpatialFanout | 419,493 | 102,421 | 120,029 | 102,421 | 64.99 MiB | 60.20 MiB | 79.25 MiB | 66.76 MiB |

The dense result is intentionally not overstated. Fewer reverse frames do not reduce
dense datagram count: cap-eight already packed the scalar proofs into the same number
of send opportunities, so average frames/datagram falls from 3.66 to 2.28 for
ChunkPublication and 2.46 to 1.00 for DirtyChunk. The dense CPU improvement is only
2.7-4.5%. Sparse proof pressure was high enough to remove 10.8-14.7% of Data
datagrams, producing the larger 4.9-5.7% tick improvement.

Client-side protocol selection time falls materially because the reverse ready set is
smaller: 11.987 to 6.594 ms in dense ChunkPublication, 12.245 to 6.899 ms in dense
DirtyChunk, 21.227 to 9.235 ms in sparse ChunkPublication, and 27.011 to 10.871 ms in
sparse DirtyChunk. Client decode/apply/proof-queue and server proof-apply p95 remain
approximately neutral; P0.3I removes message/transport multiplication rather than
semantic work.

## Probes-off observations

The same accepted implementation was rerun from the clean implementation SHA with
protocol/listener stage timing disabled:

| Workload | Profile | Tick p95 | Server-role p95 | Client-role p95 |
| --- | --- | ---: | ---: | ---: |
| Dense-400 | ChunkPublication | 29.194 ms | 5.200 ms | 16.927 ms |
| Dense-400 | DirtyChunkSpatialFanout | 38.575 ms | 13.578 ms | 17.105 ms |
| Sparse-512 | ChunkPublication | 50.789 ms | 15.154 ms | 26.789 ms |
| Sparse-512 | DirtyChunkSpatialFanout | 62.325 ms | 25.533 ms | 27.216 ms |
| Dense-400 | DirtyEntitySpatialFanout control | 8.213 ms | 2.144 ms | 0.791 ms |

These are localhost whole-flow observations, not a public-WAN or CCU rating. The
probes-off rows retain identical counters, bytes, datagrams, digests, and convergence
to the causal after rows.

## No-change entity control

Dense-400 DirtyEntitySpatialFanout remains exactly 36,722 forward Game messages,
36,722 proof messages, 73,444 frames/datagrams, 32,076,100 Game bytes, 36,996,848
wire bytes, and digest `B0FE2421B442C143`. Its causal tick/server p95 moved from
9.337/3.349 ms to 8.844/3.230 ms, which is treated as run variance rather than an
effect because no entity code, traffic, or work counter changed. No rated entity CCU
sweep was rerun.

## Correctness and regression gates

- 126 five-profile Game replication tests pass.
- 22 Client Core tests pass.
- 11 Network/Hosted tests pass.
- 69 Network application tests pass.
- 8 Network codec-generator tests pass.
- Benchmark build: zero warnings and zero errors.
- Permanent proof-envelope coverage includes exact MTU sizing, malformed count and
  length, invalid enum, insufficient output capacity, transactional decode, duplicate
  IDs, mixed applied/rejected/stale entries, replay, sibling independence, convergence,
  lost proof/retry, leave/re-entry, publication replacement, snapshot recovery, and
  zero warmed codec allocation.
- All five causal controls and all five probes-off controls converge with their prior
  semantic digest and zero capacity drops.

The whole-flow harness itself reports a few kilobytes of measurement-thread
allocation in both before and after captures; P0.3I does not claim that the harness is
allocation-free. The new codec has a permanent zero-warmed-allocation test and the
chunk client uses fixed retained proof, envelope, and byte scratch.

## Evidence files

| File | SHA-256 |
| --- | --- |
| `network-p0.3i-chunk-proof-envelope-before-chunk-2026-08-24.json` | `492fa3bb9a3e08e3aac7abeadf0e51d36d911c21dbeba002bd9b0184acd7ed5e` |
| `network-p0.3i-chunk-proof-envelope-before-entity-control-2026-08-24.json` | `2101699266a0831a01b8c99144d90cb21a5b1f21307f3b4b7a605cfce2c5caaf` |
| `network-p0.3i-chunk-proof-envelope-after-chunk-2026-08-24.json` | `a63796d9c01e2d2a26a81229962c7cbfdefa8f26baa8a88475d7343ef6c48795` |
| `network-p0.3i-chunk-proof-envelope-after-entity-control-2026-08-24.json` | `dbe37695e48a65ae28e6baa2afe5cec53956b4136ec960f38952483b2d5d30b3` |
| `network-p0.3i-chunk-proof-envelope-probes-off-chunk-2026-08-24.json` | `4e9c07ce4467629abcc3c989744c62a3a874e914d2473e0aad4a5187b7b5061e` |
| `network-p0.3i-chunk-proof-envelope-probes-off-entity-control-2026-08-24.json` | `bd77ae8145700ba575ac6983a244e30fe5b8d2299c3c4f931c34ff6a7c3ee2b8` |

## Checkpoint verdict

P0.3I closes the artificial reverse-proof message multiplication. Correct chunk
semantics remain materially more expensive than the dynamic entity profiles in these
workloads, especially DirtyChunk's dirty-time recipient discovery, but that conclusion
is no longer explained by one reverse Game message per chunk entry.

`ChunkPublication` remains the selected current-game public profile. No entity path,
socket batching policy, rated CCU result, or profile composition decision changes in
this checkpoint. The next architecture work may make the P0.4 semantic ownership /
composition decision from the now-normalized whole-flow evidence.
