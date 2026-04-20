# ECS Benchmark Summary

Generated: 2026-04-20T01:49:10.7839930+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.074 | 0.288 | 73.7 | 288.1 | new | 64 |
| CreateDefaultDeferred_Position | 10000 | 0.300 | 0.848 | 30.0 | 84.8 | new | -635056 |
| CreateDefaultDeferred_Position | 50000 | 1.437 | 5.745 | 28.7 | 114.9 | new | -2253698 |
| CreateDestroyReuseChurn_Position | 1000 | 0.681 | 0.667 | 340.5 | 333.4 | old | -2008 |
| CreateDestroyReuseChurn_Position | 10000 | 1.273 | 2.376 | 65.4 | 122.1 | new | 26408 |
| CreateDestroyReuseChurn_Position | 50000 | 3.080 | 2.956 | 31.0 | 29.8 | old | 31400 |
| DeferredAdd_Velocity | 1000 | 0.399 | 0.439 | 398.7 | 439.3 | new | -22640 |
| DeferredAdd_Velocity | 10000 | 3.070 | 3.219 | 307.0 | 321.9 | new | 107488 |
| DeferredAdd_Velocity | 50000 | 6.468 | 6.834 | 129.4 | 136.7 | new | 76032 |
| DeferredDestroy_SparseVictims | 1000 | 0.252 | 0.209 | 504.1 | 417.6 | old | -36136 |
| DeferredDestroy_SparseVictims | 10000 | 0.806 | 1.843 | 161.3 | 368.7 | new | 181408 |
| DeferredDestroy_SparseVictims | 50000 | 3.580 | 3.400 | 143.2 | 136.0 | old | 299412 |
| DeferredDestroy | 1000 | 0.278 | 0.239 | 277.6 | 239.0 | old | -27920 |
| DeferredDestroy | 10000 | 1.236 | 1.516 | 123.5 | 151.6 | new | 312504 |
| DeferredDestroy | 50000 | 3.984 | 3.565 | 79.7 | 71.3 | old | 823768 |
| DeferredRemove_Velocity | 1000 | 0.636 | 0.413 | 636.5 | 413.4 | old | -22824 |
| DeferredRemove_Velocity | 10000 | 2.282 | 3.284 | 228.2 | 328.4 | new | 490112 |
| DeferredRemove_Velocity | 50000 | 6.440 | 7.178 | 128.8 | 143.6 | new | 1469699 |
| DirtyDrain_Position | 1000 | 0.289 | 0.238 | 289.3 | 238.2 | old | -45480 |
| DirtyDrain_Position | 10000 | 0.815 | 2.418 | 81.5 | 241.8 | new | 49088 |
| DirtyDrain_Position | 50000 | 3.901 | 3.981 | 78.0 | 79.6 | new | -226176 |
| HotLoop_DeferredAdd_Velocity | 50000 | 2.846 | 3.702 | 56.9 | 74.0 | new | 305232 |
| HotLoop_DirtyDrain_Position | 50000 | 0.888 | 0.855 | 17.8 | 17.1 | old | -1344 |
| HotLoop_Query1_Read_Position | 50000 | 0.239 | 0.169 | 4.8 | 3.4 | old | -1344 |
| HotLoop_SetComponent_Position | 50000 | 0.742 | 0.748 | 14.8 | 15.0 | new | -1344 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 80.997 | 189.556 | 10.1 | 23.7 | new | -837872 |
| HotLoop_TickParallel_QuerySystems | 10000 | 79.729 | 41.806 | 10.0 | 5.2 | old | -25344 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 47.070 | 48.591 | 5.9 | 6.1 | new | 256 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 37.064 | 71.272 | 4.6 | 8.9 | new | -39744 |
| HotLoop_TickSerial_QuerySystems | 10000 | 36.765 | 17.781 | 4.6 | 2.2 | old | -26944 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 17.944 | 24.283 | 2.2 | 3.0 | new | -1344 |
| HotLoop_TryGetComponent_Position | 50000 | 0.231 | 0.253 | 4.6 | 5.1 | new | -1344 |
| MigrationCycle_AddRemoveVelocity | 1000 | 1.904 | 2.158 | 951.8 | 1079.2 | new | -8280 |
| MigrationCycle_AddRemoveVelocity | 10000 | 1.636 | 3.700 | 81.8 | 185.0 | new | 368040 |
| MigrationCycle_AddRemoveVelocity | 50000 | 5.405 | 7.400 | 54.1 | 74.0 | new | 1127432 |
| PersistenceView_Filtered_Position | 1000 | 0.401 | 0.324 | 401.1 | 323.9 | old | -35240 |
| PersistenceView_Filtered_Position | 10000 | 1.604 | 4.779 | 160.4 | 477.9 | new | 309816 |
| PersistenceView_Filtered_Position | 50000 | 5.443 | 5.705 | 108.9 | 114.1 | new | 695712 |
| Query1_Read_Position | 1000 | 0.229 | 0.196 | 228.6 | 196.2 | old | -44536 |
| Query1_Read_Position | 10000 | 2.174 | 1.623 | 217.4 | 162.3 | old | 49024 |
| Query1_Read_Position | 50000 | 4.433 | 4.531 | 88.7 | 90.6 | new | -226067 |
| Query2_Read_PositionVelocity | 1000 | 0.330 | 0.255 | 329.9 | 255.0 | old | -36560 |
| Query2_Read_PositionVelocity | 10000 | 3.547 | 2.593 | 354.7 | 259.3 | old | 229512 |
| Query2_Read_PositionVelocity | 50000 | 6.764 | 6.283 | 135.3 | 125.7 | old | 422245 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.437 | 0.314 | 437.2 | 313.9 | old | -32520 |
| Query3_Read_PositionVelocityHealth | 10000 | 4.584 | 3.461 | 458.4 | 346.1 | old | 320528 |
| Query3_Read_PositionVelocityHealth | 50000 | 9.288 | 8.216 | 185.8 | 164.3 | old | 746534 |
| SetComponent_Position | 1000 | 0.324 | 1.450 | 323.7 | 1450.3 | new | -44472 |
| SetComponent_Position | 10000 | 2.748 | 2.261 | 274.8 | 226.1 | old | 49088 |
| SetComponent_Position | 50000 | 5.561 | 13.128 | 111.2 | 262.6 | new | -226036 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 83.632 | 192.295 | 10.5 | 24.0 | new | -2209992 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 84.566 | 44.516 | 10.6 | 5.6 | old | 4201955 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 47.345 | 54.655 | 5.9 | 6.8 | new | 226178 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 38.506 | 78.148 | 4.8 | 9.8 | new | 193760 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 49.576 | 30.460 | 6.2 | 3.8 | old | 206560 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 28.802 | 33.083 | 3.6 | 4.1 | new | 231976 |
| TryGetComponent_Position | 1000 | 0.358 | 0.370 | 357.9 | 370.4 | new | -44472 |
| TryGetComponent_Position | 10000 | 2.253 | 1.831 | 225.3 | 183.1 | old | 49088 |
| TryGetComponent_Position | 50000 | 5.009 | 4.824 | 100.2 | 96.5 | old | -225998 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3536`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.148 ms`, alloc `104992`
- `PatchComponent_Position` scale `1000` mean `0.296 ms`, alloc `158080`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.381 ms`, alloc `2563264`
- `PatchComponent_Position` scale `10000` mean `0.878 ms`, alloc `3743584`
- `CreateDefaultImmediate_Position` scale `50000` mean `1.669 ms`, alloc `10428568`
- `PatchComponent_Position` scale `50000` mean `4.288 ms`, alloc `15394616`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.008 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.190 ms`, alloc `203560`
- `GetComponentRef_Write_Position` scale `10000` mean `2.877 ms`, alloc `3694496`
- `GetComponentRef_Write_Position` scale `50000` mean `4.442 ms`, alloc `15620782`
