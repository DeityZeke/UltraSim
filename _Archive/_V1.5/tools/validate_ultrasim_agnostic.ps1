param(
    [Parameter(Mandatory = $true)]
    [string]$Root
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $Root)) {
    exit 0
}

$pattern = '\busing\s+Godot\b|\bGodot\.'
$violations = New-Object System.Collections.Generic.List[object]

Get-ChildItem -Path $Root -Recurse -Filter *.cs -File | ForEach-Object {
    $matches = Select-String -Path $_.FullName -Pattern $pattern
    foreach ($match in $matches) {
        $violations.Add($match)
    }
}

if ($violations.Count -gt 0) {
    Write-Host "[UltraSim Guard] Godot references detected in UltraSim sources:" -ForegroundColor Red
    foreach ($v in $violations) {
        $line = $v.Line.Trim()
        Write-Host " - $($v.Path):$($v.LineNumber) -> $line"
    }

    Write-Host ""
    Write-Host "Move Godot-specific code to Client.Godot / Server.Godot / Shared.Godot." -ForegroundColor Yellow
    exit 1
}

exit 0
