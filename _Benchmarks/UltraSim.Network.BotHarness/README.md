# Network Bot Harness

This is the first standalone ramp harness for the rebuilt network stack.

It is intentionally narrow:

- real UDP sockets
- shared client/server session hosts
- handshake + movement on one transport poll path
- staged client-count ramps
- per-stage tick timing stats
- simple client pending-input backlog stats

It does not yet try to recreate the full old bot harness feature set:

- no TCP path
- no shared UDP mux
- no chat traffic
- no external server-process launch
- no persistence or replication breadth beyond the current movement slice

## Running

```powershell
dotnet build .\_Benchmarks\UltraSim.Network.BotHarness\UltraSim.Network.BotHarness.csproj -c Release
dotnet .\_Benchmarks\UltraSim.Network.BotHarness\bin\Release\net8.0\UltraSim.Network.BotHarness.dll --stages 32,64,128,256 --stage-duration 30 --settle-seconds 3 --tick-hz 62
```

Reports are written to `_Benchmarks/results/network_bot_harness_*.json`.

## Current Role

This tool exists to answer the question:

- does the real socket/session/movement path still look healthy once many clients are connected at once?

It is the intended checkpoint before a broader external-process or gameplay-richer harness.
