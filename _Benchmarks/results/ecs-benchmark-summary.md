# ECS Benchmark Summary

Generated: 2026-04-20T15:37:21.4977535+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.067 | 0.323 | 66.6 | 322.6 | new | -23656 |
| CreateDefaultDeferred_Position | 10000 | 0.159 | 0.824 | 15.9 | 82.4 | new | -1324224 |
| CreateDefaultDeferred_Position | 50000 | 0.991 | 5.829 | 19.8 | 116.6 | new | -4982032 |
| CreateDestroyReuseChurn_Position | 1000 | 0.583 | 0.630 | 291.6 | 315.0 | new | -23672 |
| CreateDestroyReuseChurn_Position | 10000 | 1.176 | 1.885 | 60.4 | 96.9 | new | -591088 |
| CreateDestroyReuseChurn_Position | 50000 | 2.019 | 2.975 | 20.3 | 29.9 | new | -2478448 |
| DeferredAdd_Velocity | 1000 | 0.332 | 0.421 | 331.6 | 421.4 | new | -70056 |
| DeferredAdd_Velocity | 10000 | 1.805 | 3.290 | 180.5 | 329.0 | new | -2343960 |
| DeferredAdd_Velocity | 50000 | 6.285 | 7.010 | 125.7 | 140.2 | new | -9394620 |
| DeferredDestroy_SparseVictims | 1000 | 0.191 | 0.205 | 383.0 | 410.2 | new | -67304 |
| DeferredDestroy_SparseVictims | 10000 | 1.596 | 1.445 | 319.1 | 289.0 | old | -1761624 |
| DeferredDestroy_SparseVictims | 50000 | 2.330 | 3.320 | 93.2 | 132.8 | new | -7090641 |
| DeferredDestroy | 1000 | 0.160 | 0.213 | 160.1 | 212.5 | new | -67496 |
| DeferredDestroy | 10000 | 1.149 | 0.978 | 114.9 | 97.8 | old | -1812696 |
| DeferredDestroy | 50000 | 2.316 | 3.531 | 46.3 | 70.6 | new | -7214856 |
| DeferredRemove_Velocity | 1000 | 0.317 | 0.416 | 317.2 | 415.5 | new | -78208 |
| DeferredRemove_Velocity | 10000 | 2.073 | 3.270 | 207.3 | 327.0 | new | -1977328 |
| DeferredRemove_Velocity | 50000 | 6.541 | 7.671 | 130.8 | 153.4 | new | -8017487 |
| DirtyDrain_Position | 1000 | 0.217 | 0.235 | 216.6 | 234.6 | new | -68160 |
| DirtyDrain_Position | 10000 | 1.498 | 1.460 | 149.8 | 146.0 | old | -1711600 |
| DirtyDrain_Position | 50000 | 2.712 | 3.948 | 54.2 | 79.0 | new | -6967406 |
| HotLoop_DeferredAdd_Velocity | 50000 | 3.312 | 3.850 | 66.2 | 77.0 | new | -2824200 |
| HotLoop_DirtyDrain_Position | 50000 | 0.817 | 0.862 | 16.3 | 17.2 | new | -1344 |
| HotLoop_Query1_Read_Position | 50000 | 0.256 | 0.167 | 5.1 | 3.3 | old | -1344 |
| HotLoop_SetComponent_Position | 50000 | 0.680 | 0.738 | 13.6 | 14.8 | new | -1344 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 178.498 | 189.473 | 22.3 | 23.7 | new | -448351 |
| HotLoop_TickParallel_OscillatingMovement | 10000 | 143.227 | 378.406 | 17.9 | 47.3 | new | 99456 |
| HotLoop_TickParallel_OscillatingMovement | 100000 | 1068.081 | 3721.844 | 13.4 | 46.5 | new | 99456 |
| HotLoop_TickParallel_QuerySystems | 10000 | 33.842 | 41.595 | 4.2 | 5.2 | new | 25856 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 3.099 | 45.356 | 0.4 | 5.7 | new | 64256 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 67.980 | 70.093 | 8.5 | 8.8 | new | -1344 |
| HotLoop_TickSerial_OscillatingMovement | 10000 | 173.252 | 325.957 | 21.7 | 40.7 | new | 72256 |
| HotLoop_TickSerial_OscillatingMovement | 100000 | 1451.920 | 3031.965 | 18.1 | 37.9 | new | 72256 |
| HotLoop_TickSerial_QuerySystems | 10000 | 13.074 | 17.297 | 1.6 | 2.2 | new | -26944 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 6.206 | 20.750 | 0.8 | 2.6 | new | -1344 |
| HotLoop_TryGetComponent_Position | 50000 | 0.209 | 0.248 | 4.2 | 5.0 | new | -1344 |
| MigrationCycle_AddRemoveVelocity | 1000 | 1.571 | 2.118 | 785.3 | 1059.2 | new | -39232 |
| MigrationCycle_AddRemoveVelocity | 10000 | 1.782 | 4.530 | 89.1 | 226.5 | new | -2185496 |
| MigrationCycle_AddRemoveVelocity | 50000 | 4.142 | 7.787 | 41.4 | 77.9 | new | -8591745 |
| PersistenceView_Filtered_Position | 1000 | 0.262 | 0.316 | 262.1 | 316.0 | new | -68896 |
| PersistenceView_Filtered_Position | 10000 | 2.543 | 2.308 | 254.3 | 230.8 | old | -2070872 |
| PersistenceView_Filtered_Position | 50000 | 3.347 | 5.977 | 66.9 | 119.5 | new | -8297480 |
| Query1_Read_Position | 1000 | 0.166 | 0.202 | 166.4 | 201.8 | new | -51136 |
| Query1_Read_Position | 10000 | 1.093 | 1.636 | 109.3 | 163.6 | new | -1551584 |
| Query1_Read_Position | 50000 | 3.336 | 5.042 | 66.7 | 100.8 | new | -6171264 |
| Query2_Read_PositionVelocity | 1000 | 0.231 | 0.263 | 231.1 | 262.9 | new | -59320 |
| Query2_Read_PositionVelocity | 10000 | 1.802 | 2.647 | 180.2 | 264.7 | new | -1807544 |
| Query2_Read_PositionVelocity | 50000 | 5.752 | 6.766 | 115.0 | 135.3 | new | -7210411 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.317 | 0.329 | 317.3 | 328.5 | new | -67504 |
| Query3_Read_PositionVelocityHealth | 10000 | 2.568 | 3.436 | 256.8 | 343.6 | new | -2061488 |
| Query3_Read_PositionVelocityHealth | 50000 | 7.521 | 9.086 | 150.4 | 181.7 | new | -8251977 |
| SetComponent_Position | 1000 | 0.702 | 1.449 | 701.7 | 1449.5 | new | -59152 |
| SetComponent_Position | 10000 | 1.516 | 2.438 | 151.6 | 243.8 | new | -1631600 |
| SetComponent_Position | 50000 | 4.071 | 8.564 | 81.4 | 171.3 | new | -6571280 |
| TickParallel_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 463.376 | 2037.134 | 5.8 | 25.5 | new | 195193974 |
| TickParallel_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 304.272 | 542.877 | 5.1 | 9.0 | new | 160737468 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 184.689 | 194.304 | 23.1 | 24.3 | new | -4432823 |
| TickParallel_OscillatingMovement_100k_200Ticks | 100000 | 957.303 | 3700.769 | 12.0 | 46.3 | new | 2122304 |
| TickParallel_OscillatingMovement_10k_200Ticks | 10000 | 127.741 | 382.330 | 16.0 | 47.8 | new | 159664 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 41.006 | 45.312 | 5.1 | 5.7 | new | -67676 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 6.639 | 60.462 | 0.8 | 7.6 | new | -1885144 |
| TickParallel_V1PulseMovement_100k_50Ticks | 100000 | 128.951 | 216.214 | 8.6 | 14.4 | new | 25862784 |
| TickSerial_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 730.351 | 2257.748 | 9.1 | 28.2 | new | -16258888 |
| TickSerial_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 171.018 | 638.865 | 2.9 | 10.6 | new | 8104120 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 74.904 | 78.900 | 9.4 | 9.9 | new | -1804328 |
| TickSerial_OscillatingMovement_100k_200Ticks | 100000 | 1203.948 | 2976.923 | 15.0 | 37.2 | new | -16527816 |
| TickSerial_OscillatingMovement_10k_200Ticks | 10000 | 145.260 | 280.816 | 18.2 | 35.1 | new | -1974952 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 16.734 | 30.792 | 2.1 | 3.8 | new | -1305264 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 9.063 | 32.417 | 1.1 | 4.1 | new | -1804408 |
| TickSerial_V1PulseMovement_100k_50Ticks | 100000 | 59.882 | 161.078 | 4.0 | 10.7 | new | 4008328 |
| TryGetComponent_Position | 1000 | 0.205 | 0.381 | 205.4 | 381.0 | new | -59152 |
| TryGetComponent_Position | 10000 | 1.199 | 1.878 | 119.9 | 187.8 | new | -1631600 |
| TryGetComponent_Position | 50000 | 3.639 | 8.850 | 72.8 | 177.0 | new | -6571280 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3536`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.110 ms`, alloc `91896`
- `PatchComponent_Position` scale `1000` mean `0.218 ms`, alloc `145480`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.736 ms`, alloc `2058648`
- `PatchComponent_Position` scale `10000` mean `1.421 ms`, alloc `2064976`
- `CreateDefaultImmediate_Position` scale `50000` mean `1.521 ms`, alloc `8350992`
- `PatchComponent_Position` scale `50000` mean `2.702 ms`, alloc `9055456`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.008 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.188 ms`, alloc `205640`
- `GetComponentRef_Write_Position` scale `10000` mean `1.628 ms`, alloc `3696576`
- `GetComponentRef_Write_Position` scale `50000` mean `3.697 ms`, alloc `15622872`
