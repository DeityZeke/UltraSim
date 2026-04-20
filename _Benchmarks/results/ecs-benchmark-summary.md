# ECS Benchmark Summary

Generated: 2026-04-20T00:26:15.8121880+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.145 | 0.301 | 145.0 | 300.6 | new | 11704 |
| CreateDefaultDeferred_Position | 10000 | 1.007 | 0.865 | 100.7 | 86.5 | old | 782784 |
| CreateDefaultDeferred_Position | 50000 | 4.365 | 2.637 | 87.3 | 52.7 | old | 3215440 |
| DeferredAdd_Velocity | 1000 | 0.564 | 0.424 | 564.4 | 424.4 | old | -15552 |
| DeferredAdd_Velocity | 10000 | 5.404 | 3.253 | 540.4 | 325.3 | old | 1124800 |
| DeferredAdd_Velocity | 50000 | 11.798 | 7.531 | 236.0 | 150.6 | old | 3895733 |
| DeferredDestroy | 1000 | 0.312 | 0.215 | 312.1 | 214.7 | old | -20760 |
| DeferredDestroy | 10000 | 1.465 | 1.396 | 146.5 | 139.6 | old | 463664 |
| DeferredDestroy | 50000 | 8.270 | 4.568 | 165.4 | 91.4 | old | 1614333 |
| DeferredRemove_Velocity | 1000 | 0.578 | 0.421 | 577.9 | 421.3 | old | -23704 |
| DeferredRemove_Velocity | 10000 | 3.160 | 3.143 | 316.0 | 314.3 | old | 994008 |
| DeferredRemove_Velocity | 50000 | 11.347 | 7.836 | 226.9 | 156.7 | old | 3372536 |
| DirtyDrain_Position | 1000 | 0.303 | 0.241 | 302.9 | 241.2 | old | -54344 |
| DirtyDrain_Position | 10000 | 1.297 | 0.859 | 129.7 | 85.9 | old | 40224 |
| DirtyDrain_Position | 50000 | 6.900 | 5.438 | 138.0 | 108.8 | old | -235592 |
| HotLoop_DeferredAdd_Velocity | 50000 | 5.288 | 3.642 | 105.8 | 72.8 | old | 4130192 |
| HotLoop_DirtyDrain_Position | 50000 | 0.987 | 0.853 | 19.7 | 17.1 | old | -2016 |
| HotLoop_Query1_Read_Position | 50000 | 0.242 | 0.203 | 4.8 | 4.1 | old | -2016 |
| HotLoop_SetComponent_Position | 50000 | 0.836 | 0.756 | 16.7 | 15.1 | old | -2016 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 85.135 | 194.323 | 10.6 | 24.3 | new | -438680 |
| HotLoop_TickParallel_QuerySystems | 10000 | 82.705 | 43.691 | 10.3 | 5.5 | old | 373848 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 48.559 | 46.937 | 6.1 | 5.9 | old | -416 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 37.747 | 72.919 | 4.7 | 9.1 | new | -40416 |
| HotLoop_TickSerial_QuerySystems | 10000 | 36.984 | 17.660 | 4.6 | 2.2 | old | -27616 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 17.979 | 22.543 | 2.2 | 2.8 | new | -2016 |
| HotLoop_TryGetComponent_Position | 50000 | 0.228 | 0.576 | 4.6 | 11.5 | new | -2016 |
| PersistenceView_Filtered_Position | 1000 | 0.427 | 0.315 | 426.8 | 314.5 | old | -44104 |
| PersistenceView_Filtered_Position | 10000 | 2.475 | 1.992 | 247.5 | 199.2 | old | 300280 |
| PersistenceView_Filtered_Position | 50000 | 8.520 | 8.469 | 170.4 | 169.4 | old | 686205 |
| Query1_Read_Position | 1000 | 0.225 | 0.202 | 224.5 | 202.4 | old | -53000 |
| Query1_Read_Position | 10000 | 2.467 | 1.675 | 246.7 | 167.5 | old | 40224 |
| Query1_Read_Position | 50000 | 5.336 | 4.350 | 106.7 | 87.0 | old | -235544 |
| Query2_Read_PositionVelocity | 1000 | 0.330 | 0.278 | 330.4 | 278.3 | old | -45032 |
| Query2_Read_PositionVelocity | 10000 | 3.719 | 2.730 | 371.9 | 273.0 | old | 220032 |
| Query2_Read_PositionVelocity | 50000 | 9.127 | 7.294 | 182.5 | 145.9 | old | 412712 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.457 | 0.323 | 457.1 | 323.4 | old | -41000 |
| Query3_Read_PositionVelocityHealth | 10000 | 4.969 | 3.920 | 496.9 | 392.0 | old | 311040 |
| Query3_Read_PositionVelocityHealth | 50000 | 12.026 | 11.690 | 240.5 | 233.8 | old | 737008 |
| SetComponent_Position | 1000 | 0.346 | 1.485 | 346.2 | 1485.4 | new | -53000 |
| SetComponent_Position | 10000 | 2.865 | 2.440 | 286.5 | 244.0 | old | 40224 |
| SetComponent_Position | 50000 | 6.483 | 5.340 | 129.7 | 106.8 | old | -235534 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 86.303 | 197.178 | 10.8 | 24.6 | new | -1853544 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 87.324 | 46.254 | 10.9 | 5.8 | old | 5356884 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 49.742 | 51.310 | 6.2 | 6.4 | new | 183091 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 38.781 | 76.767 | 4.8 | 9.6 | new | 184768 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 51.896 | 29.256 | 6.5 | 3.7 | old | 197568 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 27.629 | 36.229 | 3.5 | 4.5 | new | 223168 |
| TryGetComponent_Position | 1000 | 0.389 | 0.383 | 388.7 | 383.0 | old | -53000 |
| TryGetComponent_Position | 10000 | 2.438 | 1.875 | 243.8 | 187.5 | old | 40224 |
| TryGetComponent_Position | 50000 | 5.877 | 4.854 | 117.5 | 97.1 | old | -235544 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3504`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.140 ms`, alloc `100304`
- `PatchComponent_Position` scale `1000` mean `0.307 ms`, alloc `149216`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.581 ms`, alloc `2558576`
- `PatchComponent_Position` scale `10000` mean `1.398 ms`, alloc `3734720`
- `CreateDefaultImmediate_Position` scale `50000` mean `2.982 ms`, alloc `10423184`
- `PatchComponent_Position` scale `50000` mean `6.421 ms`, alloc `15385176`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.009 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.202 ms`, alloc `203560`
- `GetComponentRef_Write_Position` scale `10000` mean `0.772 ms`, alloc `3694496`
- `GetComponentRef_Write_Position` scale `50000` mean `4.650 ms`, alloc `15620768`
