# ECS Benchmark Summary

Generated: 2026-04-20T13:37:52.6960441+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.072 | 0.323 | 72.5 | 322.6 | new | 1272 |
| CreateDefaultDeferred_Position | 10000 | 0.296 | 0.824 | 29.6 | 82.4 | new | -633848 |
| CreateDefaultDeferred_Position | 50000 | 1.430 | 5.829 | 28.6 | 116.6 | new | -2252317 |
| CreateDestroyReuseChurn_Position | 1000 | 0.657 | 0.630 | 328.4 | 315.0 | old | 31992 |
| CreateDestroyReuseChurn_Position | 10000 | 1.152 | 1.885 | 59.2 | 96.9 | new | 27616 |
| CreateDestroyReuseChurn_Position | 50000 | 2.492 | 2.975 | 25.1 | 29.9 | new | 32608 |
| DeferredAdd_Velocity | 1000 | 0.537 | 0.421 | 537.0 | 421.4 | old | -21432 |
| DeferredAdd_Velocity | 10000 | 2.867 | 3.290 | 286.7 | 329.0 | new | 108696 |
| DeferredAdd_Velocity | 50000 | 5.492 | 7.010 | 109.8 | 140.2 | new | 77936 |
| DeferredDestroy_SparseVictims | 1000 | 0.239 | 0.205 | 477.9 | 410.2 | old | -34928 |
| DeferredDestroy_SparseVictims | 10000 | 1.049 | 1.445 | 209.8 | 289.0 | new | 182616 |
| DeferredDestroy_SparseVictims | 50000 | 3.292 | 3.320 | 131.7 | 132.8 | new | 300621 |
| DeferredDestroy | 1000 | 0.257 | 0.213 | 256.6 | 212.5 | old | -26712 |
| DeferredDestroy | 10000 | 2.520 | 0.978 | 252.0 | 97.8 | old | 313712 |
| DeferredDestroy | 50000 | 3.424 | 3.531 | 68.5 | 70.6 | new | 825072 |
| DeferredRemove_Velocity | 1000 | 0.386 | 0.416 | 386.4 | 415.5 | new | -21616 |
| DeferredRemove_Velocity | 10000 | 3.224 | 3.270 | 322.4 | 327.0 | new | 491320 |
| DeferredRemove_Velocity | 50000 | 5.897 | 7.671 | 117.9 | 153.4 | new | 1470384 |
| DirtyDrain_Position | 1000 | 0.281 | 0.235 | 280.7 | 234.6 | old | -44272 |
| DirtyDrain_Position | 10000 | 1.220 | 1.460 | 122.0 | 146.0 | new | 50296 |
| DirtyDrain_Position | 50000 | 4.148 | 3.948 | 83.0 | 79.0 | old | -224949 |
| HotLoop_DeferredAdd_Velocity | 50000 | 2.958 | 3.850 | 59.2 | 77.0 | new | 305232 |
| HotLoop_DirtyDrain_Position | 50000 | 0.975 | 0.862 | 19.5 | 17.2 | old | -1344 |
| HotLoop_Query1_Read_Position | 50000 | 0.238 | 0.167 | 4.8 | 3.3 | old | -1344 |
| HotLoop_SetComponent_Position | 50000 | 0.829 | 0.738 | 16.6 | 14.8 | old | -1344 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 64.235 | 189.473 | 8.0 | 23.7 | new | -786672 |
| HotLoop_TickParallel_OscillatingMovement | 10000 | 145.573 | 378.406 | 18.2 | 47.3 | new | 99456 |
| HotLoop_TickParallel_OscillatingMovement | 100000 | 1054.324 | 3721.844 | 13.2 | 46.5 | new | 99456 |
| HotLoop_TickParallel_QuerySystems | 10000 | 64.644 | 41.595 | 8.1 | 5.2 | old | 293083 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 45.635 | 45.356 | 5.7 | 5.7 | old | 64256 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 27.547 | 70.093 | 3.4 | 8.8 | new | -26944 |
| HotLoop_TickSerial_OscillatingMovement | 10000 | 175.134 | 325.957 | 21.9 | 40.7 | new | 72256 |
| HotLoop_TickSerial_OscillatingMovement | 100000 | 1484.263 | 3031.965 | 18.6 | 37.9 | new | 72256 |
| HotLoop_TickSerial_QuerySystems | 10000 | 26.807 | 17.297 | 3.4 | 2.2 | old | -14144 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 17.355 | 20.750 | 2.2 | 2.6 | new | 11456 |
| HotLoop_TryGetComponent_Position | 50000 | 0.226 | 0.248 | 4.5 | 5.0 | new | -1344 |
| MigrationCycle_AddRemoveVelocity | 1000 | 1.731 | 2.118 | 865.4 | 1059.2 | new | 58512 |
| MigrationCycle_AddRemoveVelocity | 10000 | 1.851 | 4.530 | 92.5 | 226.5 | new | 1213584 |
| MigrationCycle_AddRemoveVelocity | 50000 | 6.097 | 7.787 | 61.0 | 77.9 | new | 4825860 |
| PersistenceView_Filtered_Position | 1000 | 0.361 | 0.316 | 361.0 | 316.0 | old | -36736 |
| PersistenceView_Filtered_Position | 10000 | 1.981 | 2.308 | 198.1 | 230.8 | new | 308320 |
| PersistenceView_Filtered_Position | 50000 | 5.661 | 5.977 | 113.2 | 119.5 | new | 694153 |
| Query1_Read_Position | 1000 | 0.234 | 0.202 | 234.0 | 201.8 | old | -47440 |
| Query1_Read_Position | 10000 | 2.117 | 1.636 | 211.7 | 163.6 | old | 46120 |
| Query1_Read_Position | 50000 | 4.182 | 5.042 | 83.6 | 100.8 | new | -228976 |
| Query2_Read_PositionVelocity | 1000 | 0.285 | 0.263 | 284.9 | 262.9 | old | -39472 |
| Query2_Read_PositionVelocity | 10000 | 3.046 | 2.647 | 304.6 | 264.7 | old | 226600 |
| Query2_Read_PositionVelocity | 50000 | 6.887 | 6.766 | 137.7 | 135.3 | old | 419295 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.401 | 0.329 | 401.1 | 328.5 | old | -35440 |
| Query3_Read_PositionVelocityHealth | 10000 | 4.098 | 3.436 | 409.8 | 343.6 | old | 317608 |
| Query3_Read_PositionVelocityHealth | 50000 | 9.074 | 9.086 | 181.5 | 181.7 | new | 743615 |
| SetComponent_Position | 1000 | 0.302 | 1.449 | 302.0 | 1449.5 | new | -43264 |
| SetComponent_Position | 10000 | 2.560 | 2.438 | 256.0 | 243.8 | old | 50296 |
| SetComponent_Position | 50000 | 5.734 | 8.564 | 114.7 | 171.3 | new | -224800 |
| TickParallel_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 579.914 | 2037.134 | 7.2 | 25.5 | new | 218778340 |
| TickParallel_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 336.992 | 542.877 | 5.6 | 9.0 | new | 191576720 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 68.472 | 194.304 | 8.6 | 24.3 | new | -1903248 |
| TickParallel_OscillatingMovement_100k_200Ticks | 100000 | 984.258 | 3700.769 | 12.3 | 46.3 | new | 3584408 |
| TickParallel_OscillatingMovement_10k_200Ticks | 10000 | 132.948 | 382.330 | 16.6 | 47.8 | new | 691576 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 69.501 | 45.312 | 8.7 | 5.7 | old | 5039876 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 49.596 | 60.462 | 6.2 | 7.6 | new | 287387 |
| TickParallel_V1PulseMovement_100k_50Ticks | 100000 | 130.483 | 216.214 | 8.7 | 14.4 | new | 52346280 |
| TickSerial_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 838.880 | 2257.748 | 10.5 | 28.2 | new | 3981273 |
| TickSerial_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 186.741 | 638.865 | 3.1 | 10.6 | new | 29734580 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 28.473 | 78.900 | 3.6 | 9.9 | new | 1776400 |
| TickSerial_OscillatingMovement_100k_200Ticks | 100000 | 1267.715 | 2976.923 | 15.8 | 37.2 | new | 3572272 |
| TickSerial_OscillatingMovement_10k_200Ticks | 10000 | 157.575 | 280.816 | 19.7 | 35.1 | new | 679440 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 42.120 | 30.792 | 5.3 | 3.8 | old | 1789200 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 40.956 | 32.417 | 5.1 | 4.1 | old | 241864 |
| TickSerial_V1PulseMovement_100k_50Ticks | 100000 | 78.708 | 161.078 | 5.2 | 10.7 | new | 41824904 |
| TryGetComponent_Position | 1000 | 0.330 | 0.381 | 329.6 | 381.0 | new | -43264 |
| TryGetComponent_Position | 10000 | 2.085 | 1.878 | 208.5 | 187.8 | old | 50296 |
| TryGetComponent_Position | 50000 | 4.710 | 8.850 | 94.2 | 177.0 | new | -224800 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3536`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.132 ms`, alloc `108280`
- `PatchComponent_Position` scale `1000` mean `0.297 ms`, alloc `161368`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.519 ms`, alloc `2566552`
- `PatchComponent_Position` scale `10000` mean `1.306 ms`, alloc `3746872`
- `CreateDefaultImmediate_Position` scale `50000` mean `1.550 ms`, alloc `10431808`
- `PatchComponent_Position` scale `50000` mean `4.301 ms`, alloc `15397899`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.008 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.188 ms`, alloc `205640`
- `GetComponentRef_Write_Position` scale `10000` mean `1.628 ms`, alloc `3696576`
- `GetComponentRef_Write_Position` scale `50000` mean `3.697 ms`, alloc `15622872`
