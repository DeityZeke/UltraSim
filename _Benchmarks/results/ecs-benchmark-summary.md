# ECS Benchmark Summary

Generated: 2026-04-20T14:01:22.6047135+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.073 | 0.323 | 72.7 | 322.6 | new | 1272 |
| CreateDefaultDeferred_Position | 10000 | 0.296 | 0.824 | 29.6 | 82.4 | new | -633848 |
| CreateDefaultDeferred_Position | 50000 | 1.461 | 5.829 | 29.2 | 116.6 | new | -2252312 |
| CreateDestroyReuseChurn_Position | 1000 | 0.654 | 0.630 | 326.8 | 315.0 | old | 31992 |
| CreateDestroyReuseChurn_Position | 10000 | 1.251 | 1.885 | 64.3 | 96.9 | new | 27616 |
| CreateDestroyReuseChurn_Position | 50000 | 2.539 | 2.975 | 25.6 | 29.9 | new | 32608 |
| DeferredAdd_Velocity | 1000 | 0.468 | 0.421 | 468.0 | 421.4 | old | -21432 |
| DeferredAdd_Velocity | 10000 | 2.915 | 3.290 | 291.5 | 329.0 | new | 108696 |
| DeferredAdd_Velocity | 50000 | 5.714 | 7.010 | 114.3 | 140.2 | new | 77940 |
| DeferredDestroy_SparseVictims | 1000 | 0.247 | 0.205 | 494.6 | 410.2 | old | -34928 |
| DeferredDestroy_SparseVictims | 10000 | 1.047 | 1.445 | 209.3 | 289.0 | new | 182616 |
| DeferredDestroy_SparseVictims | 50000 | 3.479 | 3.320 | 139.1 | 132.8 | old | 300616 |
| DeferredDestroy | 1000 | 0.287 | 0.213 | 287.3 | 212.5 | old | -26712 |
| DeferredDestroy | 10000 | 1.813 | 0.978 | 181.3 | 97.8 | old | 313712 |
| DeferredDestroy | 50000 | 3.359 | 3.531 | 67.2 | 70.6 | new | 825072 |
| DeferredRemove_Velocity | 1000 | 0.373 | 0.416 | 373.2 | 415.5 | new | -21616 |
| DeferredRemove_Velocity | 10000 | 3.317 | 3.270 | 331.7 | 327.0 | old | 491320 |
| DeferredRemove_Velocity | 50000 | 5.938 | 7.671 | 118.8 | 153.4 | new | 1470384 |
| DirtyDrain_Position | 1000 | 0.269 | 0.235 | 269.1 | 234.6 | old | -44272 |
| DirtyDrain_Position | 10000 | 1.264 | 1.460 | 126.4 | 146.0 | new | 50296 |
| DirtyDrain_Position | 50000 | 4.243 | 3.948 | 84.9 | 79.0 | old | -224968 |
| HotLoop_DeferredAdd_Velocity | 50000 | 2.776 | 3.850 | 55.5 | 77.0 | new | 305232 |
| HotLoop_DirtyDrain_Position | 50000 | 0.993 | 0.862 | 19.9 | 17.2 | old | -1344 |
| HotLoop_Query1_Read_Position | 50000 | 0.238 | 0.167 | 4.8 | 3.3 | old | -1344 |
| HotLoop_SetComponent_Position | 50000 | 0.838 | 0.738 | 16.8 | 14.8 | old | -1344 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 67.308 | 189.473 | 8.4 | 23.7 | new | -519445 |
| HotLoop_TickParallel_OscillatingMovement | 10000 | 150.017 | 378.406 | 18.8 | 47.3 | new | 99456 |
| HotLoop_TickParallel_OscillatingMovement | 100000 | 1055.219 | 3721.844 | 13.2 | 46.5 | new | 99456 |
| HotLoop_TickParallel_QuerySystems | 10000 | 65.936 | 41.595 | 8.2 | 5.2 | old | 293083 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 49.204 | 45.356 | 6.2 | 5.7 | old | 64256 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 31.350 | 70.093 | 3.9 | 8.8 | new | -26944 |
| HotLoop_TickSerial_OscillatingMovement | 10000 | 180.432 | 325.957 | 22.6 | 40.7 | new | 72256 |
| HotLoop_TickSerial_OscillatingMovement | 100000 | 1491.294 | 3031.965 | 18.6 | 37.9 | new | 72256 |
| HotLoop_TickSerial_QuerySystems | 10000 | 28.113 | 17.297 | 3.5 | 2.2 | old | -14144 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 17.862 | 20.750 | 2.2 | 2.6 | new | 11456 |
| HotLoop_TryGetComponent_Position | 50000 | 0.230 | 0.248 | 4.6 | 5.0 | new | -1344 |
| MigrationCycle_AddRemoveVelocity | 1000 | 1.698 | 2.118 | 849.1 | 1059.2 | new | 58512 |
| MigrationCycle_AddRemoveVelocity | 10000 | 1.973 | 4.530 | 98.6 | 226.5 | new | 1213584 |
| MigrationCycle_AddRemoveVelocity | 50000 | 6.654 | 7.787 | 66.5 | 77.9 | new | 4825864 |
| PersistenceView_Filtered_Position | 1000 | 0.355 | 0.316 | 355.4 | 316.0 | old | -36736 |
| PersistenceView_Filtered_Position | 10000 | 2.206 | 2.308 | 220.6 | 230.8 | new | 308320 |
| PersistenceView_Filtered_Position | 50000 | 5.958 | 5.977 | 119.2 | 119.5 | new | 694139 |
| Query1_Read_Position | 1000 | 0.206 | 0.202 | 206.1 | 201.8 | old | -47440 |
| Query1_Read_Position | 10000 | 1.974 | 1.636 | 197.4 | 163.6 | old | 46120 |
| Query1_Read_Position | 50000 | 4.422 | 5.042 | 88.4 | 100.8 | new | -228976 |
| Query2_Read_PositionVelocity | 1000 | 0.276 | 0.263 | 276.0 | 262.9 | old | -39472 |
| Query2_Read_PositionVelocity | 10000 | 3.008 | 2.647 | 300.8 | 264.7 | old | 226600 |
| Query2_Read_PositionVelocity | 50000 | 6.400 | 6.766 | 128.0 | 135.3 | new | 419299 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.383 | 0.329 | 382.8 | 328.5 | old | -35440 |
| Query3_Read_PositionVelocityHealth | 10000 | 4.096 | 3.436 | 409.6 | 343.6 | old | 317608 |
| Query3_Read_PositionVelocityHealth | 50000 | 9.363 | 9.086 | 187.3 | 181.7 | old | 743615 |
| SetComponent_Position | 1000 | 0.295 | 1.449 | 294.8 | 1449.5 | new | -43264 |
| SetComponent_Position | 10000 | 2.540 | 2.438 | 254.0 | 243.8 | old | 50296 |
| SetComponent_Position | 50000 | 5.601 | 8.564 | 112.0 | 171.3 | new | -224800 |
| TickParallel_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 579.028 | 2037.134 | 7.2 | 25.5 | new | 220246360 |
| TickParallel_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 336.176 | 542.877 | 5.6 | 9.0 | new | 189974316 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 65.483 | 194.304 | 8.2 | 24.3 | new | -2972157 |
| TickParallel_OscillatingMovement_100k_200Ticks | 100000 | 983.783 | 3700.769 | 12.3 | 46.3 | new | 3584408 |
| TickParallel_OscillatingMovement_10k_200Ticks | 10000 | 134.792 | 382.330 | 16.8 | 47.8 | new | 691576 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 67.195 | 45.312 | 8.4 | 5.7 | old | 4238195 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 48.449 | 60.462 | 6.1 | 7.6 | new | 287387 |
| TickParallel_V1PulseMovement_100k_50Ticks | 100000 | 128.429 | 216.214 | 8.6 | 14.4 | new | 52346280 |
| TickSerial_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 851.389 | 2257.748 | 10.6 | 28.2 | new | 3981273 |
| TickSerial_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 189.044 | 638.865 | 3.2 | 10.6 | new | 29734580 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 27.698 | 78.900 | 3.5 | 9.9 | new | 1776400 |
| TickSerial_OscillatingMovement_100k_200Ticks | 100000 | 1270.466 | 2976.923 | 15.9 | 37.2 | new | 3572272 |
| TickSerial_OscillatingMovement_10k_200Ticks | 10000 | 158.741 | 280.816 | 19.8 | 35.1 | new | 679440 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 43.843 | 30.792 | 5.5 | 3.8 | old | 1789200 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 39.226 | 32.417 | 4.9 | 4.1 | old | 241864 |
| TickSerial_V1PulseMovement_100k_50Ticks | 100000 | 78.405 | 161.078 | 5.2 | 10.7 | new | 41824904 |
| TryGetComponent_Position | 1000 | 0.330 | 0.381 | 330.3 | 381.0 | new | -43264 |
| TryGetComponent_Position | 10000 | 2.170 | 1.878 | 217.0 | 187.8 | old | 50296 |
| TryGetComponent_Position | 50000 | 4.789 | 8.850 | 95.8 | 177.0 | new | -224810 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3536`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.129 ms`, alloc `108280`
- `PatchComponent_Position` scale `1000` mean `0.284 ms`, alloc `161368`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.526 ms`, alloc `2566552`
- `PatchComponent_Position` scale `10000` mean `1.349 ms`, alloc `3746872`
- `CreateDefaultImmediate_Position` scale `50000` mean `1.581 ms`, alloc `10431808`
- `PatchComponent_Position` scale `50000` mean `4.035 ms`, alloc `15397918`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.008 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.188 ms`, alloc `205640`
- `GetComponentRef_Write_Position` scale `10000` mean `1.628 ms`, alloc `3696576`
- `GetComponentRef_Write_Position` scale `50000` mean `3.697 ms`, alloc `15622872`
