# ECS Benchmark Summary

Generated: 2026-04-20T00:31:02.7894831+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.143 | 0.301 | 143.3 | 300.6 | new | 11704 |
| CreateDefaultDeferred_Position | 10000 | 0.969 | 0.865 | 96.9 | 86.5 | old | 782784 |
| CreateDefaultDeferred_Position | 50000 | 4.220 | 2.637 | 84.4 | 52.7 | old | 3215440 |
| DeferredAdd_Velocity | 1000 | 0.536 | 0.424 | 536.5 | 424.4 | old | -31048 |
| DeferredAdd_Velocity | 10000 | 4.265 | 3.253 | 426.5 | 325.3 | old | 430000 |
| DeferredAdd_Velocity | 50000 | 12.325 | 7.531 | 246.5 | 150.6 | old | 1335061 |
| DeferredDestroy | 1000 | 0.298 | 0.215 | 298.0 | 214.7 | old | -20760 |
| DeferredDestroy | 10000 | 1.466 | 1.396 | 146.6 | 139.6 | old | 463664 |
| DeferredDestroy | 50000 | 7.189 | 4.568 | 143.8 | 91.4 | old | 1614333 |
| DeferredRemove_Velocity | 1000 | 0.584 | 0.421 | 583.6 | 421.3 | old | -31232 |
| DeferredRemove_Velocity | 10000 | 3.816 | 3.143 | 381.6 | 314.3 | old | 481032 |
| DeferredRemove_Velocity | 50000 | 10.254 | 7.836 | 205.1 | 156.7 | old | 1460168 |
| DirtyDrain_Position | 1000 | 0.297 | 0.241 | 296.8 | 241.2 | old | -54344 |
| DirtyDrain_Position | 10000 | 1.159 | 0.859 | 115.9 | 85.9 | old | 40224 |
| DirtyDrain_Position | 50000 | 6.233 | 5.438 | 124.7 | 108.8 | old | -235597 |
| HotLoop_DeferredAdd_Velocity | 50000 | 6.318 | 3.642 | 126.4 | 72.8 | old | 1569520 |
| HotLoop_DirtyDrain_Position | 50000 | 0.857 | 0.853 | 17.1 | 17.1 | old | -2016 |
| HotLoop_Query1_Read_Position | 50000 | 0.243 | 0.203 | 4.9 | 4.1 | old | -2016 |
| HotLoop_SetComponent_Position | 50000 | 0.692 | 0.756 | 13.8 | 15.1 | new | -2016 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 85.047 | 194.323 | 10.6 | 24.3 | new | -438680 |
| HotLoop_TickParallel_QuerySystems | 10000 | 86.883 | 43.691 | 10.9 | 5.5 | old | -26016 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 47.050 | 46.937 | 5.9 | 5.9 | old | -416 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 37.456 | 72.919 | 4.7 | 9.1 | new | -40416 |
| HotLoop_TickSerial_QuerySystems | 10000 | 37.257 | 17.660 | 4.7 | 2.2 | old | -27616 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 17.500 | 22.543 | 2.2 | 2.8 | new | -2016 |
| HotLoop_TryGetComponent_Position | 50000 | 0.224 | 0.576 | 4.5 | 11.5 | new | -2016 |
| PersistenceView_Filtered_Position | 1000 | 0.415 | 0.315 | 415.5 | 314.5 | old | -44104 |
| PersistenceView_Filtered_Position | 10000 | 1.992 | 1.992 | 199.2 | 199.2 | old | 300280 |
| PersistenceView_Filtered_Position | 50000 | 8.326 | 8.469 | 166.5 | 169.4 | new | 686205 |
| Query1_Read_Position | 1000 | 0.217 | 0.202 | 216.8 | 202.4 | old | -53000 |
| Query1_Read_Position | 10000 | 2.371 | 1.675 | 237.1 | 167.5 | old | 40224 |
| Query1_Read_Position | 50000 | 5.225 | 4.350 | 104.5 | 87.0 | old | -235544 |
| Query2_Read_PositionVelocity | 1000 | 0.333 | 0.278 | 333.3 | 278.3 | old | -45032 |
| Query2_Read_PositionVelocity | 10000 | 3.641 | 2.730 | 364.1 | 273.0 | old | 220032 |
| Query2_Read_PositionVelocity | 50000 | 6.942 | 7.294 | 138.8 | 145.9 | new | 412712 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.439 | 0.323 | 438.9 | 323.4 | old | -41000 |
| Query3_Read_PositionVelocityHealth | 10000 | 4.929 | 3.920 | 492.9 | 392.0 | old | 311040 |
| Query3_Read_PositionVelocityHealth | 50000 | 10.146 | 11.690 | 202.9 | 233.8 | new | 737008 |
| SetComponent_Position | 1000 | 0.334 | 1.485 | 333.5 | 1485.4 | new | -53000 |
| SetComponent_Position | 10000 | 2.820 | 2.440 | 282.0 | 244.0 | old | 40224 |
| SetComponent_Position | 50000 | 6.230 | 5.340 | 124.6 | 106.8 | old | -235534 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 88.028 | 197.178 | 11.0 | 24.6 | new | -2653272 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 87.377 | 46.254 | 10.9 | 5.8 | old | 4557156 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 48.879 | 51.310 | 6.1 | 6.4 | new | 183091 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 37.521 | 76.767 | 4.7 | 9.6 | new | 184768 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 48.738 | 29.256 | 6.1 | 3.7 | old | 198806 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 28.861 | 36.229 | 3.6 | 4.5 | new | 223168 |
| TryGetComponent_Position | 1000 | 0.388 | 0.383 | 387.5 | 383.0 | old | -53000 |
| TryGetComponent_Position | 10000 | 2.462 | 1.875 | 246.2 | 187.5 | old | 40224 |
| TryGetComponent_Position | 50000 | 5.492 | 4.854 | 109.8 | 97.1 | old | -235544 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3504`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.138 ms`, alloc `100304`
- `PatchComponent_Position` scale `1000` mean `0.303 ms`, alloc `149216`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.632 ms`, alloc `2558576`
- `PatchComponent_Position` scale `10000` mean `1.276 ms`, alloc `3734720`
- `CreateDefaultImmediate_Position` scale `50000` mean `3.145 ms`, alloc `10423184`
- `PatchComponent_Position` scale `50000` mean `6.145 ms`, alloc `15385176`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.009 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.202 ms`, alloc `203560`
- `GetComponentRef_Write_Position` scale `10000` mean `0.772 ms`, alloc `3694496`
- `GetComponentRef_Write_Position` scale `50000` mean `4.650 ms`, alloc `15620768`
