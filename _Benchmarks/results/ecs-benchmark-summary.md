# ECS Benchmark Summary

Generated: 2026-04-21T02:09:01.4112602+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.067 | 0.323 | 66.8 | 322.6 | new | -23656 |
| CreateDefaultDeferred_Position | 10000 | 0.166 | 0.824 | 16.6 | 82.4 | new | -1324224 |
| CreateDefaultDeferred_Position | 50000 | 0.980 | 5.829 | 19.6 | 116.6 | new | -4982032 |
| CreateDestroyReuseChurn_Position | 1000 | 0.575 | 0.630 | 287.5 | 315.0 | new | -23672 |
| CreateDestroyReuseChurn_Position | 10000 | 1.281 | 1.885 | 65.9 | 96.9 | new | -591088 |
| CreateDestroyReuseChurn_Position | 50000 | 1.988 | 2.975 | 20.0 | 29.9 | new | -2478448 |
| DeferredAdd_Velocity | 1000 | 0.322 | 0.421 | 321.9 | 421.4 | new | -70056 |
| DeferredAdd_Velocity | 10000 | 1.754 | 3.290 | 175.4 | 329.0 | new | -2343960 |
| DeferredAdd_Velocity | 50000 | 6.246 | 7.010 | 124.9 | 140.2 | new | -9394640 |
| DeferredDestroy_SparseVictims | 1000 | 0.190 | 0.205 | 379.2 | 410.2 | new | -67304 |
| DeferredDestroy_SparseVictims | 10000 | 1.533 | 1.445 | 306.6 | 289.0 | old | -1761624 |
| DeferredDestroy_SparseVictims | 50000 | 2.381 | 3.320 | 95.2 | 132.8 | new | -7090641 |
| DeferredDestroy | 1000 | 0.162 | 0.213 | 162.1 | 212.5 | new | -67496 |
| DeferredDestroy | 10000 | 1.171 | 0.978 | 117.1 | 97.8 | old | -1812696 |
| DeferredDestroy | 50000 | 2.524 | 3.531 | 50.5 | 70.6 | new | -7214856 |
| DeferredRemove_Velocity | 1000 | 0.325 | 0.416 | 324.7 | 415.5 | new | -78208 |
| DeferredRemove_Velocity | 10000 | 2.058 | 3.270 | 205.8 | 327.0 | new | -1977328 |
| DeferredRemove_Velocity | 50000 | 6.553 | 7.671 | 131.1 | 153.4 | new | -8017508 |
| DirtyDrain_Position | 1000 | 0.218 | 0.235 | 217.5 | 234.6 | new | -68160 |
| DirtyDrain_Position | 10000 | 0.816 | 1.460 | 81.6 | 146.0 | new | -1711600 |
| DirtyDrain_Position | 50000 | 2.760 | 3.948 | 55.2 | 79.0 | new | -6967411 |
| HotLoop_DeferredAdd_Velocity | 50000 | 3.126 | 3.850 | 62.5 | 77.0 | new | -2824200 |
| HotLoop_DirtyDrain_Position | 50000 | 0.806 | 0.862 | 16.1 | 17.2 | new | -1344 |
| HotLoop_Query1_Read_Position | 50000 | 0.117 | 0.167 | 2.3 | 3.3 | new | -1432 |
| HotLoop_SetComponent_Position | 50000 | 0.675 | 0.738 | 13.5 | 14.8 | new | -1344 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 167.517 | 189.473 | 20.9 | 23.7 | new | -748272 |
| HotLoop_TickParallel_OscillatingMovement | 10000 | 132.227 | 378.406 | 16.5 | 47.3 | new | 99456 |
| HotLoop_TickParallel_OscillatingMovement | 100000 | 928.386 | 3721.844 | 11.6 | 46.5 | new | 99456 |
| HotLoop_TickParallel_QuerySystems | 10000 | 23.884 | 41.595 | 3.0 | 5.2 | new | 25856 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 3.079 | 45.356 | 0.4 | 5.7 | new | 64256 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 68.362 | 70.093 | 8.5 | 8.8 | new | -1344 |
| HotLoop_TickSerial_OscillatingMovement | 10000 | 172.624 | 325.957 | 21.6 | 40.7 | new | 72256 |
| HotLoop_TickSerial_OscillatingMovement | 100000 | 1414.887 | 3031.965 | 17.7 | 37.9 | new | 72256 |
| HotLoop_TickSerial_QuerySystems | 10000 | 12.927 | 17.297 | 1.6 | 2.2 | new | -26944 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 6.230 | 20.750 | 0.8 | 2.6 | new | -1344 |
| HotLoop_TryGetComponent_Position | 50000 | 0.205 | 0.248 | 4.1 | 5.0 | new | -1344 |
| MigrationCycle_AddRemoveVelocity | 1000 | 1.593 | 2.118 | 796.7 | 1059.2 | new | -39232 |
| MigrationCycle_AddRemoveVelocity | 10000 | 1.754 | 4.530 | 87.7 | 226.5 | new | -2185496 |
| MigrationCycle_AddRemoveVelocity | 50000 | 3.667 | 7.787 | 36.7 | 77.9 | new | -8591745 |
| PersistenceView_Filtered_Position | 1000 | 0.268 | 0.316 | 267.6 | 316.0 | new | -68896 |
| PersistenceView_Filtered_Position | 10000 | 1.380 | 2.308 | 138.0 | 230.8 | new | -2070872 |
| PersistenceView_Filtered_Position | 50000 | 3.390 | 5.977 | 67.8 | 119.5 | new | -8297472 |
| Query1_Read_Position | 1000 | 0.169 | 0.202 | 169.3 | 201.8 | new | -51136 |
| Query1_Read_Position | 10000 | 1.098 | 1.636 | 109.8 | 163.6 | new | -1551584 |
| Query1_Read_Position | 50000 | 2.837 | 5.042 | 56.7 | 100.8 | new | -6171264 |
| Query2_Read_PositionVelocity | 1000 | 0.250 | 0.263 | 249.6 | 262.9 | new | -59320 |
| Query2_Read_PositionVelocity | 10000 | 1.887 | 2.647 | 188.7 | 264.7 | new | -1807544 |
| Query2_Read_PositionVelocity | 50000 | 5.230 | 6.766 | 104.6 | 135.3 | new | -7211649 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.290 | 0.329 | 289.9 | 328.5 | new | -67504 |
| Query3_Read_PositionVelocityHealth | 10000 | 2.515 | 3.436 | 251.5 | 343.6 | new | -2061488 |
| Query3_Read_PositionVelocityHealth | 50000 | 7.855 | 9.086 | 157.1 | 181.7 | new | -8251977 |
| SetComponent_Position | 1000 | 0.703 | 1.449 | 702.8 | 1449.5 | new | -59152 |
| SetComponent_Position | 10000 | 1.536 | 2.438 | 153.6 | 243.8 | new | -1631600 |
| SetComponent_Position | 50000 | 3.417 | 8.564 | 68.3 | 171.3 | new | -6571280 |
| TickParallel_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 451.400 | 2037.134 | 5.6 | 25.5 | new | -12819266 |
| TickParallel_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 260.311 | 542.877 | 4.3 | 9.0 | new | 11600072 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 171.481 | 194.304 | 21.4 | 24.3 | new | -7356791 |
| TickParallel_OscillatingMovement_100k_200Ticks | 100000 | 794.056 | 3700.769 | 9.9 | 46.3 | new | -12917424 |
| TickParallel_OscillatingMovement_10k_200Ticks | 10000 | 115.396 | 382.330 | 14.4 | 47.8 | new | -1964560 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 30.162 | 45.312 | 3.8 | 5.7 | new | -1530951 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 9.230 | 60.462 | 1.2 | 7.6 | new | -2164216 |
| TickParallel_V1PulseMovement_100k_50Ticks | 100000 | 93.572 | 216.214 | 6.2 | 14.4 | new | 13691752 |
| TickSerial_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 723.049 | 2257.748 | 9.0 | 28.2 | new | -16256663 |
| TickSerial_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 159.297 | 638.865 | 2.7 | 10.6 | new | 7613036 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 73.029 | 78.900 | 9.1 | 9.9 | new | -1804328 |
| TickSerial_OscillatingMovement_100k_200Ticks | 100000 | 1192.497 | 2976.923 | 14.9 | 37.2 | new | -16527816 |
| TickSerial_OscillatingMovement_10k_200Ticks | 10000 | 151.595 | 280.816 | 18.9 | 35.1 | new | -1974952 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 15.648 | 30.792 | 2.0 | 3.8 | new | -1305264 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 9.969 | 32.417 | 1.2 | 4.1 | new | -1804408 |
| TickSerial_V1PulseMovement_100k_50Ticks | 100000 | 60.115 | 161.078 | 4.0 | 10.7 | new | 4008328 |
| TryGetComponent_Position | 1000 | 0.206 | 0.381 | 206.3 | 381.0 | new | -59152 |
| TryGetComponent_Position | 10000 | 1.150 | 1.878 | 115.0 | 187.8 | new | -1631600 |
| TryGetComponent_Position | 50000 | 2.938 | 8.850 | 58.8 | 177.0 | new | -6571280 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3536`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.111 ms`, alloc `91896`
- `PatchComponent_Position` scale `1000` mean `0.225 ms`, alloc `145480`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.740 ms`, alloc `2058648`
- `PatchComponent_Position` scale `10000` mean `0.786 ms`, alloc `2064976`
- `CreateDefaultImmediate_Position` scale `50000` mean `1.601 ms`, alloc `8350992`
- `PatchComponent_Position` scale `50000` mean `2.649 ms`, alloc `9055456`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.008 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.188 ms`, alloc `205640`
- `GetComponentRef_Write_Position` scale `10000` mean `1.628 ms`, alloc `3696576`
- `GetComponentRef_Write_Position` scale `50000` mean `3.697 ms`, alloc `15622872`
