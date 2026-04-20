# ECS Benchmark Summary

Generated: 2026-04-20T00:57:17.8101957+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.114 | 0.301 | 114.2 | 300.6 | new | 4576 |
| CreateDefaultDeferred_Position | 10000 | 0.672 | 0.865 | 67.2 | 86.5 | new | -558880 |
| CreateDefaultDeferred_Position | 50000 | 4.643 | 2.637 | 92.9 | 52.7 | old | -1857714 |
| DeferredAdd_Velocity | 1000 | 0.505 | 0.424 | 505.1 | 424.4 | old | -30648 |
| DeferredAdd_Velocity | 10000 | 3.882 | 3.253 | 388.2 | 325.3 | old | 430736 |
| DeferredAdd_Velocity | 50000 | 9.817 | 7.531 | 196.3 | 150.6 | old | 1335797 |
| DeferredDestroy | 1000 | 0.284 | 0.215 | 284.1 | 214.7 | old | -20360 |
| DeferredDestroy | 10000 | 1.502 | 1.396 | 150.2 | 139.6 | old | 464064 |
| DeferredDestroy | 50000 | 6.043 | 4.568 | 120.9 | 91.4 | old | 1615069 |
| DeferredRemove_Velocity | 1000 | 0.761 | 0.421 | 760.8 | 421.3 | old | -30832 |
| DeferredRemove_Velocity | 10000 | 4.677 | 3.143 | 467.7 | 314.3 | old | 481768 |
| DeferredRemove_Velocity | 50000 | 9.424 | 7.836 | 188.5 | 156.7 | old | 1460904 |
| DirtyDrain_Position | 1000 | 0.297 | 0.241 | 296.8 | 241.2 | old | -53944 |
| DirtyDrain_Position | 10000 | 1.116 | 0.859 | 111.6 | 85.9 | old | 40624 |
| DirtyDrain_Position | 50000 | 5.372 | 5.438 | 107.4 | 108.8 | new | -234856 |
| HotLoop_DeferredAdd_Velocity | 50000 | 4.626 | 3.642 | 92.5 | 72.8 | old | 1569856 |
| HotLoop_DirtyDrain_Position | 50000 | 0.854 | 0.853 | 17.1 | 17.1 | old | -1680 |
| HotLoop_Query1_Read_Position | 50000 | 0.262 | 0.203 | 5.2 | 4.1 | old | -1680 |
| HotLoop_SetComponent_Position | 50000 | 0.702 | 0.756 | 14.0 | 15.1 | new | -1680 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 83.133 | 194.323 | 10.4 | 24.3 | new | -38480 |
| HotLoop_TickParallel_QuerySystems | 10000 | 82.550 | 43.691 | 10.3 | 5.5 | old | 374184 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 48.077 | 46.937 | 6.0 | 5.9 | old | -80 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 38.130 | 72.919 | 4.8 | 9.1 | new | -40080 |
| HotLoop_TickSerial_QuerySystems | 10000 | 36.808 | 17.660 | 4.6 | 2.2 | old | -27280 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 17.324 | 22.543 | 2.2 | 2.8 | new | -1680 |
| HotLoop_TryGetComponent_Position | 50000 | 0.228 | 0.576 | 4.6 | 11.5 | new | -1680 |
| PersistenceView_Filtered_Position | 1000 | 0.461 | 0.315 | 461.4 | 314.5 | old | -43704 |
| PersistenceView_Filtered_Position | 10000 | 2.085 | 1.992 | 208.5 | 199.2 | old | 301016 |
| PersistenceView_Filtered_Position | 50000 | 8.051 | 8.469 | 161.0 | 169.4 | new | 686941 |
| Query1_Read_Position | 1000 | 0.221 | 0.202 | 220.6 | 202.4 | old | -52600 |
| Query1_Read_Position | 10000 | 2.382 | 1.675 | 238.2 | 167.5 | old | 40624 |
| Query1_Read_Position | 50000 | 4.464 | 4.350 | 89.3 | 87.0 | old | -234813 |
| Query2_Read_PositionVelocity | 1000 | 0.321 | 0.278 | 320.6 | 278.3 | old | -44632 |
| Query2_Read_PositionVelocity | 10000 | 3.705 | 2.730 | 370.5 | 273.0 | old | 220768 |
| Query2_Read_PositionVelocity | 50000 | 6.261 | 7.294 | 125.2 | 145.9 | new | 413448 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.418 | 0.323 | 418.5 | 323.4 | old | -40600 |
| Query3_Read_PositionVelocityHealth | 10000 | 4.506 | 3.920 | 450.6 | 392.0 | old | 311776 |
| Query3_Read_PositionVelocityHealth | 50000 | 9.366 | 11.690 | 187.3 | 233.8 | new | 737744 |
| SetComponent_Position | 1000 | 0.320 | 1.485 | 319.6 | 1485.4 | new | -52600 |
| SetComponent_Position | 10000 | 2.795 | 2.440 | 279.5 | 244.0 | old | 40624 |
| SetComponent_Position | 50000 | 5.392 | 5.340 | 107.8 | 106.8 | old | -234808 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 87.978 | 197.178 | 11.0 | 24.6 | new | -1853144 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 88.762 | 46.254 | 11.1 | 5.8 | old | 4957420 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 50.583 | 51.310 | 6.3 | 6.4 | new | 183491 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 37.743 | 76.767 | 4.7 | 9.6 | new | 185168 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 44.929 | 29.256 | 5.6 | 3.7 | old | 197968 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 28.374 | 36.229 | 3.5 | 4.5 | new | 223568 |
| TryGetComponent_Position | 1000 | 0.389 | 0.383 | 389.4 | 383.0 | old | -52600 |
| TryGetComponent_Position | 10000 | 2.499 | 1.875 | 249.9 | 187.5 | old | 40624 |
| TryGetComponent_Position | 50000 | 4.803 | 4.854 | 96.1 | 97.1 | new | -234818 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3504`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.133 ms`, alloc `100704`
- `PatchComponent_Position` scale `1000` mean `0.308 ms`, alloc `149616`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.559 ms`, alloc `2558976`
- `PatchComponent_Position` scale `10000` mean `1.184 ms`, alloc `3735120`
- `CreateDefaultImmediate_Position` scale `50000` mean `2.553 ms`, alloc `10423944`
- `PatchComponent_Position` scale `50000` mean `5.545 ms`, alloc `15385912`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.009 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.202 ms`, alloc `203560`
- `GetComponentRef_Write_Position` scale `10000` mean `0.772 ms`, alloc `3694496`
- `GetComponentRef_Write_Position` scale `50000` mean `4.650 ms`, alloc `15620768`
