param(
    [switch]$NoAutoStart
)

$ErrorActionPreference = 'Stop'

$source = Join-Path $PSScriptRoot 'AimpTaskbarPlayer'
$target = Join-Path $env:LOCALAPPDATA 'Programs\AimpTaskbarPlayer'

if (-not (Test-Path (Join-Path $source 'AimpTaskbarPlayer.exe'))) {
    throw "Не найден AimpTaskbarPlayer.exe рядом со скриптом. Распакуйте архив целиком."
}

Write-Host "Установка в $target ..."
Get-Process AimpTaskbarPlayer -ErrorAction SilentlyContinue | ForEach-Object {
    $_.CloseMainWindow() | Out-Null
    if (-not $_.WaitForExit(3000)) { $_.Kill() }
}

New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force

$exe = Join-Path $target 'AimpTaskbarPlayer.exe'

# Ярлык в меню "Пуск"
$startMenu = [Environment]::GetFolderPath('Programs')
$shortcutPath = Join-Path $startMenu 'AIMP Taskbar Player.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $target
$shortcut.Description = 'AIMP: плеер в панели задач'
$shortcut.IconLocation = "$exe,0"
$shortcut.Save()

# Автозапуск
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if ($NoAutoStart) {
    Remove-ItemProperty -Path $runKey -Name 'AimpTaskbarPlayer' -ErrorAction SilentlyContinue
    Write-Host 'Автозапуск отключён (-NoAutoStart).'
}
else {
    Set-ItemProperty -Path $runKey -Name 'AimpTaskbarPlayer' -Value ('"' + $exe + '"')
    Write-Host 'Автозапуск вместе с Windows включён (отключается в меню панели).'
}

# Настройки (%LOCALAPPDATA%\AimpTaskbarPlayer\settings.json) не трогаем:
# при обновлении позиция панели и путь к AIMP сохраняются.

Start-Process -FilePath $exe
Write-Host 'Готово. Панель появится у правого края панели задач.'
