#Requires -Version 5.1
<#
.SYNOPSIS
    ChemForge 管道打断类命令的端到端验收（AutoCAD Core Console 无界面驱动）。

.DESCRIPTION
    在真实 AutoCAD 内核里跑完整命令链路：建图形 -> NETLOAD 插件 -> 敲命令 -> 回读几何断言。
    与 src\Tests 下的 MSTest 单元测试互补：那边验菜单/命令名等纯逻辑，这边验真实几何结果。

    覆盖三个场景：
      1. INSTBREAK 缺口模式   —— 仪表线被管线打断，管线/同层交叉/未选图层不受影响
      2. INSTBREAK 架桥模式   —— 缺口位置生成本图层半圆架桥（2 顶点 + |bulge|=1）
      3. PIPEBREAK 回归       —— 改造前的单图层行为不变

    断言全部写在 lisp\Check-*.lisp 里，结果以 "##CF## " 前缀打印，本脚本负责汇总。
    只读源码之外的一切都发生在临时目录；仓库 runtime\PipeBreak.settings 会被临时改写并在
    结束时还原（备份 -> 写入已知图层记忆 -> finally 还原）。

.PARAMETER AcadDir
    AutoCAD 安装目录（需要里面的 accoreconsole.exe），默认 2025。

.PARAMETER SkipBuild
    跳过 dotnet build，直接用现有 deploy\Bundle 里的 ChemForge.dll。

.PARAMETER TimeoutSeconds
    单个场景的超时秒数，默认 180。超时会杀掉 accoreconsole 并判该场景失败。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File src\Tests\Integration\Run-IntegrationTests.ps1
#>
[CmdletBinding()]
param(
    [string]$AcadDir = 'C:\Program Files\Autodesk\AutoCAD 2025',
    [switch]$SkipBuild,
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------- 路径与工具

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$lispDir = Join-Path $PSScriptRoot 'lisp'
$dllPath = Join-Path $repoRoot 'deploy\Bundle\ChemForge.bundle\Contents\net8.0\ChemForge.dll'
$projPath = Join-Path $repoRoot 'src\Build\ChemForge.Net80.csproj'
$settingsPath = Join-Path $repoRoot 'runtime\PipeBreak.settings'
$acCore = Join-Path $AcadDir 'accoreconsole.exe'

# accoreconsole 默认用当前用户的 AutoCAD 配置，退出时会把系统变量写回注册表：
# 实测会把 FILEDIA / CMDDIA / ATTREQ / SECURELOAD 落成 FixedProfile 下的「偏离记录」，
# 用户下次开 AutoCAD 时行为就被改了（内核里没有对话框，本不需要动这些变量）。
# 语法 /isolate <regkey> <userDataFolder> 让它用一套独立的注册表键 + 用户数据目录，
# 实测真实配置完全不被写。下面自检段会回读确认。
$isolateKey = 'CFE2E'

function ConvertTo-Slash([string]$p) { return ($p -replace '\\', '/') }

# 注意：PowerShell 里逗号的优先级高于 '+'，所以脚本行的拼接必须走函数或用 -f，
# 直接在 @(...) 里写 'a' + $x + 'b' 会被逗号切开成多个元素。
function Lisp-Form([string]$path) { return ('(load "{0}")' -f (ConvertTo-Slash $path)) }

# PS 5.1 的 Set-Content -Encoding utf8 会写 BOM，插件那边的解析器只按「键=值」读，
# 稳妥起见统一用无 BOM 的 UTF-8 写文件。
function Write-Utf8NoBom([string]$path, [string]$content) {
    $enc = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($path, $content, $enc)
}

function Resolve-DotNet {
    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath) {
        $sdks = & $onPath.Source --list-sdks 2>$null
        if ($sdks) { return $onPath.Source }
    }
    $local = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    if (Test-Path $local) { return $local }
    throw '找不到带 SDK 的 dotnet（PATH 上没有，%USERPROFILE%\.dotnet\dotnet.exe 也没有）。'
}

