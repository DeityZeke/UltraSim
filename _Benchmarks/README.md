# ECS Benchmarks

This folder contains an offline benchmark suite for comparing:

- `Kernels/ECS` as the new kernel
- `_OLD/UltraSim/ECS` as the legacy kernel

The suite avoids external packages so it can run inside the repo without pulling BenchmarkDotNet.

## Coverage

The benchmark matrix currently covers:

- bootstrap/registration
- deferred structural creation
- direct component writes
- safe component reads
- 1/2/3-component queries
- deferred add/remove/destroy
- sparse-victim destroy / swap-remove pressure
- add/remove migration cycles
- create/destroy reuse churn
- dirty drain cost
- scheduler tick cost in serial and parallel modes
- persistence-view capture cost

It also includes kernel-specific scenarios:

- new kernel: immediate create and explicit patch writes
- old kernel: mutable ref write path

## Running

```powershell
powershell -ExecutionPolicy Bypass -File .\_Benchmarks\run_ecs_benchmarks.ps1
```

Results are written to:

- `_Benchmarks/results/new-ecs-benchmarks.json`
- `_Benchmarks/results/old-ecs-benchmarks.json`
- `_Benchmarks/results/ecs-benchmark-summary.md`
