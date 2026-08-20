# ECS Benchmark Summary

Generated: 2026-04-28T01:55:29.8022225+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.067 | 0.324 | 67.2 | 324.4 | new | -23416 |
| CreateDefaultDeferred_Position | 10000 | 0.181 | 0.971 | 18.1 | 97.1 | new | -1323984 |
| CreateDefaultDeferred_Position | 50000 | 1.344 | 3.423 | 26.9 | 68.5 | new | -4981792 |
| CreateDestroyReuseChurn_Position | 1000 | 0.750 | 0.670 | 375.1 | 334.9 | old | -23432 |
| CreateDestroyReuseChurn_Position | 10000 | 1.343 | 1.599 | 69.0 | 82.2 | new | -590848 |
| CreateDestroyReuseChurn_Position | 50000 | 1.959 | 3.588 | 19.7 | 36.1 | new | -2478208 |
| DeferredAdd_Velocity | 1000 | 0.358 | 0.472 | 357.7 | 472.3 | new | -69816 |
| DeferredAdd_Velocity | 10000 | 2.088 | 3.616 | 208.8 | 361.6 | new | -2343720 |
| DeferredAdd_Velocity | 50000 | 6.130 | 7.195 | 122.6 | 143.9 | new | -9395183 |
| DeferredDestroy_SparseVictims | 1000 | 0.209 | 0.219 | 418.7 | 438.8 | new | -67064 |
| DeferredDestroy_SparseVictims | 10000 | 1.728 | 1.462 | 345.6 | 292.4 | old | -1761384 |
| DeferredDestroy_SparseVictims | 50000 | 2.480 | 3.797 | 99.2 | 151.9 | new | -7090436 |
| DeferredDestroy | 1000 | 0.181 | 0.231 | 181.2 | 231.1 | new | -67256 |
| DeferredDestroy | 10000 | 1.286 | 1.780 | 128.6 | 178.0 | new | -1812456 |
| DeferredDestroy | 50000 | 2.706 | 3.880 | 54.1 | 77.6 | new | -7214648 |
| DeferredRemove_Velocity | 1000 | 0.356 | 0.459 | 356.2 | 459.0 | new | -87750 |
| DeferredRemove_Velocity | 10000 | 2.392 | 4.646 | 239.2 | 464.6 | new | -2159488 |
| DeferredRemove_Velocity | 50000 | 5.180 | 10.190 | 103.6 | 203.8 | new | -8666508 |
| DirtyDrain_Position | 1000 | 0.248 | 0.256 | 248.0 | 256.0 | new | -67920 |
| DirtyDrain_Position | 10000 | 1.746 | 2.027 | 174.6 | 202.7 | new | -1711360 |
| DirtyDrain_Position | 50000 | 3.117 | 4.819 | 62.3 | 96.4 | new | -6967191 |
| HotLoop_DeferredAdd_Velocity | 50000 | 3.115 | 4.607 | 62.3 | 92.1 | new | -2824200 |
| HotLoop_DirtyDrain_Position | 50000 | 0.850 | 1.180 | 17.0 | 23.6 | new | -1344 |
| HotLoop_Query1_Read_Position | 50000 | 0.121 | 0.286 | 2.4 | 5.7 | new | -1432 |
| HotLoop_SetComponent_Position | 50000 | 0.760 | 1.061 | 15.2 | 21.2 | new | -1344 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 286.241 | 324.484 | 35.8 | 40.6 | new | -1946264 |
| HotLoop_TickParallel_OscillatingMovement | 10000 | 122.541 | 481.086 | 15.3 | 60.1 | new | 101056 |
| HotLoop_TickParallel_OscillatingMovement | 100000 | 965.344 | 5302.388 | 12.1 | 66.3 | new | 101056 |
| HotLoop_TickParallel_QuerySystems | 10000 | 28.163 | 46.128 | 3.5 | 5.8 | new | 27456 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 2.928 | 115.867 | 0.4 | 14.5 | new | 65856 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 77.118 | 90.809 | 9.6 | 11.4 | new | -1344 |
| HotLoop_TickSerial_OscillatingMovement | 10000 | 139.729 | 265.468 | 17.5 | 33.2 | new | 72256 |
| HotLoop_TickSerial_OscillatingMovement | 100000 | 1327.581 | 3016.915 | 16.6 | 37.7 | new | 72256 |
| HotLoop_TickSerial_QuerySystems | 10000 | 13.558 | 17.255 | 1.7 | 2.2 | new | -26944 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 6.537 | 50.352 | 0.8 | 6.3 | new | -1344 |
| HotLoop_TryGetComponent_Position | 50000 | 0.225 | 0.291 | 4.5 | 5.8 | new | -1344 |
| MigrationCycle_AddRemoveVelocity | 1000 | 2.357 | 2.141 | 1178.3 | 1070.5 | old | -64624 |
| MigrationCycle_AddRemoveVelocity | 10000 | 1.887 | 3.370 | 94.3 | 168.5 | new | -2732456 |
| MigrationCycle_AddRemoveVelocity | 50000 | 3.762 | 8.428 | 37.6 | 84.3 | new | -10538180 |
| PersistenceView_Filtered_Position | 1000 | 0.428 | 0.330 | 428.1 | 330.0 | old | -68656 |
| PersistenceView_Filtered_Position | 10000 | 3.797 | 2.899 | 379.7 | 289.9 | old | -2070632 |
| PersistenceView_Filtered_Position | 50000 | 4.835 | 7.790 | 96.7 | 155.8 | new | -8296954 |
| Query1_Read_Position | 1000 | 0.216 | 0.345 | 216.0 | 344.9 | new | -50896 |
| Query1_Read_Position | 10000 | 1.311 | 1.809 | 131.1 | 180.9 | new | -1551344 |
| Query1_Read_Position | 50000 | 3.432 | 5.263 | 68.6 | 105.3 | new | -6171024 |
| Query2_Read_PositionVelocity | 1000 | 0.266 | 0.299 | 265.7 | 299.4 | new | -59080 |
| Query2_Read_PositionVelocity | 10000 | 2.272 | 3.160 | 227.2 | 316.0 | new | -1807304 |
| Query2_Read_PositionVelocity | 50000 | 5.488 | 7.256 | 109.8 | 145.1 | new | -7211428 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.330 | 0.372 | 329.8 | 371.7 | new | -67264 |
| Query3_Read_PositionVelocityHealth | 10000 | 3.075 | 3.919 | 307.5 | 391.9 | new | -2061248 |
| Query3_Read_PositionVelocityHealth | 50000 | 7.400 | 9.892 | 148.0 | 197.8 | new | -8251728 |
| SetComponent_Position | 1000 | 0.795 | 1.768 | 795.2 | 1768.4 | new | -58912 |
| SetComponent_Position | 10000 | 1.833 | 2.569 | 183.3 | 256.9 | new | -1631360 |
| SetComponent_Position | 50000 | 4.255 | 8.039 | 85.1 | 160.8 | new | -6571040 |
| TickParallel_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 504.888 | 2698.106 | 6.3 | 33.7 | new | -8483338 |
| TickParallel_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 292.164 | 821.583 | 4.9 | 13.7 | new | 14458032 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 286.835 | 326.182 | 35.9 | 40.8 | new | -13751055 |
| TickParallel_OscillatingMovement_100k_200Ticks | 100000 | 883.702 | 4979.770 | 11.0 | 62.2 | new | -12913896 |
| TickParallel_OscillatingMovement_10k_200Ticks | 10000 | 113.187 | 467.439 | 14.1 | 58.4 | new | -1961032 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 35.211 | 48.904 | 4.4 | 6.1 | new | -1444088 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 6.206 | 116.980 | 0.8 | 14.6 | new | -2159433 |
| TickParallel_V1PulseMovement_100k_50Ticks | 100000 | 106.146 | 132.039 | 7.1 | 8.8 | new | 16098897 |
| TickSerial_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 786.341 | 2299.883 | 9.8 | 28.7 | new | -16254621 |
| TickSerial_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 212.423 | 1017.609 | 3.5 | 17.0 | new | 6132873 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 78.585 | 94.762 | 9.8 | 11.8 | new | -1802400 |
| TickSerial_OscillatingMovement_100k_200Ticks | 100000 | 1162.845 | 2731.867 | 14.5 | 34.1 | new | -12331920 |
| TickSerial_OscillatingMovement_10k_200Ticks | 10000 | 126.276 | 238.136 | 15.8 | 29.8 | new | -1449072 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 17.574 | 29.050 | 2.2 | 3.6 | new | -1303336 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 10.062 | 54.945 | 1.3 | 6.9 | new | -1802480 |
| TickSerial_V1PulseMovement_100k_50Ticks | 100000 | 55.160 | 78.045 | 3.7 | 5.2 | new | 1767144 |
| TryGetComponent_Position | 1000 | 0.232 | 0.647 | 232.1 | 646.8 | new | -58912 |
| TryGetComponent_Position | 10000 | 1.460 | 2.115 | 146.0 | 211.5 | new | -1631360 |
| TryGetComponent_Position | 50000 | 3.838 | 7.095 | 76.8 | 141.9 | new | -6571044 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.010 ms`, alloc `3536`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.123 ms`, alloc `92136`
- `PatchComponent_Position` scale `1000` mean `0.271 ms`, alloc `145720`
- `CreateDefaultImmediate_Position` scale `10000` mean `1.148 ms`, alloc `2058350`
- `PatchComponent_Position` scale `10000` mean `0.975 ms`, alloc `2065216`
- `CreateDefaultImmediate_Position` scale `50000` mean `1.745 ms`, alloc `8351236`
- `PatchComponent_Position` scale `50000` mean `3.411 ms`, alloc `9054716`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.010 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.205 ms`, alloc `205640`
- `GetComponentRef_Write_Position` scale `10000` mean `1.337 ms`, alloc `3696576`
- `GetComponentRef_Write_Position` scale `50000` mean `4.288 ms`, alloc `15622872`
