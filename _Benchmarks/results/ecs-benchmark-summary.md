# ECS Benchmark Summary

Generated: 2026-04-20T03:50:52.9901895+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.073 | 0.323 | 72.6 | 322.6 | new | 1272 |
| CreateDefaultDeferred_Position | 10000 | 0.295 | 0.824 | 29.5 | 82.4 | new | -633848 |
| CreateDefaultDeferred_Position | 50000 | 1.395 | 5.829 | 27.9 | 116.6 | new | -2252490 |
| CreateDestroyReuseChurn_Position | 1000 | 0.671 | 0.630 | 335.5 | 315.0 | old | -800 |
| CreateDestroyReuseChurn_Position | 10000 | 1.263 | 1.885 | 64.9 | 96.9 | new | 27616 |
| CreateDestroyReuseChurn_Position | 50000 | 2.668 | 2.975 | 26.9 | 29.9 | new | 32608 |
| DeferredAdd_Velocity | 1000 | 0.454 | 0.421 | 453.8 | 421.4 | old | -21432 |
| DeferredAdd_Velocity | 10000 | 3.059 | 3.290 | 305.9 | 329.0 | new | 108696 |
| DeferredAdd_Velocity | 50000 | 6.106 | 7.010 | 122.1 | 140.2 | new | 77244 |
| DeferredDestroy_SparseVictims | 1000 | 0.265 | 0.205 | 529.2 | 410.2 | old | -34928 |
| DeferredDestroy_SparseVictims | 10000 | 0.812 | 1.445 | 162.4 | 289.0 | new | 182616 |
| DeferredDestroy_SparseVictims | 50000 | 3.629 | 3.320 | 145.1 | 132.8 | old | 300631 |
| DeferredDestroy | 1000 | 0.279 | 0.213 | 279.2 | 212.5 | old | -26712 |
| DeferredDestroy | 10000 | 1.265 | 0.978 | 126.5 | 97.8 | old | 313712 |
| DeferredDestroy | 50000 | 3.989 | 3.531 | 79.8 | 70.6 | old | 824985 |
| DeferredRemove_Velocity | 1000 | 0.652 | 0.416 | 652.4 | 415.5 | old | -21616 |
| DeferredRemove_Velocity | 10000 | 2.382 | 3.270 | 238.2 | 327.0 | new | 491320 |
| DeferredRemove_Velocity | 50000 | 6.645 | 7.671 | 132.9 | 153.4 | new | 1471032 |
| DirtyDrain_Position | 1000 | 0.322 | 0.235 | 322.3 | 234.6 | old | -44272 |
| DirtyDrain_Position | 10000 | 0.865 | 1.460 | 86.5 | 146.0 | new | 50296 |
| DirtyDrain_Position | 50000 | 4.151 | 3.948 | 83.0 | 79.0 | old | -224958 |
| HotLoop_DeferredAdd_Velocity | 50000 | 3.147 | 3.850 | 62.9 | 77.0 | new | 305232 |
| HotLoop_DirtyDrain_Position | 50000 | 0.999 | 0.862 | 20.0 | 17.2 | old | -1344 |
| HotLoop_Query1_Read_Position | 50000 | 0.240 | 0.167 | 4.8 | 3.3 | old | -1344 |
| HotLoop_SetComponent_Position | 50000 | 0.851 | 0.738 | 17.0 | 14.8 | old | -1344 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 80.280 | 189.473 | 10.0 | 23.7 | new | 13056 |
| HotLoop_TickParallel_OscillatingMovement | 10000 | 150.108 | 378.406 | 18.8 | 47.3 | new | 99456 |
| HotLoop_TickParallel_OscillatingMovement | 100000 | 1087.342 | 3721.844 | 13.6 | 46.5 | new | 99456 |
| HotLoop_TickParallel_QuerySystems | 10000 | 77.479 | 41.595 | 9.7 | 5.2 | old | 25856 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 46.920 | 45.356 | 5.9 | 5.7 | old | 64256 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 35.958 | 70.093 | 4.5 | 8.8 | new | -26944 |
| HotLoop_TickSerial_OscillatingMovement | 10000 | 184.782 | 325.957 | 23.1 | 40.7 | new | 72256 |
| HotLoop_TickSerial_OscillatingMovement | 100000 | 1548.951 | 3031.965 | 19.4 | 37.9 | new | 72256 |
| HotLoop_TickSerial_QuerySystems | 10000 | 36.005 | 17.297 | 4.5 | 2.2 | old | -14144 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 16.037 | 20.750 | 2.0 | 2.6 | new | 11456 |
| HotLoop_TryGetComponent_Position | 50000 | 0.235 | 0.248 | 4.7 | 5.0 | new | -1344 |
| MigrationCycle_AddRemoveVelocity | 1000 | 1.901 | 2.118 | 950.5 | 1059.2 | new | -7072 |
| MigrationCycle_AddRemoveVelocity | 10000 | 1.610 | 4.530 | 80.5 | 226.5 | new | 369248 |
| MigrationCycle_AddRemoveVelocity | 50000 | 5.481 | 7.787 | 54.8 | 77.9 | new | 1128664 |
| PersistenceView_Filtered_Position | 1000 | 0.410 | 0.316 | 409.9 | 316.0 | old | -32472 |
| PersistenceView_Filtered_Position | 10000 | 2.076 | 2.308 | 207.6 | 230.8 | new | 312584 |
| PersistenceView_Filtered_Position | 50000 | 5.791 | 5.977 | 115.8 | 119.5 | new | 698480 |
| Query1_Read_Position | 1000 | 0.238 | 0.202 | 238.4 | 201.8 | old | -43328 |
| Query1_Read_Position | 10000 | 2.286 | 1.636 | 228.6 | 163.6 | old | 50232 |
| Query1_Read_Position | 50000 | 4.586 | 5.042 | 91.7 | 100.8 | new | -224864 |
| Query2_Read_PositionVelocity | 1000 | 0.339 | 0.263 | 339.0 | 262.9 | old | -35352 |
| Query2_Read_PositionVelocity | 10000 | 3.553 | 2.647 | 355.3 | 264.7 | old | 230720 |
| Query2_Read_PositionVelocity | 50000 | 8.762 | 6.766 | 175.2 | 135.3 | old | 423439 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.439 | 0.329 | 438.6 | 328.5 | old | -31312 |
| Query3_Read_PositionVelocityHealth | 10000 | 4.614 | 3.436 | 461.4 | 343.6 | old | 321736 |
| Query3_Read_PositionVelocityHealth | 50000 | 8.266 | 9.086 | 165.3 | 181.7 | new | 747743 |
| SetComponent_Position | 1000 | 0.333 | 1.449 | 332.7 | 1449.5 | new | -43264 |
| SetComponent_Position | 10000 | 2.817 | 2.438 | 281.7 | 243.8 | old | 50296 |
| SetComponent_Position | 50000 | 5.592 | 8.564 | 111.8 | 171.3 | new | -224805 |
| TickParallel_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 586.023 | 2037.134 | 7.3 | 25.5 | new | 221291912 |
| TickParallel_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 340.472 | 542.877 | 5.7 | 9.0 | new | 192273094 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 85.038 | 194.304 | 10.6 | 24.3 | new | -958000 |
| TickParallel_OscillatingMovement_100k_200Ticks | 100000 | 1006.422 | 3700.769 | 12.6 | 46.3 | new | 3584504 |
| TickParallel_OscillatingMovement_10k_200Ticks | 10000 | 136.791 | 382.330 | 17.1 | 47.8 | new | 691681 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 85.946 | 45.312 | 10.7 | 5.7 | old | 6253683 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 49.275 | 60.462 | 6.2 | 7.6 | new | 291462 |
| TickParallel_V1PulseMovement_100k_50Ticks | 100000 | 155.900 | 216.214 | 10.4 | 14.4 | new | 62593000 |
| TickSerial_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 847.401 | 2257.748 | 10.6 | 28.2 | new | 3981369 |
| TickSerial_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 186.313 | 638.865 | 3.1 | 10.6 | new | 29734580 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 38.127 | 78.900 | 4.8 | 9.9 | new | 207768 |
| TickSerial_OscillatingMovement_100k_200Ticks | 100000 | 1309.284 | 2976.923 | 16.4 | 37.2 | new | 3572368 |
| TickSerial_OscillatingMovement_10k_200Ticks | 10000 | 161.128 | 280.816 | 20.1 | 35.1 | new | 679536 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 49.675 | 30.792 | 6.2 | 3.8 | old | 220568 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 29.143 | 32.417 | 3.6 | 4.1 | new | 245984 |
| TickSerial_V1PulseMovement_100k_50Ticks | 100000 | 101.830 | 161.078 | 6.8 | 10.7 | new | 29241920 |
| TryGetComponent_Position | 1000 | 0.359 | 0.381 | 359.1 | 381.0 | new | -43264 |
| TryGetComponent_Position | 10000 | 2.295 | 1.878 | 229.5 | 187.8 | old | 50296 |
| TryGetComponent_Position | 50000 | 4.704 | 8.850 | 94.1 | 177.0 | new | -224800 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3536`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.130 ms`, alloc `108280`
- `PatchComponent_Position` scale `1000` mean `0.306 ms`, alloc `161368`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.353 ms`, alloc `2566552`
- `PatchComponent_Position` scale `10000` mean `0.968 ms`, alloc `3746872`
- `CreateDefaultImmediate_Position` scale `50000` mean `1.583 ms`, alloc `10431856`
- `PatchComponent_Position` scale `50000` mean `4.113 ms`, alloc `15397904`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.008 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.188 ms`, alloc `205640`
- `GetComponentRef_Write_Position` scale `10000` mean `1.628 ms`, alloc `3696576`
- `GetComponentRef_Write_Position` scale `50000` mean `3.697 ms`, alloc `15622872`
