<#
.SYNOPSIS
    按 Express 方式把 ChemForge 菜单注册进 AutoCAD 定制系统（局部菜单 + 工作空间引用）。

.DESCRIPTION
    Express（acetmain.cuix）的做法：
      1) acetmain.cuix 作为 PnP 菜单组的 PartialMenuFile 在启动时随组加载；
      2) 各工作空间定义的 <WSPop> 引用 EXPRESS 组的菜单，菜单栏按工作空间渲染，
         切换工作空间菜单不丢失、无需运行时挂载或轮询。

    本脚本为 ChemForge 做同样的注册（幂等，CAD 必须关闭）：
      * 确保支持目录存在 ChemForge.cuix（缺省则从 -CuixSource 复制引导文件；
        插件每次启动会按 MenuDefinition 自动保持其最新）；
      * 在**主 acad.CUIX** 的 Header.cui 增加
        <PartialMenuFile>ChemForge.cuix</PartialMenuFile>（最早的加载阶段，
        使菜单组在布局恢复前就绪，避免"不能加载菜单项"）；
      * 在每个工作空间的 <WSPop MenuGroup="EXPRESS"> 之后增加
        <WSPop MenuGroup="CHEMFORGE" pUID="ID_ChemForgeMenu" Display="1" ...>。

    **实测结论（Plant 3D 2025，2026-10-08 复核）**：真正让 ChemForge 出现在经典菜单栏的是
    本脚本末尾调用的 Mount-ChemForgeMenu.ps1（扁平菜单写进出厂 PnP.cuix 的 PNP 组 +
    工作空间 <WSPop MenuGroup="PNP"> + 配置文件布局连续块条目）。局部菜单的 CHEMFORGE 组
    能加载（COM 里可见 5 个菜单），但**不会被画进菜单栏**；根菜单别名即使落在官方
    POP1–POP16 区间（现在的 POP16）也不会自动上栏；运行时 COM 插入
    （旧 MenuServiceNet80.EnsureMenuInBar）已因崩溃风险删除。
    本脚本前面的 PartialMenuFile 注册仍然必要：它让 CHEMFORGE 组在启动时就绪，
    功能区选项卡的工作空间引用（TabId=CF_RIBBON_TAB_CHEMFORGE）依赖该组存在。

    修改前自动备份。加 -Uninstall 撤销全部注册（并可选删除菜单文件）。

.PARAMETER Uninstall
    撤销注册：移除 PartialMenuFile 与所有 CHEMFORGE WSPop 引用。

.PARAMETER RemoveFile
    仅与 -Uninstall 配合：同时删除支持目录里 ChemForge **自己产出**的全部文件
    （ChemForge.cuix、ChemForge.cuix.<自身备份>、ChemForge.mnr、ChemForge_light.mnr、
    chemforge_*.png），摘掉 Autoloader bundle 链接，并把已安装的程序目录
    %LOCALAPPDATA%\Programs\ChemForge 移入隔离区（可逆）。
    **还原基线 *.chemforge-bak 一律保留**：它们是从 ChemForge 改动前留下的原状文件，
    是最后一道回滚手段。

.PARAMETER Restore
    从备份目录整份还原 CUIx（回滚一次注册或卸载）。备份目录由 -RestoreFrom 指定；
    省略时自动取 %TEMP% 下最近一次的 ChemForge-MenuRegister-* / ChemForge-MenuMount-*。
    还原前会把当前 CUIx 另存到 ChemForge-RestoreUndo-*，可以再退回来。

.PARAMETER RestoreFrom
    配合 -Restore：显式指定备份目录。

.PARAMETER CuixSource
    引导用 ChemForge.cuix 路径，默认 deploy\Dev\ChemForge.cuix。

.EXAMPLE
    pwsh -File .\Register-ChemForgeMenu.ps1
    pwsh -File .\Register-ChemForgeMenu.ps1 -Uninstall
    pwsh -File .\Register-ChemForgeMenu.ps1 -Uninstall -RemoveFile
