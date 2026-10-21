<#
.SYNOPSIS
    把 ChemForge 菜单**以能真正画出来的形态**挂进 AutoCAD 出厂主定制文件。

    宿主文件与组名**按支持目录自动判定**（2026-10-16 起支持纯 AutoCAD）：
      * Plant 3D 垂直产品：宿主 = PnP.cuix，组名 = PNP；
      * 纯 AutoCAD（含 2019-2024）：宿主 = acad.cuix / acad.CUIX，组名 = ACAD。
    组名从宿主文件的 MenuGroup.cui 里读出来，不写死 —— 下面的说明以 Plant 3D 为例。

.DESCRIPTION
    为什么不用"独立 CHEMFORGE 组 + 局部菜单注册"那条路（实测结论，Plant 3D 2025）：
      * 局部菜单（ChemForge.cuix）能加载、菜单组也在，但经典菜单栏**只在启动构建菜单栏那一次**
        由 AutoCAD 把工作空间 WSPop 引用的弹出菜单追加并画出；切换工作空间/重建菜单栏即丢失；
      * 一旦配置文件布局里存在 `Pop<n> = CHEMFORGE ...` 条目，AutoCAD 恢复菜单栏时还会报
        "不能加载菜单项: CHEMFORGE:ChemForge" 并丢弃该项（COM 里有、界面没有）；
      * 嵌套子菜单（根菜单 + <PopMenuRef>）挂在宿主组里能加载但**不会被画出来**。

    实测能稳定画出的形态（重启、切换工作空间都在）——本脚本就是把这三件事写进文件：
      1) 在 **宿主 CUIX** 的 PopMenuRoot.cui 里放一个**扁平**弹出菜单（无子菜单，用分隔线分组）：
         UID=ID_CFPNP_ROOT、<Alias>POPn</Alias>、字面名 `<Name xlate="false">ChemForge</Name>`、
         工厂同款头部；菜单项从插件生成的 ChemForge.cuix 收割（标签/命令单一来源），
         宏引用改写成 MenuMacroID="ID_CF_*"，宏定义同时写进宿主 CUIX 的 MenuGroup.cui；
      2) 各工作空间 WorkspaceRoot.cui 里加 `<WSPop MenuGroup="<宿主组>" pUID="ID_CFPNP_ROOT">`
         ——**组名必须是被挂载菜单所在组的真实名字**（写 CHEMFORGE 不生效）；
      3) 配置文件布局 `<配置>\Menus` 里写 `Pop<下一个连续槽> = <宿主组> pop<n>`
         ——槽号必须落在 Pop1..PopN **连续块**内，缺号会让其后的条目全部被忽略。

      4) 各工作空间 WorkspaceRoot.cui 里加 `<WSRibbonTabSourceReference MenuGroup="CHEMFORGE"
         TabId="CF_RIBBON_TAB_CHEMFORGE">`（含各面板的 WSRibbonPanelSourceReference）
         ——**这是"功能区选项卡与自带选项卡一起、开 CAD 就出现、切换工作空间不丢"的正路**：
         Express Tools 就是这么做的（局部菜单 acetmain.cuix 里的 RibbonTabSource UID=RBN_0047，
         被宿主 CUIX 的 9 个工作空间逐个引用）。只声明 RibbonTabSource 而不写工作空间引用时，
         AutoCAD 会把它读进来但**不显示**，只能靠插件运行时补建（表现为"先出默认选项卡，
         过一会儿 ChemForge 才刷新出来"）。面板按 "<组名>.<面板UID>" 引用，故插件生成 CUIx 时
         面板 UID 必须是稳定值（CF_PANEL_*），否则每次重新生成都会让引用失效。

.PARAMETER Uninstall
    撤销：删掉挂载的弹出菜单、宏、WSPop、功能区选项卡引用与布局条目
    （同时清理历史遗留的 CHEMFORGE 连续块条目）。

.PARAMETER CuixSource
    收割菜单项/宏的来源 CUIx；默认为支持目录里的 ChemForge.cuix（插件生成的），
    找不到时回退到 deploy\Dev\ChemForge.cuix（仓库引导副本）。

    ⚠ 时间戳陷阱（2026-10-13 踩过；2026-10-16 已在插件侧根治）：
      支持目录那份 ChemForge.cuix 只有在插件**重新生成**过之后才等于当前菜单定义。
      本脚本第 2/4 步会用 ZipFile 'Update' 重写每个 .cuix/.cui 的工作空间，这一写会把
      ChemForge.cuix 的 LastWriteTimeUtc 刷新到挂载时刻；而插件原先判断「要不要重建菜单
      文件」用的是时间戳判据（cuix 时间 < 插件 DLL 时间 → 才重建）。于是「编译 → 挂载」
      之后 CUIx 恒比 DLL 新，插件永远跳过重建，**改了 MenuDefinition 也传不进 CUIx**：
      挂载收割到的还是旧菜单，菜单栏纹丝不动（v1.1.0 加 INSTBREAK 时就是这么被坑的）。

      ⇒ 2026-10-16 起插件改用**内容判据**（src\Shared\CAD\Impl\MenuService_Net80.cs 的
      MenuFileNeedsRebuild：比对 CUIx 里 MenuMacro 的 UID 集合与 MenuDefinition 的命令项
      别名集合，不一致就重建），上面那条陷阱不再存在。改了菜单定义只要：
        ① 重新编译并部署到本机（本机部署脚本）；
        ② 启动 AutoCAD 一次 —— 插件发现 CUIx 内容与菜单定义不一致，自动重建权威版本；
        ③ 完全退出 AutoCAD，再跑本脚本挂载。
      不必再手工删 CUIx。挂载前请核对本脚本打印的「菜单项 N 个」= 当前 MenuDefinition 的叶子项数。

