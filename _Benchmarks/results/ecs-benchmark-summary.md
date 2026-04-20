# ECS Benchmark Summary

Generated: 2026-04-20T01:04:01.3235677+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.137 | 0.301 | 137.2 | 300.6 | new | 4576 |
| CreateDefaultDeferred_Position | 10000 | 0.628 | 0.865 | 62.8 | 86.5 | new | -558544 |
| CreateDefaultDeferred_Position | 50000 | 3.837 | 2.637 | 76.7 | 52.7 | old | -1857032 |
| DeferredAdd_Velocity | 1000 | 0.402 | 0.424 | 402.3 | 424.4 | new | -30752 |
| DeferredAdd_Velocity | 10000 | 3.136 | 3.253 | 313.6 | 325.3 | new | 99376 |
| DeferredAdd_Velocity | 50000 | 6.165 | 7.531 | 123.3 | 150.6 | new | 72144 |
| DeferredDestroy | 1000 | 0.285 | 0.215 | 285.1 | 214.7 | old | -20024 |
| DeferredDestroy | 10000 | 1.279 | 1.396 | 127.9 | 139.6 | new | 464400 |
| DeferredDestroy | 50000 | 4.241 | 4.568 | 84.8 | 91.4 | new | 1615741 |
| DeferredRemove_Velocity | 1000 | 0.652 | 0.421 | 652.0 | 421.3 | old | -30936 |
| DeferredRemove_Velocity | 10000 | 4.222 | 3.143 | 422.2 | 314.3 | old | 482000 |
| DeferredRemove_Velocity | 50000 | 6.095 | 7.836 | 121.9 | 156.7 | new | 1461064 |
| DirtyDrain_Position | 1000 | 0.326 | 0.241 | 325.8 | 241.2 | old | -53608 |
| DirtyDrain_Position | 10000 | 1.088 | 0.859 | 108.8 | 85.9 | old | 40960 |
| DirtyDrain_Position | 50000 | 3.804 | 5.438 | 76.1 | 108.8 | new | -234184 |
| HotLoop_DeferredAdd_Velocity | 50000 | 2.949 | 3.642 | 59.0 | 72.8 | new | 305248 |
| HotLoop_DirtyDrain_Position | 50000 | 0.855 | 0.853 | 17.1 | 17.1 | old | -1344 |
| HotLoop_Query1_Read_Position | 50000 | 0.239 | 0.203 | 4.8 | 4.1 | old | -1344 |
| HotLoop_SetComponent_Position | 50000 | 0.718 | 0.756 | 14.4 | 15.1 | new | -1344 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 82.760 | 194.323 | 10.3 | 24.3 | new | -438008 |
| HotLoop_TickParallel_QuerySystems | 10000 | 85.806 | 43.691 | 10.7 | 5.5 | old | -25344 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 48.636 | 46.937 | 6.1 | 5.9 | old | 256 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 36.054 | 72.919 | 4.5 | 9.1 | new | -39744 |
| HotLoop_TickSerial_QuerySystems | 10000 | 37.142 | 17.660 | 4.6 | 2.2 | old | -26944 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 18.147 | 22.543 | 2.3 | 2.8 | new | -1344 |
| HotLoop_TryGetComponent_Position | 50000 | 0.226 | 0.576 | 4.5 | 11.5 | new | -1344 |
| PersistenceView_Filtered_Position | 1000 | 0.423 | 0.315 | 423.2 | 314.5 | old | -43368 |
| PersistenceView_Filtered_Position | 10000 | 2.059 | 1.992 | 205.9 | 199.2 | old | 301688 |
| PersistenceView_Filtered_Position | 50000 | 5.961 | 8.469 | 119.2 | 169.4 | new | 687493 |
| Query1_Read_Position | 1000 | 0.218 | 0.202 | 218.4 | 202.4 | old | -52600 |
| Query1_Read_Position | 10000 | 2.312 | 1.675 | 231.2 | 167.5 | old | 40960 |
| Query1_Read_Position | 50000 | 4.418 | 4.350 | 88.4 | 87.0 | old | -234136 |
| Query2_Read_PositionVelocity | 1000 | 0.328 | 0.278 | 328.4 | 278.3 | old | -44632 |
| Query2_Read_PositionVelocity | 10000 | 3.622 | 2.730 | 362.2 | 273.0 | old | 221440 |
| Query2_Read_PositionVelocity | 50000 | 6.215 | 7.294 | 124.3 | 145.9 | new | 414120 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.445 | 0.323 | 444.8 | 323.4 | old | -40600 |
| Query3_Read_PositionVelocityHealth | 10000 | 4.827 | 3.920 | 482.7 | 392.0 | old | 312448 |
| Query3_Read_PositionVelocityHealth | 50000 | 9.776 | 11.690 | 195.5 | 233.8 | new | 738420 |
| SetComponent_Position | 1000 | 0.329 | 1.485 | 328.5 | 1485.4 | new | -52600 |
| SetComponent_Position | 10000 | 2.872 | 2.440 | 287.2 | 244.0 | old | 40960 |
| SetComponent_Position | 50000 | 5.252 | 5.340 | 105.0 | 106.8 | new | -234126 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 87.260 | 197.178 | 10.9 | 24.6 | new | -1452944 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 86.366 | 46.254 | 10.8 | 5.8 | old | 5757484 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 50.370 | 51.310 | 6.3 | 6.4 | new | 185065 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 40.644 | 76.767 | 5.1 | 9.6 | new | 185504 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 44.949 | 29.256 | 5.6 | 3.7 | old | 198304 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 28.373 | 36.229 | 3.5 | 4.5 | new | 223904 |
| TryGetComponent_Position | 1000 | 0.390 | 0.383 | 390.4 | 383.0 | old | -52600 |
| TryGetComponent_Position | 10000 | 2.347 | 1.875 | 234.7 | 187.5 | old | 40960 |
| TryGetComponent_Position | 50000 | 4.664 | 4.854 | 93.3 | 97.1 | new | -234136 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.010 ms`, alloc `3504`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.132 ms`, alloc `101040`
- `PatchComponent_Position` scale `1000` mean `0.317 ms`, alloc `149952`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.528 ms`, alloc `2559312`
- `PatchComponent_Position` scale `10000` mean `1.256 ms`, alloc `3735456`
- `CreateDefaultImmediate_Position` scale `50000` mean `1.630 ms`, alloc `10424520`
- `PatchComponent_Position` scale `50000` mean `3.925 ms`, alloc `15386584`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.009 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.202 ms`, alloc `203560`
- `GetComponentRef_Write_Position` scale `10000` mean `0.772 ms`, alloc `3694496`
- `GetComponentRef_Write_Position` scale `50000` mean `4.650 ms`, alloc `15620768`
