$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$results = Join-Path $PSScriptRoot "results"
New-Item -ItemType Directory -Force -Path $results | Out-Null

dotnet run --configuration Release --project (Join-Path $PSScriptRoot "UltraSim.ECS.NewBenchmarks\UltraSim.ECS.NewBenchmarks.csproj") -- (Join-Path $results "new-ecs-benchmarks.json")
dotnet run --configuration Release --project (Join-Path $PSScriptRoot "UltraSim.ECS.OldBenchmarks\UltraSim.ECS.OldBenchmarks.csproj") -- (Join-Path $results "old-ecs-benchmarks.json")
dotnet run --configuration Release --project (Join-Path $PSScriptRoot "UltraSim.ECS.Compare\UltraSim.ECS.Compare.csproj") -- $results
