param(
    # Универсальные параметры — можно переопределить для другого плеера/приложения.
    [string]$PlayerName = 'AimpTaskbarPlayer',
    [string]$DisplayName = 'AIMP Taskbar Player',
    [string]$SourceDir = 'AimpTaskbarPlayer',
    [string]$ExeName = 'AimpTaskbarPlayer.exe',
    [string]$ProcessName = 'AimpTaskbarPlayer',
    [switch]$NoAutoStart
)

$ErrorActionPreference = 'Stop'

$source = Join-Path $PSScriptRoot $SourceDir
$target = Join-Path $env:LOCALAPPDATA ('Programs\' + $PlayerName)
$exe = Join-Path $target $ExeName

function Stop-TargetApp {
    Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host ("Остановка процесса {0} (PID {1}) ..." -f $ProcessName, $_.Id) -ForegroundColor Yellow
        $_.CloseMainWindow() | Out-Null
        if (-not $_.WaitForExit(3000)) { $_.Kill() }
        $_.WaitForExit(3000) | Out-Null
    }
}

Write-Host 'Taskbar Media Player — установка' -ForegroundColor Cyan
Write-Host ("От: {0}" -f $source) -ForegroundColor DarkGray
Write-Host ("В: {0}" -f $target) -ForegroundColor DarkGray

if (-not (Test-Path (Join-Path $source $ExeName))) {
    throw ("Не найден {0} рядом со скриптом. Распакуйте архив целиком." -f $ExeName)
}

Write-Host 'Остановка запущенной версии …' -ForegroundColor Yellow
Stop-TargetApp

Write-Host 'Копирование файлов …' -ForegroundColor Yellow
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force

if (-not (Test-Path $exe)) {
    throw ("Ошибка: после копирования не найден {0} в {1}." -f $ExeName, $target)
}

Write-Host 'Создание ярлыка в меню «Пуск» …' -ForegroundColor Yellow
$startMenu = [Environment]::GetFolderPath('Programs')
$shortcutPath = Join-Path $startMenu ('{0}.lnk' -f $DisplayName)
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $target
$shortcut.Description = $DisplayName
$shortcut.IconLocation = "$exe,0"
$shortcut.Save()

Write-Host 'Настройка автозапуска …' -ForegroundColor Yellow
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$appName = $PlayerName
if ($NoAutoStart) {
    Remove-ItemProperty -Path $runKey -Name $appName -ErrorAction SilentlyContinue
    Write-Host 'Автозапуск отключён (-NoAutoStart).' -ForegroundColor Yellow
}
else {
    Set-ItemProperty -Path $runKey -Name $appName -Value ('"' + $exe + '"')
    Write-Host 'Автозапуск вместе с Windows включён (отключается в меню панели).' -ForegroundColor Green
}

Write-Host 'Запуск …' -ForegroundColor Yellow
Start-Process -FilePath $exe
Start-Sleep -Seconds 2

Write-Host 'Готово.' -ForegroundColor Green
Write-Host 'Панель появится у правого края панели задач.'
Write-Host 'Настройки при обновлении не сбрасываются: %LOCALAPPDATA%\AimpTaskbarPlayer\settings.json'
if ($NoAutoStart) { Write-Host 'Запуск с параметром -NoAutoStart: автозапуск не включён.' }
