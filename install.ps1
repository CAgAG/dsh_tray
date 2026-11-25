param(
    [switch]$NoDesktop,
    [switch]$NoStartMenu
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $root 'dist\DeepSeekHarnessTray.exe'
if (-not (Test-Path $source)) { throw "找不到 $source，请先运行 build.cmd" }

$programs = Join-Path $env:LOCALAPPDATA 'Programs\DeepSeekHarnessTray'
New-Item -ItemType Directory -Force -Path $programs | Out-Null
$target = Join-Path $programs 'DeepSeekHarnessTray.exe'

# A running exe cannot be overwritten, but it can be renamed. Move the old file
# aside, drop the new build in place, and let the next launch use the new one.
$oldName = 'DeepSeekHarnessTray.old-' + (Get-Date -Format 'HHmmss') + '.exe'
$moved = $false
if (Test-Path $target) {
    try {
        Rename-Item -LiteralPath $target -NewName $oldName -ErrorAction Stop
        $moved = $true
    }
    catch {
        throw "无法替换 $target（文件被占用），请先从托盘菜单退出启动器，再运行 install.cmd。"
    }
}

try {
    Copy-Item -LiteralPath $source -Destination $target -Force
}
catch {
    if ($moved) {
        try { Rename-Item -LiteralPath (Join-Path $programs $oldName) -NewName 'DeepSeekHarnessTray.exe' } catch { }
    }
    throw
}

# Remove renamed copies that are no longer locked by a running process.
$leftover = 0
Get-ChildItem -Path $programs -Filter 'DeepSeekHarnessTray.old-*.exe' -ErrorAction SilentlyContinue | ForEach-Object {
    try { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction Stop }
    catch { $leftover++ }
}

$shell = New-Object -ComObject WScript.Shell
function New-Shortcut([string]$linkPath) {
    $shortcut = $shell.CreateShortcut($linkPath)
    $shortcut.TargetPath = $target
    $shortcut.WorkingDirectory = $programs
    $shortcut.IconLocation = $target + ',0'
    $shortcut.Description = '启动 DeepSeek Harness (DSH) Web 界面'
    $shortcut.Save()
}

if (-not $NoDesktop) {
    $desktop = [Environment]::GetFolderPath('Desktop')
    New-Shortcut (Join-Path $desktop 'DeepSeek Harness.lnk')
    Write-Host "已创建桌面快捷方式: $(Join-Path $desktop 'DeepSeek Harness.lnk')"
}
if (-not $NoStartMenu) {
    $startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
    New-Item -ItemType Directory -Force -Path $startMenu | Out-Null
    New-Shortcut (Join-Path $startMenu 'DeepSeek Harness.lnk')
    Write-Host "已创建开始菜单快捷方式: $(Join-Path $startMenu 'DeepSeek Harness.lnk')"
}

Write-Host "已安装到: $target"
if ($moved -and $leftover -gt 0) {
    Write-Host ''
    Write-Host '注意：旧版托盘还停在内存里。请右键托盘图标 → 退出，再双击桌面「DeepSeek Harness」启动新版本。'
    Write-Host '（1.0.x 退出时会同时停止 DSH，本页面会断开；1.1.0 起退出不再停止 DSH。）'
}
Write-Host '数据与配置在 %LOCALAPPDATA%\DeepSeekHarnessTray。'
