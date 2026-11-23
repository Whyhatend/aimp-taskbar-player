param(
    [string]$Root = (Resolve-Path "$PSScriptRoot\..").Path,
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
$project = Join-Path $Root 'src\AimpTaskbarPlayer\AimpTaskbarPlayer.csproj'

Get-Process AimpTaskbarPlayer -ErrorAction SilentlyContinue | ForEach-Object {
    $_.CloseMainWindow() | Out-Null
    if (-not $_.WaitForExit(3000)) { $_.Kill() }
}

& $dotnet build $project -c Release -v q --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $Root 'src\AimpTaskbarPlayer\bin\Release\net8.0-windows\AimpTaskbarPlayer.exe'
Write-Host "built: $exe"

if ($Run) {
    Start-Process -FilePath $exe
    Start-Sleep -Seconds 2
    Write-Host "started"
}
