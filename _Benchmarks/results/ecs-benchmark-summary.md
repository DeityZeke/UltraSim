# ECS Benchmark Summary

Generated: 2026-04-22T01:29:51.3262476+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.069 | 0.297 | 68.6 | 296.7 | new | -23416 |
| CreateDefaultDeferred_Position | 10000 | 0.161 | 0.881 | 16.1 | 88.1 | new | -1323984 |
| CreateDefaultDeferred_Position | 50000 | 0.978 | 3.710 | 19.6 | 74.2 | new | -4981792 |
| CreateDestroyReuseChurn_Position | 1000 | 0.591 | 0.647 | 295.5 | 323.3 | new | -23432 |
| CreateDestroyReuseChurn_Position | 10000 | 1.166 | 1.919 | 59.9 | 98.6 | new | -590848 |
| CreateDestroyReuseChurn_Position | 50000 | 2.568 | 3.086 | 25.9 | 31.1 | new | -2478208 |
| DeferredAdd_Velocity | 1000 | 0.331 | 0.418 | 330.7 | 417.7 | new | -69816 |
| DeferredAdd_Velocity | 10000 | 1.847 | 3.261 | 184.7 | 326.1 | new | -2343720 |
| DeferredAdd_Velocity | 50000 | 6.518 | 8.108 | 130.4 | 162.2 | new | -9390866 |
| DeferredDestroy_SparseVictims | 1000 | 0.193 | 0.206 | 386.2 | 411.8 | new | -67064 |
| DeferredDestroy_SparseVictims | 10000 | 1.543 | 1.751 | 308.6 | 350.3 | new | -1761384 |
| DeferredDestroy_SparseVictims | 50000 | 2.227 | 4.721 | 89.1 | 188.8 | new | -7090421 |
| DeferredDestroy | 1000 | 0.184 | 0.211 | 184.2 | 210.6 | new | -67256 |
| DeferredDestroy | 10000 | 1.112 | 1.668 | 111.2 | 166.8 | new | -1812456 |
| DeferredDestroy | 50000 | 2.298 | 5.175 | 46.0 | 103.5 | new | -7214655 |
| DeferredRemove_Velocity | 1000 | 0.326 | 0.416 | 326.3 | 415.7 | new | -86512 |
| DeferredRemove_Velocity | 10000 | 2.050 | 3.105 | 205.0 | 310.5 | new | -2159488 |
| DeferredRemove_Velocity | 50000 | 5.805 | 9.311 | 116.1 | 186.2 | new | -8666152 |
| DirtyDrain_Position | 1000 | 0.221 | 0.238 | 221.5 | 238.3 | new | -67920 |
| DirtyDrain_Position | 10000 | 1.507 | 1.732 | 150.7 | 173.2 | new | -1711360 |
| DirtyDrain_Position | 50000 | 2.719 | 5.547 | 54.4 | 110.9 | new | -6967181 |
| HotLoop_DeferredAdd_Velocity | 50000 | 3.420 | 3.742 | 68.4 | 74.8 | new | -2824200 |
| HotLoop_DirtyDrain_Position | 50000 | 0.843 | 0.881 | 16.9 | 17.6 | new | -1344 |
| HotLoop_Query1_Read_Position | 50000 | 0.118 | 0.193 | 2.4 | 3.9 | new | -1432 |
| HotLoop_SetComponent_Position | 50000 | 0.667 | 0.754 | 13.3 | 15.1 | new | -1344 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 164.859 | 191.699 | 20.6 | 24.0 | new | -346808 |
| HotLoop_TickParallel_OscillatingMovement | 10000 | 130.921 | 397.562 | 16.4 | 49.7 | new | 101056 |
| HotLoop_TickParallel_OscillatingMovement | 100000 | 929.935 | 3897.665 | 11.6 | 48.7 | new | 101056 |
| HotLoop_TickParallel_QuerySystems | 10000 | 23.738 | 41.475 | 3.0 | 5.2 | new | 27456 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 3.061 | 45.141 | 0.4 | 5.6 | new | 65856 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 79.878 | 71.813 | 10.0 | 9.0 | old | -1344 |
| HotLoop_TickSerial_OscillatingMovement | 10000 | 161.769 | 301.238 | 20.2 | 37.7 | new | 72256 |
| HotLoop_TickSerial_OscillatingMovement | 100000 | 1354.761 | 3133.502 | 16.9 | 39.2 | new | 72256 |
| HotLoop_TickSerial_QuerySystems | 10000 | 13.847 | 16.913 | 1.7 | 2.1 | new | -26944 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 6.333 | 22.680 | 0.8 | 2.8 | new | -1344 |
| HotLoop_TryGetComponent_Position | 50000 | 0.205 | 0.569 | 4.1 | 11.4 | new | -1344 |
| MigrationCycle_AddRemoveVelocity | 1000 | 1.576 | 2.149 | 787.8 | 1074.6 | new | -64624 |
| MigrationCycle_AddRemoveVelocity | 10000 | 1.623 | 4.142 | 81.1 | 207.1 | new | -2732456 |
| MigrationCycle_AddRemoveVelocity | 50000 | 4.592 | 10.116 | 45.9 | 101.2 | new | -10538148 |
| PersistenceView_Filtered_Position | 1000 | 0.270 | 0.312 | 269.8 | 312.3 | new | -68656 |
| PersistenceView_Filtered_Position | 10000 | 2.572 | 2.878 | 257.2 | 287.8 | new | -2070632 |
| PersistenceView_Filtered_Position | 50000 | 3.670 | 8.070 | 73.4 | 161.4 | new | -8297336 |
| Query1_Read_Position | 1000 | 0.174 | 0.201 | 174.0 | 200.9 | new | -50896 |
| Query1_Read_Position | 10000 | 1.079 | 1.604 | 107.9 | 160.4 | new | -1551344 |
| Query1_Read_Position | 50000 | 2.733 | 5.332 | 54.7 | 106.6 | new | -6171019 |
| Query2_Read_PositionVelocity | 1000 | 0.239 | 0.262 | 238.8 | 262.4 | new | -59080 |
| Query2_Read_PositionVelocity | 10000 | 1.934 | 2.635 | 193.4 | 263.5 | new | -1807304 |
| Query2_Read_PositionVelocity | 50000 | 5.166 | 8.007 | 103.3 | 160.1 | new | -7211380 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.300 | 0.320 | 299.5 | 319.6 | new | -67264 |
| Query3_Read_PositionVelocityHealth | 10000 | 2.588 | 3.503 | 258.8 | 350.3 | new | -2061248 |
| Query3_Read_PositionVelocityHealth | 50000 | 7.510 | 8.038 | 150.2 | 160.8 | new | -8251728 |
| SetComponent_Position | 1000 | 0.784 | 1.448 | 783.7 | 1448.1 | new | -58912 |
| SetComponent_Position | 10000 | 1.559 | 2.284 | 155.9 | 228.4 | new | -1631360 |
| SetComponent_Position | 50000 | 4.193 | 5.491 | 83.9 | 109.8 | new | -6571016 |
| TickParallel_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 459.540 | 2184.648 | 5.7 | 27.3 | new | -12676018 |
| TickParallel_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 261.041 | 558.754 | 4.4 | 9.3 | new | 11758668 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 170.664 | 194.648 | 21.3 | 24.3 | new | -6353631 |
| TickParallel_OscillatingMovement_100k_200Ticks | 100000 | 809.563 | 3615.867 | 10.1 | 45.2 | new | -12913896 |
| TickParallel_OscillatingMovement_10k_200Ticks | 10000 | 114.624 | 374.209 | 14.3 | 46.8 | new | -1961032 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 33.600 | 45.436 | 4.2 | 5.7 | new | -1493764 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 9.391 | 61.617 | 1.2 | 7.7 | new | -2160688 |
| TickParallel_V1PulseMovement_100k_50Ticks | 100000 | 95.762 | 218.001 | 6.4 | 14.5 | new | 13693920 |
| TickSerial_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 739.647 | 2066.269 | 9.2 | 25.8 | new | -16256960 |
| TickSerial_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 159.974 | 653.729 | 2.7 | 10.9 | new | 8047195 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 78.012 | 78.727 | 9.8 | 9.8 | new | -1802400 |
| TickSerial_OscillatingMovement_100k_200Ticks | 100000 | 1129.576 | 2690.489 | 14.1 | 33.6 | new | -12331536 |
| TickSerial_OscillatingMovement_10k_200Ticks | 10000 | 139.933 | 263.133 | 17.5 | 32.9 | new | -1448688 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 16.905 | 28.110 | 2.1 | 3.5 | new | -1302098 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 10.582 | 35.301 | 1.3 | 4.4 | new | -1802480 |
| TickSerial_V1PulseMovement_100k_50Ticks | 100000 | 52.374 | 164.034 | 3.5 | 10.9 | new | 1767528 |
| TryGetComponent_Position | 1000 | 0.210 | 0.379 | 209.8 | 378.9 | new | -58912 |
| TryGetComponent_Position | 10000 | 1.144 | 1.887 | 114.4 | 188.7 | new | -1631360 |
| TryGetComponent_Position | 50000 | 3.053 | 5.100 | 61.1 | 102.0 | new | -6571030 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3536`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.116 ms`, alloc `92136`
- `PatchComponent_Position` scale `1000` mean `0.227 ms`, alloc `145720`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.726 ms`, alloc `2058888`
- `PatchComponent_Position` scale `10000` mean `1.711 ms`, alloc `2065216`
- `CreateDefaultImmediate_Position` scale `50000` mean `1.541 ms`, alloc `8351232`
- `PatchComponent_Position` scale `50000` mean `3.005 ms`, alloc `9055696`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.008 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.189 ms`, alloc `205640`
- `GetComponentRef_Write_Position` scale `10000` mean `1.556 ms`, alloc `3696576`
- `GetComponentRef_Write_Position` scale `50000` mean `4.943 ms`, alloc `15622872`
