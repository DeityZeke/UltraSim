# ECS Benchmark Summary

Generated: 2026-04-20T03:41:51.4984895+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.085 | 0.323 | 85.3 | 322.6 | new | 1272 |
| CreateDefaultDeferred_Position | 10000 | 0.307 | 0.824 | 30.7 | 82.4 | new | -633848 |
| CreateDefaultDeferred_Position | 50000 | 1.418 | 5.829 | 28.4 | 116.6 | new | -2252490 |
| CreateDestroyReuseChurn_Position | 1000 | 0.877 | 0.630 | 438.6 | 315.0 | old | -800 |
| CreateDestroyReuseChurn_Position | 10000 | 1.507 | 1.885 | 77.4 | 96.9 | new | 27616 |
| CreateDestroyReuseChurn_Position | 50000 | 2.612 | 2.975 | 26.3 | 29.9 | new | 32608 |
| DeferredAdd_Velocity | 1000 | 0.437 | 0.421 | 436.6 | 421.4 | old | -21432 |
| DeferredAdd_Velocity | 10000 | 3.519 | 3.290 | 351.9 | 329.0 | old | 108696 |
| DeferredAdd_Velocity | 50000 | 6.614 | 7.010 | 132.3 | 140.2 | new | 77244 |
| DeferredDestroy_SparseVictims | 1000 | 0.261 | 0.205 | 522.3 | 410.2 | old | -34928 |
| DeferredDestroy_SparseVictims | 10000 | 1.397 | 1.445 | 279.4 | 289.0 | new | 182616 |
| DeferredDestroy_SparseVictims | 50000 | 3.832 | 3.320 | 153.3 | 132.8 | old | 300631 |
| DeferredDestroy | 1000 | 0.275 | 0.213 | 274.7 | 212.5 | old | -26712 |
| DeferredDestroy | 10000 | 1.912 | 0.978 | 191.2 | 97.8 | old | 313712 |
| DeferredDestroy | 50000 | 4.044 | 3.531 | 80.9 | 70.6 | old | 824990 |
| DeferredRemove_Velocity | 1000 | 0.641 | 0.416 | 641.4 | 415.5 | old | -21616 |
| DeferredRemove_Velocity | 10000 | 3.288 | 3.270 | 328.8 | 327.0 | old | 491320 |
| DeferredRemove_Velocity | 50000 | 6.989 | 7.671 | 139.8 | 153.4 | new | 1471032 |
| DirtyDrain_Position | 1000 | 0.445 | 0.235 | 445.1 | 234.6 | old | -44272 |
| DirtyDrain_Position | 10000 | 1.611 | 1.460 | 161.1 | 146.0 | old | 50296 |
| DirtyDrain_Position | 50000 | 4.221 | 3.948 | 84.4 | 79.0 | old | -224958 |
| HotLoop_DeferredAdd_Velocity | 50000 | 2.727 | 3.850 | 54.5 | 77.0 | new | 305232 |
| HotLoop_DirtyDrain_Position | 50000 | 0.972 | 0.862 | 19.4 | 17.2 | old | -1344 |
| HotLoop_Query1_Read_Position | 50000 | 0.227 | 0.167 | 4.5 | 3.3 | old | -1344 |
| HotLoop_SetComponent_Position | 50000 | 0.849 | 0.738 | 17.0 | 14.8 | old | -1344 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 80.953 | 189.473 | 10.1 | 23.7 | new | -386808 |
| HotLoop_TickParallel_OscillatingMovement | 10000 | 150.195 | 378.406 | 18.8 | 47.3 | new | 99456 |
| HotLoop_TickParallel_OscillatingMovement | 100000 | 1087.961 | 3721.844 | 13.6 | 46.5 | new | 99456 |
| HotLoop_TickParallel_QuerySystems | 10000 | 79.094 | 41.595 | 9.9 | 5.2 | old | 25856 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 46.403 | 45.356 | 5.8 | 5.7 | old | 64256 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 36.262 | 70.093 | 4.5 | 8.8 | new | -26944 |
| HotLoop_TickSerial_OscillatingMovement | 10000 | 181.162 | 325.957 | 22.6 | 40.7 | new | 72256 |
| HotLoop_TickSerial_OscillatingMovement | 100000 | 1545.449 | 3031.965 | 19.3 | 37.9 | new | 72256 |
| HotLoop_TickSerial_QuerySystems | 10000 | 35.606 | 17.297 | 4.5 | 2.2 | old | -14144 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 17.377 | 20.750 | 2.2 | 2.6 | new | 11456 |
| HotLoop_TryGetComponent_Position | 50000 | 0.225 | 0.248 | 4.5 | 5.0 | new | -1344 |
| MigrationCycle_AddRemoveVelocity | 1000 | 2.208 | 2.118 | 1104.0 | 1059.2 | old | -7207 |
| MigrationCycle_AddRemoveVelocity | 10000 | 2.249 | 4.530 | 112.4 | 226.5 | new | 369248 |
| MigrationCycle_AddRemoveVelocity | 50000 | 5.284 | 7.787 | 52.8 | 77.9 | new | 1128660 |
| PersistenceView_Filtered_Position | 1000 | 0.414 | 0.316 | 414.1 | 316.0 | old | -32472 |
| PersistenceView_Filtered_Position | 10000 | 3.045 | 2.308 | 304.5 | 230.8 | old | 312584 |
| PersistenceView_Filtered_Position | 50000 | 5.524 | 5.977 | 110.5 | 119.5 | new | 698480 |
| Query1_Read_Position | 1000 | 0.244 | 0.202 | 244.0 | 201.8 | old | -43328 |
| Query1_Read_Position | 10000 | 2.340 | 1.636 | 234.0 | 163.6 | old | 50232 |
| Query1_Read_Position | 50000 | 4.631 | 5.042 | 92.6 | 100.8 | new | -224864 |
| Query2_Read_PositionVelocity | 1000 | 0.336 | 0.263 | 336.3 | 262.9 | old | -35352 |
| Query2_Read_PositionVelocity | 10000 | 3.484 | 2.647 | 348.4 | 264.7 | old | 230720 |
| Query2_Read_PositionVelocity | 50000 | 8.330 | 6.766 | 166.6 | 135.3 | old | 423434 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.450 | 0.329 | 450.2 | 328.5 | old | -31312 |
| Query3_Read_PositionVelocityHealth | 10000 | 4.515 | 3.436 | 451.5 | 343.6 | old | 321736 |
| Query3_Read_PositionVelocityHealth | 50000 | 8.849 | 9.086 | 177.0 | 181.7 | new | 747743 |
| SetComponent_Position | 1000 | 0.329 | 1.449 | 329.1 | 1449.5 | new | -43264 |
| SetComponent_Position | 10000 | 2.751 | 2.438 | 275.1 | 243.8 | old | 50296 |
| SetComponent_Position | 50000 | 5.569 | 8.564 | 111.4 | 171.3 | new | -224800 |
| TickParallel_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 692.086 | 2037.134 | 8.7 | 25.5 | new | 3584504 |
| TickParallel_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 659.098 | 542.877 | 11.0 | 9.0 | old | 62692600 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 83.053 | 194.304 | 10.4 | 24.3 | new | -2957312 |
| TickParallel_OscillatingMovement_100k_200Ticks | 100000 | 1004.827 | 3700.769 | 12.6 | 46.3 | new | 3584532 |
| TickParallel_OscillatingMovement_10k_200Ticks | 10000 | 135.970 | 382.330 | 17.0 | 47.8 | new | 691681 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 84.339 | 45.312 | 10.5 | 5.7 | old | 4254339 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 49.008 | 60.462 | 6.1 | 7.6 | new | 291486 |
| TickParallel_V1PulseMovement_100k_50Ticks | 100000 | 146.860 | 216.214 | 9.8 | 14.4 | new | 62593000 |
| TickSerial_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 987.653 | 2257.748 | 12.3 | 28.2 | new | 3572368 |
| TickSerial_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 371.206 | 638.865 | 6.2 | 10.6 | new | 29321120 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 36.975 | 78.900 | 4.6 | 9.9 | new | 207768 |
| TickSerial_OscillatingMovement_100k_200Ticks | 100000 | 1310.872 | 2976.923 | 16.4 | 37.2 | new | 3572368 |
| TickSerial_OscillatingMovement_10k_200Ticks | 10000 | 159.718 | 280.816 | 20.0 | 35.1 | new | 679536 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 49.358 | 30.792 | 6.2 | 3.8 | old | 220568 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 28.175 | 32.417 | 3.5 | 4.1 | new | 245984 |
| TickSerial_V1PulseMovement_100k_50Ticks | 100000 | 103.516 | 161.078 | 6.9 | 10.7 | new | 29241920 |
| TryGetComponent_Position | 1000 | 0.415 | 0.381 | 414.6 | 381.0 | old | -43264 |
| TryGetComponent_Position | 10000 | 2.302 | 1.878 | 230.2 | 187.8 | old | 50296 |
| TryGetComponent_Position | 50000 | 4.683 | 8.850 | 93.7 | 177.0 | new | -224800 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3536`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.173 ms`, alloc `108280`
- `PatchComponent_Position` scale `1000` mean `0.443 ms`, alloc `161368`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.358 ms`, alloc `2566552`
- `PatchComponent_Position` scale `10000` mean `0.948 ms`, alloc `3746872`
- `CreateDefaultImmediate_Position` scale `50000` mean `1.615 ms`, alloc `10431856`
- `PatchComponent_Position` scale `50000` mean `4.261 ms`, alloc `15397904`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.008 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.188 ms`, alloc `205640`
- `GetComponentRef_Write_Position` scale `10000` mean `1.628 ms`, alloc `3696576`
- `GetComponentRef_Write_Position` scale `50000` mean `3.697 ms`, alloc `15622872`