#>
[CmdletBinding()]
param(
    [switch]$Uninstall,
    [switch]$RemoveFile,
    [switch]$Restore,
    [string]$RestoreFrom = '',
    [string]$CuixSource = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

# =====================================================================
#  顶层兜底：任何未捕获的终止性错误都在这里落地
# =====================================================================
# ★ 为什么需要它：安装/卸载程序是用 Exec(..., SW_HIDE) 调本脚本的，窗口是隐藏的，
#   脚本一旦抛异常，用户只能看到一句「菜单注册撤销未完成（退出码 1）」，现场全丢
#   （2026-10-16 卸载时实测踩到，查了半天才定位）。这里把错误连同**出错位置**写进
#   %TEMP%\ChemForge-register-error.log，并在 stdout 打印一份，退出码仍为 1。
$script:ErrorLogPath = Join-Path $env:TEMP 'ChemForge-register-error.log'
trap {
    $err = $_
    $detail = @(
        '==== ChemForge 菜单注册脚本异常 ===='
        ('时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
        ('脚本：' + $PSCommandPath)
        ('参数：' + (($PSBoundParameters.GetEnumerator() | ForEach-Object { '-' + $_.Key + ' ' + $_.Value }) -join ' '))
        ('错误：' + $err.Exception.Message)
        ('类型：' + $err.Exception.GetType().FullName)
        ('位置：' + ($err.InvocationInfo.PositionMessage -replace '\s+', ' '))
    ) -join [Environment]::NewLine
    try { Add-Content -LiteralPath $script:ErrorLogPath -Value ($detail + [Environment]::NewLine) -Encoding UTF8 } catch { }
    Write-Host $detail
    Write-Output ("诊断已写入：{0}" -f $script:ErrorLogPath)
    exit 1
}

if (-not $CuixSource) {
    if (-not $PSScriptRoot) {
        throw '无法定位 ChemForge.cuix：请用 -CuixSource 显式指定路径，或以文件方式运行本脚本（$PSScriptRoot 才可用）。'
    }
    $CuixSource = Join-Path $PSScriptRoot 'ChemForge.cuix'
}

if (Get-Process -Name acad -ErrorAction SilentlyContinue) {
    throw 'AutoCAD 正在运行：请先完全退出 AutoCAD（含所有图纸窗口）再运行本脚本，否则退出时 AutoCAD 会用内存中的旧配置覆盖注册结果。'
}

# 扫描所有 Autodesk 产品的支持目录（含 acad.CUIX 或 PnP.cuix 的目录）
$supportDirs = @()
$autodeskRoot = Join-Path $env:APPDATA 'Autodesk'
if (Test-Path $autodeskRoot) {
    $supportDirs = Get-ChildItem $autodeskRoot -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ChildItem $_.FullName -Directory -ErrorAction SilentlyContinue } |
        ForEach-Object { Get-ChildItem $_.FullName -Directory -Recurse -Depth 2 -ErrorAction SilentlyContinue } |
        Where-Object { $_.Name -eq 'support' } |
        Select-Object -ExpandProperty FullName -Unique
}
if ($supportDirs.Count -eq 0) {
    # 全新机器（或装完还没启动过 AutoCAD）走这里：此时没有用户支持目录可改。
    if ($Uninstall) {
        Write-Output '未找到任何 Autodesk support 目录，无需撤销注册。'
        exit 0
    }
    Write-Output '未找到任何 Autodesk support 目录，这台机器可能还没有启动过 AutoCAD：'
    Write-Output ("  " + $autodeskRoot)
    Write-Output '请先启动一次 AutoCAD（插件会自动生成 ChemForge.cuix 并完成 CUIx 注册），完全退出 AutoCAD 后再运行本脚本。'
    exit 2
}

# =====================================================================
#  -Restore：从备份目录整份还原 CUIx
# =====================================================================
if ($Restore) {
    if (-not $RestoreFrom) {
        $cand = Get-ChildItem $env:TEMP -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^ChemForge-Menu(Register|Mount)-\d{8}-\d{6}$' } |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if (-not $cand) { throw '没有找到可还原的备份目录（%TEMP%\ChemForge-MenuRegister-* / ChemForge-MenuMount-*），请用 -RestoreFrom 显式指定。' }
        $RestoreFrom = $cand.FullName
    }
    if (-not (Test-Path $RestoreFrom)) { throw ("备份目录不存在：{0}" -f $RestoreFrom) }

    Write-Output ("从备份还原 CUIx：{0}" -f $RestoreFrom)
    $undoRoot = Join-Path $env:TEMP ("ChemForge-RestoreUndo-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Force -Path $undoRoot | Out-Null
    $restored = 0
    foreach ($dir in $supportDirs) {
        foreach ($src in (Get-ChildItem -LiteralPath $RestoreFrom -File -ErrorAction SilentlyContinue |
                          Where-Object { $_.Name -match '\.(cuix|cui)$' })) {
            $dst = Join-Path $dir $src.Name
            if (-not (Test-Path $dst)) { continue }
            Copy-Item $dst (Join-Path $undoRoot $src.Name) -Force -ErrorAction SilentlyContinue
            Copy-Item $src.FullName $dst -Force
            Write-Output ("  已还原 {0}（{1} bytes）" -f $src.Name, $src.Length)
            $restored++
        }
    }
    Write-Output ''
    Write-Output ("还原完成：{0} 个文件。还原前的状态备份在 {1}" -f $restored, $undoRoot)
    exit 0
}

# 备份目录**按需创建**：升级（含同版本重装）时 CUIx 通常一个字都不用改，
# 那就既不该在 %TEMP% 里堆一个空目录，也不该把每个 CUIx 都重写一遍（2026-10-16 用户要求）。
$backupRoot = ''
function Get-BackupRoot {
    if (-not $script:backupRoot) {
        $script:backupRoot = Join-Path $env:TEMP ("ChemForge-MenuRegister-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        New-Item -ItemType Directory -Force -Path $script:backupRoot | Out-Null
    }
    return $script:backupRoot
}

# 只读预检：这个 CUIx 到底用不用改？不改就让调用方整个跳过 —— 不建备份、不以 Update 模式打开。
# ★ 以 Update 模式打开会重写文件、刷新 mtime，**即使一个字节都没变**；升级时这是绝大多数文件的情形。
# ★ 判据与下面真正的改动逻辑一一对应，改了那边必须同步改这里。
function Test-CuixNeedsChange([string]$path, [bool]$uninstall, [bool]$isGroupFile) {
    $zip = $null
    try { $zip = [System.IO.Compression.ZipFile]::OpenRead($path) }
    catch { return $true }   # 只读都打不开，交回原路径去处理/报错
    try {
        if ($isGroupFile) {
            $e = $zip.GetEntry('Header.cui')
            if ($null -ne $e) {
                $sr = New-Object System.IO.StreamReader($e.Open(), [System.Text.Encoding]::UTF8)
                $t = $sr.ReadToEnd(); $sr.Close()
                if ($uninstall) {
                    if ($t -match 'ChemForge\.cuix') { return $true }
                }
                elseif ($t -notmatch 'PartialMenuFile>ChemForge\.cuix') { return $true }
            }
            # 方案2：宿主组里要有 ChemForge 根菜单副本（别名 CFHOST_POP）
            $ep = $zip.GetEntry('PopMenuRoot.cui')
            if ($null -ne $ep) {
                $srp = New-Object System.IO.StreamReader($ep.Open(), [System.Text.Encoding]::UTF8)
                $tp = $srp.ReadToEnd(); $srp.Close()
                if ($uninstall) {
                    if ($tp -match 'CFHOST_POP') { return $true }
                }
                elseif ($tp -notmatch 'CFHOST_POP') { return $true }
            }
        }
        # WorkspaceRoot.cui：
        #   安装 —— 每个工作空间都必须有一条指向宿主组副本的 WSPop（经典菜单栏靠它把
        #            ChemForge 菜单项画出来），且不得残留旧的 CHEMFORGE 组引用。
        #   卸载 —— 这类引用必须一条不剩，连同历史扁平方案遗留的 pUID=ID_CFPNP_ROOT。
        $e2 = $zip.GetEntry('WorkspaceRoot.cui')
        if ($null -ne $e2) {
            $sr2 = New-Object System.IO.StreamReader($e2.Open(), [System.Text.Encoding]::UTF8)
            $t2 = $sr2.ReadToEnd(); $sr2.Close()
            if ($uninstall) {
                if ($t2 -match '<WSPop\s+MenuGroup="CHEMFORGE"' -or $t2 -match 'pUID="ID_CFPNP_ROOT"' -or $t2 -match ('pUID="' + $script:HostPopAlias + '"')) { return $true }
            }
            elseif ($isGroupFile -and $script:HostPopEnabled) {
                $wsCount = ([regex]::Matches($t2, '<Workspace\b[^>]*>')).Count
                $cfCount = ([regex]::Matches($t2, ('<WSPop\s+MenuGroup="[A-Z0-9]+"\s+pUID="' + $script:HostPopAlias + '"'))).Count
                if ($cfCount -lt $wsCount) { return $true }
                if ($t2 -match '<WSPop\s+MenuGroup="CHEMFORGE"') { return $true }
            }
            else {
                # 非宿主文件、或声明式宿主组方案已停用时：只清理历史废弃引用，有残留才需要动它。
                if ($t2 -match 'pUID="ID_CFPNP_ROOT"' -or $t2 -match ('pUID="' + $script:HostPopAlias + '"')) { return $true }
            }
        }
        return $false
    }
    finally { $zip.Dispose() }
}

$partialLine   = "        <PartialMenuFile>ChemForge.cuix</PartialMenuFile>"
# ★ 必须显式 (?i)：AutoCAD 自己写回配置时会把文件名小写成 chemforge.cuix，
#   而 .NET 正则默认区分大小写 —— 2026-10-14 实测踩过：-Uninstall 打印「已完成」，
#   文件里的 <PartialMenuFile>chemforge.cuix</PartialMenuFile> 却原封不动留了下来。
$headerPattern = '(?im)(^[ \t]*<PartialMenuFile>ChemForge\.cuix</PartialMenuFile>[ \t]*\r?\n)'

# ChemForge **自己产出**的文件名（删除用）。
# ★ 绝不匹配 *.chemforge-bak：那是 ChemForge 改动前留下的还原基线，必须保留。
$script:ChemForgeOwnPatterns = @(
    '^ChemForge\.cuix$',
    '^ChemForge\.cuix\..+$',        # ChemForge.cuix.bak-* / ChemForge.cuix.expresslike.bak
    '^ChemForge(_light)?\.mnr$',
    '^chemforge_.+\.png$'
)

function Remove-ChemForgeOwnFiles([string]$dir) {
    $n = 0
    $failed = @()
    foreach ($f in (Get-ChildItem -LiteralPath $dir -File -ErrorAction SilentlyContinue)) {
        foreach ($p in $script:ChemForgeOwnPatterns) {
            if ($f.Name -match $p) {
                # ★ 必须 Write-Host：调用方是 $x = Remove-ChemForgeOwnFiles $dir，
                #   函数里的 Write-Output 会一起进管道被当成返回值收走 —— 旧版就是这样
                #   「文件确实删了，却一个字都没打印」，看起来像没干活。
                # ★ 单个文件删不掉**不能**让整个卸载失败：某个文件被资源管理器/杀软/残留进程
                #   占着是常事，旧版裸调 Remove-Item 在 $ErrorActionPreference='Stop' 下会直接
                #   抛异常，整个脚本以退出码 1 收场、卸载器弹「撤销未完成」的假警报
                #   （2026-10-16 实测踩到）。这里降级为警告，其余文件照常清理。
                try {
                    Remove-Item -LiteralPath $f.FullName -Force -ErrorAction Stop
                    Write-Host ("  已删除 " + $f.Name)
                    $n++
                }
                catch {
                    $failed += $f.Name
                    Write-Warning ("  未能删除 " + $f.Name + "：" + $_.Exception.Message)
                }
                break
            }
        }
    }
    if ($failed.Count -gt 0) {
        Write-Warning ("以下文件未能删除（可能被占用），不影响卸载完成，可稍后手工清理：{0}" -f ($failed -join '、'))
    }
    return $n
}

# 改动后的校验：被改动的条目 XML 必须合法；$Forbid 里的模式一个都不许再出现。
# ★ $Only 必须传「本次真正改过的条目名」：PnP.cuix 里还有 Mount-ChemForgeMenu.ps1 管的
#   PopMenuRoot.cui（挂载的弹出菜单里就写着 ChemForge），而本脚本跑在 Mount 之前 ——
#   若整包校验，会把还没轮到清理的条目当成残留而误判失败，触发不必要的回滚。
# 返回 $true / $false，失败原因写进 $script:LastCheckReason。
function Test-CuixEntrySanity([string]$path, [string[]]$Forbid, [string[]]$Only) {
    $script:LastCheckReason = ''
    $z = $null
    try { $z = [System.IO.Compression.ZipFile]::OpenRead($path) }
    catch { $script:LastCheckReason = ("无法作为 ZIP 打开：" + $_.Exception.Message); return $false }
    try {
        foreach ($e in $z.Entries) {
            if ($e.Name -notmatch '\.(cui|xml)$') { continue }
            if ($Only.Count -gt 0 -and ($Only -notcontains $e.Name)) { continue }
            $sr = New-Object System.IO.StreamReader($e.Open())
            $t = $sr.ReadToEnd(); $sr.Close()
            $doc = New-Object System.Xml.XmlDocument
            try { $doc.LoadXml($t) }
            catch { $script:LastCheckReason = ("条目 {0} 的 XML 已损坏：{1}" -f $e.Name, $_.Exception.Message); return $false }
            foreach ($p in $Forbid) {
                if ($t -match $p) { $script:LastCheckReason = ("条目 {0} 仍残留 {1}" -f $e.Name, $p); return $false }
            }
        }
    }
    finally { $z.Dispose() }
    return $true
}

# 目标 WSPop 块（插在 EXPRESS WSPop 之后；UID 用随机 4 位十六进制保证全局唯一）
function New-WsPopBlock([string]$hostGroup) {
    $uid = 'WSPU_250_' + (-join ((48..57) + (65..70) | Get-Random -Count 4 | ForEach-Object { [char]$_ }))
    return @"
      <WSPop MenuGroup="$hostGroup" pUID="$($script:HostPopAlias)" Display="1" UID="$uid">
        <ModifiedRev MajorVersion="25" MinorVersion="0" UserVersion="1" />
      </WSPop>
"@
}

# ---------------------------------------------------------------------
#  方案2（2026-10-24 定案）：把 ChemForge 根菜单整棵复制进**宿主菜单组**
# ---------------------------------------------------------------------
#  经典菜单栏在 AutoCAD 启动、构建菜单栏那一刻就定型，只认那一刻已经存在的菜单组。
#  CHEMFORGE 是插件加载（启动后 4~5 秒）才出现的局部菜单，它的
#  <WSPop MenuGroup="CHEMFORGE"> 永远被忽略 —— Express 能上栏，是因为宿主启动时
#  就加载了 acetmain.cuix。所以：根菜单复制一份进宿主组的 PopMenuRoot.cui，
#  别名 CFHOST_POP，工作空间引用改成 MenuGroup="<宿主组>" pUID="CFHOST_POP"。
#  菜单宏仍由 CHEMFORGE 局部菜单提供（MacroRef 按 ID 全局解析，不受分组影响）。
#  卸载：按别名/UID 前缀把这一整块摘掉，宿主的 PopMenuRoot.cui 复原。
$script:HostPopAlias = 'CFHOST_POP'
$script:HostPopUidPrefix = 'CFH_250_'
# 2026-10-24 定案：经典菜单栏改为**运行时插入**（MenuService.EnsureMenuBarItem 用 COM
# Application.MenuGroups.Item("CHEMFORGE").Menus.Item("ChemForge").InsertInMenuBar）。
# 理由是实测：宿主只认启动时已注册的菜单组里的 WSPop，ChemForge 组由插件在启动 5 秒后才
# 注册，写多少条 WSPop 都不会进菜单栏；把根菜单复制进宿主组也不被采纳。
# 因此下面这段「宿主组内声明」的写入代码默认**停用**，只保留清理能力（保证老机器残留能撤干净），
# 需要复现当年实验时设置环境变量 CF_DECLARATIVE_HOSTPOP=1。
$script:HostPopEnabled = ($env:CF_DECLARATIVE_HOSTPOP -eq '1')

function Get-HostPopMenuBlock([string]$cuixSource) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($cuixSource)
    try { $t = Get-EntryText $zip 'PopMenuRoot.cui' } finally { $zip.Dispose() }
    if (-not $t) { throw ("无法从 {0} 读取 PopMenuRoot.cui" -f $cuixSource) }
    $m = [regex]::Match($t, '(?s)\r?\n([ \t]*)<PopMenu\b[^>]*UID="ID_ChemForgeMenu"[^>]*>.*?</PopMenu>')
    if (-not $m.Success) { throw 'ChemForge.cuix 里没有找到根弹出菜单（UID=ID_ChemForgeMenu）' }
    $block = $m.Value.Trim("`r", "`n")
    # ★ 所有 UID 加前缀：宿主组里已有大量对象，撞号会让宿主静默丢弃整棵菜单。
    $block = [regex]::Replace($block, 'UID="', 'UID="' + $script:HostPopUidPrefix)
    # 别名只留一个，指向宿主组里那份副本；POP16 是 ChemForge 局部菜单自己的槽位，不带过去。
    $block = $block -replace '(?m)^[ \t]*<Alias>POP16</Alias>[ \t]*\r?\n', ''
    $block = $block -replace '(?m)^([ \t]*)<Alias>ID_ChemForgeMenu</Alias>', ('$1<Alias>' + $script:HostPopAlias + '</Alias>')
    return $block
}

function Get-EntryText($zip, $name) {
    $e = $zip.GetEntry($name)
    if ($null -eq $e) { return $null }
    $sr = New-Object System.IO.StreamReader($e.Open())
    $t = $sr.ReadToEnd(); $sr.Close()
    return $t
}

function Set-EntryText($zip, $name, $text, [bool]$hasBom) {
    $e = $zip.GetEntry($name)
    $out = $e.Open()
    try {
        $out.SetLength(0)
        $enc = New-Object System.Text.UTF8Encoding($hasBom)
        $w = New-Object System.IO.StreamWriter($out, $enc)
        $w.Write($text); $w.Flush(); $w.Dispose()
    } finally { $out.Dispose() }
}

function Get-BomFlag($bytes) {
    return $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
}

$touchedFiles = 0
foreach ($dir in $supportDirs) {
    # ---- 卸载：先删我们自己产出的文件 ----
    # ★ 这段必须在「找到宿主 CUIX」的判断**之前**，且不依赖 $target。
    #   旧写法把删除塞在 if ($null -eq $target) { continue } 之后：只要这个支持目录里
    #   没有 PnP.cuix / acad.CUIX / acad.cuix（宿主 CUIX 不在这个目录就会这样），
    #   整段被跳过，ChemForge.cuix 与 chemforge_*.png 就留在 Support\ 里 ——
    #   2026-10-14 用户反馈的「卸载后 Support\ 里还有残留」正是这里。
    #   *.chemforge-bak 是改动前的还原基线，不在匹配范围内，必须保留。
    if ($Uninstall -and $RemoveFile) {
        $removed = Remove-ChemForgeOwnFiles $dir
        if ($removed -gt 0) { Write-Host ("support 目录：{0}" -f $dir) }
    }

    $target = $null
    foreach ($candidate in @((Join-Path $dir 'PnP.cuix'), (Join-Path $dir 'acad.CUIX'), (Join-Path $dir 'acad.cuix'))) {
        if (Test-Path $candidate) { $target = $candidate; break }
    }
    if ($null -eq $target) { continue }
    Write-Output ("support 目录：{0}" -f $dir)

    # ---- 菜单文件本身（仅安装路径：补引导副本 / 提示已存在）----
    # 卸载路径已在循环开头删过，这里不再重复。
    $cuixInSupport = Join-Path $dir 'ChemForge.cuix'
    if (-not $Uninstall) {
        if (-not (Test-Path $cuixInSupport)) {
            if (-not (Test-Path $CuixSource)) {
                Write-Warning ("支持目录缺少 ChemForge.cuix 且未找到引导文件 {0}：请先启动一次 AutoCAD（插件会自动生成），再运行本脚本。" -f $CuixSource)
            }
            else {
                Copy-Item $CuixSource $cuixInSupport -Force
                Write-Output ("  已复制 ChemForge.cuix ({0} bytes)" -f (Get-Item $cuixInSupport).Length)
            }
        }
        else {
            Write-Output '  ChemForge.cuix 已存在（插件启动时自动保持最新）'
        }
    }

    # ---- 逐个 CUIx 处理 Header / Workspace ----
    $files = Get-ChildItem $dir -File | Where-Object { $_.Name -match '\.(cuix|cui)$' -and $_.Name -ne 'PnP.bak.cuix' }
    foreach ($file in $files) {
        # 宿主（组）文件 = 本目录里被选中的那一份：Plant 3D 是 PnP.cuix，纯 AutoCAD 是 acad.cuix。
        # 局部菜单登记必须写进宿主文件的 Header.cui —— 写进非宿主文件不会生效（见下面注释）。
        $isGroupFile = ($file.FullName -eq $target)

        # ★ 先只读预检，不改就整个跳过：不建备份、不以 Update 模式打开（后者会刷新 mtime）。
        #   升级时「什么都不用改」是常态，这一步把「每次安装都重写一遍全部 CUIx」变成零写入。
        if (-not (Test-CuixNeedsChange $file.FullName ([bool]$Uninstall) $isGroupFile)) { continue }

        # ★ 备份必须在改动**之前**：ZipFile 以 Update 模式打开会独占文件，
        #   改完再 Copy-Item 只会拷到「已经改过」的内容，那份备份毫无用处（旧版就是这个 bug）。
        $fileBackup = Join-Path (Get-BackupRoot) $file.Name
        Copy-Item $file.FullName $fileBackup -Force -ErrorAction SilentlyContinue

        $zip = $null
        try { $zip = [System.IO.Compression.ZipFile]::Open($file.FullName, 'Update') }
        catch { continue }

        $headerChanged = $false
        $wsChanged = $false
        $popChanged = $false
        # 宿主组名：Plant 3D 是 PNP，纯 AutoCAD 是 ACAD。工作空间引用里的 MenuGroup 必须用它，
        # 否则引用指向不存在的组 —— 悬空引用会连带把整条菜单栏打塌。
        $script:CurrentHostGroup = $(if ($file.Name -match '(?i)^acad') { 'ACAD' } else { 'PNP' })
        # 局部菜单注册放进**宿主（组）文件**：Plant 3D 是 PnP.cuix（Express/acetmain.cuix 同链，
        # 实测菜单组随启动加载）；纯 AutoCAD 是 acad.cuix —— 它的 Header.cui 里本来就用同样
        # 的 <PartialMenuFile> 机制加载 custom.cuix / appmanager.cuix，机制一致。
        # （2026-10-14 的「放进 acad.CUIX 不生效」是在 Plant 3D 上试的：那里的宿主是 PnP.cuix，
        #   写进 acad.CUIX 自然不会生效 —— 判据是「是不是宿主」，不是文件名。）
        # ChemForge.cuix 等局部菜单文件不得出现在其它 cuix 的 Header 里（防重复加载/自引用）
        try {
            # --- Header.cui：PartialMenuFile 注册（仅 PnP.cuix 组文件）---
            $hBytes = $null
            $hEntry = $zip.GetEntry('Header.cui')
            if ($null -ne $hEntry -and $isGroupFile) {
                $ms = New-Object System.IO.MemoryStream
                $st = $hEntry.Open(); $st.CopyTo($ms); $st.Dispose()
                $hBytes = $ms.ToArray(); $ms.Dispose()
                $headerText = [System.Text.Encoding]::UTF8.GetString($hBytes)
                $hasBom = Get-BomFlag $hBytes

                if ($Uninstall) {
                    if ($headerText -match 'ChemForge\.cuix') {
                        $new = [regex]::Replace($headerText, $headerPattern, '')
                        # 兼容无换行的写法
                        $new = [regex]::Replace($new, '<PartialMenuFile>\s*ChemForge\.cuix\s*</PartialMenuFile>', '', 'IgnoreCase')
                        if ($new -ne $headerText) {
                            Set-EntryText $zip 'Header.cui' $new $hasBom
                            $headerChanged = $true
                        }
                    }
                }
                elseif ($headerText -notmatch 'PartialMenuFile>ChemForge\.cuix') {
                    $insertAfter = [regex]::Match($headerText, '(?m)^[ \t]*<PartialMenuFile>[^<]*</PartialMenuFile>[ \t]*\r?\n')
                    if ($insertAfter.Success) {
                        $pos = $insertAfter.Index + $insertAfter.Length
                        $new = $headerText.Substring(0, $pos) + $partialLine + "`r`n" + $headerText.Substring($pos)
                    }
                    else {
                        $new = $headerText -replace '</CommonItems>', ($partialLine + "`r`n      </CommonItems>")
                    }
                    if ($new -ne $headerText) {
                        Set-EntryText $zip 'Header.cui' $new $hasBom
                        $headerChanged = $true
                    }
                }
            }

            # --- WorkspaceRoot.cui：每个 EXPRESS WSPop 后插入 CHEMFORGE WSPop ---
            $wsEntry = $zip.GetEntry('WorkspaceRoot.cui')
            if ($null -ne $wsEntry) {
                $ms = New-Object System.IO.MemoryStream
                $st = $wsEntry.Open(); $st.CopyTo($ms); $st.Dispose()
                $wsBytes = $ms.ToArray(); $ms.Dispose()
                $wsText = [System.Text.Encoding]::UTF8.GetString($wsBytes)
                $wsBom = Get-BomFlag $wsBytes

                if ($Uninstall) {
                    $new = [regex]::Replace($wsText, '(?s)[ \t]*<WSPop\s+MenuGroup="CHEMFORGE"[^>]*>.*?</WSPop>[ \t]*\r?\n?', '')
                    # 方案2 写进宿主组的副本引用（MenuGroup="PNP"/"ACAD" + pUID=CFHOST_POP）
                    $new = [regex]::Replace($new, ('(?s)[ \t]*<WSPop\s+MenuGroup="[A-Z0-9]+"\s+pUID="' + $script:HostPopAlias + '"[^>]*>.*?</WSPop>[ \t]*\r?\n?'), '')
                    # 历史扁平方案遗留：pUID 指向宿主组里那个 ID_CFPNP_ROOT 弹出菜单。
                    $new = [regex]::Replace($new, '(?s)[ \t]*<WSPop\b[^>]*pUID="ID_CFPNP_ROOT"[^>]*>.*?</WSPop>[ \t]*\r?\n?', '')
                    # 功能区选项卡引用（Mount-ChemForgeMenu.ps1 写的）一并摘掉，
                    # 否则卸载校验会因「仍残留 chemforge」判定失败而整份回滚。
                    $new = [regex]::Replace($new, '(?s)[ \t]*<WSRibbonTabSourceReference\b[^>]*MenuGroup="CHEMFORGE"[^>]*>.*?</WSRibbonTabSourceReference>[ \t]*\r?\n?', '')
                    if ($new -ne $wsText) {
                        Set-EntryText $zip 'WorkspaceRoot.cui' $new $wsBom
                        $wsChanged = $true
                    }
                }
                elseif (-not $script:HostPopEnabled) {
                    # 默认路径：工作空间里不写任何 CHEMFORGE 菜单引用（原因见 $script:HostPopEnabled 定义处）。
                    # 只清理历史遗留，避免老机器上残留的悬空引用把菜单栏打塌。
                    $clean = [regex]::Replace($wsText, ('(?s)[ \t]*<WSPop\s+MenuGroup="[A-Z0-9]+"\s+pUID="' + $script:HostPopAlias + '"[^>]*>.*?</WSPop>[ \t]*\r?\n?'), '')
                    $clean = [regex]::Replace($clean, '(?s)[ \t]*<WSPop\b[^>]*pUID="ID_CFPNP_ROOT"[^>]*>.*?</WSPop>[ \t]*\r?\n?', '')
                    if ($clean -ne $wsText) {
                        Set-EntryText $zip 'WorkspaceRoot.cui' $clean $wsBom
                        $wsChanged = $true
                    }
                }
                else {
                    #   工作空间引用指向宿主组 + 别名 CFHOST_POP。旧的 CHEMFORGE 组引用必须清掉：
                    #   那条引用指向「启动时还不存在的组」，AutoCAD 静默忽略，还会挡住重复检查。
                    $clean = [regex]::Replace($wsText, '(?s)[ \t]*<WSPop\s+MenuGroup="CHEMFORGE"[^>]*>.*?</WSPop>[ \t]*\r?\n?', '')
                    # ★ 只给**宿主组文件**加引用。非宿主文件（acad.CUIX、acad.bak.cuix）里没有
                    #   CFHOST_POP 这个菜单定义，写进去就是悬空引用 —— 悬空的 WSPop 会让宿主
                    #   把整条菜单栏塌掉（2026-10-23 实测）。这类文件只做清理，不做新增。
                    if (-not $isGroupFile) {
                        # 只清理，不新增（理由见上）。
                        if ($clean -ne $wsText) {
                            Set-EntryText $zip 'WorkspaceRoot.cui' $clean $wsBom
                            $wsChanged = $true
                        }
                    }
                    else {
                    $addWsPop = {
                        param($m)
                        $blk = $m.Value
                        if ($blk -match ('pUID="' + $script:HostPopAlias + '"')) { return $blk }
                        $pop = New-WsPopBlock $script:CurrentHostGroup
                        # 锚点：优先 EXPRESS 之后，退化到本工作空间第一个 WSPop 之后，
                        # 再退化到 </Workspace> 之前。只加一条，不重复。
                        $anchor = [regex]::Match($blk, '(?s)<WSPop\s+MenuGroup="EXPRESS"[^>]*>\s*<ModifiedRev[^>]*/>\s*</WSPop>')
                        if (-not $anchor.Success) {
                            $anchor = [regex]::Match($blk, '(?s)<WSPop\b[^>]*>\s*<ModifiedRev[^>]*/>\s*</WSPop>')
                        }
                        if ($anchor.Success) {
                            $pos = $anchor.Index + $anchor.Length
                            return $blk.Substring(0, $pos) + "`r`n" + $pop + $blk.Substring($pos)
                        }
                        $end = $blk.LastIndexOf('</Workspace>')
                        if ($end -lt 0) { return $blk }
                        return $blk.Substring(0, $end) + $pop + "`r`n    " + $blk.Substring($end)
                    }
                    $new = [regex]::Replace($clean, '(?s)<Workspace\b[^>]*>.*?</Workspace>', $addWsPop)
                    if ($new -ne $wsText) {
                        Set-EntryText $zip 'WorkspaceRoot.cui' $new $wsBom
                        $wsChanged = $true
                    }
                    }
                }
            }

            # --- PopMenuRoot.cui：宿主组里放一份 ChemForge 根菜单副本（方案2 的核心）---
            $popEntry = $zip.GetEntry('PopMenuRoot.cui')
            if ($null -ne $popEntry -and $isGroupFile) {
                $msp = New-Object System.IO.MemoryStream
                $stp = $popEntry.Open(); $stp.CopyTo($msp); $stp.Dispose()
                $popBytes = $msp.ToArray(); $msp.Dispose()
                $popText = [System.Text.Encoding]::UTF8.GetString($popBytes)
                $popBom = Get-BomFlag $popBytes
                # 按根菜单 UID 前缀整块定位：卸载/重装都先摘掉旧副本，保证幂等、内容永远最新。
                $popPattern = '(?s)\r?\n?[ \t]*<PopMenu\b[^>]*UID="' + $script:HostPopUidPrefix + 'ID_ChemForgeMenu"[^>]*>.*?</PopMenu>'
                if ($Uninstall) {
                    $new = [regex]::Replace($popText, $popPattern, '')
                }
                elseif (-not $script:HostPopEnabled) {
                    # 默认路径：宿主组里不放 ChemForge 副本；若历史版本放过，这里顺手摘掉。
                    $new = [regex]::Replace($popText, $popPattern, '')
                }
                else {
                    $stripped = [regex]::Replace($popText, $popPattern, '')
                    $block = Get-HostPopMenuBlock $CuixSource
                    $idx = $stripped.LastIndexOf('</PopMenuRoot>')
                    if ($idx -lt 0) { $new = $stripped }
                    else { $new = $stripped.Substring(0, $idx) + $block + "`r`n  " + $stripped.Substring($idx) }
                }
                if ($new -ne $popText) {
                    Set-EntryText $zip 'PopMenuRoot.cui' $new $popBom
                    $popChanged = $true
                }
            }
        }
        finally { $zip.Dispose() }

        if ($headerChanged -or $wsChanged -or $popChanged) {
            # ★ 改完必须校验，不能只打印「已完成」就了事：
            #   XML 是否还合法、该删的残留是否真的归零。任一不过就从备份整份回滚。
            $forbid = @()
            if ($Uninstall -and $file.Name -notmatch '(?i)^chemforge') { $forbid = @('(?i)chemforge') }
            $only = @()
            if ($headerChanged) { $only += 'Header.cui' }
            if ($wsChanged)     { $only += 'WorkspaceRoot.cui' }
            if ($popChanged)    { $only += 'PopMenuRoot.cui' }
            if (Test-CuixEntrySanity $file.FullName $forbid $only) {
                $touchedFiles++
                Write-Output ("  {0}：Header={1} Workspace={2} PopMenu={3} —— 校验通过" -f $file.Name, $headerChanged, $wsChanged, $popChanged)
            }
            else {
                Copy-Item $fileBackup $file.FullName -Force
                Write-Warning ("  {0}：{1} —— 已从备份回滚，本文件未改动" -f $file.Name, $script:LastCheckReason)
            }
        }
    }
}

Write-Output ('')
if ($Uninstall) {
    Write-Output ("已撤销 ChemForge 菜单注册（改动了 {0} 个文件）" -f $touchedFiles)
}
else {
    Write-Output ("已完成 ChemForge 菜单注册（改动了 {0} 个文件）" -f $touchedFiles)
}
if ($backupRoot) { Write-Output ("备份目录：{0}" -f $backupRoot) }
else { Write-Output '本次没有任何 CUIx 需要改动，未创建备份目录。' }

# ---- 功能区选项卡工作空间引用（Mount 脚本）----
# 2026-10-23 定案分工：
#   经典菜单栏 = 本脚本的 PartialMenuFile + CHEMFORGE WSPop（全部工作空间）+ 根菜单
#   POP16 别名 + 扁平根菜单（插件生成 ChemForge.cuix 时直挂 PopMenuItem）；
#   Mount-ChemForgeMenu.ps1 只负责功能区选项卡的工作空间引用（Express 同款做法）。
#   安装与卸载都调用 Mount：安装写功能区引用；卸载时清理它历史写入的扁平菜单
#   （ID_CFPNP_ROOT）、ID_CF_* 宏、宿主组 WSPop 与注册表布局条目。
$mountScript = Join-Path $PSScriptRoot 'Mount-ChemForgeMenu.ps1'
if (Test-Path $mountScript) {
    Write-Output ''
    try {
        # 子 PowerShell 进程 + -File：绕开执行策略，同时保住 $PSScriptRoot。
        # 不要改回 [scriptblock]::Create(...)：$PSScriptRoot 会变成空值，
        # 上面 Join-Path 出来的 $mountScript 立刻失效（菜单挂载被静默跳过）。
        $shell = (Get-Process -Id $PID).Path
        $mountArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $mountScript)
        if ($CuixSource) { $mountArgs += @('-CuixSource', $CuixSource) }
        if ($Uninstall)  { $mountArgs += '-Uninstall' }
        & $shell @mountArgs
        if ($LASTEXITCODE -ne 0) { throw "Mount-ChemForgeMenu.ps1 退出码 $LASTEXITCODE" }
    }
    catch {
        Write-Warning ("ChemForge 菜单挂载失败（可稍后单独运行 Mount-ChemForgeMenu.ps1）：{0}" -f $_.Exception.Message)
    }
}
else {
    Write-Warning '未找到 Mount-ChemForgeMenu.ps1：菜单栏可能不会出现 ChemForge。'
}

# ---- 彻底卸载：摘 Autoloader bundle + 移除已安装的程序目录 ----
# 只做「删掉 ChemForge 自己的东西」，不碰任何 Autodesk 的配置值。
# ★★ 特别注意：本脚本（以及任何部署脚本）**不得**整体改写配置里的支持文件搜索路径
#    （Profiles\<配置>\General 的 ACAD 值）。2026-10-13 那次事故的教训：AutoCAD 的
#    「重置为默认值」按纯 AutoCAD 出厂默认重建该值，抹掉了 Plant 3D 垂直产品自己的
#    ...\AutoCAD 2025\PLNT3D 与 ...\PLNT3D\zh-CN 两条路径，于是 PnPImages.dll / PnP.dll /
#    PnP_Light.dll 无法按名字解析，功能区里所有 Plant 3D 图标变成「?」。
#    真要动，只能做「增删自己那一条」的精确操作，并且必须先备份原值。
if ($Uninstall -and $RemoveFile) {
    $bundle = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\ChemForge.bundle'
    if (Test-Path $bundle) {
        $bi = Get-Item -LiteralPath $bundle -Force
        if (($bi.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            # ★ junction/符号链接：只能 rmdir 摘链接。用 Remove-Item -Recurse 会把
            #   链接指向的目标目录（仓库或 %LOCALAPPDATA% 下的真实 bundle）一起删掉。
            cmd /c rmdir "$bundle" | Out-Null
            Write-Output ("已摘除 Autoloader bundle 链接（目标目录不受影响）：{0}" -f $bundle)
        }
        else {
            Remove-Item -LiteralPath $bundle -Recurse -Force
            Write-Output ("已删除 Autoloader bundle 目录：{0}" -f $bundle)
        }
    }

    $prog = Join-Path $env:LOCALAPPDATA 'Programs\ChemForge'
    if (Test-Path $prog) {
        # ★ 本脚本正从 $prog\Tools\ 里跑（Inno 卸载器调用的就是安装目录里的那一份），
        #   而卸载器自己的 unins000.exe / _unins.tmp 也正在 $prog 里运行 ——
        #   此时 Move-Item 整个目录必然失败（运行中的可执行文件被映射，目录不可改名），
        #   脚本于是以退出码 1 收场，卸载器弹「菜单注册撤销未完成」的**假警报**
        #   （2026-10-16 实测踩到）。目录的删除本来就由卸载器负责，这里直接跳过。
        $runningFromProg = $PSScriptRoot -and $PSScriptRoot.StartsWith($prog, [System.StringComparison]::OrdinalIgnoreCase)
        if ($runningFromProg) {
            Write-Output ("程序目录由卸载程序负责删除，本脚本跳过隔离搬运：{0}" -f $prog)
        }
        else {
            try {
                $quarantine = Join-Path $env:TEMP ("ChemForge-Uninstall-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
                New-Item -ItemType Directory -Force -Path $quarantine | Out-Null
                Move-Item -LiteralPath $prog -Destination (Join-Path $quarantine 'Programs-ChemForge') -Force
                Write-Output ("已移除已安装的程序目录（移入隔离区，可逆）：{0} -> {1}" -f $prog, $quarantine)
            }
            catch {
                Write-Warning ("程序目录未能移入隔离区（{0}）：{1}" -f $prog, $_.Exception.Message)
            }
        }
    }
}