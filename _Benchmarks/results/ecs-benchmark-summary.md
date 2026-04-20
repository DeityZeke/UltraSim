# ECS Benchmark Summary

Generated: 2026-04-20T00:04:10.7995349+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.170 | 0.295 | 170.1 | 295.4 | new | 80824 |
| CreateDefaultDeferred_Position | 10000 | 1.127 | 0.843 | 112.7 | 84.3 | old | 708464 |
| CreateDefaultDeferred_Position | 50000 | 5.976 | 2.669 | 119.5 | 53.4 | old | 3125304 |
| DeferredAdd_Velocity | 1000 | 0.684 | 0.425 | 684.1 | 424.9 | old | 192488 |
| DeferredAdd_Velocity | 10000 | 7.773 | 3.232 | 777.3 | 323.2 | old | 902520 |
| DeferredAdd_Velocity | 50000 | 13.222 | 8.091 | 264.4 | 161.8 | old | 3626130 |
| DeferredDestroy | 1000 | 0.403 | 0.213 | 403.3 | 212.9 | old | 117936 |
| DeferredDestroy | 10000 | 4.324 | 1.290 | 432.4 | 129.0 | old | 315480 |
| DeferredDestroy | 50000 | 7.742 | 4.601 | 154.8 | 92.0 | old | 1434580 |
| DeferredRemove_Velocity | 1000 | 0.759 | 0.427 | 758.7 | 427.1 | old | 184384 |
| DeferredRemove_Velocity | 10000 | 7.790 | 3.216 | 779.0 | 321.6 | old | 771776 |
| DeferredRemove_Velocity | 50000 | 13.300 | 8.061 | 266.0 | 161.2 | old | 3103000 |
| DirtyDrain_Position | 1000 | 0.437 | 0.246 | 437.4 | 246.3 | old | 84400 |
| DirtyDrain_Position | 10000 | 4.395 | 0.831 | 439.5 | 83.1 | old | -107912 |
| DirtyDrain_Position | 50000 | 7.867 | 5.144 | 157.3 | 102.9 | old | -415288 |
| HotLoop_DeferredAdd_Velocity | 50000 | 5.437 | 3.608 | 108.7 | 72.2 | old | 4040328 |
| HotLoop_DirtyDrain_Position | 50000 | 1.414 | 0.860 | 28.3 | 17.2 | old | -1968 |
| HotLoop_Query1_Read_Position | 50000 | 0.254 | 0.198 | 5.1 | 4.0 | old | -2016 |
| HotLoop_SetComponent_Position | 50000 | 1.152 | 0.737 | 23.0 | 14.7 | old | -2016 |
| HotLoop_TickParallel_QuerySystems | 10000 | 134.381 | 43.443 | 16.8 | 5.4 | old | 12384 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 70.080 | 44.777 | 8.8 | 5.6 | old | -416 |
| HotLoop_TickSerial_QuerySystems | 10000 | 117.014 | 17.784 | 14.6 | 2.2 | old | 10784 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 24.556 | 24.468 | 3.1 | 3.1 | old | -2016 |
| HotLoop_TryGetComponent_Position | 50000 | 0.229 | 0.560 | 4.6 | 11.2 | new | -2016 |
| PersistenceView_Filtered_Position | 1000 | 0.638 | 0.318 | 638.2 | 317.9 | old | 233376 |
| PersistenceView_Filtered_Position | 10000 | 7.549 | 2.021 | 754.9 | 202.1 | old | 4000 |
| PersistenceView_Filtered_Position | 50000 | 11.056 | 7.231 | 221.1 | 144.6 | old | 326863 |
| Query1_Read_Position | 1000 | 0.336 | 0.198 | 335.8 | 197.7 | old | 85696 |
| Query1_Read_Position | 10000 | 2.863 | 1.609 | 286.3 | 160.9 | old | -107960 |
| Query1_Read_Position | 50000 | 8.895 | 4.331 | 177.9 | 86.6 | old | -415288 |
| Query2_Read_PositionVelocity | 1000 | 0.506 | 0.258 | 506.0 | 258.4 | old | 163056 |
| Query2_Read_PositionVelocity | 10000 | 4.427 | 2.740 | 442.7 | 274.0 | old | -2200 |
| Query2_Read_PositionVelocity | 50000 | 9.383 | 7.447 | 187.7 | 148.9 | old | 143176 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.667 | 0.316 | 667.2 | 316.0 | old | 236480 |
| Query3_Read_PositionVelocityHealth | 10000 | 6.134 | 3.555 | 613.4 | 355.5 | old | 14760 |
| Query3_Read_PositionVelocityHealth | 50000 | 10.908 | 11.463 | 218.2 | 229.3 | new | 377656 |
| SetComponent_Position | 1000 | 0.500 | 1.450 | 500.0 | 1450.4 | new | 85696 |
| SetComponent_Position | 10000 | 3.536 | 2.293 | 353.6 | 229.3 | old | -107960 |
| SetComponent_Position | 50000 | 10.934 | 5.198 | 218.7 | 104.0 | old | -415278 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 139.157 | 46.042 | 17.4 | 5.8 | old | -556028 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 70.695 | 46.888 | 8.8 | 5.9 | old | -568750 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 128.100 | 31.016 | 16.0 | 3.9 | old | 13736 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 42.094 | 34.599 | 5.3 | 4.3 | old | 936 |
| TryGetComponent_Position | 1000 | 0.503 | 0.375 | 503.4 | 374.8 | old | 85696 |
| TryGetComponent_Position | 10000 | 2.930 | 1.900 | 293.0 | 190.0 | old | -107960 |
| TryGetComponent_Position | 50000 | 9.270 | 4.941 | 185.4 | 98.8 | old | -415288 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3504`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.165 ms`, alloc `169424`
- `PatchComponent_Position` scale `1000` mean `0.417 ms`, alloc `287912`
- `CreateDefaultImmediate_Position` scale `10000` mean `1.037 ms`, alloc `2484256`
- `PatchComponent_Position` scale `10000` mean `3.102 ms`, alloc `3586536`
- `CreateDefaultImmediate_Position` scale `50000` mean `3.411 ms`, alloc `10333120`
- `PatchComponent_Position` scale `50000` mean `7.600 ms`, alloc `15205432`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.008 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.189 ms`, alloc `203560`
- `GetComponentRef_Write_Position` scale `10000` mean `0.790 ms`, alloc `3694496`
- `GetComponentRef_Write_Position` scale `50000` mean `4.587 ms`, alloc `15620768`
