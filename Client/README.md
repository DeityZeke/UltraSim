# Client

The client subsystem is being rebuilt with the same layering rules as the server:

- `Client/Core` for engine-agnostic client runtime state
- `Client/Network` for the client-side bridge over `Kernels/Network`
- `Client/_Godot` as the wrapper/front end

Current focus:

- first real movement vertical slice
- input sequencing
- prediction acknowledgement
- interpolation-ready authoritative state buffering

What now exists:

- `Client/Core`
  - input sequencing
  - prediction storage
  - interpolation buffering
  - minimal movement input/state contracts
- `Client/Network`
  - movement input send path over `Kernels/Network`
  - authoritative movement-state receive path
  - prediction acknowledgement hook
  - loopback-testable movement bridge
  - `ClientMovementSessionHost` for shared control-plane + movement polling over one live transport path
  - the first paired server/client movement round-trip slice now exists over loopback via `Server/Network`

Current benchmark note:

- the first client loopback movement scenario now exists in `_Benchmarks/results/network-benchmarks.json`
- current snapshot:
  - `ClientMovementLoopback_1024Inputs`: `0.913 ms`, `1.22 MB`
  - `ServerMovementLoopback_1024Inputs`: `2.176 ms`, `3.43 MB`
  - `UdpMovementRoundTrip_1024Inputs`: `15.431 ms`, `4.32 MB`
  - `UdpMovementMiniRamp_32Clients_240Ticks`: `108.954 ms`, `14.46 MB`

The current client/network checkpoint is now beyond narrow loopback only:

- real UDP socket transport exists
- the live session path keeps handshake and movement on one shared poll loop
- the first small multi-client UDP ramp signal exists without putting protocol/runtime logic into `Client/_Godot`

The old client networking code remains the behavioral reference for these concepts, but not the architecture template.
