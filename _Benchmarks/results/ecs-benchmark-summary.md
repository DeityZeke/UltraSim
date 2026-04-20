# ECS Benchmark Summary

Generated: 2026-04-20T01:58:53.2241004+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.070 | 0.288 | 70.2 | 288.1 | new | 64 |
| CreateDefaultDeferred_Position | 10000 | 0.326 | 0.848 | 32.6 | 84.8 | new | -635056 |
| CreateDefaultDeferred_Position | 50000 | 1.696 | 5.745 | 33.9 | 114.9 | new | -2253631 |
| CreateDestroyReuseChurn_Position | 1000 | 0.677 | 0.667 | 338.4 | 333.4 | old | -2008 |
| CreateDestroyReuseChurn_Position | 10000 | 1.236 | 2.376 | 63.5 | 122.1 | new | 26408 |
| CreateDestroyReuseChurn_Position | 50000 | 2.559 | 2.956 | 25.8 | 29.8 | new | 31400 |
| DeferredAdd_Velocity | 1000 | 0.406 | 0.439 | 406.1 | 439.3 | new | -22640 |
| DeferredAdd_Velocity | 10000 | 3.221 | 3.219 | 322.1 | 321.9 | old | 107488 |
| DeferredAdd_Velocity | 50000 | 6.345 | 6.834 | 126.9 | 136.7 | new | 76036 |
| DeferredDestroy_SparseVictims | 1000 | 0.293 | 0.209 | 585.6 | 417.6 | old | -36136 |
| DeferredDestroy_SparseVictims | 10000 | 0.823 | 1.843 | 164.7 | 368.7 | new | 181408 |
| DeferredDestroy_SparseVictims | 50000 | 3.549 | 3.400 | 142.0 | 136.0 | old | 299417 |
| DeferredDestroy | 1000 | 0.287 | 0.239 | 286.6 | 239.0 | old | -27920 |
| DeferredDestroy | 10000 | 1.457 | 1.516 | 145.7 | 151.6 | new | 312504 |
| DeferredDestroy | 50000 | 4.205 | 3.565 | 84.1 | 71.3 | old | 823768 |
| DeferredRemove_Velocity | 1000 | 0.656 | 0.413 | 655.6 | 413.4 | old | -22824 |
| DeferredRemove_Velocity | 10000 | 3.982 | 3.284 | 398.2 | 328.4 | old | 490112 |
| DeferredRemove_Velocity | 50000 | 6.821 | 7.178 | 136.4 | 143.6 | new | 1469704 |
| DirtyDrain_Position | 1000 | 0.319 | 0.238 | 319.2 | 238.2 | old | -45480 |
| DirtyDrain_Position | 10000 | 0.811 | 2.418 | 81.1 | 241.8 | new | 49088 |
| DirtyDrain_Position | 50000 | 3.747 | 3.981 | 74.9 | 79.6 | new | -226176 |
| HotLoop_DeferredAdd_Velocity | 50000 | 2.776 | 3.702 | 55.5 | 74.0 | new | 305232 |
| HotLoop_DirtyDrain_Position | 50000 | 0.866 | 0.855 | 17.3 | 17.1 | old | -1344 |
| HotLoop_Query1_Read_Position | 50000 | 0.229 | 0.169 | 4.6 | 3.4 | old | -1344 |
| HotLoop_SetComponent_Position | 50000 | 0.698 | 0.748 | 14.0 | 15.0 | new | -1344 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 79.623 | 189.556 | 10.0 | 23.7 | new | -837872 |
| HotLoop_TickParallel_QuerySystems | 10000 | 81.934 | 41.806 | 10.2 | 5.2 | old | -25344 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 47.107 | 48.591 | 5.9 | 6.1 | new | 256 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 36.215 | 71.272 | 4.5 | 8.9 | new | -39744 |
| HotLoop_TickSerial_QuerySystems | 10000 | 35.341 | 17.781 | 4.4 | 2.2 | old | -26944 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 17.601 | 24.283 | 2.2 | 3.0 | new | -1344 |
| HotLoop_TryGetComponent_Position | 50000 | 0.224 | 0.253 | 4.5 | 5.1 | new | -1344 |
| MigrationCycle_AddRemoveVelocity | 1000 | 1.918 | 2.158 | 958.9 | 1079.2 | new | -8280 |
| MigrationCycle_AddRemoveVelocity | 10000 | 1.635 | 3.700 | 81.8 | 185.0 | new | 368040 |
| MigrationCycle_AddRemoveVelocity | 50000 | 5.501 | 7.400 | 55.0 | 74.0 | new | 1127432 |
| PersistenceView_Filtered_Position | 1000 | 0.398 | 0.324 | 397.5 | 323.9 | old | -35240 |
| PersistenceView_Filtered_Position | 10000 | 1.666 | 4.779 | 166.6 | 477.9 | new | 309816 |
| PersistenceView_Filtered_Position | 50000 | 5.171 | 5.705 | 103.4 | 114.1 | new | 695712 |
| Query1_Read_Position | 1000 | 0.224 | 0.196 | 224.2 | 196.2 | old | -44536 |
| Query1_Read_Position | 10000 | 2.286 | 1.623 | 228.6 | 162.3 | old | 49024 |
| Query1_Read_Position | 50000 | 4.319 | 4.531 | 86.4 | 90.6 | new | -226067 |
| Query2_Read_PositionVelocity | 1000 | 0.327 | 0.255 | 327.3 | 255.0 | old | -36560 |
| Query2_Read_PositionVelocity | 10000 | 3.594 | 2.593 | 359.4 | 259.3 | old | 229512 |
| Query2_Read_PositionVelocity | 50000 | 6.394 | 6.283 | 127.9 | 125.7 | old | 422250 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.431 | 0.314 | 431.2 | 313.9 | old | -32520 |
| Query3_Read_PositionVelocityHealth | 10000 | 4.683 | 3.461 | 468.3 | 346.1 | old | 320528 |
| Query3_Read_PositionVelocityHealth | 50000 | 9.243 | 8.216 | 184.9 | 164.3 | old | 746544 |
| SetComponent_Position | 1000 | 0.322 | 1.450 | 322.2 | 1450.3 | new | -44472 |
| SetComponent_Position | 10000 | 2.839 | 2.261 | 283.9 | 226.1 | old | 49088 |
| SetComponent_Position | 50000 | 5.778 | 13.128 | 115.6 | 262.6 | new | -226036 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 83.106 | 192.295 | 10.4 | 24.0 | new | -1810128 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 83.799 | 44.516 | 10.5 | 5.6 | old | 4201955 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 48.088 | 54.655 | 6.0 | 6.8 | new | 226348 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 38.062 | 78.148 | 4.8 | 9.8 | new | 193760 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 50.703 | 30.460 | 6.3 | 3.8 | old | 206560 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 30.697 | 33.083 | 3.8 | 4.1 | new | 231976 |
| TryGetComponent_Position | 1000 | 0.355 | 0.370 | 355.1 | 370.4 | new | -44472 |
| TryGetComponent_Position | 10000 | 2.353 | 1.831 | 235.3 | 183.1 | old | 49088 |
| TryGetComponent_Position | 50000 | 5.017 | 4.824 | 100.3 | 96.5 | old | -225998 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3536`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.145 ms`, alloc `104992`
- `PatchComponent_Position` scale `1000` mean `0.308 ms`, alloc `158080`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.376 ms`, alloc `2563264`
- `PatchComponent_Position` scale `10000` mean `0.861 ms`, alloc `3743584`
- `CreateDefaultImmediate_Position` scale `50000` mean `1.649 ms`, alloc `10428568`
- `PatchComponent_Position` scale `50000` mean `3.835 ms`, alloc `15394630`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.008 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.190 ms`, alloc `203560`
- `GetComponentRef_Write_Position` scale `10000` mean `2.877 ms`, alloc `3694496`
- `GetComponentRef_Write_Position` scale `50000` mean `4.442 ms`, alloc `15620782`
