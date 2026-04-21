# ECS Benchmark Summary

Generated: 2026-04-21T01:36:58.7825993+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.067 | 0.323 | 67.3 | 322.6 | new | -23656 |
| CreateDefaultDeferred_Position | 10000 | 0.162 | 0.824 | 16.2 | 82.4 | new | -1324224 |
| CreateDefaultDeferred_Position | 50000 | 1.060 | 5.829 | 21.2 | 116.6 | new | -4982032 |
| CreateDestroyReuseChurn_Position | 1000 | 0.590 | 0.630 | 294.8 | 315.0 | new | -23672 |
| CreateDestroyReuseChurn_Position | 10000 | 1.186 | 1.885 | 61.0 | 96.9 | new | -591088 |
| CreateDestroyReuseChurn_Position | 50000 | 2.150 | 2.975 | 21.6 | 29.9 | new | -2478448 |
| DeferredAdd_Velocity | 1000 | 0.389 | 0.421 | 388.8 | 421.4 | new | -70056 |
| DeferredAdd_Velocity | 10000 | 1.713 | 3.290 | 171.3 | 329.0 | new | -2343960 |
| DeferredAdd_Velocity | 50000 | 5.655 | 7.010 | 113.1 | 140.2 | new | -9394628 |
| DeferredDestroy_SparseVictims | 1000 | 0.199 | 0.205 | 398.1 | 410.2 | new | -67304 |
| DeferredDestroy_SparseVictims | 10000 | 1.560 | 1.445 | 311.9 | 289.0 | old | -1761624 |
| DeferredDestroy_SparseVictims | 50000 | 2.352 | 3.320 | 94.1 | 132.8 | new | -7090641 |
| DeferredDestroy | 1000 | 0.161 | 0.213 | 161.4 | 212.5 | new | -67496 |
| DeferredDestroy | 10000 | 1.193 | 0.978 | 119.3 | 97.8 | old | -1812696 |
| DeferredDestroy | 50000 | 2.957 | 3.531 | 59.1 | 70.6 | new | -7214856 |
| DeferredRemove_Velocity | 1000 | 0.327 | 0.416 | 327.2 | 415.5 | new | -78208 |
| DeferredRemove_Velocity | 10000 | 2.111 | 3.270 | 211.1 | 327.0 | new | -1977328 |
| DeferredRemove_Velocity | 50000 | 5.229 | 7.671 | 104.6 | 153.4 | new | -8017496 |
| DirtyDrain_Position | 1000 | 0.218 | 0.235 | 218.0 | 234.6 | new | -68160 |
| DirtyDrain_Position | 10000 | 1.648 | 1.460 | 164.8 | 146.0 | old | -1711600 |
| DirtyDrain_Position | 50000 | 2.723 | 3.948 | 54.5 | 79.0 | new | -6967411 |
| HotLoop_DeferredAdd_Velocity | 50000 | 3.301 | 3.850 | 66.0 | 77.0 | new | -2824200 |
| HotLoop_DirtyDrain_Position | 50000 | 0.826 | 0.862 | 16.5 | 17.2 | new | -1344 |
| HotLoop_Query1_Read_Position | 50000 | 0.119 | 0.167 | 2.4 | 3.3 | new | -1432 |
| HotLoop_SetComponent_Position | 50000 | 0.666 | 0.738 | 13.3 | 14.8 | new | -1344 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 191.798 | 189.473 | 24.0 | 23.7 | old | -748272 |
| HotLoop_TickParallel_OscillatingMovement | 10000 | 144.290 | 378.406 | 18.0 | 47.3 | new | 99456 |
| HotLoop_TickParallel_OscillatingMovement | 100000 | 1034.195 | 3721.844 | 12.9 | 46.5 | new | 99456 |
| HotLoop_TickParallel_QuerySystems | 10000 | 32.831 | 41.595 | 4.1 | 5.2 | new | 25856 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 3.104 | 45.356 | 0.4 | 5.7 | new | 64256 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 66.229 | 70.093 | 8.3 | 8.8 | new | -1344 |
| HotLoop_TickSerial_OscillatingMovement | 10000 | 172.184 | 325.957 | 21.5 | 40.7 | new | 72256 |
| HotLoop_TickSerial_OscillatingMovement | 100000 | 1413.895 | 3031.965 | 17.7 | 37.9 | new | 72256 |
| HotLoop_TickSerial_QuerySystems | 10000 | 12.617 | 17.297 | 1.6 | 2.2 | new | -26944 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 6.250 | 20.750 | 0.8 | 2.6 | new | -1344 |
| HotLoop_TryGetComponent_Position | 50000 | 0.209 | 0.248 | 4.2 | 5.0 | new | -1344 |
| MigrationCycle_AddRemoveVelocity | 1000 | 1.758 | 2.118 | 878.8 | 1059.2 | new | -39232 |
| MigrationCycle_AddRemoveVelocity | 10000 | 1.815 | 4.530 | 90.7 | 226.5 | new | -2185496 |
| MigrationCycle_AddRemoveVelocity | 50000 | 3.650 | 7.787 | 36.5 | 77.9 | new | -8591740 |
| PersistenceView_Filtered_Position | 1000 | 0.265 | 0.316 | 264.9 | 316.0 | new | -68896 |
| PersistenceView_Filtered_Position | 10000 | 1.634 | 2.308 | 163.4 | 230.8 | new | -2070872 |
| PersistenceView_Filtered_Position | 50000 | 3.529 | 5.977 | 70.6 | 119.5 | new | -8297472 |
| Query1_Read_Position | 1000 | 0.167 | 0.202 | 167.1 | 201.8 | new | -51136 |
| Query1_Read_Position | 10000 | 1.190 | 1.636 | 119.0 | 163.6 | new | -1551584 |
| Query1_Read_Position | 50000 | 3.437 | 5.042 | 68.7 | 100.8 | new | -6171264 |
| Query2_Read_PositionVelocity | 1000 | 0.245 | 0.263 | 245.2 | 262.9 | new | -59320 |
| Query2_Read_PositionVelocity | 10000 | 1.818 | 2.647 | 181.8 | 264.7 | new | -1807544 |
| Query2_Read_PositionVelocity | 50000 | 5.000 | 6.766 | 100.0 | 135.3 | new | -7211649 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.296 | 0.329 | 295.5 | 328.5 | new | -67504 |
| Query3_Read_PositionVelocityHealth | 10000 | 2.582 | 3.436 | 258.2 | 343.6 | new | -2061488 |
| Query3_Read_PositionVelocityHealth | 50000 | 6.318 | 9.086 | 126.4 | 181.7 | new | -8251977 |
| SetComponent_Position | 1000 | 0.772 | 1.449 | 772.0 | 1449.5 | new | -59152 |
| SetComponent_Position | 10000 | 1.500 | 2.438 | 150.0 | 243.8 | new | -1631600 |
| SetComponent_Position | 50000 | 4.183 | 8.564 | 83.7 | 171.3 | new | -6571280 |
| TickParallel_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 462.442 | 2037.134 | 5.8 | 25.5 | new | 194492744 |
| TickParallel_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 293.494 | 542.877 | 4.9 | 9.0 | new | 161681801 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 183.815 | 194.304 | 23.0 | 24.3 | new | -3832980 |
| TickParallel_OscillatingMovement_100k_200Ticks | 100000 | 919.671 | 3700.769 | 11.5 | 46.3 | new | 2122304 |
| TickParallel_OscillatingMovement_10k_200Ticks | 10000 | 130.557 | 382.330 | 16.3 | 47.8 | new | 159664 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 40.124 | 45.312 | 5.0 | 5.7 | new | 17272 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 6.487 | 60.462 | 0.8 | 7.6 | new | -1885144 |
| TickParallel_V1PulseMovement_100k_50Ticks | 100000 | 117.311 | 216.214 | 7.8 | 14.4 | new | 25862784 |
| TickSerial_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 725.530 | 2257.748 | 9.1 | 28.2 | new | -16258888 |
| TickSerial_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 159.227 | 638.865 | 2.7 | 10.6 | new | 7808974 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 75.909 | 78.900 | 9.5 | 9.9 | new | -1804328 |
| TickSerial_OscillatingMovement_100k_200Ticks | 100000 | 1189.864 | 2976.923 | 14.9 | 37.2 | new | -16527816 |
| TickSerial_OscillatingMovement_10k_200Ticks | 10000 | 150.334 | 280.816 | 18.8 | 35.1 | new | -1974952 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 15.903 | 30.792 | 2.0 | 3.8 | new | -1304026 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 9.048 | 32.417 | 1.1 | 4.1 | new | -1804408 |
| TickSerial_V1PulseMovement_100k_50Ticks | 100000 | 57.902 | 161.078 | 3.9 | 10.7 | new | 4008328 |
| TryGetComponent_Position | 1000 | 0.206 | 0.381 | 205.8 | 381.0 | new | -59152 |
| TryGetComponent_Position | 10000 | 1.225 | 1.878 | 122.5 | 187.8 | new | -1631600 |
| TryGetComponent_Position | 50000 | 3.612 | 8.850 | 72.2 | 177.0 | new | -6571280 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.010 ms`, alloc `3536`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.112 ms`, alloc `91896`
- `PatchComponent_Position` scale `1000` mean `0.222 ms`, alloc `145480`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.713 ms`, alloc `2058648`
- `PatchComponent_Position` scale `10000` mean `0.952 ms`, alloc `2064976`
- `CreateDefaultImmediate_Position` scale `50000` mean `1.551 ms`, alloc `8350992`
- `PatchComponent_Position` scale `50000` mean `2.680 ms`, alloc `9055456`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.008 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.188 ms`, alloc `205640`
- `GetComponentRef_Write_Position` scale `10000` mean `1.628 ms`, alloc `3696576`
- `GetComponentRef_Write_Position` scale `50000` mean `3.697 ms`, alloc `15622872`
