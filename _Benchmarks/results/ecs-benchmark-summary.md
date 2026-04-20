# ECS Benchmark Summary

Generated: 2026-04-20T14:39:08.2308392+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.079 | 0.323 | 79.2 | 322.6 | new | 1272 |
| CreateDefaultDeferred_Position | 10000 | 0.310 | 0.824 | 31.0 | 82.4 | new | -633848 |
| CreateDefaultDeferred_Position | 50000 | 1.407 | 5.829 | 28.1 | 116.6 | new | -2252312 |
| CreateDestroyReuseChurn_Position | 1000 | 0.615 | 0.630 | 307.5 | 315.0 | new | 31992 |
| CreateDestroyReuseChurn_Position | 10000 | 1.375 | 1.885 | 70.6 | 96.9 | new | 27616 |
| CreateDestroyReuseChurn_Position | 50000 | 2.333 | 2.975 | 23.5 | 29.9 | new | 32608 |
| DeferredAdd_Velocity | 1000 | 0.326 | 0.421 | 326.5 | 421.4 | new | 3256 |
| DeferredAdd_Velocity | 10000 | 1.915 | 3.290 | 191.5 | 329.0 | new | -811968 |
| DeferredAdd_Velocity | 50000 | 5.951 | 7.010 | 119.0 | 140.2 | new | -3143936 |
| DeferredDestroy_SparseVictims | 1000 | 0.182 | 0.205 | 364.8 | 410.2 | new | -10240 |
| DeferredDestroy_SparseVictims | 10000 | 1.518 | 1.445 | 303.7 | 289.0 | old | -737376 |
| DeferredDestroy_SparseVictims | 50000 | 2.433 | 3.320 | 97.3 | 132.8 | new | -2920545 |
| DeferredDestroy | 1000 | 0.160 | 0.213 | 159.9 | 212.5 | new | -2024 |
| DeferredDestroy | 10000 | 1.220 | 0.978 | 122.0 | 97.8 | old | -606280 |
| DeferredDestroy | 50000 | 2.468 | 3.531 | 49.4 | 70.6 | new | -2396200 |
| DeferredRemove_Velocity | 1000 | 0.293 | 0.416 | 292.8 | 415.5 | new | -4896 |
| DeferredRemove_Velocity | 10000 | 2.113 | 3.270 | 211.3 | 327.0 | new | -610496 |
| DeferredRemove_Velocity | 50000 | 4.312 | 7.671 | 86.2 | 153.4 | new | -2399136 |
| DirtyDrain_Position | 1000 | 0.231 | 0.235 | 230.7 | 234.6 | new | -19584 |
| DirtyDrain_Position | 10000 | 1.624 | 1.460 | 162.4 | 146.0 | old | -869696 |
| DirtyDrain_Position | 50000 | 2.908 | 3.948 | 58.2 | 79.0 | new | -3446134 |
| HotLoop_DeferredAdd_Velocity | 50000 | 3.732 | 3.850 | 74.6 | 77.0 | new | 305232 |
| HotLoop_DirtyDrain_Position | 50000 | 0.850 | 0.862 | 17.0 | 17.2 | new | -1344 |
| HotLoop_Query1_Read_Position | 50000 | 0.255 | 0.167 | 5.1 | 3.3 | old | -1344 |
| HotLoop_SetComponent_Position | 50000 | 0.686 | 0.738 | 13.7 | 14.8 | new | -1344 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 39.706 | 189.473 | 5.0 | 23.7 | new | -519445 |
| HotLoop_TickParallel_OscillatingMovement | 10000 | 154.464 | 378.406 | 19.3 | 47.3 | new | 99456 |
| HotLoop_TickParallel_OscillatingMovement | 100000 | 1145.876 | 3721.844 | 14.3 | 46.5 | new | 99456 |
| HotLoop_TickParallel_QuerySystems | 10000 | 40.367 | 41.595 | 5.0 | 5.2 | new | 25856 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 3.303 | 45.356 | 0.4 | 5.7 | new | 64256 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 32.598 | 70.093 | 4.1 | 8.8 | new | -26944 |
| HotLoop_TickSerial_OscillatingMovement | 10000 | 194.931 | 325.957 | 24.4 | 40.7 | new | 72256 |
| HotLoop_TickSerial_OscillatingMovement | 100000 | 1679.710 | 3031.965 | 21.0 | 37.9 | new | 72256 |
| HotLoop_TickSerial_QuerySystems | 10000 | 32.830 | 17.297 | 4.1 | 2.2 | old | -14144 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 6.952 | 20.750 | 0.9 | 2.6 | new | 11456 |
| HotLoop_TryGetComponent_Position | 50000 | 0.221 | 0.248 | 4.4 | 5.0 | new | -1344 |
| MigrationCycle_AddRemoveVelocity | 1000 | 1.668 | 2.118 | 834.1 | 1059.2 | new | 83200 |
| MigrationCycle_AddRemoveVelocity | 10000 | 2.112 | 4.530 | 105.6 | 226.5 | new | 89304 |
| MigrationCycle_AddRemoveVelocity | 50000 | 4.248 | 7.787 | 42.5 | 77.9 | new | 1107532 |
| PersistenceView_Filtered_Position | 1000 | 0.244 | 0.316 | 243.7 | 316.0 | new | -19936 |
| PersistenceView_Filtered_Position | 10000 | 2.682 | 2.308 | 268.2 | 230.8 | old | -880392 |
| PersistenceView_Filtered_Position | 50000 | 3.846 | 5.977 | 76.9 | 119.5 | new | -3495528 |
| Query1_Read_Position | 1000 | 0.154 | 0.202 | 154.4 | 201.8 | new | -18640 |
| Query1_Read_Position | 10000 | 1.136 | 1.636 | 113.6 | 163.6 | new | -869760 |
| Query1_Read_Position | 50000 | 3.686 | 5.042 | 73.7 | 100.8 | new | -3450120 |
| Query2_Read_PositionVelocity | 1000 | 0.196 | 0.263 | 195.7 | 262.9 | new | -18640 |
| Query2_Read_PositionVelocity | 10000 | 1.640 | 2.647 | 164.0 | 264.7 | new | -871776 |
| Query2_Read_PositionVelocity | 50000 | 5.582 | 6.766 | 111.6 | 135.3 | new | -3450153 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.265 | 0.329 | 265.2 | 328.5 | new | -18640 |
| Query3_Read_PositionVelocityHealth | 10000 | 2.643 | 3.436 | 264.3 | 343.6 | new | -871104 |
| Query3_Read_PositionVelocityHealth | 50000 | 6.837 | 9.086 | 136.7 | 181.7 | new | -3450129 |
| SetComponent_Position | 1000 | 0.729 | 1.449 | 728.7 | 1449.5 | new | -18576 |
| SetComponent_Position | 10000 | 1.535 | 2.438 | 153.5 | 243.8 | new | -869696 |
| SetComponent_Position | 50000 | 4.754 | 8.564 | 95.1 | 171.3 | new | -3450056 |
| TickParallel_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 482.297 | 2037.134 | 6.0 | 25.5 | new | 210629494 |
| TickParallel_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 334.903 | 542.877 | 5.6 | 9.0 | new | 178856988 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 42.890 | 194.304 | 5.4 | 24.3 | new | -3311557 |
| TickParallel_OscillatingMovement_100k_200Ticks | 100000 | 1034.530 | 3700.769 | 12.9 | 46.3 | new | 18164664 |
| TickParallel_OscillatingMovement_10k_200Ticks | 10000 | 140.717 | 382.330 | 17.6 | 47.8 | new | 2241888 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 43.261 | 45.312 | 5.4 | 5.7 | new | 3898572 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 6.543 | 60.462 | 0.8 | 7.6 | new | -810536 |
| TickParallel_V1PulseMovement_100k_50Ticks | 100000 | 146.496 | 216.214 | 9.8 | 14.4 | new | 49598496 |
| TickSerial_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 776.382 | 2257.748 | 9.7 | 28.2 | new | 210710908 |
| TickSerial_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 193.024 | 638.865 | 3.2 | 10.6 | new | 301449721 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 34.544 | 78.900 | 4.3 | 9.9 | new | 2012152 |
| TickSerial_OscillatingMovement_100k_200Ticks | 100000 | 1453.216 | 2976.923 | 18.2 | 37.2 | new | 18717768 |
| TickSerial_OscillatingMovement_10k_200Ticks | 10000 | 177.729 | 280.816 | 22.2 | 35.1 | new | 2794992 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 40.986 | 30.792 | 5.1 | 3.8 | old | 2024952 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 9.622 | 32.417 | 1.2 | 4.1 | new | -290512 |
| TickSerial_V1PulseMovement_100k_50Ticks | 100000 | 143.691 | 161.078 | 9.6 | 10.7 | new | 47610296 |
| TryGetComponent_Position | 1000 | 0.190 | 0.381 | 190.0 | 381.0 | new | -18576 |
| TryGetComponent_Position | 10000 | 1.122 | 1.878 | 112.2 | 187.8 | new | -869696 |
| TryGetComponent_Position | 50000 | 4.252 | 8.850 | 85.0 | 177.0 | new | -3450056 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3536`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.138 ms`, alloc `108280`
- `PatchComponent_Position` scale `1000` mean `0.239 ms`, alloc `186056`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.963 ms`, alloc `2566552`
- `PatchComponent_Position` scale `10000` mean `1.185 ms`, alloc `2826880`
- `CreateDefaultImmediate_Position` scale `50000` mean `1.680 ms`, alloc `10431808`
- `PatchComponent_Position` scale `50000` mean `2.908 ms`, alloc `12176728`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.008 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.188 ms`, alloc `205640`
- `GetComponentRef_Write_Position` scale `10000` mean `1.628 ms`, alloc `3696576`
- `GetComponentRef_Write_Position` scale `50000` mean `3.697 ms`, alloc `15622872`
