# ECS Benchmark Summary

Generated: 2026-04-22T02:00:43.6803527+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.090 | 0.296 | 89.8 | 295.9 | new | -23416 |
| CreateDefaultDeferred_Position | 10000 | 0.162 | 0.857 | 16.2 | 85.7 | new | -1323984 |
| CreateDefaultDeferred_Position | 50000 | 0.995 | 5.187 | 19.9 | 103.7 | new | -4981792 |
| CreateDestroyReuseChurn_Position | 1000 | 0.642 | 0.645 | 320.9 | 322.4 | new | -23432 |
| CreateDestroyReuseChurn_Position | 10000 | 1.209 | 2.340 | 62.1 | 120.3 | new | -590848 |
| CreateDestroyReuseChurn_Position | 50000 | 2.655 | 3.030 | 26.7 | 30.5 | new | -2478208 |
| DeferredAdd_Velocity | 1000 | 0.380 | 0.442 | 380.4 | 441.5 | new | -69816 |
| DeferredAdd_Velocity | 10000 | 1.768 | 3.476 | 176.8 | 347.6 | new | -2343720 |
| DeferredAdd_Velocity | 50000 | 5.756 | 7.744 | 115.1 | 154.9 | new | -9391748 |
| DeferredDestroy_SparseVictims | 1000 | 0.196 | 0.207 | 392.8 | 413.5 | new | -67064 |
| DeferredDestroy_SparseVictims | 10000 | 1.534 | 1.846 | 306.9 | 369.1 | new | -1761384 |
| DeferredDestroy_SparseVictims | 50000 | 2.468 | 4.078 | 98.7 | 163.1 | new | -7090421 |
| DeferredDestroy | 1000 | 0.171 | 0.215 | 171.3 | 215.1 | new | -67256 |
| DeferredDestroy | 10000 | 1.204 | 1.422 | 120.4 | 142.2 | new | -1812456 |
| DeferredDestroy | 50000 | 2.369 | 4.614 | 47.4 | 92.3 | new | -7214664 |
| DeferredRemove_Velocity | 1000 | 0.331 | 0.424 | 331.1 | 423.7 | new | -86512 |
| DeferredRemove_Velocity | 10000 | 2.118 | 3.215 | 211.8 | 321.5 | new | -2159488 |
| DeferredRemove_Velocity | 50000 | 5.813 | 7.963 | 116.3 | 159.3 | new | -8666136 |
| DirtyDrain_Position | 1000 | 0.229 | 0.291 | 229.4 | 290.7 | new | -67920 |
| DirtyDrain_Position | 10000 | 1.524 | 2.291 | 152.4 | 229.1 | new | -1711360 |
| DirtyDrain_Position | 50000 | 2.767 | 4.630 | 55.3 | 92.6 | new | -6967176 |
| HotLoop_DeferredAdd_Velocity | 50000 | 3.579 | 3.687 | 71.6 | 73.7 | new | -2824200 |
| HotLoop_DirtyDrain_Position | 50000 | 0.840 | 0.863 | 16.8 | 17.3 | new | -1344 |
| HotLoop_Query1_Read_Position | 50000 | 0.120 | 0.168 | 2.4 | 3.4 | new | -1432 |
| HotLoop_SetComponent_Position | 50000 | 0.675 | 0.732 | 13.5 | 14.6 | new | -1344 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 170.785 | 190.182 | 21.3 | 23.8 | new | -746672 |
| HotLoop_TickParallel_OscillatingMovement | 10000 | 134.124 | 395.183 | 16.8 | 49.4 | new | 101056 |
| HotLoop_TickParallel_OscillatingMovement | 100000 | 935.995 | 3883.832 | 11.7 | 48.5 | new | 101056 |
| HotLoop_TickParallel_QuerySystems | 10000 | 24.239 | 41.149 | 3.0 | 5.1 | new | 27456 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 3.337 | 46.322 | 0.4 | 5.8 | new | 65856 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 69.744 | 71.970 | 8.7 | 9.0 | new | -1344 |
| HotLoop_TickSerial_OscillatingMovement | 10000 | 169.186 | 310.746 | 21.1 | 38.8 | new | 72256 |
| HotLoop_TickSerial_OscillatingMovement | 100000 | 1375.155 | 3161.845 | 17.2 | 39.5 | new | 72256 |
| HotLoop_TickSerial_QuerySystems | 10000 | 13.496 | 17.345 | 1.7 | 2.2 | new | -26944 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 6.321 | 21.090 | 0.8 | 2.6 | new | -1344 |
| HotLoop_TryGetComponent_Position | 50000 | 0.209 | 0.562 | 4.2 | 11.2 | new | -1344 |
| MigrationCycle_AddRemoveVelocity | 1000 | 1.578 | 2.172 | 789.0 | 1086.2 | new | -64624 |
| MigrationCycle_AddRemoveVelocity | 10000 | 1.676 | 3.854 | 83.8 | 192.7 | new | -2732456 |
| MigrationCycle_AddRemoveVelocity | 50000 | 4.065 | 9.106 | 40.7 | 91.1 | new | -10538160 |
| PersistenceView_Filtered_Position | 1000 | 0.272 | 0.362 | 272.0 | 362.4 | new | -68656 |
| PersistenceView_Filtered_Position | 10000 | 2.574 | 3.140 | 257.4 | 314.0 | new | -2070632 |
| PersistenceView_Filtered_Position | 50000 | 3.818 | 7.391 | 76.4 | 147.8 | new | -8297308 |
| Query1_Read_Position | 1000 | 0.171 | 0.214 | 171.2 | 213.6 | new | -50896 |
| Query1_Read_Position | 10000 | 1.115 | 1.818 | 111.5 | 181.8 | new | -1551344 |
| Query1_Read_Position | 50000 | 2.806 | 4.719 | 56.1 | 94.4 | new | -6171009 |
| Query2_Read_PositionVelocity | 1000 | 0.271 | 0.289 | 270.8 | 289.3 | new | -59080 |
| Query2_Read_PositionVelocity | 10000 | 2.046 | 2.977 | 204.6 | 297.7 | new | -1807304 |
| Query2_Read_PositionVelocity | 50000 | 5.236 | 8.100 | 104.7 | 162.0 | new | -7211419 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.298 | 0.362 | 298.1 | 361.9 | new | -67264 |
| Query3_Read_PositionVelocityHealth | 10000 | 2.693 | 4.055 | 269.3 | 405.5 | new | -2061248 |
| Query3_Read_PositionVelocityHealth | 50000 | 7.786 | 9.364 | 155.7 | 187.3 | new | -8251728 |
| SetComponent_Position | 1000 | 0.729 | 1.458 | 729.2 | 1458.2 | new | -58912 |
| SetComponent_Position | 10000 | 1.522 | 2.414 | 152.2 | 241.4 | new | -1631360 |
| SetComponent_Position | 50000 | 3.853 | 7.283 | 77.1 | 145.7 | new | -6571030 |
| TickParallel_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 465.586 | 2190.144 | 5.8 | 27.4 | new | -12278842 |
| TickParallel_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 263.450 | 558.174 | 4.4 | 9.3 | new | 12740355 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 180.062 | 195.319 | 22.5 | 24.4 | new | -7351984 |
| TickParallel_OscillatingMovement_100k_200Ticks | 100000 | 815.667 | 3758.589 | 10.2 | 47.0 | new | -12913896 |
| TickParallel_OscillatingMovement_10k_200Ticks | 10000 | 119.324 | 382.390 | 14.9 | 47.8 | new | -1961032 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 31.196 | 45.349 | 3.9 | 5.7 | new | -1444104 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 9.876 | 59.575 | 1.2 | 7.4 | new | -2159441 |
| TickParallel_V1PulseMovement_100k_50Ticks | 100000 | 96.424 | 216.428 | 6.4 | 14.4 | new | 13693920 |
| TickSerial_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 748.263 | 2089.892 | 9.4 | 26.1 | new | -16256960 |
| TickSerial_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 164.001 | 650.057 | 2.7 | 10.8 | new | 8237230 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 78.270 | 82.676 | 9.8 | 10.3 | new | -1802400 |
| TickSerial_OscillatingMovement_100k_200Ticks | 100000 | 1138.733 | 2801.267 | 14.2 | 35.0 | new | -12331536 |
| TickSerial_OscillatingMovement_10k_200Ticks | 10000 | 146.953 | 271.506 | 18.4 | 33.9 | new | -1448688 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 17.127 | 32.158 | 2.1 | 4.0 | new | -1303336 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 10.852 | 34.928 | 1.4 | 4.4 | new | -1802480 |
| TickSerial_V1PulseMovement_100k_50Ticks | 100000 | 53.079 | 162.125 | 3.5 | 10.8 | new | 1767528 |
| TryGetComponent_Position | 1000 | 0.210 | 0.388 | 209.6 | 388.0 | new | -58912 |
| TryGetComponent_Position | 10000 | 1.198 | 1.863 | 119.8 | 186.3 | new | -1631360 |
| TryGetComponent_Position | 50000 | 3.085 | 5.201 | 61.7 | 104.0 | new | -6571025 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3536`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.118 ms`, alloc `92136`
- `PatchComponent_Position` scale `1000` mean `0.222 ms`, alloc `145720`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.730 ms`, alloc `2058888`
- `PatchComponent_Position` scale `10000` mean `1.803 ms`, alloc `2065216`
- `CreateDefaultImmediate_Position` scale `50000` mean `1.668 ms`, alloc `8351232`
- `PatchComponent_Position` scale `50000` mean `2.729 ms`, alloc `9055691`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.008 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.197 ms`, alloc `205640`
- `GetComponentRef_Write_Position` scale `10000` mean `1.543 ms`, alloc `3696576`
- `GetComponentRef_Write_Position` scale `50000` mean `4.080 ms`, alloc `15622872`
