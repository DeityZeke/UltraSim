# ECS Benchmark Summary

Generated: 2026-04-20T00:46:07.5600677+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.115 | 0.301 | 115.3 | 300.6 | new | 4512 |
| CreateDefaultDeferred_Position | 10000 | 0.662 | 0.865 | 66.2 | 86.5 | new | -558944 |
| CreateDefaultDeferred_Position | 50000 | 4.018 | 2.637 | 80.4 | 52.7 | old | -1857768 |
| DeferredAdd_Velocity | 1000 | 0.507 | 0.424 | 507.0 | 424.4 | old | -30712 |
| DeferredAdd_Velocity | 10000 | 3.825 | 3.253 | 382.5 | 325.3 | old | 430672 |
| DeferredAdd_Velocity | 50000 | 10.724 | 7.531 | 214.5 | 150.6 | old | 1335718 |
| DeferredDestroy | 1000 | 0.272 | 0.215 | 272.1 | 214.7 | old | -20424 |
| DeferredDestroy | 10000 | 2.870 | 1.396 | 287.0 | 139.6 | old | 464000 |
| DeferredDestroy | 50000 | 5.714 | 4.568 | 114.3 | 91.4 | old | 1615005 |
| DeferredRemove_Velocity | 1000 | 0.754 | 0.421 | 753.8 | 421.3 | old | -30896 |
| DeferredRemove_Velocity | 10000 | 4.437 | 3.143 | 443.7 | 314.3 | old | 481704 |
| DeferredRemove_Velocity | 50000 | 9.600 | 7.836 | 192.0 | 156.7 | old | 1460840 |
| DirtyDrain_Position | 1000 | 0.276 | 0.241 | 276.2 | 241.2 | old | -54008 |
| DirtyDrain_Position | 10000 | 1.554 | 0.859 | 155.4 | 85.9 | old | 40560 |
| DirtyDrain_Position | 50000 | 5.817 | 5.438 | 116.3 | 108.8 | old | -234920 |
| HotLoop_DeferredAdd_Velocity | 50000 | 4.599 | 3.642 | 92.0 | 72.8 | old | 1569856 |
| HotLoop_DirtyDrain_Position | 50000 | 0.969 | 0.853 | 19.4 | 17.1 | old | -1680 |
| HotLoop_Query1_Read_Position | 50000 | 0.241 | 0.203 | 4.8 | 4.1 | old | -1680 |
| HotLoop_SetComponent_Position | 50000 | 0.871 | 0.756 | 17.4 | 15.1 | old | -1680 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 84.020 | 194.323 | 10.5 | 24.3 | new | -438344 |
| HotLoop_TickParallel_QuerySystems | 10000 | 84.450 | 43.691 | 10.6 | 5.5 | old | -25680 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 47.862 | 46.937 | 6.0 | 5.9 | old | -80 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 36.413 | 72.919 | 4.6 | 9.1 | new | -40080 |
| HotLoop_TickSerial_QuerySystems | 10000 | 36.871 | 17.660 | 4.6 | 2.2 | old | -27280 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 17.334 | 22.543 | 2.2 | 2.8 | new | -1680 |
| HotLoop_TryGetComponent_Position | 50000 | 0.228 | 0.576 | 4.6 | 11.5 | new | -1680 |
| PersistenceView_Filtered_Position | 1000 | 0.408 | 0.315 | 408.1 | 314.5 | old | -43768 |
| PersistenceView_Filtered_Position | 10000 | 3.045 | 1.992 | 304.5 | 199.2 | old | 300952 |
| PersistenceView_Filtered_Position | 50000 | 8.549 | 8.469 | 171.0 | 169.4 | old | 686877 |
| Query1_Read_Position | 1000 | 0.210 | 0.202 | 209.8 | 202.4 | old | -52664 |
| Query1_Read_Position | 10000 | 2.206 | 1.675 | 220.6 | 167.5 | old | 40560 |
| Query1_Read_Position | 50000 | 4.449 | 4.350 | 89.0 | 87.0 | old | -234872 |
| Query2_Read_PositionVelocity | 1000 | 0.313 | 0.278 | 312.6 | 278.3 | old | -44696 |
| Query2_Read_PositionVelocity | 10000 | 3.426 | 2.730 | 342.6 | 273.0 | old | 220704 |
| Query2_Read_PositionVelocity | 50000 | 8.209 | 7.294 | 164.2 | 145.9 | old | 413384 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.414 | 0.323 | 414.1 | 323.4 | old | -40664 |
| Query3_Read_PositionVelocityHealth | 10000 | 4.517 | 3.920 | 451.7 | 392.0 | old | 311712 |
| Query3_Read_PositionVelocityHealth | 50000 | 10.038 | 11.690 | 200.8 | 233.8 | new | 737680 |
| SetComponent_Position | 1000 | 0.313 | 1.485 | 312.7 | 1485.4 | new | -52664 |
| SetComponent_Position | 10000 | 2.618 | 2.440 | 261.8 | 244.0 | old | 40560 |
| SetComponent_Position | 50000 | 5.709 | 5.340 | 114.2 | 106.8 | old | -234862 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 85.996 | 197.178 | 10.7 | 24.6 | new | -2253072 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 87.444 | 46.254 | 10.9 | 5.8 | old | 4958595 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 49.194 | 51.310 | 6.1 | 6.4 | new | 183427 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 38.750 | 76.767 | 4.8 | 9.6 | new | 185104 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 45.938 | 29.256 | 5.7 | 3.7 | old | 197904 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 28.216 | 36.229 | 3.5 | 4.5 | new | 223504 |
| TryGetComponent_Position | 1000 | 0.372 | 0.383 | 371.9 | 383.0 | new | -52664 |
| TryGetComponent_Position | 10000 | 2.425 | 1.875 | 242.5 | 187.5 | old | 40560 |
| TryGetComponent_Position | 50000 | 4.878 | 4.854 | 97.6 | 97.1 | old | -234872 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.010 ms`, alloc `3504`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.138 ms`, alloc `100640`
- `PatchComponent_Position` scale `1000` mean `0.328 ms`, alloc `149552`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.560 ms`, alloc `2558912`
- `PatchComponent_Position` scale `10000` mean `1.676 ms`, alloc `3735056`
- `CreateDefaultImmediate_Position` scale `50000` mean `2.463 ms`, alloc `10423880`
- `PatchComponent_Position` scale `50000` mean `5.763 ms`, alloc `15385848`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.009 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.202 ms`, alloc `203560`
- `GetComponentRef_Write_Position` scale `10000` mean `0.772 ms`, alloc `3694496`
- `GetComponentRef_Write_Position` scale `50000` mean `4.650 ms`, alloc `15620768`
