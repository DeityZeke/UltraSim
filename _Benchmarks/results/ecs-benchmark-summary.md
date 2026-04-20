# ECS Benchmark Summary

Generated: 2026-04-20T03:29:11.4286974+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.075 | 0.323 | 75.2 | 322.6 | new | 1272 |
| CreateDefaultDeferred_Position | 10000 | 0.317 | 0.824 | 31.7 | 82.4 | new | -633848 |
| CreateDefaultDeferred_Position | 50000 | 1.431 | 5.829 | 28.6 | 116.6 | new | -2252500 |
| CreateDestroyReuseChurn_Position | 1000 | 0.688 | 0.630 | 344.0 | 315.0 | old | -800 |
| CreateDestroyReuseChurn_Position | 10000 | 1.218 | 1.885 | 62.6 | 96.9 | new | 27616 |
| CreateDestroyReuseChurn_Position | 50000 | 2.541 | 2.975 | 25.6 | 29.9 | new | 32608 |
| DeferredAdd_Velocity | 1000 | 0.398 | 0.421 | 397.9 | 421.4 | new | -21432 |
| DeferredAdd_Velocity | 10000 | 3.076 | 3.290 | 307.6 | 329.0 | new | 108696 |
| DeferredAdd_Velocity | 50000 | 6.843 | 7.010 | 136.9 | 140.2 | new | 77249 |
| DeferredDestroy_SparseVictims | 1000 | 0.275 | 0.205 | 549.4 | 410.2 | old | -34928 |
| DeferredDestroy_SparseVictims | 10000 | 0.822 | 1.445 | 164.4 | 289.0 | new | 182616 |
| DeferredDestroy_SparseVictims | 50000 | 3.854 | 3.320 | 154.1 | 132.8 | old | 300635 |
| DeferredDestroy | 1000 | 0.265 | 0.213 | 265.3 | 212.5 | old | -26712 |
| DeferredDestroy | 10000 | 1.311 | 0.978 | 131.1 | 97.8 | old | 313712 |
| DeferredDestroy | 50000 | 4.301 | 3.531 | 86.0 | 70.6 | old | 824980 |
| DeferredRemove_Velocity | 1000 | 0.661 | 0.416 | 661.0 | 415.5 | old | -21616 |
| DeferredRemove_Velocity | 10000 | 3.152 | 3.270 | 315.2 | 327.0 | new | 491320 |
| DeferredRemove_Velocity | 50000 | 7.309 | 7.671 | 146.2 | 153.4 | new | 1471032 |
| DirtyDrain_Position | 1000 | 0.287 | 0.235 | 286.7 | 234.6 | old | -44272 |
| DirtyDrain_Position | 10000 | 0.815 | 1.460 | 81.5 | 146.0 | new | 50296 |
| DirtyDrain_Position | 50000 | 3.942 | 3.948 | 78.8 | 79.0 | new | -224954 |
| HotLoop_DeferredAdd_Velocity | 50000 | 2.777 | 3.850 | 55.5 | 77.0 | new | 306240 |
| HotLoop_DirtyDrain_Position | 50000 | 0.878 | 0.862 | 17.6 | 17.2 | old | -336 |
| HotLoop_Query1_Read_Position | 50000 | 0.230 | 0.167 | 4.6 | 3.3 | old | -336 |
| HotLoop_SetComponent_Position | 50000 | 0.709 | 0.738 | 14.2 | 14.8 | new | -336 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 82.429 | 189.473 | 10.3 | 23.7 | new | -785664 |
| HotLoop_TickParallel_OscillatingMovement | 10000 | 153.994 | 378.406 | 19.2 | 47.3 | new | 84464 |
| HotLoop_TickParallel_OscillatingMovement | 100000 | 1098.413 | 3721.844 | 13.7 | 46.5 | new | 84464 |
| HotLoop_TickParallel_QuerySystems | 10000 | 81.283 | 41.595 | 10.2 | 5.2 | old | 26864 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 45.471 | 45.356 | 5.7 | 5.7 | old | 65264 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 36.366 | 70.093 | 4.5 | 8.8 | new | -25936 |
| HotLoop_TickSerial_OscillatingMovement | 10000 | 188.711 | 325.957 | 23.6 | 40.7 | new | 57264 |
| HotLoop_TickSerial_OscillatingMovement | 100000 | 1572.124 | 3031.965 | 19.7 | 37.9 | new | 57264 |
| HotLoop_TickSerial_QuerySystems | 10000 | 36.272 | 17.297 | 4.5 | 2.2 | old | -13136 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 18.576 | 20.750 | 2.3 | 2.6 | new | 12464 |
| HotLoop_TryGetComponent_Position | 50000 | 0.230 | 0.248 | 4.6 | 5.0 | new | -336 |
| MigrationCycle_AddRemoveVelocity | 1000 | 1.879 | 2.118 | 939.6 | 1059.2 | new | -7072 |
| MigrationCycle_AddRemoveVelocity | 10000 | 1.617 | 4.530 | 80.9 | 226.5 | new | 369248 |
| MigrationCycle_AddRemoveVelocity | 50000 | 5.619 | 7.787 | 56.2 | 77.9 | new | 1128660 |
| PersistenceView_Filtered_Position | 1000 | 0.403 | 0.316 | 403.3 | 316.0 | old | -32472 |
| PersistenceView_Filtered_Position | 10000 | 1.769 | 2.308 | 176.9 | 230.8 | new | 312584 |
| PersistenceView_Filtered_Position | 50000 | 5.378 | 5.977 | 107.6 | 119.5 | new | 698480 |
| Query1_Read_Position | 1000 | 0.243 | 0.202 | 242.5 | 201.8 | old | -43328 |
| Query1_Read_Position | 10000 | 2.126 | 1.636 | 212.6 | 163.6 | old | 50232 |
| Query1_Read_Position | 50000 | 4.676 | 5.042 | 93.5 | 100.8 | new | -224864 |
| Query2_Read_PositionVelocity | 1000 | 0.324 | 0.263 | 323.5 | 262.9 | old | -35352 |
| Query2_Read_PositionVelocity | 10000 | 3.493 | 2.647 | 349.3 | 264.7 | old | 230720 |
| Query2_Read_PositionVelocity | 50000 | 6.973 | 6.766 | 139.5 | 135.3 | old | 423439 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.431 | 0.329 | 431.2 | 328.5 | old | -31312 |
| Query3_Read_PositionVelocityHealth | 10000 | 4.486 | 3.436 | 448.6 | 343.6 | old | 321736 |
| Query3_Read_PositionVelocityHealth | 50000 | 10.621 | 9.086 | 212.4 | 181.7 | old | 747743 |
| SetComponent_Position | 1000 | 0.317 | 1.449 | 317.4 | 1449.5 | new | -43264 |
| SetComponent_Position | 10000 | 2.647 | 2.438 | 264.7 | 243.8 | old | 50296 |
| SetComponent_Position | 50000 | 5.706 | 8.564 | 114.1 | 171.3 | new | -224805 |
| TickParallel_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 707.401 | 2037.134 | 8.8 | 25.5 | new | 3568644 |
| TickParallel_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 672.764 | 542.877 | 11.2 | 9.0 | old | 62674012 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 82.365 | 194.304 | 10.3 | 24.3 | new | -2557456 |
| TickParallel_OscillatingMovement_100k_200Ticks | 100000 | 1016.654 | 3700.769 | 12.7 | 46.3 | new | 3568616 |
| TickParallel_OscillatingMovement_10k_200Ticks | 10000 | 138.946 | 382.330 | 17.4 | 47.8 | new | 675784 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 85.287 | 45.312 | 10.7 | 5.7 | old | 5852580 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 48.142 | 60.462 | 6.0 | 7.6 | new | 291454 |
| TickParallel_V1PulseMovement_100k_50Ticks | 100000 | 151.722 | 216.214 | 10.1 | 14.4 | new | 62589928 |
| TickSerial_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 1001.237 | 2257.748 | 12.5 | 28.2 | new | 3556480 |
| TickSerial_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 375.971 | 638.865 | 6.3 | 10.6 | new | 29301168 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 38.306 | 78.900 | 4.8 | 9.9 | new | 207768 |
| TickSerial_OscillatingMovement_100k_200Ticks | 100000 | 1324.551 | 2976.923 | 16.6 | 37.2 | new | 3556480 |
| TickSerial_OscillatingMovement_10k_200Ticks | 10000 | 166.251 | 280.816 | 20.8 | 35.1 | new | 663648 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 50.851 | 30.792 | 6.4 | 3.8 | old | 220568 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 28.740 | 32.417 | 3.6 | 4.1 | new | 245984 |
| TickSerial_V1PulseMovement_100k_50Ticks | 100000 | 106.479 | 161.078 | 7.1 | 10.7 | new | 29237568 |
| TryGetComponent_Position | 1000 | 0.354 | 0.381 | 353.7 | 381.0 | new | -43264 |
| TryGetComponent_Position | 10000 | 2.206 | 1.878 | 220.6 | 187.8 | old | 50296 |
| TryGetComponent_Position | 50000 | 4.700 | 8.850 | 94.0 | 177.0 | new | -224800 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3536`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.127 ms`, alloc `108280`
- `PatchComponent_Position` scale `1000` mean `0.290 ms`, alloc `161368`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.377 ms`, alloc `2566552`
- `PatchComponent_Position` scale `10000` mean `0.865 ms`, alloc `3746872`
- `CreateDefaultImmediate_Position` scale `50000` mean `1.635 ms`, alloc `10431856`
- `PatchComponent_Position` scale `50000` mean `3.981 ms`, alloc `15397904`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.008 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.188 ms`, alloc `205640`
- `GetComponentRef_Write_Position` scale `10000` mean `1.628 ms`, alloc `3696576`
- `GetComponentRef_Write_Position` scale `50000` mean `3.697 ms`, alloc `15622872`
