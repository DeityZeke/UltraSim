# ECS Benchmark Summary

Generated: 2026-04-20T15:12:17.6459227+00:00
ProcessorCount: 16

| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| CreateDefaultDeferred_Position | 1000 | 0.067 | 0.323 | 66.8 | 322.6 | new | -23656 |
| CreateDefaultDeferred_Position | 10000 | 0.158 | 0.824 | 15.8 | 82.4 | new | -1324224 |
| CreateDefaultDeferred_Position | 50000 | 0.994 | 5.829 | 19.9 | 116.6 | new | -4982032 |
| CreateDestroyReuseChurn_Position | 1000 | 0.570 | 0.630 | 285.2 | 315.0 | new | -23672 |
| CreateDestroyReuseChurn_Position | 10000 | 1.178 | 1.885 | 60.5 | 96.9 | new | -591088 |
| CreateDestroyReuseChurn_Position | 50000 | 2.097 | 2.975 | 21.1 | 29.9 | new | -2478448 |
| DeferredAdd_Velocity | 1000 | 0.327 | 0.421 | 326.7 | 421.4 | new | -70056 |
| DeferredAdd_Velocity | 10000 | 1.769 | 3.290 | 176.9 | 329.0 | new | -2343960 |
| DeferredAdd_Velocity | 50000 | 5.773 | 7.010 | 115.5 | 140.2 | new | -9394632 |
| DeferredDestroy_SparseVictims | 1000 | 0.190 | 0.205 | 379.3 | 410.2 | new | -67304 |
| DeferredDestroy_SparseVictims | 10000 | 1.587 | 1.445 | 317.3 | 289.0 | old | -1761624 |
| DeferredDestroy_SparseVictims | 50000 | 2.350 | 3.320 | 94.0 | 132.8 | new | -7090641 |
| DeferredDestroy | 1000 | 0.163 | 0.213 | 162.9 | 212.5 | new | -67496 |
| DeferredDestroy | 10000 | 1.159 | 0.978 | 115.9 | 97.8 | old | -1812696 |
| DeferredDestroy | 50000 | 2.841 | 3.531 | 56.8 | 70.6 | new | -7214866 |
| DeferredRemove_Velocity | 1000 | 0.329 | 0.416 | 329.1 | 415.5 | new | -78208 |
| DeferredRemove_Velocity | 10000 | 2.050 | 3.270 | 205.0 | 327.0 | new | -1977328 |
| DeferredRemove_Velocity | 50000 | 5.855 | 7.671 | 117.1 | 153.4 | new | -8017512 |
| DirtyDrain_Position | 1000 | 0.219 | 0.235 | 219.4 | 234.6 | new | -68160 |
| DirtyDrain_Position | 10000 | 1.506 | 1.460 | 150.6 | 146.0 | old | -1711600 |
| DirtyDrain_Position | 50000 | 2.657 | 3.948 | 53.1 | 79.0 | new | -6967406 |
| HotLoop_DeferredAdd_Velocity | 50000 | 3.255 | 3.850 | 65.1 | 77.0 | new | -2824200 |
| HotLoop_DirtyDrain_Position | 50000 | 0.808 | 0.862 | 16.2 | 17.2 | new | -1344 |
| HotLoop_Query1_Read_Position | 50000 | 0.246 | 0.167 | 4.9 | 3.3 | old | -1344 |
| HotLoop_SetComponent_Position | 50000 | 0.667 | 0.738 | 13.3 | 14.8 | new | -1344 |
| HotLoop_TickParallel_DirtyAwareWriteSystems | 10000 | 176.356 | 189.473 | 22.0 | 23.7 | new | -748272 |
| HotLoop_TickParallel_OscillatingMovement | 10000 | 182.507 | 378.406 | 22.8 | 47.3 | new | 99456 |
| HotLoop_TickParallel_OscillatingMovement | 100000 | 1026.806 | 3721.844 | 12.8 | 46.5 | new | 99456 |
| HotLoop_TickParallel_QuerySystems | 10000 | 32.761 | 41.595 | 4.1 | 5.2 | new | 25856 |
| HotLoop_TickParallel_ReadOnlySystems | 10000 | 3.061 | 45.356 | 0.4 | 5.7 | new | 64256 |
| HotLoop_TickSerial_DirtyAwareWriteSystems | 10000 | 63.473 | 70.093 | 7.9 | 8.8 | new | -1344 |
| HotLoop_TickSerial_OscillatingMovement | 10000 | 185.320 | 325.957 | 23.2 | 40.7 | new | 72256 |
| HotLoop_TickSerial_OscillatingMovement | 100000 | 1557.539 | 3031.965 | 19.5 | 37.9 | new | 72256 |
| HotLoop_TickSerial_QuerySystems | 10000 | 11.424 | 17.297 | 1.4 | 2.2 | new | -26944 |
| HotLoop_TickSerial_ReadOnlySystems | 10000 | 6.116 | 20.750 | 0.8 | 2.6 | new | -1344 |
| HotLoop_TryGetComponent_Position | 50000 | 0.205 | 0.248 | 4.1 | 5.0 | new | -1344 |
| MigrationCycle_AddRemoveVelocity | 1000 | 1.601 | 2.118 | 800.3 | 1059.2 | new | -39232 |
| MigrationCycle_AddRemoveVelocity | 10000 | 1.759 | 4.530 | 88.0 | 226.5 | new | -2185496 |
| MigrationCycle_AddRemoveVelocity | 50000 | 3.600 | 7.787 | 36.0 | 77.9 | new | -8591764 |
| PersistenceView_Filtered_Position | 1000 | 0.262 | 0.316 | 262.2 | 316.0 | new | -68896 |
| PersistenceView_Filtered_Position | 10000 | 2.489 | 2.308 | 248.9 | 230.8 | old | -2070872 |
| PersistenceView_Filtered_Position | 50000 | 3.457 | 5.977 | 69.1 | 119.5 | new | -8297472 |
| Query1_Read_Position | 1000 | 0.166 | 0.202 | 166.0 | 201.8 | new | -51216 |
| Query1_Read_Position | 10000 | 1.084 | 1.636 | 108.4 | 163.6 | new | -1551664 |
| Query1_Read_Position | 50000 | 3.391 | 5.042 | 67.8 | 100.8 | new | -6171344 |
| Query2_Read_PositionVelocity | 1000 | 0.310 | 0.263 | 310.5 | 262.9 | old | -59408 |
| Query2_Read_PositionVelocity | 10000 | 1.803 | 2.647 | 180.3 | 264.7 | new | -1807632 |
| Query2_Read_PositionVelocity | 50000 | 5.721 | 6.766 | 114.4 | 135.3 | new | -7211737 |
| Query3_Read_PositionVelocityHealth | 1000 | 0.289 | 0.329 | 288.9 | 328.5 | new | -67600 |
| Query3_Read_PositionVelocityHealth | 10000 | 2.491 | 3.436 | 249.1 | 343.6 | new | -2061584 |
| Query3_Read_PositionVelocityHealth | 50000 | 7.247 | 9.086 | 144.9 | 181.7 | new | -8252073 |
| SetComponent_Position | 1000 | 0.695 | 1.449 | 694.9 | 1449.5 | new | -59152 |
| SetComponent_Position | 10000 | 1.486 | 2.438 | 148.6 | 243.8 | new | -1631600 |
| SetComponent_Position | 50000 | 4.213 | 8.564 | 84.3 | 171.3 | new | -6571280 |
| TickParallel_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 458.771 | 2037.134 | 5.7 | 25.5 | new | 193882014 |
| TickParallel_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 289.757 | 542.877 | 4.8 | 9.0 | new | 160306593 |
| TickParallel_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 185.668 | 194.304 | 23.2 | 24.3 | new | -4133485 |
| TickParallel_OscillatingMovement_100k_200Ticks | 100000 | 907.639 | 3700.769 | 11.3 | 46.3 | new | 2121801 |
| TickParallel_OscillatingMovement_10k_200Ticks | 10000 | 125.451 | 382.330 | 15.7 | 47.8 | new | 159152 |
| TickParallel_QuerySystems_10k_200Ticks | 10000 | 40.122 | 45.312 | 5.0 | 5.7 | new | -84192 |
| TickParallel_ReadOnlySystems_10k_200Ticks | 10000 | 6.401 | 60.462 | 0.8 | 7.6 | new | -1885656 |
| TickParallel_V1PulseMovement_100k_50Ticks | 100000 | 115.303 | 216.214 | 7.7 | 14.4 | new | 25862272 |
| TickSerial_Active35_Shuffle10_OscillatingMovement_100k_200Ticks | 100000 | 746.690 | 2257.748 | 9.3 | 28.2 | new | 196948361 |
| TickSerial_Active35_Shuffle10_V1PulseMovement_100k_200Ticks | 100000 | 165.730 | 638.865 | 2.8 | 10.6 | new | 287013353 |
| TickSerial_DirtyAwareWriteSystems_10k_200Ticks | 10000 | 76.520 | 78.900 | 9.6 | 9.9 | new | -1804416 |
| TickSerial_OscillatingMovement_100k_200Ticks | 100000 | 1331.168 | 2976.923 | 16.6 | 37.2 | new | 2674896 |
| TickSerial_OscillatingMovement_10k_200Ticks | 10000 | 164.473 | 280.816 | 20.6 | 35.1 | new | 712256 |
| TickSerial_QuerySystems_10k_200Ticks | 10000 | 15.793 | 30.792 | 2.0 | 3.8 | new | -1304026 |
| TickSerial_ReadOnlySystems_10k_200Ticks | 10000 | 8.755 | 32.417 | 1.1 | 4.1 | new | -1804408 |
| TickSerial_V1PulseMovement_100k_50Ticks | 100000 | 110.472 | 161.078 | 7.4 | 10.7 | new | 24035728 |
| TryGetComponent_Position | 1000 | 0.217 | 0.381 | 217.1 | 381.0 | new | -59152 |
| TryGetComponent_Position | 10000 | 1.176 | 1.878 | 117.6 | 187.8 | new | -1631600 |
| TryGetComponent_Position | 50000 | 3.575 | 8.850 | 71.5 | 177.0 | new | -6571280 |

## New-Only Scenarios
- `SchemaBuild_4Components` scale `4` mean `0.009 ms`, alloc `3536`
- `CreateDefaultImmediate_Position` scale `1000` mean `0.116 ms`, alloc `91896`
- `PatchComponent_Position` scale `1000` mean `0.223 ms`, alloc `145480`
- `CreateDefaultImmediate_Position` scale `10000` mean `0.798 ms`, alloc `2058648`
- `PatchComponent_Position` scale `10000` mean `1.729 ms`, alloc `2064976`
- `CreateDefaultImmediate_Position` scale `50000` mean `1.510 ms`, alloc `8350992`
- `PatchComponent_Position` scale `50000` mean `2.619 ms`, alloc `9055451`

## Old-Only Scenarios
- `ComponentRegister_4Components` scale `4` mean `0.008 ms`, alloc `2128`
- `GetComponentRef_Write_Position` scale `1000` mean `0.188 ms`, alloc `205640`
- `GetComponentRef_Write_Position` scale `10000` mean `1.628 ms`, alloc `3696576`
- `GetComponentRef_Write_Position` scale `50000` mean `3.697 ms`, alloc `15622872`
