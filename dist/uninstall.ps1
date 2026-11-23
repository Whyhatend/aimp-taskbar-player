$ErrorActionPreference = 'Stop'

$target = Join-Path $env:LOCALAPPDATA 'Programs\AimpTaskbarPlayer'

Get-Process AimpTaskbarPlayer -ErrorAction SilentlyContinue | ForEach-Object {
    $_.CloseMainWindow() | Out-Null
    if (-not $_.WaitForExit(3000)) { $_.Kill() }
}

Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'AimpTaskbarPlayer' -ErrorAction SilentlyContinue

$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'AIMP Taskbar Player.lnk'
if (Test-Path $shortcutPath) { Remove-Item $shortcutPath -Force }

if (Test-Path $target) { Remove-Item $target -Recurse -Force }

Write-Host 'Удалено. Настройки остались в %LOCALAPPDATA%\AimpTaskbarPlayer (их можно удалить вручную).'