.EXAMPLE
    pwsh -File .\Mount-ChemForgeMenu.ps1
    pwsh -File .\Mount-ChemForgeMenu.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [switch]$Uninstall,
    [string]$CuixSource = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$rootUid = 'ID_CFPNP_ROOT'
$menuTitle = 'ChemForge'

if (Get-Process -Name acad -ErrorAction SilentlyContinue) {
    throw 'AutoCAD 正在运行：请先完全退出 AutoCAD 再运行本脚本，否则退出时 AutoCAD 会用内存中的旧配置覆盖挂载结果。'
}

function Get-ZipEntryText($zip, $name) {
    $e = $zip.GetEntry($name)
    if ($null -eq $e) { return $null }
    $sr = New-Object System.IO.StreamReader($e.Open())
    $t = $sr.ReadToEnd(); $sr.Close()
    return $t
}
function Set-ZipEntryText($zip, $name, $text) {
    $e = $zip.GetEntry($name)
    $out = $e.Open()
    try {
        $out.SetLength(0)
        $w = New-Object System.IO.StreamWriter($out, (New-Object System.Text.UTF8Encoding($false)))
        $w.Write($text); $w.Flush(); $w.Dispose()
    } finally { $out.Dispose() }
}
function Read-CuixEntry($path, $entry) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($path)
    try { return Get-ZipEntryText $zip $entry } finally { $zip.Dispose() }
}

# 去掉菜单项/宏里的图片引用。插件生成的 CUIx 里写的是 <SmallImage Name="chemforge_xxx_16.png" />，
# 指的是内嵌在 ChemForge.cuix 里的 PNG；挂到出厂 PnP.cuix 后这个文件名在支持目录里并不存在，
# 于是菜单左侧渲染成一个 "?"。宁可没有图标，也不要问号（功能区按钮的图标走另一条路，不受影响）。
function Remove-MenuImageRef([string]$xml) {
    foreach ($tag in @('SmallImage', 'LargeImage')) {
        $xml = [regex]::Replace($xml, "\s*<$tag\b[^>]*/>", '')
        $xml = [regex]::Replace($xml, "\s*<$tag\b[^>]*>[\s\S]*?</$tag>", '')
    }
    return $xml
}

# ---------- 支持目录 ----------
$supportDirs = @()
$autodeskRoot = Join-Path $env:APPDATA 'Autodesk'
if (Test-Path $autodeskRoot) {
    $supportDirs = Get-ChildItem $autodeskRoot -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ChildItem $_.FullName -Directory -ErrorAction SilentlyContinue } |
        ForEach-Object { Get-ChildItem $_.FullName -Directory -Recurse -Depth 2 -ErrorAction SilentlyContinue } |
        Where-Object { $_.Name -eq 'support' } |
        Select-Object -ExpandProperty FullName -Unique
}
if ($supportDirs.Count -eq 0) { throw "未找到任何 Autodesk support 目录：$autodeskRoot" }

if (-not $CuixSource) {
    if (-not $PSScriptRoot) {
        throw '无法定位 ChemForge.cuix：请用 -CuixSource 显式指定路径，或以文件方式运行本脚本（$PSScriptRoot 才可用）。'
    }
    $CuixSource = Join-Path $PSScriptRoot 'ChemForge.cuix'
}

