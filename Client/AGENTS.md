# Client AGENTS

## Purpose

The client subsystem owns the local runtime and presentation-facing network bridge.

It should preserve the useful ideas from the old client networking stack:

- input sequencing
- prediction bookkeeping
- authoritative acknowledgment
- interpolation buffers
- thin host wrappers

without rebuilding the old Godot-heavy ownership shape.

## Boundaries

Keep this split:

- `Client/Core`
  - client runtime state
  - input samples/sequencing
  - prediction state
  - interpolation state
- `Client/Network`
  - client-side bridge over `Kernels/Network`
  - packet decode/encode
  - transport/session-facing client flow
- `Client/_Godot`
  - wrapper only
  - input collection, rendering, UI

Do not:

- put protocol/runtime logic in `Client/_Godot`
- reference Godot from `Client/Core` or `Client/Network`
- reference server host/runtime code from client runtime layers

## Implementation Rules

- Use the old client network code as behavioral reference, not as the architecture to port verbatim.
- Prefer small explicit types over a giant all-in-one client bridge.
- Keep the first vertical slice narrow:
  - movement input
  - authoritative movement state
  - prediction/interpolation seams
- Add tests with each slice.
- Benchmark hot client/network paths early once they exist.

## Current Direction

The first client rebuild slice should prove:

1. client can queue and send movement input
2. client can receive authoritative movement state
3. prediction acknowledgements work
4. interpolation storage works
5. all of that is usable without Godot
