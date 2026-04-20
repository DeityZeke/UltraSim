# ECS Benchmark Summary

Generated: 2026-04-20T01:31:06.6565004+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.117 | 0.301 | 116.6 | 300.6 | new | 8448 |
| CreateDefaultDeferred_Position | 10000 | 0.671 | 0.865 | 67.1 | 86.5 | new | -554672 |
| CreateDefaultDeferred_Position | 50000 | 3.773 | 2.637 | 75.5 | 52.7 | old | -1853165 |
| DeferredAdd_Velocity | 1000 | 0.392 | 0.424 | 392.1 | 424.4 | new | -22720 |
| DeferredAdd_Velocity | 10000 | 3.122 | 3.253 | 312.2 | 325.3 | new | 107408 |
| DeferredAdd_Velocity | 50000 | 5.883 | 7.531 | 117.7 | 150.6 | new | 80181 |
| DeferredDestroy | 1000 | 0.276 | 0.215 | 276.3 | 214.7 | old | -11976 |
| DeferredDestroy | 10000 | 1.311 | 1.396 | 131.1 | 139.6 | new | 472448 |
| DeferredDestroy | 50000 | 4.872 | 4.568 | 97.4 | 91.4 | old | 1623789 |
| DeferredRemove_Velocity | 1000 | 0.625 | 0.421 | 625.2 | 421.3 | old | -22904 |
| DeferredRemove_Velocity | 10000 | 3.869 | 3.143 | 386.9 | 314.3 | old | 490032 |
| DeferredRemove_Velocity | 50000 | 6.328 | 7.836 | 126.6 | 156.7 | new | 1469096 |
| DirtyDrain_Position | 1000 | 0.295 | 0.241 | 294.9 | 241.2 | old | -45560 |
| DirtyDrain_Position | 10000 | 1.148 | 0.859 | 114.8 | 85.9 | old | 49008 |
| DirtyDrain_Position | 50000 | 3.965 | 5.438 | 79.3 | 108.8 | new | -226136 |
| HotLoop_DeferredAdd_Velocity | 50000 | 2.743 | 3.642 | 54.9 | 72.8 | new | 305232 |
| HotLoop_DirtyDrain_Position | 50000 | 0.853 | 0.853 | 17.1 | 17.1 | new | -1344 |
| HotLoop_Query1_Read_Position | 50000 | 0.229 | 0.203 | 4.6 | 4.1 | old | -1344 |
| HotLoop_SetComponent_Position | 50000 | 0.694 | 0.756 | 13.9 | 15.1 | new | -1344 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 79.237 | 194.323 | 9.9 | 24.3 | new | -438008 |
| HotLoop_TickParallel_QuerySystems | 10000 | 80.518 | 43.691 | 10.1 | 5.5 | old | -25344 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 46.236 | 46.937 | 5.8 | 5.9 | new | 256 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 38.169 | 72.919 | 4.8 | 9.1 | new | -39744 |
| HotLoop_TickSerial_QuerySystems | 10000 | 37.153 | 17.660 | 4.6 | 2.2 | old | -26944 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 16.445 | 22.543 | 2.1 | 2.8 | new | -1344 |
| HotLoop_TryGetComponent_Position | 50000 | 0.226 | 0.576 | 4.5 | 11.5 | new | -1344 |
| PersistenceView_Filtered_Position | 1000 | 0.399 | 0.315 | 399.3 | 314.5 | old | -35320 |
| PersistenceView_Filtered_Position | 10000 | 1.856 | 1.992 | 185.6 | 199.2 | new | 309736 |
| PersistenceView_Filtered_Position | 50000 | 5.971 | 8.469 | 119.4 | 169.4 | new | 695541 |
| Query1_Read_Position | 1000 | 0.212 | 0.202 | 212.0 | 202.4 | old | -44616 |
| Query1_Read_Position | 10000 | 2.126 | 1.675 | 212.6 | 167.5 | old | 48944 |
| Query1_Read_Position | 50000 | 4.425 | 4.350 | 88.5 | 87.0 | old | -226152 |
| Query2_Read_PositionVelocity | 1000 | 0.311 | 0.278 | 310.6 | 278.3 | old | -36640 |
| Query2_Read_PositionVelocity | 10000 | 3.428 | 2.730 | 342.8 | 273.0 | old | 229432 |
| Query2_Read_PositionVelocity | 50000 | 6.385 | 7.294 | 127.7 | 145.9 | new | 422112 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.439 | 0.323 | 439.3 | 323.4 | old | -32600 |
| Query3_Read_PositionVelocityHealth | 10000 | 4.531 | 3.920 | 453.1 | 392.0 | old | 320448 |
| Query3_Read_PositionVelocityHealth | 50000 | 9.165 | 11.690 | 183.3 | 233.8 | new | 746416 |
| SetComponent_Position | 1000 | 0.313 | 1.485 | 312.8 | 1485.4 | new | -44552 |
| SetComponent_Position | 10000 | 2.619 | 2.440 | 261.9 | 244.0 | old | 49008 |
| SetComponent_Position | 50000 | 5.429 | 5.340 | 108.6 | 106.8 | old | -226078 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 82.607 | 197.178 | 10.3 | 24.6 | new | -3009800 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 83.005 | 46.254 | 10.4 | 5.8 | old | 4200620 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 47.200 | 51.310 | 5.9 | 6.4 | new | 226429 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 39.439 | 76.767 | 4.9 | 9.6 | new | 193680 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 47.787 | 29.256 | 6.0 | 3.7 | old | 206480 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 30.548 | 36.229 | 3.8 | 4.5 | new | 231896 |
| TryGetComponent_Position | 1000 | 0.343 | 0.383 | 343.5 | 383.0 | new | -44552 |
| TryGetComponent_Position | 10000 | 2.271 | 1.875 | 227.1 | 187.5 | old | 49008 |
| TryGetComponent_Position | 50000 | 4.800 | 4.854 | 96.0 | 97.1 | new | -226088 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3536`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.129 ms`, alloc `104912`
- `PatchComponent_Position` scale `1000` mean `0.299 ms`, alloc `158000`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.556 ms`, alloc `2563184`
- `PatchComponent_Position` scale `10000` mean `1.148 ms`, alloc `3743504`
- `CreateDefaultImmediate_Position` scale `50000` mean `1.671 ms`, alloc `10428392`
- `PatchComponent_Position` scale `50000` mean `4.019 ms`, alloc `15394632`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.009 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.202 ms`, alloc `203560`
- `GetComponentRef_Write_Position` scale `10000` mean `0.772 ms`, alloc `3694496`
- `GetComponentRef_Write_Position` scale `50000` mean `4.650 ms`, alloc `15620768`
