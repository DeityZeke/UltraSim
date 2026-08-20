# Linux Requirements

This document covers what needs to change to build, run, and load-test the server on Linux.

## Runtime Status

All runtime C# code is already Linux-compatible. No changes required to:
- `UltraSim/` — ECS core, Network layer, Server gameplay, Client runtime
- `TcpFramedNetworkTransport` — uses standard `System.Net.Sockets`
- `Server.Godot/ServerRuntimeHostNode.cs` — Godot integration
- `Client.Godot/` — Godot client

Godot 4.x has first-class Linux support with headless server export.

## Required Changes

### 1. Build Guard Script (blocks `dotnet build`)

**File:** `GodotECS.csproj` line 21, `tools/validate_ultrasim_agnostic.ps1`

The build validation step calls `powershell`, which does not exist on Linux.

**Options (pick one):**
- Change `powershell` → `pwsh` and require PowerShell Core (`apt install powershell`)
- Rewrite `validate_ultrasim_agnostic.ps1` as a shell script and update the `.csproj` `Exec` command
- Make the step OS-conditional in `.csproj`:
  ```xml
  <Exec Command="powershell ..." Condition="'$(OS)'=='Windows_NT'" />
  <Exec Command="bash tools/validate_ultrasim_agnostic.sh ..." Condition="'$(OS)'!='Windows_NT'" />
  ```

### 2. Python Harness — Exe Detection

The following scripts hardcode `.exe` in their `detect_default_exe()` functions:
- `_tmp/run_island_socket_ramp_async.py` (lines 122–123)
- `_tmp/run_island_slo.py` (lines 27–28)
- `_tmp/run_island_bot_ramp.py` (lines 27–28)
- `_tmp/run_island_autoscale_bots.py` (lines 39–40)

Godot's Linux export produces `UltraSim.x86_64` (or no extension, depending on export settings). Each `detect_default_exe()` needs to check for the Linux binary name alongside the `.exe` candidates.

### 3. Python Harness — Hardcoded Launch Paths

These scripts hardcode `.output/UltraSim/UltraSim.console.exe`:
- `_tmp/run_branch_battery_compare.py` (line 322)
- `_tmp/run_branch_profile_suite.py` (line 166)

Update to detect or accept a Linux binary path.

## Already Handled

- **Python asyncio event loop**: `WindowsProactorEventLoopPolicy` is already OS-guarded (`if sys.platform.startswith("win")`). On Linux, asyncio uses the default selector/epoll loop automatically — which has lower syscall overhead than Windows IOCP and is strictly better for high connection count testing.
- **`process_metrics.py`**: Windows-specific `ctypes`/WinDLL fallback is already guarded by `os.name != "nt"`. Install `psutil` (`pip install psutil`) for full CPU/memory metrics on Linux.

## Server Export

Use Godot's export system to produce a Linux headless server binary:
1. Install Linux export templates in the Godot editor
2. Export with the "Headless" or "Server" preset
3. The binary will be named `UltraSim.x86_64` by default
4. Mark it executable: `chmod +x UltraSim.x86_64`
5. Pass it to the harness via `--launch-server-exe ./UltraSim.x86_64`

## Expected CCU Testing Benefit

Running the shared-socket harness on Linux eliminates the Windows IOCP overhead that causes harness-side loop lag at high CCU. The harness on Linux with `--workers 4` is expected to perform comparably to Windows with `--workers 8+`, making it a more trustworthy server-capacity measurement.
