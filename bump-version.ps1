param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$version = $Version.TrimStart('v')
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw '版本号格式应为 主.次.修订，例如 1.2.0'
}

# 先把所有替换都校验一遍并叠加到计划里，全部通过后再写文件，避免改到一半失败。
$updates = @(
    @{
        File = 'src\AppPaths.cs'
        Pattern = 'Version = "\d+\.\d+\.\d+"'
        Replacement = 'Version = "' + $version + '"'
    },
    @{
        File = 'src\Properties.cs'
        Pattern = 'AssemblyVersion\("\d+\.\d+\.\d+\.\d+"\)'
        Replacement = 'AssemblyVersion("' + $version + '.0")'
    },
    @{
        File = 'src\Properties.cs'
        Pattern = 'AssemblyFileVersion\("\d+\.\d+\.\d+\.\d+"\)'
        Replacement = 'AssemblyFileVersion("' + $version + '.0")'
    },
    @{
        File = 'src\app.manifest'
        Pattern = 'version="\d+\.\d+\.\d+\.\d+" name="DeepSeekHarnessTray\.app"'
        Replacement = 'version="' + $version + '.0" name="DeepSeekHarnessTray.app"'
    }
)

$planned = [ordered]@{}
foreach ($update in $updates) {
    $path = Join-Path $root $update.File
    if ($planned.Contains($path)) {
        $text = $planned[$path].Text
    }
    else {
        $text = [System.IO.File]::ReadAllText($path)
    }
    $found = [regex]::Matches($text, $update.Pattern)
    if ($found.Count -ne 1) {
        throw "$($update.File) 中匹配 '$($update.Pattern)' 的次数为 $($found.Count)，预期 1 次"
    }
    $planned[$path] = [pscustomobject]@{
        File = $update.File
        Text = [regex]::Replace($text, $update.Pattern, $update.Replacement)
    }
}

$utf8 = New-Object System.Text.UTF8Encoding($false)
foreach ($path in $planned.Keys) {
    [System.IO.File]::WriteAllText($path, $planned[$path].Text, $utf8)
    Write-Host "updated $($planned[$path].File)"
}

Write-Host ''
Write-Host "版本已改为 $version。检查改动后提交："
Write-Host '  git add -A'
Write-Host "  git commit -m `"release: v$version`""
