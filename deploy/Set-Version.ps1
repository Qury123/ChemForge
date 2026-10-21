# Set-Version.ps1 -- 改版本号。全仓库只有这一个入口。
#
# 【为什么要脚本】版本号散落在 4 个地方，漏改一处就会"插件说 1.1.0、安装包说 1.2.0"，
# 而且不会报错 —— 只有用户装完才发现。现在仓库根的 VERSION 文件是唯一真源，
# 其余地方都从它派生：
#   · deploy\Installer\ChemForge.iss        -- ISPP 在编译时读 VERSION
#   · src\Build\ChemForge.Net80.csproj      -- MSBuild 属性函数读 VERSION
#   · src\Build\ChemForge.Net48.csproj      -- 编译前生成程序集版本特性文件
#   · deploy\Bundle\...\PackageContents.xml -- AutoCAD 清单，**不读文件**，由本脚本改写
#   · deploy\Installer\Assets\Wizard-*.bmp  -- 版本号烤在图里，由本脚本重画
# 前三个改 VERSION 就够了；后两个是本脚本存在的原因。
#
# 用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File deploy\Set-Version.ps1 -Version 1.2.0
#   ... -DryRun        只显示会改什么，不落盘
#   ... -SkipAssets    不重画 banner 图（一般不用）

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [switch]$DryRun,
    [switch]$SkipAssets
)

$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$VersionFile = Join-Path $RepoRoot 'VERSION'
$BundleManifest = Join-Path $RepoRoot 'deploy\Bundle\ChemForge.bundle\PackageContents.xml'

function Write-Step($t) { Write-Host ''; Write-Host "== $t" -ForegroundColor Cyan }
function Write-Ok($t)   { Write-Host "   ok   $t" -ForegroundColor Green }
function Write-Skip($t) { Write-Host "   --   $t" -ForegroundColor DarkGray }

# ------------------------------------------------------------------ 校验
$Version = $Version.Trim()
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "版本号必须是 x.y.z 三段数字，收到的是 '$Version'"
}

if (-not (Test-Path $VersionFile)) { throw "找不到版本号文件：$VersionFile" }
$old = ([IO.File]::ReadAllText($VersionFile)).Trim()
Write-Host "版本号：$old  ->  $Version"
if ($old -eq $Version) { Write-Host '（内容相同，仍会重画图片以确保一致）' -ForegroundColor DarkGray }

# 未跟踪文件检查：VERSION 必须入库，否则 clone 下来根本没有这个文件，
# 安装器和两个 csproj 会在求值阶段直接失败。
# 注意：native 命令的 stderr 在 $ErrorActionPreference='Stop' 下会变成终止性错误，
# 而 `git ls-files --error-unmatch` 在文件未跟踪时正是往 stderr 写 —— 必须临时放宽。
$prevEap = $ErrorActionPreference
Push-Location $RepoRoot
try {
    $ErrorActionPreference = 'Continue'
    $null = & git ls-files --error-unmatch VERSION 2>&1
    $tracked = ($LASTEXITCODE -eq 0)
    if (-not $tracked) {
        Write-Host '   !!   VERSION 还没有被 git 跟踪，记得 git add VERSION' -ForegroundColor Yellow
    }
} finally {
    $ErrorActionPreference = $prevEap
    Pop-Location
}

# ------------------------------------------------------ 1. VERSION（唯一真源）
Write-Step 'VERSION（唯一真源）'
if ($DryRun) {
    Write-Skip "会写入 $Version"
} else {
    # UTF-8 无 BOM、LF 换行：.gitattributes 里没有给 VERSION 定 eol，显式用 LF 最稳，
    # 三种读取方（ISPP FileRead / MSBuild ReadAllText / 本脚本）都会 Trim，两种换行都认。
    [IO.File]::WriteAllText($VersionFile, "$Version`n", (New-Object Text.UTF8Encoding($false)))
    Write-Ok "已写入 $VersionFile"
}

# ------------------------------------------- 2. AutoCAD Bundle 清单（不读文件）
Write-Step 'PackageContents.xml（AutoCAD 清单，不读 VERSION，必须改写）'
if (-not (Test-Path $BundleManifest)) { throw "找不到 Bundle 清单：$BundleManifest" }

$xml = [IO.File]::ReadAllText($BundleManifest)
$hits = [regex]::Matches($xml, '(?m)^(\s*)(AppVersion|Version)(\s*=\s*")[^"]*(")')
if ($hits.Count -ne 3) {
    throw "清单里期望 3 处版本号（1 处 AppVersion + 2 处 ComponentEntry Version），实际找到 $($hits.Count) 处；请人工确认后再改。"
}
$newXml = [regex]::Replace($xml, '(?m)^(\s*)(AppVersion|Version)(\s*=\s*")[^"]*(")', "`${1}`${2}`${3}$Version`${4}")
foreach ($h in $hits) {
    $before = ($h.Value -replace '\s+', ' ').Trim()
    $after = $before -replace '"[^"]*"', ('"' + $Version + '"')
    Write-Ok "$before  ->  $after"
}
if ($DryRun) {
    Write-Skip '（DryRun，不落盘）'
} else {
    # 保留原文件编码：清单是 UTF-8 带 BOM（AutoCAD 读它按 UTF-8）
    $hadBom = ([IO.File]::ReadAllBytes($BundleManifest))[0] -eq 0xEF
    [IO.File]::WriteAllText($BundleManifest, $newXml, (New-Object Text.UTF8Encoding($hadBom)))
    Write-Ok "已写入 $BundleManifest"
}

# ------------------------------------------------- 3. banner 图（版本号在图里）
Write-Step 'Wizard banner 图（版本号烤在像素里）'
if ($SkipAssets) {
    Write-Skip '按 -SkipAssets 跳过'
} elseif ($DryRun) {
    Write-Skip '会重画 Wizard-100 / Wizard-150 / WizardSmall-100 / WizardSmall-150 与 ChemForge.ico'
} else {
    # deploy\Installer\ 在本机是"本地目录"：.git/info/exclude 里把整个目录排除了，
    # 公开仓库里没有它。所以脚本要能在没有这个目录的 clone 上跑通 —— 缺了它就跳过
    # 图片重画，不要让改版本号这件事整个失败。
    $mk = Join-Path $PSScriptRoot 'Installer\make-assets.ps1'
    if (-not (Test-Path $mk)) {
        Write-Skip "找不到 $mk（本地目录未入库），跳过图片重画"
    } else {
        $psExe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        & $psExe -NoProfile -ExecutionPolicy Bypass -File $mk |
            ForEach-Object { Write-Host "   $_" }
        if ($LASTEXITCODE -ne 0) { throw "make-assets.ps1 失败，退出码 $LASTEXITCODE" }
    }
}

# ------------------------------------------------------------------ 收尾
Write-Host ''
if ($DryRun) {
    Write-Host '== DryRun 结束，没有改动任何文件' -ForegroundColor Yellow
} else {
    Write-Host '== 完成。接下来：' -ForegroundColor Cyan
    Write-Host '   1) 重新编译插件（版本号进程序集）：'
    Write-Host '        powershell -File deploy\Dev\Install-Dev.ps1'
    Write-Host '   2) 重新出安装包：'
    Write-Host '        powershell -File deploy\Installer\Build-Installer.ps1'
    Write-Host '   3) 别忘了 git add VERSION 以及被本脚本改动的文件'
}
