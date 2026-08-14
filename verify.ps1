$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $repository '_Tools/RepositoryVerification/UltraSim.RepositoryVerification.csproj'
dotnet run --project $project -c Release -- @args
exit $LASTEXITCODE