function Read-ConsoleText([string]$path) {
    if (-not (Test-Path $path)) { return '' }
    $bytes = [System.IO.File]::ReadAllBytes($path)
    if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
        return [System.Text.Encoding]::Unicode.GetString($bytes, 2, $bytes.Length - 2)
    }
    # accoreconsole 的 stdout 是 UTF-16LE，但写不写 BOM 跟启动方式有关（重定向 stdin 时就没有），
    # 所以再按「前若干字节里 NUL 的密度」兜底判断一次。
    $probe = [Math]::Min(200, $bytes.Length)
    $nul = 0
    for ($i = 0; $i -lt $probe; $i++) { if ($bytes[$i] -eq 0) { $nul++ } }
    if ($probe -gt 0 -and $nul -gt ($probe / 4)) {
        return [System.Text.Encoding]::Unicode.GetString($bytes)
    }
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        return [System.Text.Encoding]::UTF8.GetString($bytes, 3, $bytes.Length - 3)
    }
    return [System.Text.Encoding]::UTF8.GetString($bytes)
}

# ---------------------------------------------------------------- 前置检查

if (-not (Test-Path $acCore)) {
    throw "找不到 accoreconsole.exe：$acCore（用 -AcadDir 指定 AutoCAD 安装目录）。"
}
foreach ($f in @('Harness.lsp', 'Fixture.lsp')) {
    if (-not (Test-Path (Join-Path $lispDir $f))) { throw "缺少 $f：$lispDir" }
}

# ---------------------------------------------------------------- 构建插件

