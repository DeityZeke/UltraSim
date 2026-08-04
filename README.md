# UltraSim Next

The root tree is the new implementation. Frozen V2 Current and Legacy source,
tests, benchmarks, and historical documents live under `_V2/`.

The rebuild starts with the ECS specification in
[`ECS_NEXT_DESIGN.md`](ECS_NEXT_DESIGN.md). Network Next remains specified by
[`NETWORK_NEXT_DESIGN.md`](NETWORK_NEXT_DESIGN.md) and will build on validated
ECS capture/identity seams.

## Current executable slices

Build and run the ECS runtime and generator tests:

```bash
dotnet run --project _Tests/UltraSim.ECS.Tests/UltraSim.ECS.Tests.csproj \
  -c Release
dotnet run --project \
  _Tests/UltraSim.ECS.Generator.Tests/UltraSim.ECS.Generator.Tests.csproj \
  -c Release
```

Run the startup benchmark suite without replacing an accepted baseline:

```bash
dotnet run --project \
  _Benchmarks/UltraSim.ECS.Benchmarks/UltraSim.ECS.Benchmarks.csproj \
  -c Release -- --output /tmp/ultrasim-next-ecs-phase1.json
```

## Local Qwen reviewer

Create the repository-specific model once:

```bash
ollama create ultrasim-qwen -f _Tools/LocalAI/Modelfile.ultrasim-qwen
```

Then run a bounded review:

```bash
_Tools/LocalAI/qwen-review.sh ecs \
  Kernels/ECS/Entities/Entity.cs \
  Kernels/ECS/Entities/EntityPool.cs
```

See [`_Tools/LocalAI/README.md`](_Tools/LocalAI/README.md) for review modes,
limits, and environment overrides.
