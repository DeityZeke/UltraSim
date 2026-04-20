# ECS Benchmark Summary

Generated: 2026-04-20T14:24:54.3011162+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.072 | 0.323 | 71.6 | 322.6 | new | 1272 |
| CreateDefaultDeferred_Position | 10000 | 0.296 | 0.824 | 29.6 | 82.4 | new | -633848 |
| CreateDefaultDeferred_Position | 50000 | 1.376 | 5.829 | 27.5 | 116.6 | new | -2252327 |
| CreateDestroyReuseChurn_Position | 1000 | 0.626 | 0.630 | 313.0 | 315.0 | new | 31992 |
| CreateDestroyReuseChurn_Position | 10000 | 1.537 | 1.885 | 79.0 | 96.9 | new | 27616 |
| CreateDestroyReuseChurn_Position | 50000 | 3.191 | 2.975 | 32.1 | 29.9 | old | 32608 |
| DeferredAdd_Velocity | 1000 | 0.303 | 0.421 | 303.3 | 421.4 | new | 3256 |
| DeferredAdd_Velocity | 10000 | 1.924 | 3.290 | 192.4 | 329.0 | new | -811968 |
| DeferredAdd_Velocity | 50000 | 4.315 | 7.010 | 86.3 | 140.2 | new | -3143928 |
| DeferredDestroy_SparseVictims | 1000 | 0.167 | 0.205 | 334.0 | 410.2 | new | -10240 |
| DeferredDestroy_SparseVictims | 10000 | 1.155 | 1.445 | 231.1 | 289.0 | new | -737376 |
| DeferredDestroy_SparseVictims | 50000 | 2.619 | 3.320 | 104.8 | 132.8 | new | -2920545 |
| DeferredDestroy | 1000 | 0.176 | 0.213 | 175.8 | 212.5 | new | -2024 |
| DeferredDestroy | 10000 | 1.204 | 0.978 | 120.4 | 97.8 | old | -606280 |
| DeferredDestroy | 50000 | 2.551 | 3.531 | 51.0 | 70.6 | new | -2396229 |
| DeferredRemove_Velocity | 1000 | 0.283 | 0.416 | 282.7 | 415.5 | new | -4896 |
| DeferredRemove_Velocity | 10000 | 2.055 | 3.270 | 205.5 | 327.0 | new | -610496 |
| DeferredRemove_Velocity | 50000 | 4.233 | 7.671 | 84.7 | 153.4 | new | -2399136 |
| DirtyDrain_Position | 1000 | 0.213 | 0.235 | 213.1 | 234.6 | new | -19584 |
| DirtyDrain_Position | 10000 | 1.548 | 1.460 | 154.8 | 146.0 | old | -869696 |
| DirtyDrain_Position | 50000 | 3.289 | 3.948 | 65.8 | 79.0 | new | -3446134 |
| HotLoop_DeferredAdd_Velocity | 50000 | 3.210 | 3.850 | 64.2 | 77.0 | new | 305232 |
| HotLoop_DirtyDrain_Position | 50000 | 0.988 | 0.862 | 19.8 | 17.2 | old | -1344 |
| HotLoop_Query1_Read_Position | 50000 | 0.255 | 0.167 | 5.1 | 3.3 | old | -1344 |
| HotLoop_SetComponent_Position | 50000 | 0.826 | 0.738 | 16.5 | 14.8 | old | -1344 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 46.631 | 189.473 | 5.8 | 23.7 | new | -786672 |
| HotLoop_TickParallel_OscillatingMovement | 10000 | 153.961 | 378.406 | 19.2 | 47.3 | new | 99456 |
| HotLoop_TickParallel_OscillatingMovement | 100000 | 1149.732 | 3721.844 | 14.4 | 46.5 | new | 99456 |
| HotLoop_TickParallel_QuerySystems | 10000 | 46.623 | 41.595 | 5.8 | 5.2 | old | 25856 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 3.390 | 45.356 | 0.4 | 5.7 | new | 64256 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 37.958 | 70.093 | 4.7 | 8.8 | new | -26944 |
| HotLoop_TickSerial_OscillatingMovement | 10000 | 207.635 | 325.957 | 26.0 | 40.7 | new | 72256 |
| HotLoop_TickSerial_OscillatingMovement | 100000 | 1680.268 | 3031.965 | 21.0 | 37.9 | new | 72256 |
| HotLoop_TickSerial_QuerySystems | 10000 | 38.861 | 17.297 | 4.9 | 2.2 | old | -14144 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 6.954 | 20.750 | 0.9 | 2.6 | new | 11456 |
| HotLoop_TryGetComponent_Position | 50000 | 0.224 | 0.248 | 4.5 | 5.0 | new | -1344 |
| MigrationCycle_AddRemoveVelocity | 1000 | 1.613 | 2.118 | 806.7 | 1059.2 | new | 83200 |
| MigrationCycle_AddRemoveVelocity | 10000 | 2.270 | 4.530 | 113.5 | 226.5 | new | 89304 |
| MigrationCycle_AddRemoveVelocity | 50000 | 4.352 | 7.787 | 43.5 | 77.9 | new | 1107532 |
| PersistenceView_Filtered_Position | 1000 | 0.232 | 0.316 | 231.8 | 316.0 | new | -19936 |
| PersistenceView_Filtered_Position | 10000 | 2.720 | 2.308 | 272.0 | 230.8 | old | -880392 |
| PersistenceView_Filtered_Position | 50000 | 4.179 | 5.977 | 83.6 | 119.5 | new | -3495528 |
| Query1_Read_Position | 1000 | 0.146 | 0.202 | 145.7 | 201.8 | new | -18640 |
| Query1_Read_Position | 10000 | 1.041 | 1.636 | 104.1 | 163.6 | new | -869760 |
| Query1_Read_Position | 50000 | 4.637 | 5.042 | 92.7 | 100.8 | new | -3450120 |
| Query2_Read_PositionVelocity | 1000 | 0.186 | 0.263 | 186.5 | 262.9 | new | -18640 |
| Query2_Read_PositionVelocity | 10000 | 1.535 | 2.647 | 153.5 | 264.7 | new | -871776 |
| Query2_Read_PositionVelocity | 50000 | 6.794 | 6.766 | 135.9 | 135.3 | old | -3450153 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.258 | 0.329 | 257.8 | 328.5 | new | -18640 |
| Query3_Read_PositionVelocityHealth | 10000 | 2.563 | 3.436 | 256.3 | 343.6 | new | -871104 |
| Query3_Read_PositionVelocityHealth | 50000 | 10.898 | 9.086 | 218.0 | 181.7 | old | -3450129 |
| SetComponent_Position | 1000 | 0.720 | 1.449 | 720.5 | 1449.5 | new | -18576 |
| SetComponent_Position | 10000 | 1.509 | 2.438 | 150.9 | 243.8 | new | -869696 |
| SetComponent_Position | 50000 | 6.429 | 8.564 | 128.6 | 171.3 | new | -3450056 |
| TickParallel_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 488.545 | 2037.134 | 6.1 | 25.5 | new | 211271800 |
| TickParallel_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 343.622 | 542.877 | 5.7 | 9.0 | new | 180681140 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 50.010 | 194.304 | 6.3 | 24.3 | new | -3579124 |
| TickParallel_OscillatingMovement_100k_200Ticks | 100000 | 1049.962 | 3700.769 | 13.1 | 46.3 | new | 18164673 |
| TickParallel_OscillatingMovement_10k_200Ticks | 10000 | 140.737 | 382.330 | 17.6 | 47.8 | new | 2241907 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 50.114 | 45.312 | 6.3 | 5.7 | old | 4165376 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 6.779 | 60.462 | 0.8 | 7.6 | new | -810536 |
| TickParallel_V1PulseMovement_100k_50Ticks | 100000 | 145.006 | 216.214 | 9.7 | 14.4 | new | 49598496 |
| TickSerial_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 781.265 | 2257.748 | 9.8 | 28.2 | new | 209740617 |
| TickSerial_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 202.055 | 638.865 | 3.4 | 10.6 | new | 306265240 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 41.547 | 78.900 | 5.2 | 9.9 | new | 2011736 |
| TickSerial_OscillatingMovement_100k_200Ticks | 100000 | 1459.507 | 2976.923 | 18.2 | 37.2 | new | 18717768 |
| TickSerial_OscillatingMovement_10k_200Ticks | 10000 | 176.435 | 280.816 | 22.1 | 35.1 | new | 2794992 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 47.270 | 30.792 | 5.9 | 3.8 | old | 2024536 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 9.908 | 32.417 | 1.2 | 4.1 | new | -290512 |
| TickSerial_V1PulseMovement_100k_50Ticks | 100000 | 143.242 | 161.078 | 9.5 | 10.7 | new | 47610296 |
| TryGetComponent_Position | 1000 | 0.184 | 0.381 | 183.9 | 381.0 | new | -18576 |
| TryGetComponent_Position | 10000 | 1.045 | 1.878 | 104.5 | 187.8 | new | -869696 |
| TryGetComponent_Position | 50000 | 4.575 | 8.850 | 91.5 | 177.0 | new | -3450056 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3536`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.119 ms`, alloc `108280`
- `PatchComponent_Position` scale `1000` mean `0.217 ms`, alloc `186056`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.947 ms`, alloc `2566552`
- `PatchComponent_Position` scale `10000` mean `1.400 ms`, alloc `2826880`
- `CreateDefaultImmediate_Position` scale `50000` mean `1.685 ms`, alloc `10431808`
- `PatchComponent_Position` scale `50000` mean `3.407 ms`, alloc `12176728`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.008 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.188 ms`, alloc `205640`
- `GetComponentRef_Write_Position` scale `10000` mean `1.628 ms`, alloc `3696576`
- `GetComponentRef_Write_Position` scale `50000` mean `3.697 ms`, alloc `15622872`
