# W7-R1 — multi-Pack lifecycle correctness repair (2026-09-25)

The original W7 two-Pack acceptance remains valid for its tested journey, but did not establish
general multi-Pack admission or session-long traversal. ASTRA's review of starting HEAD
`a964148c61052823d6a544d7f4c20a575f9598a8` identified three reproducible failures.
The corrective implementation and test commit is
`518bf75cfeefec7933765d3ed0274452a288b757`. The evidence/docs commit is discoverable with
`git log -- _Benchmarks/Baselines/world-w7-r1-multipack-lifecycle-repair-2026-09-25.md`.
No W8 editing or WorldPack/manifest/wire-format change was made. The W7-R1 integrated client
correctness candidate is the tested `518bf75c` tree.

| Failure at W7 HEAD | Repair | Permanent control |
| --- | --- | --- |
| Request C reused B's `Applied` request slot while B remained prepared; B then could not retire. | Only rejected or fully retired requests are reusable; accepted grant/retirement obligations remain paired. | Third-Pack request after A+B acquisition, then B retirement and exact retirement message. |
| The 1,025th distinct interest retirement exhausted a lifetime dictionary at 25 concurrent residents. | Fence live semantic slot generations; use the monotonic per-recipient delivery cutoff only to initialize later residents. Snapshot existing resident floors before advancing it, so unrelated in-flight delivery IDs remain eligible. | 1,101 distinct authenticated retirement controls, at most two active resident fences, unrelated older delivery accepted, old reentry delivery rejected, newer reentry delivery accepted. |
| A seam-start anchor prepared only its own Pack and rolled it back because the initial window lacked its neighbor. | Request all manifest-known Packs in the starting 5×5 window; defer initial residency until they are prepared. | Real authenticated two-Pack seam start at chunk 127: two prepared Packs, 25 residents, window proof, active guest. |
| Additional corner defect exposed by the expanded control: valid-center binary search read a list sorted only within each Pack. | Sort the bounded complete valid-center index globally before lookup. | Four exact Pack grants and preparations around chunk (127,127), 25 residents, four Pack proofs and a window proof. |

The server assigns delivery identities monotonically per recipient and queues a reliable interest
retirement before later reentry can publish new identities for that chunk. The client keeps a
floor for each *currently live* semantic slot generation; old slot generations cannot authorize
new truth. Nonresident deliveries still take the explicit rejection/proof path. This removes the
session-lifetime exploration cap without pretending sent work is applied knowledge.

The four-Pack corner fixture preinstalls four independently verified passive packages in the
realm CAS, then drives the production Pack client through exact grants and acquisition. It
observed four queued requests, four prepared Packs, four Pack proofs, 25 resident semantic
chunks, one starting window proof, and zero network package bytes in that preinstalled control.
The separate two-Pack seam-start control uses authenticated UDP application/session plumbing.
The third-Pack control specifically proves that a new request does not steal an earlier Pack's
accepted grant obligation. The original Far Gate, seam, retirement, and warm-CAS controls remain
in the full suite. This is bounded assurance, not a claim that every possible authored geometry
has been exhaustively tested.

## Verification

Environment: Linux `7.2.6-1-cachyos` x86-64, AMD Ryzen 7 7800X3D (16 logical CPUs), .NET SDK
8.0.131/runtime 8.0.31, Godot 4.7.1 Mono; Release configuration. Reproduce with:

```bash
dotnet run --project _Tests/UltraSim.Game.Replication.Tests/UltraSim.Game.Replication.Tests.csproj -c Release
dotnet run --project _Tests/UltraSim.Network.Application.Tests/UltraSim.Network.Application.Tests.csproj -c Release -- --w7-r1-only
dotnet run --project _Tests/UltraSim.Network.Application.Tests/UltraSim.Network.Application.Tests.csproj -c Release
./verify.sh --configuration=Release --architecture-only
./verify.sh --configuration=Release
bash Client/Godot/export-client.sh Complete /tmp/<fresh-directory>/UltraSimComplete.x86_64
```

On tested implementation `518bf75c`, all 181 Game-replication tests passed, the full Network
application suite passed (including 15 W7 structural-delivery tests), architecture-only passed,
and one complete canonical Release/export run ended `PASS repository verification`. That run
included native two-Pack/World tests, the exported W7 Far Gate/seam/retirement journey,
Contentless Online cold/warm, bot impairment, all three edition exports, and mixed lifecycle.
During the Complete export, Godot reported a failed first managed publish; the repository's
existing incomplete-export detector retried it and the canonical gate verified and launched
the completed binary. A separate fresh-directory Complete export then succeeded on its first
attempt, with nonempty native executable and managed dependency manifest. No export-script
change was made.

Two earlier, non-final Network runs encountered respectively the pre-existing F1.8A
sparse-public timing assertion and an overstrict W7 zero-rejection assertion during an
in-flight semantic window crossing. The latter was revised to the existing bounded native
classification: at most four missing-resident/rejected tails, no contradictory delivery, and
rejection never exceeding the matching missing-resident count. The final full Network and
canonical runs passed. No public knowledge was falsely applied.

W8 atomic Add/Replace/Remove Pack composition, W9 editing, and W10 durable topology were not
started. W8 may proceed from the W7-R1 integrated client candidate.
