# ECS Benchmark Summary

Generated: 2026-04-20T00:14:02.2216509+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.138 | 0.295 | 138.3 | 295.4 | new | 11704 |
| CreateDefaultDeferred_Position | 10000 | 1.029 | 0.843 | 102.9 | 84.3 | old | 782784 |
| CreateDefaultDeferred_Position | 50000 | 4.338 | 2.669 | 86.8 | 53.4 | old | 3215440 |
| DeferredAdd_Velocity | 1000 | 0.564 | 0.425 | 564.2 | 424.9 | old | -15552 |
| DeferredAdd_Velocity | 10000 | 5.030 | 3.232 | 503.0 | 323.2 | old | 1124800 |
| DeferredAdd_Velocity | 50000 | 12.935 | 8.091 | 258.7 | 161.8 | old | 3895738 |
| DeferredDestroy | 1000 | 0.295 | 0.213 | 294.9 | 212.9 | old | -20760 |
| DeferredDestroy | 10000 | 1.492 | 1.290 | 149.2 | 129.0 | old | 463664 |
| DeferredDestroy | 50000 | 7.184 | 4.601 | 143.7 | 92.0 | old | 1614324 |
| DeferredRemove_Velocity | 1000 | 0.581 | 0.427 | 580.8 | 427.1 | old | -23704 |
| DeferredRemove_Velocity | 10000 | 5.503 | 3.216 | 550.3 | 321.6 | old | 994008 |
| DeferredRemove_Velocity | 50000 | 11.191 | 8.061 | 223.8 | 161.2 | old | 3372536 |
| DirtyDrain_Position | 1000 | 0.294 | 0.246 | 294.5 | 246.3 | old | -54344 |
| DirtyDrain_Position | 10000 | 1.247 | 0.831 | 124.7 | 83.1 | old | 40224 |
| DirtyDrain_Position | 50000 | 6.106 | 5.144 | 122.1 | 102.9 | old | -235592 |
| HotLoop_DeferredAdd_Velocity | 50000 | 5.135 | 3.608 | 102.7 | 72.2 | old | 4130192 |
| HotLoop_DirtyDrain_Position | 50000 | 0.877 | 0.860 | 17.5 | 17.2 | old | -2016 |
| HotLoop_Query1_Read_Position | 50000 | 0.240 | 0.198 | 4.8 | 4.0 | old | -2016 |
| HotLoop_SetComponent_Position | 50000 | 0.706 | 0.737 | 14.1 | 14.7 | new | -2016 |
| HotLoop_TickParallel_QuerySystems | 10000 | 87.032 | 43.443 | 10.9 | 5.4 | old | 773712 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 47.294 | 44.777 | 5.9 | 5.6 | old | -416 |
| HotLoop_TickSerial_QuerySystems | 10000 | 37.657 | 17.784 | 4.7 | 2.2 | old | -27616 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 17.478 | 24.468 | 2.2 | 3.1 | new | -2016 |
| HotLoop_TryGetComponent_Position | 50000 | 0.255 | 0.560 | 5.1 | 11.2 | new | -2016 |
| PersistenceView_Filtered_Position | 1000 | 0.452 | 0.318 | 452.4 | 317.9 | old | -44104 |
| PersistenceView_Filtered_Position | 10000 | 2.133 | 2.021 | 213.3 | 202.1 | old | 300280 |
| PersistenceView_Filtered_Position | 50000 | 8.185 | 7.231 | 163.7 | 144.6 | old | 686215 |
| Query1_Read_Position | 1000 | 0.229 | 0.198 | 228.9 | 197.7 | old | -53000 |
| Query1_Read_Position | 10000 | 2.400 | 1.609 | 240.0 | 160.9 | old | 40224 |
| Query1_Read_Position | 50000 | 4.957 | 4.331 | 99.1 | 86.6 | old | -235544 |
| Query2_Read_PositionVelocity | 1000 | 0.344 | 0.258 | 343.6 | 258.4 | old | -45032 |
| Query2_Read_PositionVelocity | 10000 | 3.582 | 2.740 | 358.2 | 274.0 | old | 220032 |
| Query2_Read_PositionVelocity | 50000 | 7.017 | 7.447 | 140.3 | 148.9 | new | 412712 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.456 | 0.316 | 455.7 | 316.0 | old | -41000 |
| Query3_Read_PositionVelocityHealth | 10000 | 4.756 | 3.555 | 475.6 | 355.5 | old | 311040 |
| Query3_Read_PositionVelocityHealth | 50000 | 10.107 | 11.463 | 202.2 | 229.3 | new | 737008 |
| SetComponent_Position | 1000 | 0.341 | 1.450 | 341.0 | 1450.4 | new | -53000 |
| SetComponent_Position | 10000 | 2.860 | 2.293 | 286.0 | 229.3 | old | 40224 |
| SetComponent_Position | 50000 | 8.035 | 5.198 | 160.7 | 104.0 | old | -235534 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 87.635 | 46.042 | 11.0 | 5.8 | old | 6956332 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 50.827 | 46.888 | 6.4 | 5.9 | old | 185484 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 49.930 | 31.016 | 6.2 | 3.9 | old | 197568 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 28.686 | 34.599 | 3.6 | 4.3 | new | 223168 |
| TryGetComponent_Position | 1000 | 0.394 | 0.375 | 394.1 | 374.8 | old | -53000 |
| TryGetComponent_Position | 10000 | 2.314 | 1.900 | 231.4 | 190.0 | old | 40224 |
| TryGetComponent_Position | 50000 | 6.348 | 4.941 | 127.0 | 98.8 | old | -235544 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3504`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.132 ms`, alloc `100304`
- `PatchComponent_Position` scale `1000` mean `0.313 ms`, alloc `149216`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.571 ms`, alloc `2558576`
- `PatchComponent_Position` scale `10000` mean `1.398 ms`, alloc `3734720`
- `CreateDefaultImmediate_Position` scale `50000` mean `2.951 ms`, alloc `10423188`
- `PatchComponent_Position` scale `50000` mean `6.430 ms`, alloc `15385176`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.008 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.189 ms`, alloc `203560`
- `GetComponentRef_Write_Position` scale `10000` mean `0.790 ms`, alloc `3694496`
- `GetComponentRef_Write_Position` scale `50000` mean `4.587 ms`, alloc `15620768`
