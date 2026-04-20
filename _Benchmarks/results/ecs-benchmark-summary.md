# ECS Benchmark Summary

Generated: 2026-04-20T14:32:54.5239530+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.075 | 0.323 | 75.5 | 322.6 | new | 1272 |
| CreateDefaultDeferred_Position | 10000 | 0.331 | 0.824 | 33.1 | 82.4 | new | -633848 |
| CreateDefaultDeferred_Position | 50000 | 1.414 | 5.829 | 28.3 | 116.6 | new | -2252327 |
| CreateDestroyReuseChurn_Position | 1000 | 0.777 | 0.630 | 388.5 | 315.0 | old | 31992 |
| CreateDestroyReuseChurn_Position | 10000 | 1.446 | 1.885 | 74.3 | 96.9 | new | 27616 |
| CreateDestroyReuseChurn_Position | 50000 | 2.124 | 2.975 | 21.4 | 29.9 | new | 32608 |
| DeferredAdd_Velocity | 1000 | 0.322 | 0.421 | 322.5 | 421.4 | new | 3256 |
| DeferredAdd_Velocity | 10000 | 1.967 | 3.290 | 196.7 | 329.0 | new | -811968 |
| DeferredAdd_Velocity | 50000 | 6.189 | 7.010 | 123.8 | 140.2 | new | -3143936 |
| DeferredDestroy_SparseVictims | 1000 | 0.208 | 0.205 | 415.4 | 410.2 | old | -10240 |
| DeferredDestroy_SparseVictims | 10000 | 1.220 | 1.445 | 243.9 | 289.0 | new | -737376 |
| DeferredDestroy_SparseVictims | 50000 | 2.470 | 3.320 | 98.8 | 132.8 | new | -2920565 |
| DeferredDestroy | 1000 | 0.192 | 0.213 | 191.8 | 212.5 | new | -2024 |
| DeferredDestroy | 10000 | 1.334 | 0.978 | 133.4 | 97.8 | old | -606280 |
| DeferredDestroy | 50000 | 2.607 | 3.531 | 52.1 | 70.6 | new | -2396200 |
| DeferredRemove_Velocity | 1000 | 0.290 | 0.416 | 289.6 | 415.5 | new | -4896 |
| DeferredRemove_Velocity | 10000 | 2.121 | 3.270 | 212.1 | 327.0 | new | -610496 |
| DeferredRemove_Velocity | 50000 | 4.040 | 7.671 | 80.8 | 153.4 | new | -2399136 |
| DirtyDrain_Position | 1000 | 0.218 | 0.235 | 218.4 | 234.6 | new | -19584 |
| DirtyDrain_Position | 10000 | 1.329 | 1.460 | 132.9 | 146.0 | new | -869696 |
| DirtyDrain_Position | 50000 | 2.975 | 3.948 | 59.5 | 79.0 | new | -3446134 |
| HotLoop_DeferredAdd_Velocity | 50000 | 3.629 | 3.850 | 72.6 | 77.0 | new | 305232 |
| HotLoop_DirtyDrain_Position | 50000 | 0.863 | 0.862 | 17.3 | 17.2 | old | -1344 |
| HotLoop_Query1_Read_Position | 50000 | 0.253 | 0.167 | 5.1 | 3.3 | old | -1344 |
| HotLoop_SetComponent_Position | 50000 | 0.699 | 0.738 | 14.0 | 14.8 | new | -1344 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 40.288 | 189.473 | 5.0 | 23.7 | new | -786672 |
| HotLoop_TickParallel_OscillatingMovement | 10000 | 153.653 | 378.406 | 19.2 | 47.3 | new | 99456 |
| HotLoop_TickParallel_OscillatingMovement | 100000 | 1144.474 | 3721.844 | 14.3 | 46.5 | new | 99456 |
| HotLoop_TickParallel_QuerySystems | 10000 | 39.383 | 41.595 | 4.9 | 5.2 | new | 25856 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 3.314 | 45.356 | 0.4 | 5.7 | new | 64256 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 31.613 | 70.093 | 4.0 | 8.8 | new | -26944 |
| HotLoop_TickSerial_OscillatingMovement | 10000 | 197.017 | 325.957 | 24.6 | 40.7 | new | 72256 |
| HotLoop_TickSerial_OscillatingMovement | 100000 | 1670.203 | 3031.965 | 20.9 | 37.9 | new | 72256 |
| HotLoop_TickSerial_QuerySystems | 10000 | 31.748 | 17.297 | 4.0 | 2.2 | old | -14144 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 6.931 | 20.750 | 0.9 | 2.6 | new | 11456 |
| HotLoop_TryGetComponent_Position | 50000 | 0.226 | 0.248 | 4.5 | 5.0 | new | -1344 |
| MigrationCycle_AddRemoveVelocity | 1000 | 1.809 | 2.118 | 904.5 | 1059.2 | new | 83200 |
| MigrationCycle_AddRemoveVelocity | 10000 | 2.332 | 4.530 | 116.6 | 226.5 | new | 89304 |
| MigrationCycle_AddRemoveVelocity | 50000 | 4.351 | 7.787 | 43.5 | 77.9 | new | 1107517 |
| PersistenceView_Filtered_Position | 1000 | 0.263 | 0.316 | 263.4 | 316.0 | new | -19936 |
| PersistenceView_Filtered_Position | 10000 | 1.883 | 2.308 | 188.3 | 230.8 | new | -880392 |
| PersistenceView_Filtered_Position | 50000 | 4.052 | 5.977 | 81.0 | 119.5 | new | -3495528 |
| Query1_Read_Position | 1000 | 0.164 | 0.202 | 164.3 | 201.8 | new | -18640 |
| Query1_Read_Position | 10000 | 1.076 | 1.636 | 107.6 | 163.6 | new | -869760 |
| Query1_Read_Position | 50000 | 3.344 | 5.042 | 66.9 | 100.8 | new | -3450120 |
| Query2_Read_PositionVelocity | 1000 | 0.213 | 0.263 | 213.0 | 262.9 | new | -18640 |
| Query2_Read_PositionVelocity | 10000 | 1.661 | 2.647 | 166.1 | 264.7 | new | -871776 |
| Query2_Read_PositionVelocity | 50000 | 5.544 | 6.766 | 110.9 | 135.3 | new | -3450153 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.289 | 0.329 | 288.8 | 328.5 | new | -18640 |
| Query3_Read_PositionVelocityHealth | 10000 | 2.728 | 3.436 | 272.8 | 343.6 | new | -871104 |
| Query3_Read_PositionVelocityHealth | 50000 | 8.806 | 9.086 | 176.1 | 181.7 | new | -3450129 |
| SetComponent_Position | 1000 | 0.835 | 1.449 | 835.1 | 1449.5 | new | -18576 |
| SetComponent_Position | 10000 | 1.550 | 2.438 | 155.0 | 243.8 | new | -869696 |
| SetComponent_Position | 50000 | 4.348 | 8.564 | 87.0 | 171.3 | new | -3450056 |
| TickParallel_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 483.319 | 2037.134 | 6.0 | 25.5 | new | 209983849 |
| TickParallel_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 337.170 | 542.877 | 5.6 | 9.0 | new | 181110083 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 43.218 | 194.304 | 5.4 | 24.3 | new | -3044253 |
| TickParallel_OscillatingMovement_100k_200Ticks | 100000 | 1129.520 | 3700.769 | 14.1 | 46.3 | new | 18164664 |
| TickParallel_OscillatingMovement_10k_200Ticks | 10000 | 141.895 | 382.330 | 17.7 | 47.8 | new | 2241907 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 44.576 | 45.312 | 5.6 | 5.7 | new | 4165808 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 6.844 | 60.462 | 0.9 | 7.6 | new | -810536 |
| TickParallel_V1PulseMovement_100k_50Ticks | 100000 | 145.583 | 216.214 | 9.7 | 14.4 | new | 49598496 |
| TickSerial_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 775.432 | 2257.748 | 9.7 | 28.2 | new | 211059603 |
| TickSerial_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 194.377 | 638.865 | 3.2 | 10.6 | new | 306715766 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 33.861 | 78.900 | 4.2 | 9.9 | new | 2012152 |
| TickSerial_OscillatingMovement_100k_200Ticks | 100000 | 1454.198 | 2976.923 | 18.2 | 37.2 | new | 18717768 |
| TickSerial_OscillatingMovement_10k_200Ticks | 10000 | 177.970 | 280.816 | 22.2 | 35.1 | new | 2794992 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 39.270 | 30.792 | 4.9 | 3.8 | old | 2024952 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 9.818 | 32.417 | 1.2 | 4.1 | new | -290512 |
| TickSerial_V1PulseMovement_100k_50Ticks | 100000 | 140.925 | 161.078 | 9.4 | 10.7 | new | 47610296 |
| TryGetComponent_Position | 1000 | 0.202 | 0.381 | 202.5 | 381.0 | new | -18576 |
| TryGetComponent_Position | 10000 | 1.181 | 1.878 | 118.1 | 187.8 | new | -869696 |
| TryGetComponent_Position | 50000 | 3.704 | 8.850 | 74.1 | 177.0 | new | -3450056 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3536`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.151 ms`, alloc `108280`
- `PatchComponent_Position` scale `1000` mean `0.221 ms`, alloc `186056`
- `CreateDefaultImmediate_Position` scale `10000` mean `1.175 ms`, alloc `2566552`
- `PatchComponent_Position` scale `10000` mean `1.142 ms`, alloc `2826880`
- `CreateDefaultImmediate_Position` scale `50000` mean `1.640 ms`, alloc `10431808`
- `PatchComponent_Position` scale `50000` mean `2.946 ms`, alloc `12176718`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.008 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.188 ms`, alloc `205640`
- `GetComponentRef_Write_Position` scale `10000` mean `1.628 ms`, alloc `3696576`
- `GetComponentRef_Write_Position` scale `50000` mean `3.697 ms`, alloc `15622872`
