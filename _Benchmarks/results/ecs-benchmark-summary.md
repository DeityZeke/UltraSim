# ECS Benchmark Summary

Generated: 2026-04-22T01:49:29.7282179+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.066 | 0.296 | 66.5 | 296.0 | new | -23416 |
| CreateDefaultDeferred_Position | 10000 | 0.164 | 0.893 | 16.4 | 89.3 | new | -1323984 |
| CreateDefaultDeferred_Position | 50000 | 0.990 | 6.056 | 19.8 | 121.1 | new | -4981792 |
| CreateDestroyReuseChurn_Position | 1000 | 0.588 | 0.661 | 293.8 | 330.6 | new | -23432 |
| CreateDestroyReuseChurn_Position | 10000 | 1.159 | 2.509 | 59.6 | 129.0 | new | -590848 |
| CreateDestroyReuseChurn_Position | 50000 | 3.437 | 3.181 | 34.6 | 32.0 | old | -2478208 |
| DeferredAdd_Velocity | 1000 | 0.330 | 0.427 | 330.3 | 427.3 | new | -69816 |
| DeferredAdd_Velocity | 10000 | 1.743 | 3.275 | 174.3 | 327.5 | new | -2343720 |
| DeferredAdd_Velocity | 50000 | 5.589 | 10.487 | 111.8 | 209.7 | new | -9390886 |
| DeferredDestroy_SparseVictims | 1000 | 0.197 | 0.208 | 394.4 | 416.0 | new | -67064 |
| DeferredDestroy_SparseVictims | 10000 | 1.520 | 1.892 | 304.0 | 378.5 | new | -1761384 |
| DeferredDestroy_SparseVictims | 50000 | 2.191 | 4.811 | 87.6 | 192.4 | new | -7090416 |
| DeferredDestroy | 1000 | 0.165 | 0.242 | 164.6 | 241.6 | new | -67256 |
| DeferredDestroy | 10000 | 1.157 | 1.535 | 115.7 | 153.5 | new | -1812456 |
| DeferredDestroy | 50000 | 2.512 | 5.277 | 50.2 | 105.5 | new | -7214672 |
| DeferredRemove_Velocity | 1000 | 0.323 | 0.463 | 322.9 | 462.8 | new | -86512 |
| DeferredRemove_Velocity | 10000 | 2.086 | 3.793 | 208.6 | 379.3 | new | -2159488 |
| DeferredRemove_Velocity | 50000 | 5.874 | 9.150 | 117.5 | 183.0 | new | -8666152 |
| DirtyDrain_Position | 1000 | 0.252 | 0.235 | 251.8 | 235.5 | old | -67920 |
| DirtyDrain_Position | 10000 | 1.571 | 2.882 | 157.1 | 288.2 | new | -1711360 |
| DirtyDrain_Position | 50000 | 2.849 | 5.492 | 57.0 | 109.8 | new | -6967176 |
| HotLoop_DeferredAdd_Velocity | 50000 | 3.561 | 3.844 | 71.2 | 76.9 | new | -2824200 |
| HotLoop_DirtyDrain_Position | 50000 | 0.835 | 0.916 | 16.7 | 18.3 | new | -1344 |
| HotLoop_Query1_Read_Position | 50000 | 0.120 | 0.168 | 2.4 | 3.4 | new | -1432 |
| HotLoop_SetComponent_Position | 50000 | 0.693 | 0.751 | 13.9 | 15.0 | new | -1344 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 177.747 | 191.484 | 22.2 | 23.9 | new | -1446408 |
| HotLoop_TickParallel_OscillatingMovement | 10000 | 134.007 | 398.222 | 16.8 | 49.8 | new | 101056 |
| HotLoop_TickParallel_OscillatingMovement | 100000 | 931.943 | 3812.842 | 11.6 | 47.7 | new | 101056 |
| HotLoop_TickParallel_QuerySystems | 10000 | 24.248 | 42.190 | 3.0 | 5.3 | new | 27456 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 3.057 | 46.468 | 0.4 | 5.8 | new | 65856 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 72.643 | 124.617 | 9.1 | 15.6 | new | -1344 |
| HotLoop_TickSerial_OscillatingMovement | 10000 | 170.084 | 293.725 | 21.3 | 36.7 | new | 72256 |
| HotLoop_TickSerial_OscillatingMovement | 100000 | 1366.133 | 3063.266 | 17.1 | 38.3 | new | 72256 |
| HotLoop_TickSerial_QuerySystems | 10000 | 14.069 | 17.600 | 1.8 | 2.2 | new | -26944 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 6.878 | 22.715 | 0.9 | 2.8 | new | -1546 |
| HotLoop_TryGetComponent_Position | 50000 | 0.212 | 0.251 | 4.2 | 5.0 | new | -1344 |
| MigrationCycle_AddRemoveVelocity | 1000 | 1.564 | 2.183 | 782.1 | 1091.7 | new | -64624 |
| MigrationCycle_AddRemoveVelocity | 10000 | 1.633 | 4.357 | 81.6 | 217.8 | new | -2732456 |
| MigrationCycle_AddRemoveVelocity | 50000 | 4.704 | 10.149 | 47.0 | 101.5 | new | -10538148 |
| PersistenceView_Filtered_Position | 1000 | 0.274 | 0.365 | 273.8 | 365.1 | new | -68656 |
| PersistenceView_Filtered_Position | 10000 | 2.536 | 5.433 | 253.6 | 543.3 | new | -2070632 |
| PersistenceView_Filtered_Position | 50000 | 3.547 | 8.238 | 70.9 | 164.8 | new | -8297344 |
| Query1_Read_Position | 1000 | 0.174 | 0.200 | 173.9 | 199.8 | new | -50896 |
| Query1_Read_Position | 10000 | 1.088 | 1.652 | 108.8 | 165.2 | new | -1551344 |
| Query1_Read_Position | 50000 | 2.814 | 4.580 | 56.3 | 91.6 | new | -6171019 |
| Query2_Read_PositionVelocity | 1000 | 0.240 | 0.276 | 240.4 | 276.2 | new | -59080 |
| Query2_Read_PositionVelocity | 10000 | 1.868 | 2.637 | 186.8 | 263.7 | new | -1807304 |
| Query2_Read_PositionVelocity | 50000 | 5.147 | 6.465 | 102.9 | 129.3 | new | -7211390 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.305 | 0.376 | 305.4 | 375.9 | new | -67264 |
| Query3_Read_PositionVelocityHealth | 10000 | 2.578 | 3.697 | 257.8 | 369.7 | new | -2061248 |
| Query3_Read_PositionVelocityHealth | 50000 | 7.706 | 9.018 | 154.1 | 180.4 | new | -8251728 |
| SetComponent_Position | 1000 | 0.711 | 1.449 | 710.7 | 1448.8 | new | -58912 |
| SetComponent_Position | 10000 | 1.531 | 2.298 | 153.1 | 229.8 | new | -1631360 |
| SetComponent_Position | 50000 | 4.853 | 9.219 | 97.1 | 184.4 | new | -6571049 |
| TickParallel_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 464.258 | 2056.945 | 5.8 | 25.7 | new | -12624927 |
| TickParallel_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 263.044 | 562.033 | 4.4 | 9.4 | new | 11706302 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 177.730 | 193.282 | 22.2 | 24.2 | new | -5353919 |
| TickParallel_OscillatingMovement_100k_200Ticks | 100000 | 816.498 | 3630.642 | 10.2 | 45.4 | new | -12913896 |
| TickParallel_OscillatingMovement_10k_200Ticks | 10000 | 114.807 | 370.439 | 14.4 | 46.3 | new | -1961032 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 31.397 | 45.208 | 3.9 | 5.7 | new | -1477764 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 9.554 | 59.824 | 1.2 | 7.5 | new | -2159449 |
| TickParallel_V1PulseMovement_100k_50Ticks | 100000 | 97.027 | 217.412 | 6.5 | 14.5 | new | 13693920 |
| TickSerial_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 743.693 | 2149.394 | 9.3 | 26.9 | new | -16256960 |
| TickSerial_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 166.243 | 645.226 | 2.8 | 10.8 | new | 7915976 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 77.571 | 127.971 | 9.7 | 16.0 | new | -1802400 |
| TickSerial_OscillatingMovement_100k_200Ticks | 100000 | 1134.519 | 2768.430 | 14.2 | 34.6 | new | -12331536 |
| TickSerial_OscillatingMovement_10k_200Ticks | 10000 | 139.303 | 265.054 | 17.4 | 33.1 | new | -1448688 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 16.837 | 27.745 | 2.1 | 3.5 | new | -1304574 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 10.176 | 33.862 | 1.3 | 4.2 | new | -1802480 |
| TickSerial_V1PulseMovement_100k_50Ticks | 100000 | 52.842 | 163.563 | 3.5 | 10.9 | new | 1767528 |
| TryGetComponent_Position | 1000 | 0.207 | 0.379 | 207.5 | 378.5 | new | -58912 |
| TryGetComponent_Position | 10000 | 1.140 | 1.970 | 114.0 | 197.0 | new | -1631360 |
| TryGetComponent_Position | 50000 | 2.976 | 5.003 | 59.5 | 100.1 | new | -6571035 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3536`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.118 ms`, alloc `92136`
- `PatchComponent_Position` scale `1000` mean `0.226 ms`, alloc `145720`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.736 ms`, alloc `2058888`
- `PatchComponent_Position` scale `10000` mean `1.667 ms`, alloc `2065216`
- `CreateDefaultImmediate_Position` scale `50000` mean `1.560 ms`, alloc `8351232`
- `PatchComponent_Position` scale `50000` mean `2.772 ms`, alloc `9055691`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.008 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.189 ms`, alloc `205640`
- `GetComponentRef_Write_Position` scale `10000` mean `2.811 ms`, alloc `3696576`
- `GetComponentRef_Write_Position` scale `50000` mean `4.948 ms`, alloc `15622872`
