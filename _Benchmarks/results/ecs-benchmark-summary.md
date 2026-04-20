# ECS Benchmark Summary

Generated: 2026-04-20T15:01:42.7566216+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.068 | 0.323 | 68.3 | 322.6 | new | -7272 |
| CreateDefaultDeferred_Position | 10000 | 0.279 | 0.824 | 27.9 | 82.4 | new | -816248 |
| CreateDefaultDeferred_Position | 50000 | 1.629 | 5.829 | 32.6 | 116.6 | new | -2901168 |
| CreateDestroyReuseChurn_Position | 1000 | 0.599 | 0.630 | 299.5 | 315.0 | new | 26888 |
| CreateDestroyReuseChurn_Position | 10000 | 1.364 | 1.885 | 70.1 | 96.9 | new | 11024 |
| CreateDestroyReuseChurn_Position | 50000 | 1.987 | 2.975 | 20.0 | 29.9 | new | 16016 |
| DeferredAdd_Velocity | 1000 | 0.302 | 0.421 | 302.3 | 421.4 | new | 3256 |
| DeferredAdd_Velocity | 10000 | 1.886 | 3.290 | 188.6 | 329.0 | new | -811968 |
| DeferredAdd_Velocity | 50000 | 5.715 | 7.010 | 114.3 | 140.2 | new | -3143938 |
| DeferredDestroy_SparseVictims | 1000 | 0.164 | 0.205 | 327.4 | 410.2 | new | -18728 |
| DeferredDestroy_SparseVictims | 10000 | 1.499 | 1.445 | 299.7 | 289.0 | old | -919720 |
| DeferredDestroy_SparseVictims | 50000 | 2.444 | 3.320 | 97.8 | 132.8 | new | -3569369 |
| DeferredDestroy | 1000 | 0.153 | 0.213 | 152.6 | 212.5 | new | -18920 |
| DeferredDestroy | 10000 | 1.161 | 0.978 | 116.1 | 97.8 | old | -970792 |
| DeferredDestroy | 50000 | 2.338 | 3.531 | 46.8 | 70.6 | new | -3693624 |
| DeferredRemove_Velocity | 1000 | 0.285 | 0.416 | 285.1 | 415.5 | new | -4896 |
| DeferredRemove_Velocity | 10000 | 2.089 | 3.270 | 208.9 | 327.0 | new | -610496 |
| DeferredRemove_Velocity | 50000 | 3.958 | 7.671 | 79.2 | 153.4 | new | -2399136 |
| DirtyDrain_Position | 1000 | 0.202 | 0.235 | 202.1 | 234.6 | new | -19584 |
| DirtyDrain_Position | 10000 | 0.957 | 1.460 | 95.7 | 146.0 | new | -869696 |
| DirtyDrain_Position | 50000 | 2.915 | 3.948 | 58.3 | 79.0 | new | -3446134 |
| HotLoop_DeferredAdd_Velocity | 50000 | 3.882 | 3.850 | 77.6 | 77.0 | old | 305232 |
| HotLoop_DirtyDrain_Position | 50000 | 0.876 | 0.862 | 17.5 | 17.2 | old | -1344 |
| HotLoop_Query1_Read_Position | 50000 | 0.252 | 0.167 | 5.0 | 3.3 | old | -1344 |
| HotLoop_SetComponent_Position | 50000 | 0.709 | 0.738 | 14.2 | 14.8 | new | -1344 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 197.563 | 189.473 | 24.7 | 23.7 | old | -748272 |
| HotLoop_TickParallel_OscillatingMovement | 10000 | 153.613 | 378.406 | 19.2 | 47.3 | new | 99456 |
| HotLoop_TickParallel_OscillatingMovement | 100000 | 1121.671 | 3721.844 | 14.0 | 46.5 | new | 99456 |
| HotLoop_TickParallel_QuerySystems | 10000 | 39.365 | 41.595 | 4.9 | 5.2 | new | 25856 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 3.373 | 45.356 | 0.4 | 5.7 | new | 64256 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 65.745 | 70.093 | 8.2 | 8.8 | new | -1344 |
| HotLoop_TickSerial_OscillatingMovement | 10000 | 194.360 | 325.957 | 24.3 | 40.7 | new | 72256 |
| HotLoop_TickSerial_OscillatingMovement | 100000 | 1681.327 | 3031.965 | 21.0 | 37.9 | new | 72256 |
| HotLoop_TickSerial_QuerySystems | 10000 | 15.383 | 17.297 | 1.9 | 2.2 | new | -27002 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 6.886 | 20.750 | 0.9 | 2.6 | new | -1344 |
| HotLoop_TryGetComponent_Position | 50000 | 0.227 | 0.248 | 4.5 | 5.0 | new | -1344 |
| MigrationCycle_AddRemoveVelocity | 1000 | 1.615 | 2.118 | 807.5 | 1059.2 | new | 83200 |
| MigrationCycle_AddRemoveVelocity | 10000 | 2.074 | 4.530 | 103.7 | 226.5 | new | 89304 |
| MigrationCycle_AddRemoveVelocity | 50000 | 4.088 | 7.787 | 40.9 | 77.9 | new | 1107522 |
| PersistenceView_Filtered_Position | 1000 | 0.234 | 0.316 | 234.3 | 316.0 | new | -19936 |
| PersistenceView_Filtered_Position | 10000 | 1.837 | 2.308 | 183.7 | 230.8 | new | -880392 |
| PersistenceView_Filtered_Position | 50000 | 3.862 | 5.977 | 77.2 | 119.5 | new | -3495528 |
| Query1_Read_Position | 1000 | 0.145 | 0.202 | 144.8 | 201.8 | new | -18640 |
| Query1_Read_Position | 10000 | 1.011 | 1.636 | 101.1 | 163.6 | new | -869760 |
| Query1_Read_Position | 50000 | 3.788 | 5.042 | 75.8 | 100.8 | new | -3450120 |
| Query2_Read_PositionVelocity | 1000 | 0.190 | 0.263 | 190.3 | 262.9 | new | -18640 |
| Query2_Read_PositionVelocity | 10000 | 1.580 | 2.647 | 158.0 | 264.7 | new | -871776 |
| Query2_Read_PositionVelocity | 50000 | 5.258 | 6.766 | 105.2 | 135.3 | new | -3450153 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.255 | 0.329 | 254.7 | 328.5 | new | -18640 |
| Query3_Read_PositionVelocityHealth | 10000 | 2.497 | 3.436 | 249.7 | 343.6 | new | -871104 |
| Query3_Read_PositionVelocityHealth | 50000 | 8.045 | 9.086 | 160.9 | 181.7 | new | -3450129 |
| SetComponent_Position | 1000 | 0.703 | 1.449 | 702.7 | 1449.5 | new | -18576 |
| SetComponent_Position | 10000 | 1.527 | 2.438 | 152.7 | 243.8 | new | -869696 |
| SetComponent_Position | 50000 | 4.597 | 8.564 | 91.9 | 171.3 | new | -3450056 |
| TickParallel_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 487.536 | 2037.134 | 6.1 | 25.5 | new | 209541600 |
| TickParallel_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 333.299 | 542.877 | 5.6 | 9.0 | new | 178714558 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 201.735 | 194.304 | 25.2 | 24.3 | old | -2203717 |
| TickParallel_OscillatingMovement_100k_200Ticks | 100000 | 1030.059 | 3700.769 | 12.9 | 46.3 | new | 18164673 |
| TickParallel_OscillatingMovement_10k_200Ticks | 10000 | 135.253 | 382.330 | 16.9 | 47.8 | new | 2241696 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 48.194 | 45.312 | 6.0 | 5.7 | old | 3898564 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 6.894 | 60.462 | 0.9 | 7.6 | new | -810536 |
| TickParallel_V1PulseMovement_100k_50Ticks | 100000 | 145.584 | 216.214 | 9.7 | 14.4 | new | 49598496 |
| TickSerial_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 883.799 | 2257.748 | 11.0 | 28.2 | new | 210851516 |
| TickSerial_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 192.546 | 638.865 | 3.2 | 10.6 | new | 304084625 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 77.671 | 78.900 | 9.7 | 9.9 | new | -868560 |
| TickSerial_OscillatingMovement_100k_200Ticks | 100000 | 1558.459 | 2976.923 | 19.5 | 37.2 | new | 18717768 |
| TickSerial_OscillatingMovement_10k_200Ticks | 10000 | 183.576 | 280.816 | 22.9 | 35.1 | new | 2794992 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 19.954 | 30.792 | 2.5 | 3.8 | new | 474904 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 9.774 | 32.417 | 1.2 | 4.1 | new | -868552 |
| TickSerial_V1PulseMovement_100k_50Ticks | 100000 | 142.004 | 161.078 | 9.5 | 10.7 | new | 47610296 |
| TryGetComponent_Position | 1000 | 0.188 | 0.381 | 187.7 | 381.0 | new | -18576 |
| TryGetComponent_Position | 10000 | 1.066 | 1.878 | 106.6 | 187.8 | new | -869696 |
| TryGetComponent_Position | 50000 | 4.037 | 8.850 | 80.7 | 177.0 | new | -3450056 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.010 ms`, alloc `3536`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.130 ms`, alloc `108280`
- `PatchComponent_Position` scale `1000` mean `0.217 ms`, alloc `186056`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.823 ms`, alloc `2566552`
- `PatchComponent_Position` scale `10000` mean `1.045 ms`, alloc `2826880`
- `CreateDefaultImmediate_Position` scale `50000` mean `1.646 ms`, alloc `10431808`
- `PatchComponent_Position` scale `50000` mean `2.879 ms`, alloc `12176728`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.008 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.188 ms`, alloc `205640`
- `GetComponentRef_Write_Position` scale `10000` mean `1.628 ms`, alloc `3696576`
- `GetComponentRef_Write_Position` scale `50000` mean `3.697 ms`, alloc `15622872`
