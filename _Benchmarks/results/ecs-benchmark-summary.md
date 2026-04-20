# ECS Benchmark Summary

Generated: 2026-04-20T01:14:37.7613770+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.134 | 0.301 | 134.3 | 300.6 | new | 8688 |
| CreateDefaultDeferred_Position | 10000 | 0.643 | 0.865 | 64.3 | 86.5 | new | -554432 |
| CreateDefaultDeferred_Position | 50000 | 4.227 | 2.637 | 84.5 | 52.7 | old | -1852920 |
| DeferredAdd_Velocity | 1000 | 0.394 | 0.424 | 393.8 | 424.4 | new | -22464 |
| DeferredAdd_Velocity | 10000 | 2.988 | 3.253 | 298.8 | 325.3 | new | 107664 |
| DeferredAdd_Velocity | 50000 | 5.837 | 7.531 | 116.7 | 150.6 | new | 80437 |
| DeferredDestroy | 1000 | 0.300 | 0.215 | 300.1 | 214.7 | old | -11736 |
| DeferredDestroy | 10000 | 1.457 | 1.396 | 145.7 | 139.6 | old | 472688 |
| DeferredDestroy | 50000 | 3.692 | 4.568 | 73.8 | 91.4 | new | 1624029 |
| DeferredRemove_Velocity | 1000 | 0.627 | 0.421 | 627.1 | 421.3 | old | -22648 |
| DeferredRemove_Velocity | 10000 | 3.659 | 3.143 | 365.9 | 314.3 | old | 490288 |
| DeferredRemove_Velocity | 50000 | 5.970 | 7.836 | 119.4 | 156.7 | new | 1469352 |
| DirtyDrain_Position | 1000 | 0.276 | 0.241 | 275.6 | 241.2 | old | -45320 |
| DirtyDrain_Position | 10000 | 1.261 | 0.859 | 126.1 | 85.9 | old | 49248 |
| DirtyDrain_Position | 50000 | 4.369 | 5.438 | 87.4 | 108.8 | new | -225896 |
| HotLoop_DeferredAdd_Velocity | 50000 | 2.759 | 3.642 | 55.2 | 72.8 | new | 305248 |
| HotLoop_DirtyDrain_Position | 50000 | 0.977 | 0.853 | 19.5 | 17.1 | old | -1344 |
| HotLoop_Query1_Read_Position | 50000 | 0.229 | 0.203 | 4.6 | 4.1 | old | -1344 |
| HotLoop_SetComponent_Position | 50000 | 0.857 | 0.756 | 17.1 | 15.1 | old | -1344 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 93.924 | 194.323 | 11.7 | 24.3 | new | -38144 |
| HotLoop_TickParallel_QuerySystems | 10000 | 85.444 | 43.691 | 10.7 | 5.5 | old | 374520 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 47.178 | 46.937 | 5.9 | 5.9 | old | 256 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 38.205 | 72.919 | 4.8 | 9.1 | new | -39744 |
| HotLoop_TickSerial_QuerySystems | 10000 | 36.614 | 17.660 | 4.6 | 2.2 | old | -26944 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 17.779 | 22.543 | 2.2 | 2.8 | new | -1344 |
| HotLoop_TryGetComponent_Position | 50000 | 0.220 | 0.576 | 4.4 | 11.5 | new | -1344 |
| PersistenceView_Filtered_Position | 1000 | 0.401 | 0.315 | 401.5 | 314.5 | old | -35080 |
| PersistenceView_Filtered_Position | 10000 | 2.431 | 1.992 | 243.1 | 199.2 | old | 309976 |
| PersistenceView_Filtered_Position | 50000 | 6.462 | 8.469 | 129.2 | 169.4 | new | 695747 |
| Query1_Read_Position | 1000 | 0.208 | 0.202 | 208.0 | 202.4 | old | -44384 |
| Query1_Read_Position | 10000 | 2.156 | 1.675 | 215.6 | 167.5 | old | 49176 |
| Query1_Read_Position | 50000 | 4.371 | 4.350 | 87.4 | 87.0 | old | -225920 |
| Query2_Read_PositionVelocity | 1000 | 0.312 | 0.278 | 312.3 | 278.3 | old | -36408 |
| Query2_Read_PositionVelocity | 10000 | 3.372 | 2.730 | 337.2 | 273.0 | old | 229664 |
| Query2_Read_PositionVelocity | 50000 | 8.078 | 7.294 | 161.6 | 145.9 | old | 422339 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.414 | 0.323 | 413.9 | 323.4 | old | -32376 |
| Query3_Read_PositionVelocityHealth | 10000 | 4.509 | 3.920 | 450.9 | 392.0 | old | 320672 |
| Query3_Read_PositionVelocityHealth | 50000 | 10.110 | 11.690 | 202.2 | 233.8 | new | 746640 |
| SetComponent_Position | 1000 | 0.309 | 1.485 | 308.5 | 1485.4 | new | -44312 |
| SetComponent_Position | 10000 | 2.668 | 2.440 | 266.8 | 244.0 | old | 49248 |
| SetComponent_Position | 50000 | 5.651 | 5.340 | 113.0 | 106.8 | old | -225838 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 86.499 | 197.178 | 10.8 | 24.6 | new | -1044752 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 85.477 | 46.254 | 10.7 | 5.8 | old | 5765806 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 48.994 | 51.310 | 6.1 | 6.4 | new | 192102 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 38.808 | 76.767 | 4.9 | 9.6 | new | 193800 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 46.852 | 29.256 | 5.9 | 3.7 | old | 206600 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 28.394 | 36.229 | 3.5 | 4.5 | new | 232128 |
| TryGetComponent_Position | 1000 | 0.373 | 0.383 | 372.5 | 383.0 | new | -44312 |
| TryGetComponent_Position | 10000 | 2.144 | 1.875 | 214.4 | 187.5 | old | 49248 |
| TryGetComponent_Position | 50000 | 4.639 | 4.854 | 92.8 | 97.1 | new | -225848 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3504`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.123 ms`, alloc `105152`
- `PatchComponent_Position` scale `1000` mean `0.292 ms`, alloc `158240`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.528 ms`, alloc `2563424`
- `PatchComponent_Position` scale `10000` mean `1.300 ms`, alloc `3743744`
- `CreateDefaultImmediate_Position` scale `50000` mean `1.528 ms`, alloc `10428632`
- `PatchComponent_Position` scale `50000` mean `4.200 ms`, alloc `15394872`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.009 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.202 ms`, alloc `203560`
- `GetComponentRef_Write_Position` scale `10000` mean `0.772 ms`, alloc `3694496`
- `GetComponentRef_Write_Position` scale `50000` mean `4.650 ms`, alloc `15620768`
