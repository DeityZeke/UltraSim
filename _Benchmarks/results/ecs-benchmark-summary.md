# ECS Benchmark Summary

Generated: 2026-04-21T02:25:16.6855828+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.067 | 0.323 | 66.8 | 322.6 | new | -23656 |
| CreateDefaultDeferred_Position | 10000 | 0.157 | 0.824 | 15.7 | 82.4 | new | -1324224 |
| CreateDefaultDeferred_Position | 50000 | 1.012 | 5.829 | 20.2 | 116.6 | new | -4982032 |
| CreateDestroyReuseChurn_Position | 1000 | 0.574 | 0.630 | 287.0 | 315.0 | new | -23672 |
| CreateDestroyReuseChurn_Position | 10000 | 1.197 | 1.885 | 61.5 | 96.9 | new | -591088 |
| CreateDestroyReuseChurn_Position | 50000 | 1.620 | 2.975 | 16.3 | 29.9 | new | -2478448 |
| DeferredAdd_Velocity | 1000 | 0.319 | 0.421 | 319.5 | 421.4 | new | -70056 |
| DeferredAdd_Velocity | 10000 | 1.728 | 3.290 | 172.8 | 329.0 | new | -2343960 |
| DeferredAdd_Velocity | 50000 | 6.152 | 7.010 | 123.0 | 140.2 | new | -9394605 |
| DeferredDestroy_SparseVictims | 1000 | 0.195 | 0.205 | 389.4 | 410.2 | new | -67304 |
| DeferredDestroy_SparseVictims | 10000 | 1.542 | 1.445 | 308.5 | 289.0 | old | -1761624 |
| DeferredDestroy_SparseVictims | 50000 | 2.191 | 3.320 | 87.7 | 132.8 | new | -7090646 |
| DeferredDestroy | 1000 | 0.159 | 0.213 | 159.2 | 212.5 | new | -67496 |
| DeferredDestroy | 10000 | 1.160 | 0.978 | 116.0 | 97.8 | old | -1812696 |
| DeferredDestroy | 50000 | 2.414 | 3.531 | 48.3 | 70.6 | new | -7214880 |
| DeferredRemove_Velocity | 1000 | 0.324 | 0.416 | 323.7 | 415.5 | new | -78208 |
| DeferredRemove_Velocity | 10000 | 2.112 | 3.270 | 211.2 | 327.0 | new | -1977328 |
| DeferredRemove_Velocity | 50000 | 5.564 | 7.671 | 111.3 | 153.4 | new | -8017496 |
| DirtyDrain_Position | 1000 | 0.222 | 0.235 | 221.9 | 234.6 | new | -68160 |
| DirtyDrain_Position | 10000 | 1.724 | 1.460 | 172.4 | 146.0 | old | -1711600 |
| DirtyDrain_Position | 50000 | 2.648 | 3.948 | 53.0 | 79.0 | new | -6967411 |
| HotLoop_DeferredAdd_Velocity | 50000 | 3.176 | 3.850 | 63.5 | 77.0 | new | -2824200 |
| HotLoop_DirtyDrain_Position | 50000 | 0.810 | 0.862 | 16.2 | 17.2 | new | -1344 |
| HotLoop_Query1_Read_Position | 50000 | 0.118 | 0.167 | 2.4 | 3.3 | new | -1432 |
| HotLoop_SetComponent_Position | 50000 | 0.681 | 0.738 | 13.6 | 14.8 | new | -1344 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 168.358 | 189.473 | 21.0 | 23.7 | new | -748272 |
| HotLoop_TickParallel_OscillatingMovement | 10000 | 133.682 | 378.406 | 16.7 | 47.3 | new | 99456 |
| HotLoop_TickParallel_OscillatingMovement | 100000 | 927.200 | 3721.844 | 11.6 | 46.5 | new | 99456 |
| HotLoop_TickParallel_QuerySystems | 10000 | 24.675 | 41.595 | 3.1 | 5.2 | new | 25856 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 3.189 | 45.356 | 0.4 | 5.7 | new | 64256 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 69.254 | 70.093 | 8.7 | 8.8 | new | -1344 |
| HotLoop_TickSerial_OscillatingMovement | 10000 | 175.546 | 325.957 | 21.9 | 40.7 | new | 72256 |
| HotLoop_TickSerial_OscillatingMovement | 100000 | 1440.777 | 3031.965 | 18.0 | 37.9 | new | 72256 |
| HotLoop_TickSerial_QuerySystems | 10000 | 13.148 | 17.297 | 1.6 | 2.2 | new | -26944 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 6.199 | 20.750 | 0.8 | 2.6 | new | -1344 |
| HotLoop_TryGetComponent_Position | 50000 | 0.208 | 0.248 | 4.2 | 5.0 | new | -1344 |
| MigrationCycle_AddRemoveVelocity | 1000 | 1.605 | 2.118 | 802.6 | 1059.2 | new | -39232 |
| MigrationCycle_AddRemoveVelocity | 10000 | 1.783 | 4.530 | 89.2 | 226.5 | new | -2185496 |
| MigrationCycle_AddRemoveVelocity | 50000 | 3.774 | 7.787 | 37.7 | 77.9 | new | -8591745 |
| PersistenceView_Filtered_Position | 1000 | 0.264 | 0.316 | 264.4 | 316.0 | new | -68896 |
| PersistenceView_Filtered_Position | 10000 | 2.103 | 2.308 | 210.3 | 230.8 | new | -2070872 |
| PersistenceView_Filtered_Position | 50000 | 3.484 | 5.977 | 69.7 | 119.5 | new | -8297496 |
| Query1_Read_Position | 1000 | 0.167 | 0.202 | 166.5 | 201.8 | new | -51136 |
| Query1_Read_Position | 10000 | 1.208 | 1.636 | 120.8 | 163.6 | new | -1551584 |
| Query1_Read_Position | 50000 | 2.903 | 5.042 | 58.1 | 100.8 | new | -6171264 |
| Query2_Read_PositionVelocity | 1000 | 0.233 | 0.263 | 232.9 | 262.9 | new | -59320 |
| Query2_Read_PositionVelocity | 10000 | 1.932 | 2.647 | 193.2 | 264.7 | new | -1807544 |
| Query2_Read_PositionVelocity | 50000 | 5.205 | 6.766 | 104.1 | 135.3 | new | -7211649 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.291 | 0.329 | 290.5 | 328.5 | new | -67504 |
| Query3_Read_PositionVelocityHealth | 10000 | 2.590 | 3.436 | 259.0 | 343.6 | new | -2061488 |
| Query3_Read_PositionVelocityHealth | 50000 | 7.526 | 9.086 | 150.5 | 181.7 | new | -8251977 |
| SetComponent_Position | 1000 | 0.710 | 1.449 | 709.6 | 1449.5 | new | -59152 |
| SetComponent_Position | 10000 | 1.579 | 2.438 | 157.9 | 243.8 | new | -1631600 |
| SetComponent_Position | 50000 | 5.441 | 8.564 | 108.8 | 171.3 | new | -6571280 |
| TickParallel_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 461.374 | 2037.134 | 5.8 | 25.5 | new | -12806143 |
| TickParallel_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 265.631 | 542.877 | 4.4 | 9.0 | new | 11795262 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 173.631 | 194.304 | 21.7 | 24.3 | new | -7456775 |
| TickParallel_OscillatingMovement_100k_200Ticks | 100000 | 812.218 | 3700.769 | 10.2 | 46.3 | new | -12917424 |
| TickParallel_OscillatingMovement_10k_200Ticks | 10000 | 116.840 | 382.330 | 14.6 | 47.8 | new | -1964560 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 32.701 | 45.312 | 4.1 | 5.7 | new | -1497292 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 9.503 | 60.462 | 1.2 | 7.6 | new | -2164216 |
| TickParallel_V1PulseMovement_100k_50Ticks | 100000 | 99.067 | 216.214 | 6.6 | 14.4 | new | 13691752 |
| TickSerial_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 735.768 | 2257.748 | 9.2 | 28.2 | new | -16256647 |
| TickSerial_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 165.693 | 638.865 | 2.8 | 10.6 | new | 7809800 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 75.996 | 78.900 | 9.5 | 9.9 | new | -1804328 |
| TickSerial_OscillatingMovement_100k_200Ticks | 100000 | 1216.409 | 2976.923 | 15.2 | 37.2 | new | -16527816 |
| TickSerial_OscillatingMovement_10k_200Ticks | 10000 | 153.332 | 280.816 | 19.2 | 35.1 | new | -1974952 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 15.910 | 30.792 | 2.0 | 3.8 | new | -1305264 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 10.235 | 32.417 | 1.3 | 4.1 | new | -1804408 |
| TickSerial_V1PulseMovement_100k_50Ticks | 100000 | 62.375 | 161.078 | 4.2 | 10.7 | new | 4008328 |
| TryGetComponent_Position | 1000 | 0.205 | 0.381 | 205.1 | 381.0 | new | -59152 |
| TryGetComponent_Position | 10000 | 1.188 | 1.878 | 118.8 | 187.8 | new | -1631600 |
| TryGetComponent_Position | 50000 | 4.999 | 8.850 | 100.0 | 177.0 | new | -6571280 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3536`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.114 ms`, alloc `91896`
- `PatchComponent_Position` scale `1000` mean `0.225 ms`, alloc `145480`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.954 ms`, alloc `2058648`
- `PatchComponent_Position` scale `10000` mean `1.286 ms`, alloc `2064976`
- `CreateDefaultImmediate_Position` scale `50000` mean `1.536 ms`, alloc `8350992`
- `PatchComponent_Position` scale `50000` mean `2.878 ms`, alloc `9055456`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.008 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.188 ms`, alloc `205640`
- `GetComponentRef_Write_Position` scale `10000` mean `1.628 ms`, alloc `3696576`
- `GetComponentRef_Write_Position` scale `50000` mean `3.697 ms`, alloc `15622872`
