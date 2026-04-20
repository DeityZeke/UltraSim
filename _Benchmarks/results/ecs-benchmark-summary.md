# ECS Benchmark Summary

Generated: 2026-04-20T02:58:26.3939408+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.086 | 0.297 | 86.1 | 296.9 | new | 1272 |
| CreateDefaultDeferred_Position | 10000 | 0.313 | 0.861 | 31.3 | 86.1 | new | -633848 |
| CreateDefaultDeferred_Position | 50000 | 1.499 | 5.807 | 30.0 | 116.1 | new | -2252288 |
| CreateDestroyReuseChurn_Position | 1000 | 0.681 | 0.653 | 340.3 | 326.7 | old | -800 |
| CreateDestroyReuseChurn_Position | 10000 | 1.685 | 1.876 | 86.6 | 96.4 | new | 27616 |
| CreateDestroyReuseChurn_Position | 50000 | 2.586 | 3.191 | 26.0 | 32.1 | new | 32608 |
| DeferredAdd_Velocity | 1000 | 0.409 | 0.428 | 409.2 | 428.0 | new | -21432 |
| DeferredAdd_Velocity | 10000 | 3.623 | 3.427 | 362.3 | 342.7 | old | 108696 |
| DeferredAdd_Velocity | 50000 | 7.016 | 8.698 | 140.3 | 174.0 | new | 80740 |
| DeferredDestroy_SparseVictims | 1000 | 0.251 | 0.218 | 502.5 | 435.4 | old | -34928 |
| DeferredDestroy_SparseVictims | 10000 | 2.416 | 1.405 | 483.1 | 281.0 | old | 182616 |
| DeferredDestroy_SparseVictims | 50000 | 3.769 | 4.498 | 150.7 | 179.9 | new | 300616 |
| DeferredDestroy | 1000 | 0.268 | 0.229 | 267.5 | 228.7 | old | -26712 |
| DeferredDestroy | 10000 | 2.950 | 1.023 | 295.0 | 102.3 | old | 313712 |
| DeferredDestroy | 50000 | 4.194 | 5.108 | 83.9 | 102.2 | new | 824923 |
| DeferredRemove_Velocity | 1000 | 0.634 | 0.469 | 634.0 | 468.6 | old | -21616 |
| DeferredRemove_Velocity | 10000 | 4.972 | 3.384 | 497.2 | 338.4 | old | 491320 |
| DeferredRemove_Velocity | 50000 | 7.559 | 10.433 | 151.2 | 208.7 | new | 1470907 |
| DirtyDrain_Position | 1000 | 0.290 | 0.244 | 290.2 | 244.3 | old | -44272 |
| DirtyDrain_Position | 10000 | 4.053 | 1.488 | 405.3 | 148.8 | old | 50296 |
| DirtyDrain_Position | 50000 | 4.729 | 5.455 | 94.6 | 109.1 | new | -224968 |
| HotLoop_DeferredAdd_Velocity | 50000 | 2.819 | 3.895 | 56.4 | 77.9 | new | 305232 |
| HotLoop_DirtyDrain_Position | 50000 | 1.001 | 0.876 | 20.0 | 17.5 | old | -1344 |
| HotLoop_Query1_Read_Position | 50000 | 0.243 | 0.168 | 4.9 | 3.4 | old | -1344 |
| HotLoop_SetComponent_Position | 50000 | 0.871 | 0.826 | 17.4 | 16.5 | old | -1344 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 84.250 | 195.242 | 10.5 | 24.4 | new | -438008 |
| HotLoop_TickParallel_OscillatingMovement | 10000 | 247.074 | 387.838 | 30.9 | 48.5 | new | -6144 |
| HotLoop_TickParallel_OscillatingMovement | 100000 | 2470.300 | 3761.712 | 30.9 | 47.0 | new | -6144 |
| HotLoop_TickParallel_QuerySystems | 10000 | 84.019 | 42.588 | 10.5 | 5.3 | old | -25344 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 47.754 | 56.948 | 6.0 | 7.1 | new | 256 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 38.655 | 76.289 | 4.8 | 9.5 | new | -39744 |
| HotLoop_TickSerial_OscillatingMovement | 10000 | 282.999 | 303.488 | 35.4 | 37.9 | new | -7744 |
| HotLoop_TickSerial_OscillatingMovement | 100000 | 2915.020 | 3142.705 | 36.4 | 39.3 | new | -7744 |
| HotLoop_TickSerial_QuerySystems | 10000 | 38.113 | 17.532 | 4.8 | 2.2 | old | -26944 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 18.798 | 26.067 | 2.3 | 3.3 | new | -1344 |
| HotLoop_TryGetComponent_Position | 50000 | 0.219 | 0.568 | 4.4 | 11.4 | new | -1344 |
| MigrationCycle_AddRemoveVelocity | 1000 | 1.920 | 2.165 | 960.0 | 1082.5 | new | -7072 |
| MigrationCycle_AddRemoveVelocity | 10000 | 2.909 | 4.529 | 145.4 | 226.4 | new | 369248 |
| MigrationCycle_AddRemoveVelocity | 50000 | 5.699 | 10.519 | 57.0 | 105.2 | new | 1128644 |
| PersistenceView_Filtered_Position | 1000 | 0.399 | 0.333 | 399.2 | 333.2 | old | -32472 |
| PersistenceView_Filtered_Position | 10000 | 7.084 | 2.474 | 708.4 | 247.4 | old | 312584 |
| PersistenceView_Filtered_Position | 50000 | 6.171 | 8.536 | 123.4 | 170.7 | new | 698480 |
| Query1_Read_Position | 1000 | 0.222 | 0.225 | 221.6 | 225.3 | new | -43328 |
| Query1_Read_Position | 10000 | 2.156 | 1.669 | 215.6 | 166.9 | old | 50232 |
| Query1_Read_Position | 50000 | 4.486 | 4.677 | 89.7 | 93.5 | new | -224864 |
| Query2_Read_PositionVelocity | 1000 | 0.322 | 0.268 | 321.5 | 267.9 | old | -35352 |
| Query2_Read_PositionVelocity | 10000 | 3.483 | 2.717 | 348.3 | 271.7 | old | 230720 |
| Query2_Read_PositionVelocity | 50000 | 6.556 | 6.375 | 131.1 | 127.5 | old | 423443 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.425 | 0.346 | 424.9 | 346.1 | old | -31312 |
| Query3_Read_PositionVelocityHealth | 10000 | 4.559 | 3.900 | 455.9 | 390.0 | old | 321736 |
| Query3_Read_PositionVelocityHealth | 50000 | 9.473 | 9.128 | 189.5 | 182.6 | old | 747752 |
| SetComponent_Position | 1000 | 0.324 | 1.515 | 323.7 | 1514.9 | new | -43264 |
| SetComponent_Position | 10000 | 2.739 | 2.381 | 273.9 | 238.1 | old | 50296 |
| SetComponent_Position | 50000 | 7.714 | 8.367 | 154.3 | 167.3 | new | -224805 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 86.868 | 202.062 | 10.9 | 25.3 | new | -3008512 |
| TickParallel_OscillatingMovement_100k_200Ticks | 100000 | 2237.336 | 3612.228 | 28.0 | 45.2 | new | 3478680 |
| TickParallel_OscillatingMovement_10k_200Ticks | 10000 | 222.899 | 366.455 | 27.9 | 45.8 | new | 585848 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 88.052 | 46.815 | 11.0 | 5.9 | old | 4203171 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 50.719 | 59.951 | 6.3 | 7.5 | new | 228733 |
| TickParallel_V1PulseMovement_100k_50Ticks | 100000 | 188.881 | 236.817 | 12.6 | 15.8 | new | 62560040 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 40.001 | 90.116 | 5.0 | 11.3 | new | 194968 |
| TickSerial_OscillatingMovement_100k_200Ticks | 100000 | 2480.786 | 2767.409 | 31.0 | 34.6 | new | 3483272 |
| TickSerial_OscillatingMovement_10k_200Ticks | 10000 | 247.176 | 268.426 | 30.9 | 33.6 | new | 590440 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 52.367 | 28.752 | 6.5 | 3.6 | old | 207768 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 29.684 | 38.573 | 3.7 | 4.8 | new | 233184 |
| TickSerial_V1PulseMovement_100k_50Ticks | 100000 | 145.129 | 169.108 | 9.7 | 11.3 | new | 29206488 |
| TryGetComponent_Position | 1000 | 0.354 | 0.386 | 354.3 | 386.4 | new | -43264 |
| TryGetComponent_Position | 10000 | 2.450 | 2.023 | 245.0 | 202.3 | old | 50296 |
| TryGetComponent_Position | 50000 | 4.676 | 8.115 | 93.5 | 162.3 | new | -224800 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3536`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.142 ms`, alloc `108280`
- `PatchComponent_Position` scale `1000` mean `0.294 ms`, alloc `161368`
- `CreateDefaultImmediate_Position` scale `10000` mean `1.103 ms`, alloc `2566552`
- `PatchComponent_Position` scale `10000` mean `3.629 ms`, alloc `3746872`
- `CreateDefaultImmediate_Position` scale `50000` mean `1.786 ms`, alloc `10431856`
- `PatchComponent_Position` scale `50000` mean `4.310 ms`, alloc `15397904`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.009 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.195 ms`, alloc `205640`
- `GetComponentRef_Write_Position` scale `10000` mean `1.763 ms`, alloc `3696576`
- `GetComponentRef_Write_Position` scale `50000` mean `4.772 ms`, alloc `15622862`