# 备份目录**按需创建**：升级时如果某个 support 目录的内容已经是最新的，整个目录会被跳过，
# 就不该在 %TEMP% 里留下一个空目录（2026-10-16 用户要求）。
$backupRoot = ''
function Get-BackupRoot {
    if (-not $script:backupRoot) {
        $script:backupRoot = Join-Path $env:TEMP ("ChemForge-MenuMount-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        New-Item -ItemType Directory -Force -Path $script:backupRoot | Out-Null
    }
    return $script:backupRoot
}

# 分隔线位置（0 基：插在该下标菜单项**之前**）。必须与 MenuDefinition.Menus 的分组大小一致——
# 当前分组为 文字格式处理(1) / 图块批量处理(1) / 图块编号(1) / 管线与仪表(2) / 关于 ChemForge(1)，
# 共 6 个叶子命令项，而收割顺序就是 MenuDefinition 的分组顺序，故分隔线落在 1/2/3/5。
#
# ⚠ 下标只数**叶子命令项**：MenuDefinition 里的 MenuItemDefinition.Separator(...)（如关于组开头的
#   ID_AboutSep）不会写进 CUIX —— 实测生成的 PopMenuRoot.cui 里所有 PopMenuItem 都是
#   IsSeparator="false"。所以别把 Separator 算进分组大小，否则会多插一条、出现双分隔线。
#
# ⚠ 增删命令项时要**同步重算**这个数组。2026-10-16 加 INSTBREAK 时就漏了：管线与仪表组变成 2 项，
#   数组还停在 @(1,2,3,4)，结果分隔线插到了 PIPEBREAK 与 INSTBREAK 之间，而「关于」前面没有分隔线。
#
# 分隔线只影响观感，增删分组时同步这个数组即可（漏改只会少一条分隔线，不会影响命令可用性）。
$separatorBefore = @(1, 2, 3, 5)

# 工厂分隔线形状（PnP.cuix 里的真实写法：无 UID）
$separatorXml = @"
    <PopMenuItem IsSeparator="true" hasDiesel="false">
      <ModifiedRev MajorVersion="16" MinorVersion="2" UserVersion="0" />
    </PopMenuItem>
"@

function New-FlatPopMenu([string]$alias, [string[]]$itemXml) {
    $b = New-Object System.Text.StringBuilder
    [void]$b.AppendLine("  <PopMenu hasDiesel=`"false`" UID=`"$rootUid`">")
    [void]$b.AppendLine('    <ModifiedRev MajorVersion="23" MinorVersion="0" UserVersion="0" />')
    [void]$b.AppendLine("    <Alias>$alias</Alias>")
    [void]$b.AppendLine("    <Name xlate=`"false`">$menuTitle</Name>")
    [void]$b.AppendLine('    <Description xlate="true" UID="XLS_1256" />')
    $n = 0
    foreach ($it in $itemXml) {
        if ($separatorBefore -contains $n) { [void]$b.AppendLine($separatorXml.TrimEnd()) }
        [void]$b.AppendLine('    ' + $it.Trim())
        $n++
    }
    [void]$b.AppendLine('  </PopMenu>')
    return $b.ToString()
}

# 只比较 XML 结构：忽略标签之间的换行与缩进差异
function Normalize-Xml([string]$s) {
    if ($null -eq $s) { return '' }
    return ([regex]::Replace($s, '>\s+<', '><')).Trim()
}

# 把 WorkspaceRoot.cui 变成「挂载后应该长什么样」。
# 第 2 步（真正写入）与第 0 步（升级预检）共用这一段，避免两处判据漂移。
function Get-WorkspaceMounted([string]$ws, [string]$ribbonTabId, [string[]]$ribbonPanelIds, [bool]$uninstall, [string]$hostGroup) {
    # 组名必须是「被挂载菜单所在组」的真实名字：Plant 3D 是 PNP，纯 AutoCAD 是 ACAD。
    # 写错组名（或写成 CHEMFORGE）菜单栏会丢项 —— 组名由 Get-HostCuix 从宿主文件里读出。
    if (-not $hostGroup) { $hostGroup = 'PNP' }
    $wsPop = @"
      <WSPop MenuGroup="$hostGroup" pUID="$rootUid" Display="1" UID="WSPU_250_CFPNP">
        <ModifiedRev MajorVersion="25" MinorVersion="0" UserVersion="1" />
      </WSPop>
"@

    # 功能区选项卡引用（插到每个工作空间 WSRibbonRoot 的末尾，选项卡排在最后）
    $wsRibbon = ''
    if (-not $uninstall -and $ribbonPanelIds.Count -gt 0) {
        $sb = New-Object System.Text.StringBuilder
        [void]$sb.AppendLine('      <WSRibbonTabSourceReference MenuGroup="CHEMFORGE" TabId="' + $ribbonTabId + '" Show="true" IsActive="false" UID="WSRU_250_CFCHEMFORGE">')
        [void]$sb.AppendLine('        <ModifiedRev MajorVersion="25" MinorVersion="0" UserVersion="1" />')
        $pn = 0
        foreach ($panelId in $ribbonPanelIds) {
            $pn++
            [void]$sb.AppendLine('        <WSRibbonPanelSourceReference PanelId="CHEMFORGE.' + $panelId + '" Orientation="Docked" Show="true" xval="0" yval="0" FloatingGroup="0" FloatingOrder="0" FloatingOrientation="Vertical" PanelResizeOrder="100" UID="WSRP_250_CF' + $pn + '" />')
        }
        [void]$sb.AppendLine('      </WSRibbonTabSourceReference>')
        $wsRibbon = $sb.ToString().TrimEnd()
    }

    # 清掉旧挂载引用与历史遗留的 CHEMFORGE 引用（后者会导致菜单栏丢项）
    $ws = [regex]::Replace($ws, '(?s)[ \t]*<WSPop\b[^>]*pUID="' + $rootUid + '"[^>]*>.*?</WSPop>[ \t]*\r?\n?', '')
    $ws = [regex]::Replace($ws, '(?s)[ \t]*<WSPop\b[^>]*MenuGroup="CHEMFORGE"[^>]*>.*?</WSPop>[ \t]*\r?\n?', '')
    if (-not $uninstall) {
        $eval = { param($m) return $m.Value + "`r`n" + $wsPop }
        # 插入位置：优先 EXPRESS 之后（Plant 3D 的既有位置，行为与改动前逐字一致）；
        # 纯 AutoCAD 没有 EXPRESS 组，退化成「本工作空间的第一个 WSPop 之后」。
        # 只替换 1 处 —— 退化锚点会命中很多 WSPop，全替换会把菜单重复挂进去。
        $anchor = '(?s)(<WSPop\s+MenuGroup="EXPRESS"[^>]*>\s*<ModifiedRev[^>]*/>\s*</WSPop>)'
        if (-not [regex]::IsMatch($ws, $anchor)) {
            $anchor = '(?s)(<WSPop\b[^>]*>\s*<ModifiedRev[^>]*/>\s*</WSPop>)'
        }
        $rx = New-Object System.Text.RegularExpressions.Regex($anchor)
        $ws = $rx.Replace($ws, $eval, 1)
    }
    # 功能区选项卡引用：先清旧（幂等），再插到每个 WSRibbonRoot 末尾
    $ws = [regex]::Replace($ws, '(?s)\r?\n?[ \t]*<WSRibbonTabSourceReference\b[^>]*TabId="' + [regex]::Escape($ribbonTabId) + '"[^>]*>.*?</WSRibbonTabSourceReference>[ \t]*\r?\n?', '')
    $ws = [regex]::Replace($ws, '(?s)\r?\n?[ \t]*<WSRibbonTabSourceReference\b[^>]*MenuGroup="CHEMFORGE"[^>]*>.*?</WSRibbonTabSourceReference>[ \t]*\r?\n?', '')
    if (-not $uninstall -and $wsRibbon) {
        $evalRibbon = { param($m) return "`r`n" + $wsRibbon + $m.Value }
        $ws = [regex]::Replace($ws, '</WSRibbonRoot>', $evalRibbon)
    }
    return $ws
}

# 从「来源 CUIx」收割扁平菜单需要的内容（标签/命令的单一来源）。
# 找不到来源或内容为空时返回 $null，调用方按「不是最新」处理、走原来的重写路径。
function Get-SourceMenuContent([string]$dir) {
    $src = Join-Path $dir 'ChemForge.cuix'
    if (-not (Test-Path $src)) { $src = $CuixSource }
    if (-not (Test-Path $src)) { return $null }
    $srcPop = Read-CuixEntry $src 'PopMenuRoot.cui'
    $srcGrp = Read-CuixEntry $src 'MenuGroup.cui'
    if (-not $srcPop -or -not $srcGrp) { return $null }
    $items = @([regex]::Matches($srcPop, '<PopMenuItem\b[\s\S]*?</PopMenuItem>') | ForEach-Object { $_.Value })
    $macros = @([regex]::Matches($srcGrp, '<MenuMacro\b[\s\S]*?</MenuMacro>') | ForEach-Object { $_.Value })
    if ($items.Count -eq 0 -or $macros.Count -eq 0) { return $null }
    for ($i = 0; $i -lt $items.Count; $i++) {
        $items[$i] = Remove-MenuImageRef ([regex]::Replace($items[$i], 'MenuMacroID="ID_([A-Za-z]+)"', 'MenuMacroID="ID_CF_$1"'))
    }
    for ($i = 0; $i -lt $macros.Count; $i++) {
        $macros[$i] = Remove-MenuImageRef ([regex]::Replace($macros[$i], 'UID="ID_([A-Za-z]+)"', 'UID="ID_CF_$1"'))
    }
    $ribbonTabId = 'CF_RIBBON_TAB_CHEMFORGE'
    $ribbonPanelIds = @()
    $srcRibbon = Read-CuixEntry $src 'RibbonRoot.cui'
    if ($srcRibbon) {
        $tm = [regex]::Match($srcRibbon, '<RibbonTabSource\b[^>]*UID="([^"]+)"')
        if ($tm.Success) { $ribbonTabId = $tm.Groups[1].Value }
        $ribbonPanelIds = @([regex]::Matches($srcRibbon, '<RibbonPanelSource\b[^>]*UID="([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
    }
    return @{ Src = $src; Items = $items; Macros = $macros; RibbonTabId = $ribbonTabId; RibbonPanelIds = $ribbonPanelIds }
}

# ---------- 宿主 CUIX 与它的菜单组名 ----------
# Plant 3D 的主定制文件是 PnP.cuix（组名 PNP），纯 AutoCAD 是 acad.cuix / acad.CUIX（组名 ACAD）。
# 组名一律从文件里读出来，不写死：WSPop 引用与配置文件布局条目都必须用真实组名。
# 找不到宿主文件时返回 $null（该 support 目录不是 AutoCAD 的，跳过）。
function Get-HostCuix([string]$dir) {
    foreach ($name in @('PnP.cuix', 'acad.CUIX', 'acad.cuix')) {
        $p = Join-Path $dir $name
        if (-not (Test-Path $p)) { continue }
        $g = ''
        $grpText = Read-CuixEntry $p 'MenuGroup.cui'
        if ($grpText) {
            $m = [regex]::Match($grpText, '<MenuGroup\b[^>]*\bName="([^"]+)"')
            if ($m.Success) { $g = $m.Groups[1].Value }
        }
        if (-not $g) {
            if ($name -eq 'PnP.cuix') { $g = 'PNP' } else { $g = 'ACAD' }
        }
        return @{ Path = $p; Name = $name; Group = $g }
    }
    return $null
}

# ---------- 升级预检：这个 support 目录里的挂载内容已经和来源一致吗？ ----------
# 一致就整目录跳过 —— 不重写 PnP.cuix、不产生 %TEMP% 备份目录。
# 升级（以及同一版本重装）时这是最常见的情形：菜单内容一个字都没变，
# 却每次都把 PnP.cuix 重写一遍、在 %TEMP% 里堆一个备份目录（2026-10-16 用户要求改掉）。
# 判据是**内容**而不是时间戳 —— 与 MenuService_Net80.MenuFileNeedsRebuild 同一思路。
function Test-MountUpToDate([string]$dir, [ref]$aliasOut) {
    $content = Get-SourceMenuContent $dir
    if ($null -eq $content) { return $false }
    $host_ = Get-HostCuix $dir
    if ($null -eq $host_) { return $false }
    $pnp = $host_.Path
    $pop = Read-CuixEntry $pnp 'PopMenuRoot.cui'
    $grp = Read-CuixEntry $pnp 'MenuGroup.cui'
    if (-not $pop -or -not $grp) { return $false }

    $block = [regex]::Match($pop, '<PopMenu\b[^>]*UID="' + $rootUid + '">[\s\S]*?</PopMenu>')
    if (-not $block.Success) { return $false }
    $am = [regex]::Match($block.Value, '<Alias>(POP\d+)</Alias>')
    if (-not $am.Success) { return $false }

    $curMacros = @([regex]::Matches($grp, '<MenuMacro\b[^>]*UID="ID_CF_[A-Za-z]+">[\s\S]*?</MenuMacro>') | ForEach-Object { $_.Value })
    if ($curMacros.Count -ne $content.Macros.Count) { return $false }

    # 沿用已挂载的那个别名来比较（不然会为了比较而重排别名，白白判成「不是最新」）
    $want = New-FlatPopMenu $am.Groups[1].Value $content.Items
    if ((Normalize-Xml $block.Value) -ne (Normalize-Xml $want)) { return $false }

    $curMacroXml = ($curMacros | ForEach-Object { '    ' + $_.Trim() }) -join "`r`n"
    $wantMacroXml = ($content.Macros | ForEach-Object { '    ' + $_.Trim() }) -join "`r`n"
    if ((Normalize-Xml $curMacroXml) -ne (Normalize-Xml $wantMacroXml)) { return $false }

    # 工作空间引用：每个 CUIx 过一遍与第 2 步完全相同的变换，只要有一个会变就重做
    foreach ($file in (Get-ChildItem $dir -File | Where-Object { $_.Name -match '\.(cuix|cui)$' -and $_.Name -ne 'PnP.bak.cuix' })) {
        $ws = Read-CuixEntry $file.FullName 'WorkspaceRoot.cui'
        if ($null -eq $ws) { continue }
        # 必须用 Normalize-Xml 比较：变换会插入 CRLF 与缩进，裸字符串比较对空白敏感，
        # 会把「语义完全相同」判成「需要重写」，升级跳过就永远不生效（2026-10-16 实测）。
        if ((Normalize-Xml (Get-WorkspaceMounted $ws $content.RibbonTabId $content.RibbonPanelIds $false $host_.Group)) -ne (Normalize-Xml $ws)) { return $false }
    }

    $aliasOut.Value = $am.Groups[1].Value
    return $true
}

$touched = 0
$skipped = 0
$mountedAlias = ''      # 供第 3 步写/删配置文件布局条目使用
$mountedGroup = ''      # 同上：布局条目里写的组名（PNP / ACAD）
foreach ($dir in $supportDirs) {
    $host_ = Get-HostCuix $dir
    if ($null -eq $host_) { continue }
    $pnp = $host_.Path
    $hostGroup = $host_.Group
    $mountedGroup = $hostGroup
    Write-Output ("support 目录：{0}" -f $dir)
    Write-Output ("  宿主 CUIX：{0}（菜单组 {1}）" -f $host_.Name, $hostGroup)

    # ---------- 0) 升级预检：内容已是最新就整目录跳过 ----------
    # 升级（含同版本重装）时菜单内容通常一个字都没变，没必要重写 PnP.cuix、
    # 更没必要在 %TEMP% 里堆一个备份目录。判据是内容，不是时间戳。
    if (-not $Uninstall) {
        $upAlias = ''
        if (Test-MountUpToDate $dir ([ref]$upAlias)) {
            if ($upAlias) { $mountedAlias = $upAlias }
            $skipped++
            Write-Output '  菜单内容已是最新，跳过重写与备份'
            continue
        }
    }

    # 先备份全部 CUIx：ZipFile 以 Update 模式打开时会独占文件，之后 Copy-Item 会失败
    $bk = Get-BackupRoot
    foreach ($f in (Get-ChildItem $dir -File | Where-Object { $_.Name -match '\.(cuix|cui)$' -and $_.Name -ne 'PnP.bak.cuix' })) {
        Copy-Item $f.FullName (Join-Path $bk $f.Name) -Force -ErrorAction SilentlyContinue
    }

    # ---------- 1) 宿主 CUIX：弹出菜单 + 宏 ----------
    $zip = [System.IO.Compression.ZipFile]::Open($pnp, 'Update')
    try {
        $pop = Get-ZipEntryText $zip 'PopMenuRoot.cui'
        $grp = Get-ZipEntryText $zip 'MenuGroup.cui'
        if ($null -eq $pop -or $null -eq $grp) { Write-Warning ('  {0} 缺少 PopMenuRoot.cui / MenuGroup.cui，跳过' -f $host_.Name); continue }

        $hadMounted = $pop.Contains('UID="' + $rootUid + '"')
        # 记下已挂载菜单用的别名（卸载时据此精确删除配置文件布局条目）
        if ($hadMounted) {
            $mountedBlock = [regex]::Match($pop, '<PopMenu\b[^>]*UID="' + $rootUid + '">[\s\S]*?</PopMenu>')
            $aliasMatch = [regex]::Match($mountedBlock.Value, '<Alias>(POP\d+)</Alias>')
            if ($aliasMatch.Success) { $mountedAlias = $aliasMatch.Groups[1].Value }
        }
        # 先移除旧挂载（幂等）
        $pop = [regex]::Replace($pop, '\s*<PopMenu\b[^>]*UID="' + $rootUid + '">[\s\S]*?</PopMenu>', '')
        $grp = [regex]::Replace($grp, '\s*<MenuMacro\b[^>]*UID="ID_CF_[A-Za-z]+">[\s\S]*?</MenuMacro>', '')

        if ($Uninstall) {
            Set-ZipEntryText $zip 'PopMenuRoot.cui' $pop
            Set-ZipEntryText $zip 'MenuGroup.cui' $grp
            if ($hadMounted) { $touched++ }
            # ★ 这里**绝不能**再 Copy-Item 一次备份：备份已在打开 ZIP 之前做过（见上面 foreach），
            #   此刻 $pnp 已经是改后的内容，再拷只会把那份「改前」的备份覆盖掉（旧版就是这个 bug）。
            Write-Output ("  {0}：已移除挂载菜单（原先存在={1}）" -f $host_.Name, $hadMounted)
        }
        else {
            # 收割菜单项与宏（标签/命令单一来源）
            $src = Join-Path $dir 'ChemForge.cuix'
            if (-not (Test-Path $src)) { $src = $CuixSource }
            if (-not (Test-Path $src)) { throw "找不到来源 CUIx（支持目录没有 ChemForge.cuix，也没有 $CuixSource）；请先启动一次 AutoCAD 让插件生成菜单文件。" }

            # 这里刻意**不做**「来源比插件 DLL 新就警告」的自检：第 2 步重写工作空间时已经把来源
            # ChemForge.cuix 的时间戳刷到了本次挂载时刻，此刻它必然比 DLL 新，判据恒真、只会制造噪音。
            # 时间戳陷阱本身（以及改了菜单定义时的正确顺序）写在头部 .PARAMETER CuixSource；
            # 真正的判据是下面打印的「菜单项 N 个」是否等于当前 MenuDefinition 的叶子项数。
            $srcPop = Read-CuixEntry $src 'PopMenuRoot.cui'
            $srcGrp = Read-CuixEntry $src 'MenuGroup.cui'
            $items = @([regex]::Matches($srcPop, '<PopMenuItem\b[\s\S]*?</PopMenuItem>') | ForEach-Object { $_.Value })
            $macros = @([regex]::Matches($srcGrp, '<MenuMacro\b[\s\S]*?</MenuMacro>') | ForEach-Object { $_.Value })
            if ($items.Count -eq 0 -or $macros.Count -eq 0) { throw "来源 CUIx 里没有菜单项或宏：$src" }
            for ($i = 0; $i -lt $items.Count; $i++) {
                $items[$i] = [regex]::Replace($items[$i], 'MenuMacroID="ID_([A-Za-z]+)"', 'MenuMacroID="ID_CF_$1"')
                $items[$i] = Remove-MenuImageRef $items[$i]
            }
            for ($i = 0; $i -lt $macros.Count; $i++) {
                $macros[$i] = [regex]::Replace($macros[$i], 'UID="ID_([A-Za-z]+)"', 'UID="ID_CF_$1"')
                $macros[$i] = Remove-MenuImageRef $macros[$i]
            }

            # 选一个 PNP 组里空闲的低位 <Alias>POPn</Alias>（n < 500）
            $used = @([regex]::Matches($pop, '<Alias>POP(\d+)</Alias>') | ForEach-Object { [int]$_.Groups[1].Value } | Where-Object { $_ -lt 500 })
            $aliasNo = 13
            while ($used -contains $aliasNo) { $aliasNo++ }
            $alias = 'POP' + $aliasNo
            $mountedAlias = $alias
            Write-Output ("  来源 {0}：菜单项 {1} 个、宏 {2} 个；使用别名 {3}" -f (Split-Path $src -Leaf), $items.Count, $macros.Count, $alias)
            Write-Output ("    ↑ 请核对菜单项数 {0} = 当前 MenuDefinition 的叶子项数（对不上就是收割到了旧 CUIx，见头部 .PARAMETER CuixSource 的时间戳陷阱）" -f $items.Count)

            $close = $pop.LastIndexOf('</PopMenuRoot>')
            if ($close -lt 0) { throw ($host_.Name + ' PopMenuRoot.cui 结构异常') }
            $pop = $pop.Substring(0, $close) + (New-FlatPopMenu $alias $items) + $pop.Substring($close)

            $grpClose = $grp.IndexOf('</MacroGroup>')
            if ($grpClose -lt 0) { throw ($host_.Name + ' MenuGroup.cui 里找不到 </MacroGroup>') }
            $macroXml = ($macros | ForEach-Object { '    ' + $_.Trim() }) -join "`r`n"
            $grp = $grp.Substring(0, $grpClose) + $macroXml + "`r`n  " + $grp.Substring($grpClose)

            $touched++
            Set-ZipEntryText $zip 'PopMenuRoot.cui' $pop
            Set-ZipEntryText $zip 'MenuGroup.cui' $grp
            Write-Output ("  {0}：已写入扁平菜单 {1}（{2} 项含分隔线）与 {3} 个宏" -f $host_.Name, $menuTitle, ($items.Count + $separatorBefore.Count), $macros.Count)
        }
    }
    finally { $zip.Dispose() }

    # ---------- 1b) 从插件生成的 CUIx 收割功能区选项卡/面板 UID ----------
    # 选项卡与面板都由插件在 ChemForge.cuix 里声明式定义；这里只把它们的 UID 抄进
    # 工作空间引用（标签/命令/面板布局仍是插件单一来源）。
    $ribbonTabId = 'CF_RIBBON_TAB_CHEMFORGE'
    $ribbonPanelIds = @()
    if (-not $Uninstall) {
        $ribbonSrc = Join-Path $dir 'ChemForge.cuix'
        if (-not (Test-Path $ribbonSrc)) { $ribbonSrc = $CuixSource }
        if (Test-Path $ribbonSrc) {
            $srcRibbon = Read-CuixEntry $ribbonSrc 'RibbonRoot.cui'
            if ($srcRibbon) {
                $tm = [regex]::Match($srcRibbon, '<RibbonTabSource\b[^>]*UID="([^"]+)"')
                if ($tm.Success) { $ribbonTabId = $tm.Groups[1].Value }
                $ribbonPanelIds = @([regex]::Matches($srcRibbon, '<RibbonPanelSource\b[^>]*UID="([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
            }
        }
        if ($ribbonPanelIds.Count -eq 0) {
            Write-Warning '  来源 CUIx 里没有声明式功能区面板，跳过功能区选项卡挂载（插件运行时兜底仍会建选项卡）'
        }
        else {
            Write-Output ("  功能区：选项卡 {0}、面板 {1} 个" -f $ribbonTabId, $ribbonPanelIds.Count)
        }
    }

    # ---------- 2) 所有 CUIx 的工作空间：WSPop + 功能区选项卡引用 ----------
    # 变换逻辑在 Get-WorkspaceMounted 里（第 0 步的预检用的是同一段，判据不会漂移）。
    foreach ($file in (Get-ChildItem $dir -File | Where-Object { $_.Name -match '\.(cuix|cui)$' -and $_.Name -ne 'PnP.bak.cuix' })) {
        $zip = $null
        try { $zip = [System.IO.Compression.ZipFile]::Open($file.FullName, 'Update') } catch { continue }
        try {
            $ws = Get-ZipEntryText $zip 'WorkspaceRoot.cui'
            if ($null -eq $ws) { continue }
            $orig = $ws
            $ws = Get-WorkspaceMounted $ws $ribbonTabId $ribbonPanelIds ([bool]$Uninstall) $hostGroup
            if ($ws -ne $orig) {
                $touched++
                Set-ZipEntryText $zip 'WorkspaceRoot.cui' $ws
                $cnt = ([regex]::Matches($ws, 'pUID="' + $rootUid + '"')).Count
                $rcnt = ([regex]::Matches($ws, 'TabId="' + [regex]::Escape($ribbonTabId) + '"')).Count
                Write-Output ("  {0}：工作空间 WSPop(MenuGroup={1}) x{2}、功能区选项卡引用 x{3}" -f $file.Name, $hostGroup, $cnt, $rcnt)
            }
        }
        finally { $zip.Dispose() }
    }
}

# ---------- 3) 配置文件布局：Pop<下一个连续槽> = <宿主组> pop<n> ----------
# 组名跟着宿主 CUIX 走：Plant 3D 是 PNP，纯 AutoCAD 是 ACAD。
$regGroup = $mountedGroup
if (-not $regGroup) { $regGroup = 'PNP' }
$profilesRoot = 'HKCU:\Software\Autodesk\AutoCAD'
foreach ($ver in (Get-ChildItem $profilesRoot -ErrorAction SilentlyContinue)) {
    foreach ($prod in (Get-ChildItem $ver.PSPath -ErrorAction SilentlyContinue)) {
        $profiles = Join-Path $prod.PSPath 'Profiles'
        if (-not (Test-Path $profiles)) { continue }
        foreach ($prof in (Get-ChildItem $profiles -ErrorAction SilentlyContinue)) {
            $key = Join-Path $prof.PSPath 'Menus'
            if (-not (Test-Path $key)) { continue }
            $it = Get-Item $key
            $names = $it.GetValueNames()
            $maxSlot = 0
            foreach ($n in $names) {
                if ($n -match '^[Pp]op(\d+)$' -and [int]$Matches[1] -lt 500 -and [int]$Matches[1] -gt $maxSlot) { $maxSlot = [int]$Matches[1] }
            }
            # 迁移：删掉会导致丢项的 CHEMFORGE 连续块条目
            foreach ($n in $names) {
                if ($n -match '^[Pp]op(\d+)$' -and [int]$Matches[1] -lt 500 -and ($it.GetValue($n) -match '^CHEMFORGE')) {
                    Remove-ItemProperty -Path $key -Name $n -ErrorAction SilentlyContinue
                    Write-Output ("  {0}：删除历史遗留布局条目 {1} = {2}" -f $prof.PSChildName, $n, $it.GetValue($n))
                }
            }
            if ($Uninstall) {
                if ($mountedAlias) {
                    $num = ($mountedAlias -replace '\D', '')
                    $want = '^' + [regex]::Escape($regGroup) + '\s+pop' + $num + '$'
                    foreach ($n in (Get-Item $key).GetValueNames()) {
                        if ($n -notmatch '^Pop5\d\d$' -and (Get-Item $key).GetValue($n) -match $want) {
                            Remove-ItemProperty -Path $key -Name $n -ErrorAction SilentlyContinue
                            Write-Output ("  {0}：已删除布局条目 {1} = {2} pop{3}" -f $prof.PSChildName, $n, $regGroup, $num)
                        }
                    }
                }
                else {
                    Write-Output ("  {0}：未发现已挂载菜单，跳过布局条目清理" -f $prof.PSChildName)
                }
            }
            else {
                if (-not $mountedAlias) { continue }
                $num = ($mountedAlias -replace '\D', '')
                $want = '^' + [regex]::Escape($regGroup) + '\s+pop' + $num + '$'
                $keyItem = Get-Item $key
                $existing = @($keyItem.GetValueNames() | Where-Object { $_ -notmatch '^Pop5\d\d$' -and $keyItem.GetValue($_) -match $want })
                if ($existing.Count -gt 0) {
                    Write-Output ("  {0}：布局条目已存在（{1} = {2} pop{3}），跳过" -f $prof.PSChildName, ($existing -join ','), $regGroup, $num)
                }
                else {
                    $slot = 'Pop' + ($maxSlot + 1)
                    Set-ItemProperty -Path $key -Name $slot -Value ($regGroup + ' pop' + $num)
                    Write-Output ("  {0}：写入布局条目 {1} = {2}" -f $prof.PSChildName, $slot, (Get-Item $key).GetValue($slot))
                }
            }
        }
    }
}

Write-Output ''
$skipNote = ''
if ($skipped -gt 0) { $skipNote = ("，{0} 个目录内容已是最新、原样跳过" -f $skipped) }
if ($Uninstall) {
    Write-Output ("已撤销 ChemForge 菜单挂载（改动 {0} 个文件{1}）" -f $touched, $skipNote)
}
else {
    Write-Output ("已完成 ChemForge 菜单挂载（改动 {0} 个文件{1}）" -f $touched, $skipNote)
    Write-Output '下次启动 AutoCAD 后，经典菜单栏（MENUBAR=1）会出现 ChemForge，切换工作空间不掉。'
}
if ($backupRoot) {
    Write-Output ("备份目录：{0}" -f $backupRoot)
}
else {
    Write-Output '本次没有任何文件被改写，未创建备份目录。'
}
