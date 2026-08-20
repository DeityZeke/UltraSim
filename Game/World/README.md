# Game world

Terrain T0.0 establishes the game-owned integer position/profile layer over the
dependency-neutral [Spatial contracts](../../Kernels/Spatial/README.md).

The frozen first profile is:

```text
authoritative position quantum      1 mm
world chunk edge               16,000 mm
terrain cell edge                 500 mm
terrain cells/chunk edge           32
placement snap policy             100 mm
```

`WorldPosition` is a 16-byte game value: signed 32-bit chunk X/Z, unsigned
canonical local X/Z in `0..15,999`, and signed 32-bit Y millimeters. It is not an
ECS component. T0.1 will introduce the dedicated persisted component wrapper and
the maintained `WorldSpatialIndex`; those are intentionally absent at the T0.0
stop gate.

Exact millimeter input normalizes with mathematical floor division. Floating
import rejects nonfinite/out-of-profile values and rounds to nearest millimeter,
midpoint away from zero. Terrain-cell and placement granularity never constrain
dynamic positions.
