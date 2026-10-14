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

$backupRoot = Join-Path $env:TEMP ("ChemForge-MenuRegister-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null

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
    foreach ($f in (Get-ChildItem -LiteralPath $dir -File -ErrorAction SilentlyContinue)) {
        foreach ($p in $script:ChemForgeOwnPatterns) {
            if ($f.Name -match $p) {
                Remove-Item -LiteralPath $f.FullName -Force
                # ★ 必须 Write-Host：调用方是 $x = Remove-ChemForgeOwnFiles $dir，
                #   函数里的 Write-Output 会一起进管道被当成返回值收走 —— 旧版就是这样
                #   「文件确实删了，却一个字都没打印」，看起来像没干活。
                Write-Host ("  已删除 " + $f.Name)
                $n++
                break
            }
        }
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
function New-WsPopBlock {
    $uid = 'WSPU_250_' + (-join ((48..57) + (65..70) | Get-Random -Count 4 | ForEach-Object { [char]$_ }))
    return @"
      <WSPop MenuGroup="CHEMFORGE" pUID="ID_ChemForgeMenu" Display="1" UID="$uid">
        <ModifiedRev MajorVersion="25" MinorVersion="0" UserVersion="1" />
      </WSPop>
"@
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
        # ★ 备份必须在改动**之前**：ZipFile 以 Update 模式打开会独占文件，
        #   改完再 Copy-Item 只会拷到「已经改过」的内容，那份备份毫无用处（旧版就是这个 bug）。
        $fileBackup = Join-Path $backupRoot $file.Name
        Copy-Item $file.FullName $fileBackup -Force -ErrorAction SilentlyContinue

        $zip = $null
        try { $zip = [System.IO.Compression.ZipFile]::Open($file.FullName, 'Update') }
        catch { continue }

        $headerChanged = $false
        $wsChanged = $false
        # 局部菜单注册放进 **PnP 组文件**（Express/acetmain.cuix 同链，实测菜单组随启动加载）；
        # 不可放进主 acad.CUIX（实测那样 CHEMFORGE 组不会加载）。
        # ChemForge.cuix 等局部菜单文件不得出现在其它 cuix 的 Header 里（防重复加载/自引用）
        $isGroupFile = $file.Name -match '^PnP\.cuix$'
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
                    if ($new -ne $wsText) {
                        Set-EntryText $zip 'WorkspaceRoot.cui' $new $wsBom
                        $wsChanged = $true
                    }
                }
                elseif ($wsText -notmatch 'MenuGroup="CHEMFORGE"') {
                    # 已停用：工作空间 WSPop 改由 Mount-ChemForgeMenu.ps1 写
                    # MenuGroup="PNP" 的引用（Classic 菜单栏只认被挂载菜单所在组的真实组名，
                    # 写 CHEMFORGE 会让菜单栏丢项/切换工作空间后消失，详见该脚本说明）。
                    Write-Output ("  {0}：工作空间 WSPop 由 Mount-ChemForgeMenu.ps1 统一维护，此处跳过" -f $file.Name)
                }
            }
        }
        finally { $zip.Dispose() }

        if ($headerChanged -or $wsChanged) {
            # ★ 改完必须校验，不能只打印「已完成」就了事：
            #   XML 是否还合法、该删的残留是否真的归零。任一不过就从备份整份回滚。
            $forbid = @()
            if ($Uninstall -and $file.Name -notmatch '(?i)^chemforge') { $forbid = @('(?i)chemforge') }
            $only = @()
            if ($headerChanged) { $only += 'Header.cui' }
            if ($wsChanged)     { $only += 'WorkspaceRoot.cui' }
            if (Test-CuixEntrySanity $file.FullName $forbid $only) {
                $touchedFiles++
                Write-Output ("  {0}：Header={1} Workspace={2} —— 校验通过" -f $file.Name, $headerChanged, $wsChanged)
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
    Write-Output ("已撤销 ChemForge 菜单注册（改动了 {0} 个文件），备份目录：{1}" -f $touchedFiles, $backupRoot)
}
else {
    Write-Output ("已完成 ChemForge 菜单注册（改动了 {0} 个文件），备份目录：{1}" -f $touchedFiles, $backupRoot)
}

# ---- 挂载到出厂 PNP 组（唯一能被经典菜单栏稳定画出的形态）----
# 详见 Mount-ChemForgeMenu.ps1 顶部说明：扁平弹出菜单写进出厂 PnP.cuix（PNP 组）、
# 工作空间 WSPop 用 MenuGroup="PNP"、配置文件布局写连续块条目 PopN = PNP popn。
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
        $quarantine = Join-Path $env:TEMP ("ChemForge-Uninstall-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        New-Item -ItemType Directory -Force -Path $quarantine | Out-Null
        Move-Item -LiteralPath $prog -Destination (Join-Path $quarantine 'Programs-ChemForge') -Force
        Write-Output ("已移除已安装的程序目录（移入隔离区，可逆）：{0} -> {1}" -f $prog, $quarantine)
    }
}