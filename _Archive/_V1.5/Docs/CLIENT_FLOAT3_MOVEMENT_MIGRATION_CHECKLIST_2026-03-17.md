# Client Float3 Movement Migration Checklist (Temporary)

Purpose: temporary working checklist for replacing the current `EntityInt2` movement/correction path with float runtime movement and quantized centimeter wire movement.

Status: complete. All migration items done. This document is retained for historical reference.

---

## Locked Decisions

- Runtime movement state uses `float3`
- Immediate wire format uses:
  - quantized `int32` centimeters for position
  - `X/Z` only for now
  - explicit quantized yaw in corrections
- Immediate input wire format uses:
  - quantized `short` direction components
  - `X/Z` only for now
  - movement flags reserved for jump/sprint later
- Shared movement constants live in `UltraSim/Shared/...`
- Server simulation remains fixed-tick and authoritative
- Chunk-coordinate + local-offset wire addressing is deferred until terrain/chunk work becomes active

---

## Scope Split

### Do now

- float runtime movement state
- shared quantization helpers/constants
- shared input codec
- new float movement correction codecs
- server authoritative float movement apply path
- client prediction/reconciliation/interpolation cutover

### Defer

- `Y` on the wire
- jump/gravity wire input
- chunk-coordinate + local-offset serialization
- deeper server movement architecture cleanup outside the minimum migration

---

## Checklist

### 1) Shared constants and helpers

- [x] Add `UltraSim/Shared/Engines/Networking/MovementProtocolConstants.cs`
- [x] Add `UltraSim/Shared/Engines/Networking/MovementQuantization.cs`
- [ ] Define:
  - [x] `PositionQuantizationScale = 100f`
  - [x] input direction quantization scale
  - [x] `PlayerMoveSpeedMetersPerSecond`
  - [x] fixed server tick delta / tick rate
  - [x] position epsilon for duplicate-skip / float comparisons
  - [x] yaw epsilon

### 2) Shared input codec

- [x] Add `UltraSim/Shared/Engines/Networking/InputFloat3Codec.cs`
- [ ] Encode/decode:
  - [x] `short MoveX`
  - [x] `short MoveZ`
  - [x] movement flags

### 3) New runtime-side movement types

- [x] Add `UltraSim/Client/Engines/Networking/EntityFloat3Correction.cs`
- [x] Add `UltraSim/Client/Engines/Networking/EntityFloat3PredictedState.cs`
- [x] Add `UltraSim/Client/Engines/Networking/EntityFloat3InputCommand.cs`
- [x] Add `UltraSim/Client/Engines/Networking/EntityFloat3InboundMessage.cs`
- [x] Add `UltraSim/Client/Engines/Networking/EntityFloat3InterpolatedPose.cs`

### 4) New correction codecs

- [x] Add shared owner correction codec for quantized float movement
- [x] Add shared observer correction codec for quantized float movement
- [x] Add `UltraSim/Client/Engines/Networking/EntityFloat3CorrectionCodec.cs`
- [x] Add `UltraSim/Client/Engines/Networking/EntityFloat3InboundMessageCodec.cs`
- [x] Reuse existing chat/despawn/ping control codecs where possible

### 5) Codec tests first

- [x] Add quantize/dequantize roundtrip tests
- [x] Add input encode/decode tests
- [x] Add owner correction encode/decode tests
- [x] Add observer correction encode/decode tests
- [x] Add batch correction decode tests
- [x] Add yaw quantization roundtrip tests

### 6) Server-side movement migration

Primary temporary implementation target:

- `_tmp/ECSNetworkIslandSliceNode.cs`

Tasks:

- [x] Convert authoritative player movement state from integer grid position to float position
- [x] Update server input apply path to use decoded quantized direction and shared speed constants
- [x] Update authoritative yaw generation from movement input
- [x] Replace integer duplicate-skip/compare logic with epsilon-based checks where required
- [x] Move temporary server movement from packet-impulse apply to held-input-per-fixed-tick movement

### 7) Server correction emit migration

- [x] Replace active owner correction encode path with float movement correction codec
- [x] Replace active observer correction encode path with float movement correction codec
- [x] Keep existing batching / seq / tick / owner routing behavior

### 8) Client driver/runtime cutover

- [x] `Client.Godot/NetworkClientSessionDriver.cs` switches inbound pipeline to float movement message family
- [ ] `Client.Godot/GodotClientNetworkRuntime.cs` switches to:
  - [x] float movement corrections
  - [x] float predicted state
  - [x] float remote interpolation samples
  - [x] quantized short input send path
- [x] Remove local int-step emulation

### 9) Validation

- [x] Build succeeds
- [x] Codec/unit tests succeed
- [x] Local movement feels correct
- [x] Diagonal movement no longer shows periodic forward correction jumps
- [x] Remote movement is smoother than the retired `EntityInt2` path
- [x] Reconnect / ownership / camera binding remain correct
- [x] External multi-client validation pass on the Float movement path is complete

### 10) Cleanup after cutover

- [x] Remove active `EntityInt2` movement usage from current client runtime path
- [x] Remove active `EntityInt2` movement usage from current server runtime path
- [x] Remove retired `EntityInt2` movement-only code and codecs from the active codebase
- [ ] Fold this checklist into the long-term docs once stable

---

## Notes

- `Y` is intentionally runtime-only for this pass. Wire `Y` arrives with gravity/elevation work.
- `_tmp/ECSNetworkIslandSliceNode.cs` is a temporary bridge target; keep this migration minimal there.
- Do not over-optimize packet size before the new path is correct and validated.
- Control/chat/ping now use the generic `NetworkControlMessageCodec` and neutral control message types.
- Current active-path tuning values after the cutover:
  - `PlayerMoveSpeedMetersPerSecond = 3.5f`
  - temporary server correction deadband effectively disabled for Float movement validation:
    - `ServerCorrectionDeadbandUnits = 0`
    - `ServerCorrectionMaxSilentTicks = 1`
