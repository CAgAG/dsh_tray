param(
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root 'src'
$build = Join-Path $root 'build'
$dist = Join-Path $root 'dist'

if ($Clean) {
    if (Test-Path $build) { Remove-Item $build -Recurse -Force }
    if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
}
New-Item -ItemType Directory -Force -Path $build, $dist | Out-Null

# 1. Icon for the exe file (+ tray fallback art)
& (Join-Path $root 'tools\make-icon.ps1') -OutPath (Join-Path $build 'dsh.ico')

# 2. Compile with the .NET Framework 4 compiler that ships with Windows.
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $frameworkDir 'csc.exe'
if (-not (Test-Path $csc)) {
    $frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
    $csc = Join-Path $frameworkDir 'csc.exe'
}
if (-not (Test-Path $csc)) { throw 'csc.exe (.NET Framework 4.x) not found on this machine.' }

$out = Join-Path $dist 'DeepSeekHarnessTray.exe'
if (Test-Path $out) { Remove-Item $out -Force }

$sources = @(Get-ChildItem -Path (Join-Path $src '*.cs') | Sort-Object Name | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw "No C# sources found in $src" }

$cscArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/codepage:65001',
    ('/win32icon:' + (Join-Path $build 'dsh.ico')),
    ('/win32manifest:' + (Join-Path $src 'app.manifest')),
    ('/out:' + $out),
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll'
) + $sources

Write-Host "compiling $($sources.Count) files with $csc"
& $csc $cscArgs
if ($LASTEXITCODE -ne 0) { throw ("csc failed with exit code " + $LASTEXITCODE) }
if (-not (Test-Path $out)) { throw 'compile produced no exe' }

$info = Get-Item $out
Write-Host ("built: " + $info.FullName + " (" + [math]::Round($info.Length / 1KB, 1) + " KB)")
