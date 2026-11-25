param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Version,

    [switch]$Push,

    [switch]$Manual,

    [string]$Repository = ""
)

# 发布脚本：
#   默认       构建 + 打包到 .release，创建本地 tag，并打印后续命令
#   -Push      推送分支和 tag，由 GitHub Actions 构建并创建 Release
#   -Manual    推送分支后，用本机 gh 直接创建 Release（不推 tag，避免再触发 Actions）
#
# 版本号必须已经写进源码并提交（可用 .\bump-version.cmd 1.2.0 修改），且工作区要干净。

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )
    # 原生命令把进度/错误写到 stderr，在 EAP=Stop 下会被当成终止错误；
    # 这里临时放宽，统一按退出码判断，失败时把输出带进异常。
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $FilePath @Arguments 2>&1
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
    if ($code -ne 0) {
        $text = ($output | Out-String).Trim()
        throw "$FilePath $($Arguments -join ' ') 失败（exit $code）`r`n$text"
    }
    return $output
}

Push-Location $root
try {
    if ($Push -and $Manual) {
        throw '-Push 和 -Manual 只能选一个：前者交给 GitHub Actions，后者用本机 gh 发布。'
    }
    if (-not (Test-Path (Join-Path $root '.git'))) {
        throw "当前目录不是 git 仓库：$root"
    }

    $version = $Version.TrimStart('v')
    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw '版本号格式应为 主.次.修订，例如 1.2.0'
    }
    $tag = "v$version"

    # 版本号必须与源码一致
    $appPaths = [System.IO.File]::ReadAllText((Join-Path $root 'src\AppPaths.cs'))
    if ($appPaths -notmatch ('Version = "' + [regex]::Escape($version) + '"')) {
        throw "src\AppPaths.cs 的 Version 不是 $version。请先运行 .\bump-version.cmd $version 并提交。"
    }
    $properties = [System.IO.File]::ReadAllText((Join-Path $root 'src\Properties.cs'))
    if ($properties -notmatch ('AssemblyVersion\("' + [regex]::Escape($version) + '\.0"\)')) {
        throw "src\Properties.cs 的 AssemblyVersion 不是 $version.0。请先运行 .\bump-version.cmd $version 并提交。"
    }

    # 只允许在干净的提交上发布
    $dirty = @(Invoke-Native git @('status', '--porcelain'))
    if ($dirty.Count -gt 0) {
        throw ("工作区有未提交的改动，请先提交：`r`n" + ($dirty -join "`r`n"))
    }

    # 发布方式的前置条件先检查，避免白构建一遍
    $hasRemote = @(Invoke-Native git @('remote')) -contains 'origin'
    if ($Push -and -not $hasRemote) {
        throw '没有配置 origin remote。先执行 gh repo create ...，或 git remote add origin <url>。'
    }
    if ($Manual) {
        if (-not $hasRemote) { throw '本地手动发布需要 origin remote（先把仓库托管到 GitHub）。' }
        if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw '找不到 gh，请先安装 GitHub CLI。' }
    }

    Write-Host "==> 构建 v$version"
    & (Join-Path $root 'build.ps1')

    $exe = Join-Path $root 'dist\DeepSeekHarnessTray.exe'
    if (-not (Test-Path $exe)) { throw "构建没有生成 $exe" }
    $fileVersion = (Get-Item $exe).VersionInfo.FileVersion
    if (-not $fileVersion.StartsWith($version)) {
        throw "exe 文件版本 $fileVersion 与 $version 不一致"
    }

    # 打包
    $packageDir = Join-Path $root (Join-Path '.release' $tag)
    if (Test-Path $packageDir) { Remove-Item $packageDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
    $packageExe = Join-Path $packageDir 'DeepSeekHarnessTray.exe'
    $hashFile = Join-Path $packageDir 'DeepSeekHarnessTray.exe.sha256'
    Copy-Item $exe $packageExe -Force
    $hash = (Get-FileHash $packageExe -Algorithm SHA256).Hash
    Set-Content -Path $hashFile -Value ($hash + '  DeepSeekHarnessTray.exe') -Encoding Ascii
    Write-Host "==> 打包完成：$packageDir"

    # 本地 tag
    $head = ((Invoke-Native git @('rev-parse', 'HEAD')) | Out-String).Trim()
    $existingTags = @(Invoke-Native git @('tag', '--list', $tag))
    if ($existingTags.Count -gt 0) {
        $tagCommit = ((Invoke-Native git @('rev-list', '-n', '1', $tag)) | Out-String).Trim()
        if ($tagCommit -ne $head) {
            throw "标签 $tag 已存在且不在 HEAD（$tagCommit）。请换版本号，或先删除该标签。"
        }
        Write-Host "==> 标签 $tag 已存在并指向 HEAD"
    }
    else {
        Invoke-Native git @('tag', '-a', $tag, '-m', "DeepSeek Harness Tray $tag") | Out-Null
        Write-Host "==> 已创建标签 $tag"
    }

    $hasRemote = @(Invoke-Native git @('remote')) -contains 'origin'

    if ($Push) {
        Invoke-Native git @('push', 'origin', 'HEAD') | Out-Null
        Invoke-Native git @('push', 'origin', $tag) | Out-Null
        Write-Host ''
        Write-Host "==> 已推送 $tag，GitHub Actions 会在 windows-latest 上构建并创建 Release。"
        Write-Host "    查看：gh release view $tag"
        return
    }

    if ($Manual) {
        Invoke-Native git @('push', 'origin', 'HEAD') | Out-Null
        $releaseArgs = @(
            $tag,
            $packageExe,
            $hashFile,
            '--title', "DeepSeek Harness Tray $tag",
            '--generate-notes',
            '--target', $head
        )
        if ($Repository) { $releaseArgs += @('--repo', $Repository) }
        $result = Invoke-Native gh $releaseArgs
        Write-Host (($result | Out-String).Trim())
        return
    }

    Write-Host ''
    Write-Host '本地发布文件已就绪：'
    Write-Host "  $packageExe"
    Write-Host "  $hashFile"
    Write-Host ''
    Write-Host '下一步（二选一）：'
    Write-Host "  1) 让 GitHub Actions 发布： .\release.cmd $version -Push"
    Write-Host "  2) 本机 gh 直接发布：      .\release.cmd $version -Manual"
    if (-not $hasRemote) {
        Write-Host ''
        Write-Host '还没有 origin remote，先托管到 GitHub：'
        Write-Host '  gh repo create dsh-tray --public --source . --remote origin --push'
    }
}
finally {
    Pop-Location
}
