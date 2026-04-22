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

Current benchmark note:

- the first client loopback movement scenario now exists in `_Benchmarks/results/network-benchmarks.json`
- current snapshot:
  - `ClientMovementLoopback_1024Inputs`: `0.949 ms`, `1.22 MB`

The old client networking code remains the behavioral reference for these concepts, but not the architecture template.
