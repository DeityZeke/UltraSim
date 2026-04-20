# ECS Benchmark Summary

Generated: 2026-04-20T01:43:06.0142372+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.117 | 0.288 | 116.8 | 288.1 | new | 8448 |
| CreateDefaultDeferred_Position | 10000 | 0.653 | 0.848 | 65.3 | 84.8 | new | -554672 |
| CreateDefaultDeferred_Position | 50000 | 2.491 | 5.745 | 49.8 | 114.9 | new | -1853136 |
| CreateDestroyReuseChurn_Position | 1000 | 0.732 | 0.667 | 366.2 | 333.4 | old | 20456 |
| CreateDestroyReuseChurn_Position | 10000 | 2.439 | 2.376 | 125.3 | 122.1 | old | 244208 |
| CreateDestroyReuseChurn_Position | 50000 | 3.732 | 2.956 | 37.6 | 29.8 | old | 1245728 |
| DeferredAdd_Velocity | 1000 | 0.410 | 0.439 | 410.4 | 439.3 | new | -22720 |
| DeferredAdd_Velocity | 10000 | 3.970 | 3.219 | 397.0 | 321.9 | old | 107408 |
| DeferredAdd_Velocity | 50000 | 6.460 | 6.834 | 129.2 | 136.7 | new | 75956 |
| DeferredDestroy_SparseVictims | 1000 | 0.257 | 0.209 | 513.3 | 417.6 | old | -28192 |
| DeferredDestroy_SparseVictims | 10000 | 1.902 | 1.843 | 380.3 | 368.7 | old | 261352 |
| DeferredDestroy_SparseVictims | 50000 | 3.491 | 3.400 | 139.6 | 136.0 | old | 699356 |
| DeferredDestroy | 1000 | 0.283 | 0.239 | 283.1 | 239.0 | old | -11976 |
| DeferredDestroy | 10000 | 2.422 | 1.516 | 242.2 | 151.6 | old | 472448 |
| DeferredDestroy | 50000 | 4.601 | 3.565 | 92.0 | 71.3 | old | 1623712 |
| DeferredRemove_Velocity | 1000 | 0.636 | 0.413 | 636.0 | 413.4 | old | -22904 |
| DeferredRemove_Velocity | 10000 | 3.935 | 3.284 | 393.5 | 328.4 | old | 490032 |
| DeferredRemove_Velocity | 50000 | 6.865 | 7.178 | 137.3 | 143.6 | new | 1469619 |
| DirtyDrain_Position | 1000 | 0.296 | 0.238 | 296.2 | 238.2 | old | -45560 |
| DirtyDrain_Position | 10000 | 0.938 | 2.418 | 93.8 | 241.8 | new | 49008 |
| DirtyDrain_Position | 50000 | 4.298 | 3.981 | 86.0 | 79.6 | old | -226247 |
| HotLoop_DeferredAdd_Velocity | 50000 | 2.834 | 3.702 | 56.7 | 74.0 | new | 305232 |
| HotLoop_DirtyDrain_Position | 50000 | 0.968 | 0.855 | 19.4 | 17.1 | old | -1344 |
| HotLoop_Query1_Read_Position | 50000 | 0.238 | 0.169 | 4.8 | 3.4 | old | -1344 |
| HotLoop_SetComponent_Position | 50000 | 0.838 | 0.748 | 16.8 | 15.0 | old | -1344 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 79.644 | 189.556 | 10.0 | 23.7 | new | -837872 |
| HotLoop_TickParallel_QuerySystems | 10000 | 80.326 | 41.806 | 10.0 | 5.2 | old | -25344 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 45.332 | 48.591 | 5.7 | 6.1 | new | 256 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 35.889 | 71.272 | 4.5 | 8.9 | new | -39744 |
| HotLoop_TickSerial_QuerySystems | 10000 | 37.083 | 17.781 | 4.6 | 2.2 | old | -26944 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 17.584 | 24.283 | 2.2 | 3.0 | new | -1344 |
| HotLoop_TryGetComponent_Position | 50000 | 0.231 | 0.253 | 4.6 | 5.1 | new | -1344 |
| MigrationCycle_AddRemoveVelocity | 1000 | 1.899 | 2.158 | 949.6 | 1079.2 | new | -8360 |
| MigrationCycle_AddRemoveVelocity | 10000 | 2.636 | 3.700 | 131.8 | 185.0 | new | 367960 |
| MigrationCycle_AddRemoveVelocity | 50000 | 5.378 | 7.400 | 53.8 | 74.0 | new | 1127352 |
| PersistenceView_Filtered_Position | 1000 | 0.398 | 0.324 | 398.0 | 323.9 | old | -35320 |
| PersistenceView_Filtered_Position | 10000 | 2.189 | 4.779 | 218.9 | 477.9 | new | 309736 |
| PersistenceView_Filtered_Position | 50000 | 5.582 | 5.705 | 111.6 | 114.1 | new | 695632 |
| Query1_Read_Position | 1000 | 0.225 | 0.196 | 224.9 | 196.2 | old | -44616 |
| Query1_Read_Position | 10000 | 2.283 | 1.623 | 228.3 | 162.3 | old | 48944 |
| Query1_Read_Position | 50000 | 4.432 | 4.531 | 88.6 | 90.6 | new | -226147 |
| Query2_Read_PositionVelocity | 1000 | 0.326 | 0.255 | 325.8 | 255.0 | old | -36640 |
| Query2_Read_PositionVelocity | 10000 | 3.472 | 2.593 | 347.2 | 259.3 | old | 229432 |
| Query2_Read_PositionVelocity | 50000 | 6.494 | 6.283 | 129.9 | 125.7 | old | 422170 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.423 | 0.314 | 422.6 | 313.9 | old | -32600 |
| Query3_Read_PositionVelocityHealth | 10000 | 4.561 | 3.461 | 456.1 | 346.1 | old | 320448 |
| Query3_Read_PositionVelocityHealth | 50000 | 8.448 | 8.216 | 169.0 | 164.3 | old | 746464 |
| SetComponent_Position | 1000 | 0.326 | 1.450 | 326.1 | 1450.3 | new | -44552 |
| SetComponent_Position | 10000 | 2.707 | 2.261 | 270.7 | 226.1 | old | 49008 |
| SetComponent_Position | 50000 | 5.683 | 13.128 | 113.7 | 262.6 | new | -226121 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 80.892 | 192.295 | 10.1 | 24.0 | new | -2609920 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 82.378 | 44.516 | 10.3 | 5.6 | old | 3800772 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 46.983 | 54.655 | 5.9 | 6.8 | new | 226276 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 37.340 | 78.148 | 4.7 | 9.8 | new | 193680 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 48.976 | 30.460 | 6.1 | 3.8 | old | 206480 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 30.316 | 33.083 | 3.8 | 4.1 | new | 231896 |
| TryGetComponent_Position | 1000 | 0.359 | 0.370 | 359.3 | 370.4 | new | -44552 |
| TryGetComponent_Position | 10000 | 2.575 | 1.831 | 257.5 | 183.1 | old | 49008 |
| TryGetComponent_Position | 50000 | 4.683 | 4.824 | 93.7 | 96.5 | new | -226078 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3536`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.125 ms`, alloc `104912`
- `PatchComponent_Position` scale `1000` mean `0.313 ms`, alloc `158000`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.354 ms`, alloc `2563184`
- `PatchComponent_Position` scale `10000` mean `0.956 ms`, alloc `3743504`
- `CreateDefaultImmediate_Position` scale `50000` mean `1.575 ms`, alloc `10428488`
- `PatchComponent_Position` scale `50000` mean `4.229 ms`, alloc `15394536`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.008 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.190 ms`, alloc `203560`
- `GetComponentRef_Write_Position` scale `10000` mean `2.877 ms`, alloc `3694496`
- `GetComponentRef_Write_Position` scale `50000` mean `4.442 ms`, alloc `15620782`