if (-not $SkipBuild) {
    $dotnet = Resolve-DotNet
    Write-Host "[build] $dotnet build $projPath -c Debug" -ForegroundColor Cyan
    & $dotnet build $projPath -c Debug -v minimal --nologo
    if ($LASTEXITCODE -ne 0) { throw "插件构建失败（exit $LASTEXITCODE）。" }
}
if (-not (Test-Path $dllPath)) { throw "找不到插件：$dllPath" }
Write-Host ("[dll] {0} ({1:N0} 字节, {2})" -f $dllPath, (Get-Item $dllPath).Length,
            (Get-Item $dllPath).LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')) -ForegroundColor Cyan

# ---------------------------------------------------------------- 场景定义
# Input 是脚本里逐行喂给命令提示的输入；空字符串 = 回车。
# 图层靠 runtime\PipeBreak.settings 里的记忆值 + 回车确定（Core Console 没有图形拾取）。
# Fixture 指定该场景开头建哪份图形（默认统一夹具，T 型端接场景用 FixtureT.lsp）。

$scenarios = @(
    [ordered]@{
        Id      = 'instbreak-gap'
        Name    = 'INSTBREAK 缺口模式'
        Command = 'INSTBREAK'
        Input   = @('200', 'D', '', '', 'All', '')
        Check   = 'Check-InstBreakGap.lsp'
        Fixture = 'Fixture.lsp'
    }
    [ordered]@{
        Id      = 'instbreak-bridge'
        Name    = 'INSTBREAK 架桥模式（凸起向上）'
        Command = 'INSTBREAK'
        Input   = @('200', 'H', 'U', '', '', 'All', '')
        Check   = 'Check-InstBreakBridge.lsp'
        Fixture = 'Fixture.lsp'
    }
    [ordered]@{
        Id      = 'pipebreak-regression'
        Name    = 'PIPEBREAK 回归（单图层/缺口/垂直）'
        Command = 'PIPEBREAK'
        Input   = @('100', 'D', 'V', '', 'All', '')
        Check   = 'Check-PipeBreakRegression.lsp'
        Fixture = 'Fixture.lsp'
    }
    [ordered]@{
        Id      = 'pipebreak-regression-h'
        Name    = 'PIPEBREAK 回归（单图层/缺口/水平）'
        Command = 'PIPEBREAK'
        Input   = @('100', 'D', 'H', '', 'All', '')
        Check   = 'Check-PipeBreakRegressionH.lsp'
        Fixture = 'Fixture.lsp'
    }
    [ordered]@{
        Id      = 'pipebreak-tjunction'
        Name    = 'PIPEBREAK 端接（T 型）不打断'
        Command = 'PIPEBREAK'
        Input   = @('100', 'D', 'V', '', 'All', '')
        Check   = 'Check-PipeBreakTJunction.lsp'
        Fixture = 'FixtureT.lsp'
    }
)

# ---------------------------------------------------------------- 设置记忆值（带备份还原）
# 图层靠 runtime\PipeBreak.settings 里的记忆值 + 回车确定：Core Console 里没法用鼠标点选实体。

$settingsBackupBytes = $null
if (Test-Path $settingsPath) { $settingsBackupBytes = [System.IO.File]::ReadAllBytes($settingsPath) }
New-Item -ItemType Directory -Force -Path (Split-Path $settingsPath) | Out-Null
Write-Utf8NoBom $settingsPath @'
# temporary, written by Run-IntegrationTests.ps1; restored when the script ends
LastWidth=200
LastPipeLayer=PIPE
LastInstrumentLayer=INST
'@

$tempRoot = Join-Path $env:TEMP ('cf_e2e_' + (Get-Date -Format 'yyyyMMdd_HHmmss'))
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
$emptyInput = Join-Path $tempRoot 'empty.stdin'
Set-Content -Path $emptyInput -Value '' -Encoding ascii
$isolateProfile = Join-Path $tempRoot 'isoprofile'
New-Item -ItemType Directory -Force -Path $isolateProfile | Out-Null

$results = @()
$exitCode = 0

try {
    foreach ($scenario in $scenarios) {
        $dir = Join-Path $tempRoot $scenario.Id
        New-Item -ItemType Directory -Force -Path $dir | Out-Null

        $lines = @(
            'FILEDIA', '0',
            'CMDDIA', '0',
            'SECURELOAD', '0',
            'ATTREQ', '0',
            (Lisp-Form (Join-Path $lispDir 'Harness.lsp')),
            (Lisp-Form (Join-Path $lispDir $scenario.Fixture)),
            'NETLOAD',
            (ConvertTo-Slash $dllPath),
            $scenario.Command
        )
        $lines += $scenario.Input
        $lines += @(
            (Lisp-Form (Join-Path $lispDir $scenario.Check)),
            '_.QUIT',
            'Y'
        )

        $scrPath = Join-Path $dir 'run.scr'
        Set-Content -Path $scrPath -Value $lines -Encoding ascii
        $outFile = Join-Path $dir 'stdout.txt'
        $errFile = Join-Path $dir 'stderr.txt'

        Write-Host ''
        Write-Host ("=== {0} ===" -f $scenario.Name) -ForegroundColor Yellow
        Write-Host ("[scr] {0}" -f $scrPath) -ForegroundColor DarkGray

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        # stdin 必须接一个空文件：脚本跑完后 accoreconsole 会停在命令提示符上等输入，
        # 继承父进程控制台就会一直挂着不退出（EOF 才会自然结束）。
        # /isolate 必须放在 /s 之前，且两个参数都要给（否则报 Failed to create ... /isolate switch）。
        $proc = Start-Process -FilePath $acCore `
            -ArgumentList @('/isolate', $isolateKey, $isolateProfile, '/s', $scrPath) `
            -WorkingDirectory $dir -PassThru -NoNewWindow `
            -RedirectStandardInput $emptyInput `
            -RedirectStandardOutput $outFile -RedirectStandardError $errFile
        $finished = $proc.WaitForExit($TimeoutSeconds * 1000)
        if (-not $finished) {
            $proc.Kill()
            $proc.WaitForExit(15000) | Out-Null
            Write-Host ("[timeout] {0}s 未退出，已杀掉进程。" -f $TimeoutSeconds) -ForegroundColor Red
        }
        $proc.Dispose()
        $sw.Stop()

        $text = Read-ConsoleText $outFile
        $cfLines = @($text -split "`r?`n" | Where-Object { $_ -match '##CF##' })

        $pass = 0
        $fail = 0
        $summarySeen = $false
        foreach ($line in $cfLines) {
            $clean = ($line -replace '^.*##CF##\s*', '').Trim()
            Write-Host ("  " + $clean)
            if ($clean -match '^SUMMARY pass=(\d+) fail=(\d+)') {
                $pass = [int]$Matches[1]
                $fail = [int]$Matches[2]
                $summarySeen = $true
            }
        }

        $ok = $summarySeen -and $fail -eq 0 -and $finished
        if (-not $ok) { $exitCode = 1 }
        if (-not $summarySeen) {
            Write-Host '  [error] 没有拿到 ##CF## SUMMARY：命令可能没跑完或插件没加载。' -ForegroundColor Red
            $tail = @($text -split "`r?`n" | Where-Object { $_.Trim() } | Select-Object -Last 25)
            Write-Host '  --- accoreconsole 输出末尾 ---' -ForegroundColor DarkGray
            $tail | ForEach-Object { Write-Host ("  | " + $_) -ForegroundColor DarkGray }
        }

        $results += [pscustomobject]@{
            Scenario = $scenario.Name
            Pass     = $pass
            Fail     = $fail
            Seconds  = [math]::Round($sw.Elapsed.TotalSeconds, 1)
            Ok       = $ok
        }
    }
}
finally {
    if ($null -ne $settingsBackupBytes) {
        [System.IO.File]::WriteAllBytes($settingsPath, $settingsBackupBytes)
    }
    else {
        Remove-Item -Path $settingsPath -Force -ErrorAction SilentlyContinue
    }
}

# ---------------------------------------------------------------- 注册表自检
# /isolate 生效的话，真实配置里不该出现 FileDialog / CommandDialogs 的偏离记录。
# 这两条正是脚本在 .scr 里设过的系统变量——一旦出现，说明隔离没生效，用户环境已被污染。

$profileKey = 'HKCU:\Software\Autodesk\AutoCAD\R25.0\ACAD-8117:804\FixedProfile\General Configuration'
$regClean = $true
if (Test-Path $profileKey) {
    $key = Get-Item -LiteralPath $profileKey
    $leaked = @()
    foreach ($name in @('FileDialog', 'CommandDialogs')) {
        if ($key.Property -contains $name) { $leaked += ('{0}={1}' -f $name, $key.GetValue($name)) }
    }
    if ($leaked.Count -gt 0) {
        $regClean = $false
        $exitCode = 1
        Write-Host ('[注册表] 真实配置被写入偏离记录：{0}' -f ($leaked -join ', ')) -ForegroundColor Red
        Write-Host '         /isolate 没生效，请检查 accoreconsole 参数（若你刚用过 AutoCAD 图形界面可忽略）。' -ForegroundColor Red
    }
}
if ($regClean) { Write-Host '[注册表] 真实 AutoCAD 配置未被改动（/isolate 生效）。' -ForegroundColor Green }

# ---------------------------------------------------------------- 汇总

Write-Host ''
Write-Host '================ 端到端验收汇总 ================' -ForegroundColor Cyan
$results | Format-Table -AutoSize | Out-String | Write-Host
Write-Host ("临时目录（保留供排查）：{0}" -f $tempRoot) -ForegroundColor DarkGray

$totalPass = ($results | Measure-Object -Property Pass -Sum).Sum
$totalFail = ($results | Measure-Object -Property Fail -Sum).Sum
if ($exitCode -eq 0) {
    Write-Host ("结果：全部通过（{0} 项断言，{1} 个场景）" -f $totalPass, $results.Count) -ForegroundColor Green
}
else {
    Write-Host ("结果：存在失败（通过 {0}，失败 {1}）" -f $totalPass, $totalFail) -ForegroundColor Red
}
exit $exitCode
