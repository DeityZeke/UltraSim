# ECS Benchmark Summary

Generated: 2026-04-21T21:35:10.6652575+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.072 | 0.333 | 72.3 | 332.7 | new | -31416 |
| CreateDefaultDeferred_Position | 10000 | 0.226 | 0.926 | 22.6 | 92.6 | new | -1454960 |
| CreateDefaultDeferred_Position | 50000 | 1.718 | 7.917 | 34.4 | 158.3 | new | -5508048 |
| CreateDestroyReuseChurn_Position | 1000 | 0.717 | 0.698 | 358.5 | 349.0 | old | -25375 |
| CreateDestroyReuseChurn_Position | 10000 | 1.225 | 3.041 | 63.0 | 156.3 | new | -595776 |
| CreateDestroyReuseChurn_Position | 50000 | 2.269 | 4.174 | 22.8 | 42.0 | new | -2483136 |
| DeferredAdd_Velocity | 1000 | 0.473 | 0.463 | 472.6 | 463.0 | old | -97080 |
| DeferredAdd_Velocity | 10000 | 2.015 | 4.302 | 201.5 | 430.2 | new | -2801352 |
| DeferredAdd_Velocity | 50000 | 7.381 | 12.736 | 147.6 | 254.7 | new | -11230016 |
| DeferredDestroy_SparseVictims | 1000 | 0.211 | 0.278 | 422.4 | 556.1 | new | -74040 |
| DeferredDestroy_SparseVictims | 10000 | 1.797 | 2.504 | 359.5 | 500.7 | new | -1860616 |
| DeferredDestroy_SparseVictims | 50000 | 4.327 | 8.075 | 173.1 | 323.0 | new | -7490536 |
| DeferredDestroy | 1000 | 0.173 | 0.230 | 173.1 | 229.6 | new | -76304 |
| DeferredDestroy | 10000 | 1.385 | 2.202 | 138.5 | 220.2 | new | -1944480 |
| DeferredDestroy | 50000 | 4.124 | 7.860 | 82.5 | 157.2 | new | -7745920 |
| DeferredRemove_Velocity | 1000 | 0.346 | 0.435 | 346.2 | 434.7 | new | -91592 |
| DeferredRemove_Velocity | 10000 | 2.401 | 4.264 | 240.1 | 426.4 | new | -2226104 |
| DeferredRemove_Velocity | 50000 | 6.806 | 13.072 | 136.1 | 261.4 | new | -8935352 |
| DirtyDrain_Position | 1000 | 0.267 | 0.280 | 266.7 | 279.8 | new | -75516 |
| DirtyDrain_Position | 10000 | 1.105 | 3.105 | 110.5 | 310.5 | new | -1842336 |
| DirtyDrain_Position | 50000 | 4.926 | 8.518 | 98.5 | 170.4 | new | -7497368 |
| HotLoop_DeferredAdd_Velocity | 50000 | 2.705 | 4.357 | 54.1 | 87.1 | new | -4133992 |
| HotLoop_DirtyDrain_Position | 50000 | 0.910 | 0.963 | 18.2 | 19.3 | new | -1344 |
| HotLoop_Query1_Read_Position | 50000 | 0.125 | 0.188 | 2.5 | 3.8 | new | -1432 |
| HotLoop_SetComponent_Position | 50000 | 0.758 | 0.856 | 15.2 | 17.1 | new | -1344 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 190.527 | 220.399 | 23.8 | 27.5 | new | -1146738 |
| HotLoop_TickParallel_OscillatingMovement | 10000 | 154.234 | 442.022 | 19.3 | 55.3 | new | 101056 |
| HotLoop_TickParallel_OscillatingMovement | 100000 | 1049.495 | 4268.615 | 13.1 | 53.4 | new | 101056 |
| HotLoop_TickParallel_QuerySystems | 10000 | 29.106 | 45.500 | 3.6 | 5.7 | new | 27456 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 3.989 | 62.428 | 0.5 | 7.8 | new | 65856 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 96.368 | 82.201 | 12.0 | 10.3 | old | -1344 |
| HotLoop_TickSerial_OscillatingMovement | 10000 | 190.999 | 344.580 | 23.9 | 43.1 | new | 72256 |
| HotLoop_TickSerial_OscillatingMovement | 100000 | 1509.082 | 3577.854 | 18.9 | 44.7 | new | 72256 |
| HotLoop_TickSerial_QuerySystems | 10000 | 12.003 | 19.372 | 1.5 | 2.4 | new | -26886 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 6.320 | 24.081 | 0.8 | 3.0 | new | -1344 |
| HotLoop_TryGetComponent_Position | 50000 | 0.239 | 0.720 | 4.8 | 14.4 | new | -1344 |
| MigrationCycle_AddRemoveVelocity | 1000 | 2.105 | 2.380 | 1052.4 | 1190.2 | new | -70392 |
| MigrationCycle_AddRemoveVelocity | 10000 | 2.062 | 5.841 | 103.1 | 292.0 | new | -2800808 |
| MigrationCycle_AddRemoveVelocity | 50000 | 7.007 | 13.079 | 70.1 | 130.8 | new | -10807414 |
| PersistenceView_Filtered_Position | 1000 | 0.284 | 0.327 | 283.6 | 326.5 | new | -76656 |
| PersistenceView_Filtered_Position | 10000 | 1.699 | 4.704 | 169.9 | 470.4 | new | -2201608 |
| PersistenceView_Filtered_Position | 50000 | 7.042 | 12.473 | 140.8 | 249.5 | new | -8827440 |
| Query1_Read_Position | 1000 | 0.181 | 0.250 | 180.8 | 249.9 | new | -58896 |
| Query1_Read_Position | 10000 | 1.506 | 1.971 | 150.6 | 197.1 | new | -1682320 |
| Query1_Read_Position | 50000 | 4.879 | 7.453 | 97.6 | 149.1 | new | -6695360 |
| Query2_Read_PositionVelocity | 1000 | 0.261 | 0.269 | 260.6 | 268.9 | new | -67080 |
| Query2_Read_PositionVelocity | 10000 | 2.113 | 3.376 | 211.3 | 337.6 | new | -1938280 |
| Query2_Read_PositionVelocity | 50000 | 7.048 | 10.921 | 141.0 | 218.4 | new | -7737435 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.334 | 0.331 | 334.5 | 331.3 | old | -75264 |
| Query3_Read_PositionVelocityHealth | 10000 | 2.927 | 4.320 | 292.7 | 432.0 | new | -2192224 |
| Query3_Read_PositionVelocityHealth | 50000 | 10.144 | 11.200 | 202.9 | 224.0 | new | -8778056 |
| SetComponent_Position | 1000 | 0.870 | 1.651 | 870.0 | 1651.1 | new | -66912 |
| SetComponent_Position | 10000 | 1.741 | 2.641 | 174.1 | 264.1 | new | -1762336 |
| SetComponent_Position | 50000 | 5.368 | 11.412 | 107.4 | 228.2 | new | -7097368 |
| TickParallel_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 525.335 | 2345.339 | 6.6 | 29.3 | new | -10205775 |
| TickParallel_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 305.192 | 589.010 | 5.1 | 9.8 | new | 13610956 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 188.821 | 219.829 | 23.6 | 27.5 | new | -15081700 |
| TickParallel_OscillatingMovement_100k_200Ticks | 100000 | 901.494 | 3988.162 | 11.3 | 49.9 | new | -13962448 |
| TickParallel_OscillatingMovement_10k_200Ticks | 10000 | 137.288 | 401.196 | 17.2 | 50.1 | new | -2091989 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 35.404 | 49.448 | 4.4 | 6.2 | new | -1575080 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 8.608 | 59.093 | 1.1 | 7.4 | new | -2290425 |
| TickParallel_V1PulseMovement_100k_50Ticks | 100000 | 110.121 | 224.912 | 7.3 | 15.0 | new | 15212000 |
| TickSerial_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 804.793 | 2329.280 | 10.1 | 29.1 | new | -17303615 |
| TickSerial_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 197.836 | 748.130 | 3.3 | 12.5 | new | 6409134 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 93.929 | 90.384 | 11.7 | 11.3 | old | -1933376 |
| TickSerial_OscillatingMovement_100k_200Ticks | 100000 | 1246.901 | 3139.470 | 15.6 | 39.2 | new | -13379552 |
| TickSerial_OscillatingMovement_10k_200Ticks | 10000 | 168.680 | 306.603 | 21.1 | 38.3 | new | -1579128 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 20.228 | 41.130 | 2.5 | 5.1 | new | -1436328 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 10.892 | 37.331 | 1.4 | 4.7 | new | -1933456 |
| TickSerial_V1PulseMovement_100k_50Ticks | 100000 | 56.738 | 184.132 | 3.8 | 12.3 | new | 719512 |
| TryGetComponent_Position | 1000 | 0.253 | 0.403 | 253.4 | 403.1 | new | -66912 |
| TryGetComponent_Position | 10000 | 1.479 | 2.395 | 147.9 | 239.5 | new | -1761932 |
| TryGetComponent_Position | 50000 | 4.951 | 9.877 | 99.0 | 197.5 | new | -7097022 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.010 ms`, alloc `3536`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.142 ms`, alloc `91912`
- `PatchComponent_Position` scale `1000` mean `0.326 ms`, alloc `153712`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.748 ms`, alloc `2058664`
- `PatchComponent_Position` scale `10000` mean `1.030 ms`, alloc `2196088`
- `CreateDefaultImmediate_Position` scale `50000` mean `3.434 ms`, alloc `8351776`
- `PatchComponent_Position` scale `50000` mean `4.686 ms`, alloc `9575848`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.009 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.210 ms`, alloc `221632`
- `GetComponentRef_Write_Position` scale `10000` mean `2.355 ms`, alloc `3958424`
- `GetComponentRef_Write_Position` scale `50000` mean `9.532 ms`, alloc `16673072`
