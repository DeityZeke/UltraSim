# ECS Benchmark Summary

Generated: 2026-04-20T01:22:44.7284064+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.116 | 0.301 | 116.1 | 300.6 | new | 8448 |
| CreateDefaultDeferred_Position | 10000 | 0.619 | 0.865 | 61.9 | 86.5 | new | -554672 |
| CreateDefaultDeferred_Position | 50000 | 3.777 | 2.637 | 75.5 | 52.7 | old | -1853165 |
| DeferredAdd_Velocity | 1000 | 0.389 | 0.424 | 389.2 | 424.4 | new | -22720 |
| DeferredAdd_Velocity | 10000 | 3.079 | 3.253 | 307.9 | 325.3 | new | 107408 |
| DeferredAdd_Velocity | 50000 | 5.820 | 7.531 | 116.4 | 150.6 | new | 80176 |
| DeferredDestroy | 1000 | 0.266 | 0.215 | 266.4 | 214.7 | old | -11976 |
| DeferredDestroy | 10000 | 2.513 | 1.396 | 251.3 | 139.6 | old | 472448 |
| DeferredDestroy | 50000 | 3.542 | 4.568 | 70.8 | 91.4 | new | 1623789 |
| DeferredRemove_Velocity | 1000 | 0.618 | 0.421 | 618.2 | 421.3 | old | -22904 |
| DeferredRemove_Velocity | 10000 | 3.788 | 3.143 | 378.8 | 314.3 | old | 490032 |
| DeferredRemove_Velocity | 50000 | 6.282 | 7.836 | 125.6 | 156.7 | new | 1469096 |
| DirtyDrain_Position | 1000 | 0.296 | 0.241 | 295.8 | 241.2 | old | -45560 |
| DirtyDrain_Position | 10000 | 2.116 | 0.859 | 211.6 | 85.9 | old | 49008 |
| DirtyDrain_Position | 50000 | 3.746 | 5.438 | 74.9 | 108.8 | new | -226136 |
| HotLoop_DeferredAdd_Velocity | 50000 | 2.865 | 3.642 | 57.3 | 72.8 | new | 305232 |
| HotLoop_DirtyDrain_Position | 50000 | 0.851 | 0.853 | 17.0 | 17.1 | new | -1344 |
| HotLoop_Query1_Read_Position | 50000 | 0.242 | 0.203 | 4.8 | 4.1 | old | -1344 |
| HotLoop_SetComponent_Position | 50000 | 0.714 | 0.756 | 14.3 | 15.1 | new | -1344 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 78.526 | 194.323 | 9.8 | 24.3 | new | -438008 |
| HotLoop_TickParallel_QuerySystems | 10000 | 78.915 | 43.691 | 9.9 | 5.5 | old | -25344 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 44.969 | 46.937 | 5.6 | 5.9 | new | 256 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 36.610 | 72.919 | 4.6 | 9.1 | new | -39744 |
| HotLoop_TickSerial_QuerySystems | 10000 | 36.207 | 17.660 | 4.5 | 2.2 | old | -26944 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 18.879 | 22.543 | 2.4 | 2.8 | new | -1344 |
| HotLoop_TryGetComponent_Position | 50000 | 0.229 | 0.576 | 4.6 | 11.5 | new | -1344 |
| PersistenceView_Filtered_Position | 1000 | 0.401 | 0.315 | 400.5 | 314.5 | old | -35320 |
| PersistenceView_Filtered_Position | 10000 | 2.336 | 1.992 | 233.6 | 199.2 | old | 309736 |
| PersistenceView_Filtered_Position | 50000 | 6.249 | 8.469 | 125.0 | 169.4 | new | 695536 |
| Query1_Read_Position | 1000 | 0.220 | 0.202 | 219.9 | 202.4 | old | -44624 |
| Query1_Read_Position | 10000 | 2.166 | 1.675 | 216.6 | 167.5 | old | 48936 |
| Query1_Read_Position | 50000 | 4.344 | 4.350 | 86.9 | 87.0 | new | -226160 |
| Query2_Read_PositionVelocity | 1000 | 0.319 | 0.278 | 318.8 | 278.3 | old | -36648 |
| Query2_Read_PositionVelocity | 10000 | 3.433 | 2.730 | 343.3 | 273.0 | old | 229424 |
| Query2_Read_PositionVelocity | 50000 | 7.907 | 7.294 | 158.1 | 145.9 | old | 422104 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.421 | 0.323 | 421.5 | 323.4 | old | -32616 |
| Query3_Read_PositionVelocityHealth | 10000 | 4.455 | 3.920 | 445.5 | 392.0 | old | 320432 |
| Query3_Read_PositionVelocityHealth | 50000 | 10.584 | 11.690 | 211.7 | 233.8 | new | 746400 |
| SetComponent_Position | 1000 | 0.333 | 1.485 | 332.6 | 1485.4 | new | -44552 |
| SetComponent_Position | 10000 | 2.653 | 2.440 | 265.3 | 244.0 | old | 49008 |
| SetComponent_Position | 50000 | 5.292 | 5.340 | 105.8 | 106.8 | new | -226083 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 82.125 | 197.178 | 10.3 | 24.6 | new | -2610055 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 85.093 | 46.254 | 10.6 | 5.8 | old | 4600380 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 47.962 | 51.310 | 6.0 | 6.4 | new | 227635 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 37.965 | 76.767 | 4.7 | 9.6 | new | 193560 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 47.393 | 29.256 | 5.9 | 3.7 | old | 206360 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 28.710 | 36.229 | 3.6 | 4.5 | new | 231888 |
| TryGetComponent_Position | 1000 | 0.360 | 0.383 | 359.5 | 383.0 | new | -44552 |
| TryGetComponent_Position | 10000 | 2.228 | 1.875 | 222.8 | 187.5 | old | 49008 |
| TryGetComponent_Position | 50000 | 4.853 | 4.854 | 97.1 | 97.1 | new | -226093 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3536`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.124 ms`, alloc `104912`
- `PatchComponent_Position` scale `1000` mean `0.292 ms`, alloc `158000`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.609 ms`, alloc `2563184`
- `PatchComponent_Position` scale `10000` mean `1.127 ms`, alloc `3743504`
- `CreateDefaultImmediate_Position` scale `50000` mean `1.597 ms`, alloc `10428392`
- `PatchComponent_Position` scale `50000` mean `3.854 ms`, alloc `15394632`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.009 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.202 ms`, alloc `203560`
- `GetComponentRef_Write_Position` scale `10000` mean `0.772 ms`, alloc `3694496`
- `GetComponentRef_Write_Position` scale `50000` mean `4.650 ms`, alloc `15620768`
