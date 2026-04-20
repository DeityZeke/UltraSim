# ECS Benchmark Summary

Generated: 2026-04-20T14:43:36.9147175+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.075 | 0.323 | 75.0 | 322.6 | new | 1272 |
| CreateDefaultDeferred_Position | 10000 | 0.299 | 0.824 | 29.9 | 82.4 | new | -633848 |
| CreateDefaultDeferred_Position | 50000 | 1.473 | 5.829 | 29.5 | 116.6 | new | -2252322 |
| CreateDestroyReuseChurn_Position | 1000 | 0.611 | 0.630 | 305.4 | 315.0 | new | 31992 |
| CreateDestroyReuseChurn_Position | 10000 | 1.470 | 1.885 | 75.6 | 96.9 | new | 27616 |
| CreateDestroyReuseChurn_Position | 50000 | 2.416 | 2.975 | 24.3 | 29.9 | new | 32608 |
| DeferredAdd_Velocity | 1000 | 0.311 | 0.421 | 311.4 | 421.4 | new | 3256 |
| DeferredAdd_Velocity | 10000 | 1.894 | 3.290 | 189.4 | 329.0 | new | -811968 |
| DeferredAdd_Velocity | 50000 | 5.777 | 7.010 | 115.5 | 140.2 | new | -3143936 |
| DeferredDestroy_SparseVictims | 1000 | 0.171 | 0.205 | 341.3 | 410.2 | new | -10240 |
| DeferredDestroy_SparseVictims | 10000 | 1.600 | 1.445 | 320.0 | 289.0 | old | -737376 |
| DeferredDestroy_SparseVictims | 50000 | 2.692 | 3.320 | 107.7 | 132.8 | new | -2920555 |
| DeferredDestroy | 1000 | 0.162 | 0.213 | 162.3 | 212.5 | new | -2024 |
| DeferredDestroy | 10000 | 1.231 | 0.978 | 123.1 | 97.8 | old | -606280 |
| DeferredDestroy | 50000 | 2.434 | 3.531 | 48.7 | 70.6 | new | -2396210 |
| DeferredRemove_Velocity | 1000 | 0.303 | 0.416 | 303.0 | 415.5 | new | -4896 |
| DeferredRemove_Velocity | 10000 | 2.186 | 3.270 | 218.6 | 327.0 | new | -610496 |
| DeferredRemove_Velocity | 50000 | 4.346 | 7.671 | 86.9 | 153.4 | new | -2399136 |
| DirtyDrain_Position | 1000 | 0.205 | 0.235 | 205.3 | 234.6 | new | -19584 |
| DirtyDrain_Position | 10000 | 1.519 | 1.460 | 151.9 | 146.0 | old | -869696 |
| DirtyDrain_Position | 50000 | 2.914 | 3.948 | 58.3 | 79.0 | new | -3446134 |
| HotLoop_DeferredAdd_Velocity | 50000 | 3.722 | 3.850 | 74.4 | 77.0 | new | 305232 |
| HotLoop_DirtyDrain_Position | 50000 | 0.844 | 0.862 | 16.9 | 17.2 | new | -1344 |
| HotLoop_Query1_Read_Position | 50000 | 0.245 | 0.167 | 4.9 | 3.3 | old | -1344 |
| HotLoop_SetComponent_Position | 50000 | 0.678 | 0.738 | 13.6 | 14.8 | new | -1344 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 39.990 | 189.473 | 5.0 | 23.7 | new | -786672 |
| HotLoop_TickParallel_OscillatingMovement | 10000 | 152.801 | 378.406 | 19.1 | 47.3 | new | 99456 |
| HotLoop_TickParallel_OscillatingMovement | 100000 | 1152.086 | 3721.844 | 14.4 | 46.5 | new | 99456 |
| HotLoop_TickParallel_QuerySystems | 10000 | 40.600 | 41.595 | 5.1 | 5.2 | new | 25856 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 3.247 | 45.356 | 0.4 | 5.7 | new | 64256 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 12.982 | 70.093 | 1.6 | 8.8 | new | -39744 |
| HotLoop_TickSerial_OscillatingMovement | 10000 | 195.921 | 325.957 | 24.5 | 40.7 | new | 72256 |
| HotLoop_TickSerial_OscillatingMovement | 100000 | 1675.642 | 3031.965 | 20.9 | 37.9 | new | 72256 |
| HotLoop_TickSerial_QuerySystems | 10000 | 12.901 | 17.297 | 1.6 | 2.2 | new | -26944 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 7.493 | 20.750 | 0.9 | 2.6 | new | -1344 |
| HotLoop_TryGetComponent_Position | 50000 | 0.223 | 0.248 | 4.5 | 5.0 | new | -1344 |
| MigrationCycle_AddRemoveVelocity | 1000 | 1.658 | 2.118 | 829.0 | 1059.2 | new | 83200 |
| MigrationCycle_AddRemoveVelocity | 10000 | 2.086 | 4.530 | 104.3 | 226.5 | new | 89304 |
| MigrationCycle_AddRemoveVelocity | 50000 | 4.320 | 7.787 | 43.2 | 77.9 | new | 1107532 |
| PersistenceView_Filtered_Position | 1000 | 0.236 | 0.316 | 235.8 | 316.0 | new | -19936 |
| PersistenceView_Filtered_Position | 10000 | 2.042 | 2.308 | 204.2 | 230.8 | new | -880392 |
| PersistenceView_Filtered_Position | 50000 | 3.933 | 5.977 | 78.7 | 119.5 | new | -3495528 |
| Query1_Read_Position | 1000 | 0.146 | 0.202 | 146.2 | 201.8 | new | -18640 |
| Query1_Read_Position | 10000 | 1.011 | 1.636 | 101.1 | 163.6 | new | -869760 |
| Query1_Read_Position | 50000 | 3.654 | 5.042 | 73.1 | 100.8 | new | -3450120 |
| Query2_Read_PositionVelocity | 1000 | 0.193 | 0.263 | 193.0 | 262.9 | new | -18640 |
| Query2_Read_PositionVelocity | 10000 | 1.582 | 2.647 | 158.2 | 264.7 | new | -871776 |
| Query2_Read_PositionVelocity | 50000 | 5.007 | 6.766 | 100.1 | 135.3 | new | -3450153 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.266 | 0.329 | 265.8 | 328.5 | new | -18640 |
| Query3_Read_PositionVelocityHealth | 10000 | 2.683 | 3.436 | 268.3 | 343.6 | new | -871104 |
| Query3_Read_PositionVelocityHealth | 50000 | 6.519 | 9.086 | 130.4 | 181.7 | new | -3450129 |
| SetComponent_Position | 1000 | 0.730 | 1.449 | 729.6 | 1449.5 | new | -18576 |
| SetComponent_Position | 10000 | 1.501 | 2.438 | 150.1 | 243.8 | new | -869696 |
| SetComponent_Position | 50000 | 4.793 | 8.564 | 95.9 | 171.3 | new | -3450056 |
| TickParallel_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 482.408 | 2037.134 | 6.0 | 25.5 | new | 209695808 |
| TickParallel_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 331.326 | 542.877 | 5.5 | 9.0 | new | 179368206 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 44.928 | 194.304 | 5.6 | 24.3 | new | -3311557 |
| TickParallel_OscillatingMovement_100k_200Ticks | 100000 | 1031.514 | 3700.769 | 12.9 | 46.3 | new | 18164664 |
| TickParallel_OscillatingMovement_10k_200Ticks | 10000 | 139.262 | 382.330 | 17.4 | 47.8 | new | 2241888 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 47.600 | 45.312 | 5.9 | 5.7 | old | 4165792 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 6.807 | 60.462 | 0.9 | 7.6 | new | -810536 |
| TickParallel_V1PulseMovement_100k_50Ticks | 100000 | 143.914 | 216.214 | 9.6 | 14.4 | new | 49598496 |
| TickSerial_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 771.675 | 2257.748 | 9.6 | 28.2 | new | 211505204 |
| TickSerial_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 189.526 | 638.865 | 3.2 | 10.6 | new | 304934611 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 18.457 | 78.900 | 2.3 | 9.9 | new | 462104 |
| TickSerial_OscillatingMovement_100k_200Ticks | 100000 | 1455.006 | 2976.923 | 18.2 | 37.2 | new | 18717768 |
| TickSerial_OscillatingMovement_10k_200Ticks | 10000 | 176.726 | 280.816 | 22.1 | 35.1 | new | 2794992 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 18.712 | 30.792 | 2.3 | 3.8 | new | 474904 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 10.025 | 32.417 | 1.3 | 4.1 | new | -868552 |
| TickSerial_V1PulseMovement_100k_50Ticks | 100000 | 138.814 | 161.078 | 9.3 | 10.7 | new | 47610296 |
| TryGetComponent_Position | 1000 | 0.187 | 0.381 | 187.2 | 381.0 | new | -18576 |
| TryGetComponent_Position | 10000 | 1.103 | 1.878 | 110.3 | 187.8 | new | -869696 |
| TryGetComponent_Position | 50000 | 4.200 | 8.850 | 84.0 | 177.0 | new | -3450056 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3536`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.135 ms`, alloc `108280`
- `PatchComponent_Position` scale `1000` mean `0.302 ms`, alloc `186056`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.819 ms`, alloc `2566552`
- `PatchComponent_Position` scale `10000` mean `1.170 ms`, alloc `2826880`
- `CreateDefaultImmediate_Position` scale `50000` mean `1.830 ms`, alloc `10431808`
- `PatchComponent_Position` scale `50000` mean `2.950 ms`, alloc `12176728`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.008 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.188 ms`, alloc `205640`
- `GetComponentRef_Write_Position` scale `10000` mean `1.628 ms`, alloc `3696576`
- `GetComponentRef_Write_Position` scale `50000` mean `3.697 ms`, alloc `15622872`
