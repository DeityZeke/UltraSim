# ECS Benchmark Summary

Generated: 2026-04-20T00:38:45.7935825+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.140 | 0.301 | 140.3 | 300.6 | new | 4512 |
| CreateDefaultDeferred_Position | 10000 | 0.633 | 0.865 | 63.3 | 86.5 | new | -558944 |
| CreateDefaultDeferred_Position | 50000 | 3.802 | 2.637 | 76.0 | 52.7 | old | -1857768 |
| DeferredAdd_Velocity | 1000 | 0.499 | 0.424 | 498.8 | 424.4 | old | -30712 |
| DeferredAdd_Velocity | 10000 | 4.171 | 3.253 | 417.1 | 325.3 | old | 430672 |
| DeferredAdd_Velocity | 50000 | 10.768 | 7.531 | 215.4 | 150.6 | old | 1335733 |
| DeferredDestroy | 1000 | 0.286 | 0.215 | 286.1 | 214.7 | old | -20424 |
| DeferredDestroy | 10000 | 1.846 | 1.396 | 184.6 | 139.6 | old | 464000 |
| DeferredDestroy | 50000 | 6.017 | 4.568 | 120.3 | 91.4 | old | 1615005 |
| DeferredRemove_Velocity | 1000 | 0.557 | 0.421 | 557.2 | 421.3 | old | -30896 |
| DeferredRemove_Velocity | 10000 | 4.742 | 3.143 | 474.2 | 314.3 | old | 481704 |
| DeferredRemove_Velocity | 50000 | 10.021 | 7.836 | 200.4 | 156.7 | old | 1460840 |
| DirtyDrain_Position | 1000 | 0.295 | 0.241 | 295.4 | 241.2 | old | -54008 |
| DirtyDrain_Position | 10000 | 1.256 | 0.859 | 125.6 | 85.9 | old | 40560 |
| DirtyDrain_Position | 50000 | 5.861 | 5.438 | 117.2 | 108.8 | old | -234925 |
| HotLoop_DeferredAdd_Velocity | 50000 | 4.695 | 3.642 | 93.9 | 72.8 | old | 1569856 |
| HotLoop_DirtyDrain_Position | 50000 | 0.999 | 0.853 | 20.0 | 17.1 | old | -1680 |
| HotLoop_Query1_Read_Position | 50000 | 0.229 | 0.203 | 4.6 | 4.1 | old | -1680 |
| HotLoop_SetComponent_Position | 50000 | 0.823 | 0.756 | 16.5 | 15.1 | old | -1680 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 82.398 | 194.323 | 10.3 | 24.3 | new | -38480 |
| HotLoop_TickParallel_QuerySystems | 10000 | 84.270 | 43.691 | 10.5 | 5.5 | old | 374184 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 47.997 | 46.937 | 6.0 | 5.9 | old | -80 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 36.779 | 72.919 | 4.6 | 9.1 | new | -40080 |
| HotLoop_TickSerial_QuerySystems | 10000 | 36.388 | 17.660 | 4.5 | 2.2 | old | -26042 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 17.291 | 22.543 | 2.2 | 2.8 | new | -1680 |
| HotLoop_TryGetComponent_Position | 50000 | 0.229 | 0.576 | 4.6 | 11.5 | new | -1680 |
| PersistenceView_Filtered_Position | 1000 | 0.401 | 0.315 | 400.5 | 314.5 | old | -43768 |
| PersistenceView_Filtered_Position | 10000 | 2.305 | 1.992 | 230.5 | 199.2 | old | 300952 |
| PersistenceView_Filtered_Position | 50000 | 8.285 | 8.469 | 165.7 | 169.4 | new | 686877 |
| Query1_Read_Position | 1000 | 0.214 | 0.202 | 214.1 | 202.4 | old | -52664 |
| Query1_Read_Position | 10000 | 2.118 | 1.675 | 211.8 | 167.5 | old | 40560 |
| Query1_Read_Position | 50000 | 4.515 | 4.350 | 90.3 | 87.0 | old | -234872 |
| Query2_Read_PositionVelocity | 1000 | 0.320 | 0.278 | 319.7 | 278.3 | old | -44696 |
| Query2_Read_PositionVelocity | 10000 | 3.476 | 2.730 | 347.6 | 273.0 | old | 220704 |
| Query2_Read_PositionVelocity | 50000 | 8.289 | 7.294 | 165.8 | 145.9 | old | 413384 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.420 | 0.323 | 420.1 | 323.4 | old | -40664 |
| Query3_Read_PositionVelocityHealth | 10000 | 4.614 | 3.920 | 461.4 | 392.0 | old | 311712 |
| Query3_Read_PositionVelocityHealth | 50000 | 10.527 | 11.690 | 210.5 | 233.8 | new | 737680 |
| SetComponent_Position | 1000 | 0.333 | 1.485 | 333.3 | 1485.4 | new | -52664 |
| SetComponent_Position | 10000 | 2.680 | 2.440 | 268.0 | 244.0 | old | 40560 |
| SetComponent_Position | 50000 | 5.700 | 5.340 | 114.0 | 106.8 | old | -234862 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 87.076 | 197.178 | 10.9 | 24.6 | new | -1453344 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 87.612 | 46.254 | 11.0 | 5.8 | old | 5757084 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 48.988 | 51.310 | 6.1 | 6.4 | new | 183427 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 38.130 | 76.767 | 4.8 | 9.6 | new | 185104 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 49.414 | 29.256 | 6.2 | 3.7 | old | 197904 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 28.743 | 36.229 | 3.6 | 4.5 | new | 223504 |
| TryGetComponent_Position | 1000 | 0.380 | 0.383 | 380.1 | 383.0 | new | -52664 |
| TryGetComponent_Position | 10000 | 2.195 | 1.875 | 219.5 | 187.5 | old | 40560 |
| TryGetComponent_Position | 50000 | 4.773 | 4.854 | 95.5 | 97.1 | new | -234872 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.010 ms`, alloc `3504`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.132 ms`, alloc `100640`
- `PatchComponent_Position` scale `1000` mean `0.299 ms`, alloc `149552`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.559 ms`, alloc `2558912`
- `PatchComponent_Position` scale `10000` mean `1.301 ms`, alloc `3735056`
- `CreateDefaultImmediate_Position` scale `50000` mean `2.500 ms`, alloc `10423880`
- `PatchComponent_Position` scale `50000` mean `6.586 ms`, alloc `15385848`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.009 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.202 ms`, alloc `203560`
- `GetComponentRef_Write_Position` scale `10000` mean `0.772 ms`, alloc `3694496`
- `GetComponentRef_Write_Position` scale `50000` mean `4.650 ms`, alloc `15620768`
