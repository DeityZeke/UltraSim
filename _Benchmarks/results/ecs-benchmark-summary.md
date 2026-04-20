# ECS Benchmark Summary

Generated: 2026-04-20T03:59:38.3468073+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.074 | 0.323 | 74.4 | 322.6 | new | 1272 |
| CreateDefaultDeferred_Position | 10000 | 0.306 | 0.824 | 30.6 | 82.4 | new | -633848 |
| CreateDefaultDeferred_Position | 50000 | 1.382 | 5.829 | 27.6 | 116.6 | new | -2252759 |
| CreateDestroyReuseChurn_Position | 1000 | 0.672 | 0.630 | 336.2 | 315.0 | old | -800 |
| CreateDestroyReuseChurn_Position | 10000 | 1.294 | 1.885 | 66.5 | 96.9 | new | 27616 |
| CreateDestroyReuseChurn_Position | 50000 | 2.606 | 2.975 | 26.2 | 29.9 | new | 32608 |
| DeferredAdd_Velocity | 1000 | 0.437 | 0.421 | 436.9 | 421.4 | old | -21432 |
| DeferredAdd_Velocity | 10000 | 3.149 | 3.290 | 314.9 | 329.0 | new | 108696 |
| DeferredAdd_Velocity | 50000 | 6.318 | 7.010 | 126.4 | 140.2 | new | 77240 |
| DeferredDestroy_SparseVictims | 1000 | 0.259 | 0.205 | 518.2 | 410.2 | old | -34928 |
| DeferredDestroy_SparseVictims | 10000 | 0.798 | 1.445 | 159.7 | 289.0 | new | 182616 |
| DeferredDestroy_SparseVictims | 50000 | 3.383 | 3.320 | 135.3 | 132.8 | old | 300635 |
| DeferredDestroy | 1000 | 0.274 | 0.213 | 273.8 | 212.5 | old | -26712 |
| DeferredDestroy | 10000 | 1.306 | 0.978 | 130.6 | 97.8 | old | 313712 |
| DeferredDestroy | 50000 | 3.937 | 3.531 | 78.7 | 70.6 | old | 824971 |
| DeferredRemove_Velocity | 1000 | 0.645 | 0.416 | 644.8 | 415.5 | old | -21616 |
| DeferredRemove_Velocity | 10000 | 3.879 | 3.270 | 387.9 | 327.0 | old | 491320 |
| DeferredRemove_Velocity | 50000 | 6.653 | 7.671 | 133.1 | 153.4 | new | 1471032 |
| DirtyDrain_Position | 1000 | 0.314 | 0.235 | 313.8 | 234.6 | old | -44272 |
| DirtyDrain_Position | 10000 | 0.767 | 1.460 | 76.7 | 146.0 | new | 50296 |
| DirtyDrain_Position | 50000 | 3.740 | 3.948 | 74.8 | 79.0 | new | -224958 |
| HotLoop_DeferredAdd_Velocity | 50000 | 3.141 | 3.850 | 62.8 | 77.0 | new | 305232 |
| HotLoop_DirtyDrain_Position | 50000 | 0.887 | 0.862 | 17.7 | 17.2 | old | -1344 |
| HotLoop_Query1_Read_Position | 50000 | 0.238 | 0.167 | 4.8 | 3.3 | old | -1344 |
| HotLoop_SetComponent_Position | 50000 | 0.693 | 0.738 | 13.9 | 14.8 | new | -1344 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 62.170 | 189.473 | 7.8 | 23.7 | new | -786672 |
| HotLoop_TickParallel_OscillatingMovement | 10000 | 148.755 | 378.406 | 18.6 | 47.3 | new | 99456 |
| HotLoop_TickParallel_OscillatingMovement | 100000 | 1082.505 | 3721.844 | 13.5 | 46.5 | new | 99456 |
| HotLoop_TickParallel_QuerySystems | 10000 | 62.370 | 41.595 | 7.8 | 5.2 | old | 293083 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 45.850 | 45.356 | 5.7 | 5.7 | old | 64256 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 24.546 | 70.093 | 3.1 | 8.8 | new | -26944 |
| HotLoop_TickSerial_OscillatingMovement | 10000 | 183.831 | 325.957 | 23.0 | 40.7 | new | 72256 |
| HotLoop_TickSerial_OscillatingMovement | 100000 | 1539.865 | 3031.965 | 19.2 | 37.9 | new | 72256 |
| HotLoop_TickSerial_QuerySystems | 10000 | 24.314 | 17.297 | 3.0 | 2.2 | old | -14144 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 18.058 | 20.750 | 2.3 | 2.6 | new | 11456 |
| HotLoop_TryGetComponent_Position | 50000 | 0.225 | 0.248 | 4.5 | 5.0 | new | -1344 |
| MigrationCycle_AddRemoveVelocity | 1000 | 1.882 | 2.118 | 941.2 | 1059.2 | new | -7072 |
| MigrationCycle_AddRemoveVelocity | 10000 | 1.550 | 4.530 | 77.5 | 226.5 | new | 369248 |
| MigrationCycle_AddRemoveVelocity | 50000 | 5.311 | 7.787 | 53.1 | 77.9 | new | 1128664 |
| PersistenceView_Filtered_Position | 1000 | 0.404 | 0.316 | 404.4 | 316.0 | old | -32472 |
| PersistenceView_Filtered_Position | 10000 | 1.500 | 2.308 | 150.0 | 230.8 | new | 312584 |
| PersistenceView_Filtered_Position | 50000 | 5.162 | 5.977 | 103.2 | 119.5 | new | 698480 |
| Query1_Read_Position | 1000 | 0.237 | 0.202 | 237.0 | 201.8 | old | -43328 |
| Query1_Read_Position | 10000 | 2.374 | 1.636 | 237.4 | 163.6 | old | 50232 |
| Query1_Read_Position | 50000 | 4.242 | 5.042 | 84.8 | 100.8 | new | -224864 |
| Query2_Read_PositionVelocity | 1000 | 0.335 | 0.263 | 335.4 | 262.9 | old | -35352 |
| Query2_Read_PositionVelocity | 10000 | 3.561 | 2.647 | 356.1 | 264.7 | old | 230720 |
| Query2_Read_PositionVelocity | 50000 | 6.699 | 6.766 | 134.0 | 135.3 | new | 423424 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.439 | 0.329 | 438.8 | 328.5 | old | -31312 |
| Query3_Read_PositionVelocityHealth | 10000 | 4.567 | 3.436 | 456.7 | 343.6 | old | 321736 |
| Query3_Read_PositionVelocityHealth | 50000 | 8.960 | 9.086 | 179.2 | 181.7 | new | 747743 |
| SetComponent_Position | 1000 | 0.338 | 1.449 | 338.2 | 1449.5 | new | -43264 |
| SetComponent_Position | 10000 | 2.718 | 2.438 | 271.8 | 243.8 | old | 50296 |
| SetComponent_Position | 50000 | 5.190 | 8.564 | 103.8 | 171.3 | new | -224800 |
| TickParallel_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 581.517 | 2037.134 | 7.3 | 25.5 | new | 218671964 |
| TickParallel_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 343.573 | 542.877 | 5.7 | 9.0 | new | 191309516 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 69.755 | 194.304 | 8.7 | 24.3 | new | -2433367 |
| TickParallel_OscillatingMovement_100k_200Ticks | 100000 | 1008.096 | 3700.769 | 12.6 | 46.3 | new | 3584504 |
| TickParallel_OscillatingMovement_10k_200Ticks | 10000 | 136.061 | 382.330 | 17.0 | 47.8 | new | 691672 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 68.298 | 45.312 | 8.5 | 5.7 | old | 5311416 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 51.756 | 60.462 | 6.5 | 7.6 | new | 291531 |
| TickParallel_V1PulseMovement_100k_50Ticks | 100000 | 127.746 | 216.214 | 8.5 | 14.4 | new | 52346280 |
| TickSerial_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 858.278 | 2257.748 | 10.7 | 28.2 | new | 3981369 |
| TickSerial_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 182.606 | 638.865 | 3.0 | 10.6 | new | 29734580 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 27.066 | 78.900 | 3.4 | 9.9 | new | 1780704 |
| TickSerial_OscillatingMovement_100k_200Ticks | 100000 | 1304.207 | 2976.923 | 16.3 | 37.2 | new | 3572368 |
| TickSerial_OscillatingMovement_10k_200Ticks | 10000 | 164.436 | 280.816 | 20.6 | 35.1 | new | 679536 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 40.982 | 30.792 | 5.1 | 3.8 | old | 1793504 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 40.219 | 32.417 | 5.0 | 4.1 | old | 245984 |
| TickSerial_V1PulseMovement_100k_50Ticks | 100000 | 74.213 | 161.078 | 4.9 | 10.7 | new | 41824904 |
| TryGetComponent_Position | 1000 | 0.365 | 0.381 | 365.2 | 381.0 | new | -43264 |
| TryGetComponent_Position | 10000 | 2.325 | 1.878 | 232.5 | 187.8 | old | 50296 |
| TryGetComponent_Position | 50000 | 4.526 | 8.850 | 90.5 | 177.0 | new | -224800 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3536`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.132 ms`, alloc `108280`
- `PatchComponent_Position` scale `1000` mean `0.300 ms`, alloc `161368`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.352 ms`, alloc `2566552`
- `PatchComponent_Position` scale `10000` mean `0.890 ms`, alloc `3746872`
- `CreateDefaultImmediate_Position` scale `50000` mean `1.636 ms`, alloc `10431856`
- `PatchComponent_Position` scale `50000` mean `3.740 ms`, alloc `15397904`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.008 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.188 ms`, alloc `205640`
- `GetComponentRef_Write_Position` scale `10000` mean `1.628 ms`, alloc `3696576`
- `GetComponentRef_Write_Position` scale `50000` mean `3.697 ms`, alloc `15622872`
