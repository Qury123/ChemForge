<#
.SYNOPSIS
    把 ChemForge 菜单**以能真正画出来的形态**挂进 AutoCAD 出厂主定制文件（PnP.cuix / PNP 组）。

.DESCRIPTION
    为什么不用"独立 CHEMFORGE 组 + 局部菜单注册"那条路（实测结论，Plant 3D 2025）：
      * 局部菜单（ChemForge.cuix）能加载、菜单组也在，但经典菜单栏**只在启动构建菜单栏那一次**
        由 AutoCAD 把工作空间 WSPop 引用的弹出菜单追加并画出；切换工作空间/重建菜单栏即丢失；
      * 一旦配置文件布局里存在 `Pop<n> = CHEMFORGE ...` 条目，AutoCAD 恢复菜单栏时还会报
        "不能加载菜单项: CHEMFORGE:ChemForge" 并丢弃该项（COM 里有、界面没有）；
      * 嵌套子菜单（根菜单 + <PopMenuRef>）挂在 PNP 组里能加载但**不会被画出来**。

    实测能稳定画出的形态（重启、切换工作空间都在）——本脚本就是把这三件事写进文件：
      1) 在 **PnP.cuix** 的 PopMenuRoot.cui 里放一个**扁平**弹出菜单（无子菜单，用分隔线分组）：
         UID=ID_CFPNP_ROOT、<Alias>POPn</Alias>、字面名 `<Name xlate="false">ChemForge</Name>`、
         工厂同款头部；菜单项从插件生成的 ChemForge.cuix 收割（标签/命令单一来源），
         宏引用改写成 MenuMacroID="ID_CF_*"，宏定义同时写进 PnP.cuix 的 MenuGroup.cui；
      2) 各工作空间 WorkspaceRoot.cui 里加 `<WSPop MenuGroup="PNP" pUID="ID_CFPNP_ROOT">`
         ——**组名必须是被挂载菜单所在组的真实名字 PNP**（写 ACAD/CHEMFORGE 都不生效）；
      3) 配置文件布局 `<配置>\Menus` 里写 `Pop<下一个连续槽> = PNP pop<n>`
         ——槽号必须落在 Pop1..PopN **连续块**内，缺号会让其后的条目全部被忽略。

      4) 各工作空间 WorkspaceRoot.cui 里加 `<WSRibbonTabSourceReference MenuGroup="CHEMFORGE"
         TabId="CF_RIBBON_TAB_CHEMFORGE">`（含各面板的 WSRibbonPanelSourceReference）
         ——**这是"功能区选项卡与自带选项卡一起、开 CAD 就出现、切换工作空间不丢"的正路**：
         Express Tools 就是这么做的（局部菜单 acetmain.cuix 里的 RibbonTabSource UID=RBN_0047，
         被 PnP.cuix 的 9 个工作空间逐个引用）。只声明 RibbonTabSource 而不写工作空间引用时，
         AutoCAD 会把它读进来但**不显示**，只能靠插件运行时补建（表现为"先出默认选项卡，
         过一会儿 ChemForge 才刷新出来"）。面板按 "<组名>.<面板UID>" 引用，故插件生成 CUIx 时
         面板 UID 必须是稳定值（CF_PANEL_*），否则每次重新生成都会让引用失效。

.PARAMETER Uninstall
    撤销：删掉挂载的弹出菜单、宏、WSPop、功能区选项卡引用与布局条目
    （同时清理历史遗留的 CHEMFORGE 连续块条目）。

.PARAMETER CuixSource
    收割菜单项/宏的来源 CUIx；默认为支持目录里的 ChemForge.cuix（插件生成的），
    找不到时回退到 deploy\Dev\ChemForge.cuix（仓库引导副本）。

    ⚠ 时间戳陷阱（2026-10-13 实际踩过，改菜单定义时必看）：
      支持目录那份 ChemForge.cuix 只有在插件**重新生成**过之后才等于当前菜单定义。
      本脚本第 2/4 步会用 ZipFile 'Update' 重写每个 .cuix/.cui 的工作空间，这一写会把
      ChemForge.cuix 的 LastWriteTimeUtc 刷新到挂载时刻；而插件判断「要不要重建菜单文件」
      用的是一个更朴素的时间戳判据（src\Shared\CAD\Impl\MenuService_Net80.cs:237-249
      IsOlderThanPlugin：cuix 时间 < 插件 DLL 时间 → 才重建）。于是「编译 → 挂载」之后
      CUIx 恒比 DLL 新，插件永远跳过重建，**改了 MenuDefinition 也传不进 CUIx**：挂载收割到
      的还是旧菜单，表现为菜单栏纹丝不动（这次就是这么被坑的）。
      改了菜单定义时的正确顺序：
        ① 重新编译并部署到本机（本机部署脚本）；
        ② 把支持目录的 ChemForge.cuix（连带 chemforge.mnr / ChemForge_light.mnr）删掉或改名；
        ③ 启动 AutoCAD 一次 —— 文件不存在，插件会重建权威版本；
        ④ 完全退出 AutoCAD，再跑本脚本挂载。
      挂载前请核对本脚本打印的「菜单项 N 个」= 当前 MenuDefinition 的叶子项数。

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

$backupRoot = Join-Path $env:TEMP ("ChemForge-MenuMount-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null

# 分隔线位置（0 基：插在该下标菜单项**之前**）。必须与 MenuDefinition.Menus 的分组大小一致——
# 当前分组为 文字格式处理(1) / 图块批量处理(1) / 图块编号(1) / 管线与仪表(1) / 关于 ChemForge(1)，
# 五组各 1 个叶子命令项（共 5 项），而收割顺序就是 MenuDefinition 的分组顺序，故分隔线落在 1/2/3/4。
#
# ⚠ 下标只数**叶子命令项**：MenuDefinition 里的 MenuItemDefinition.Separator(...)（如关于组开头的
#   ID_AboutSep）不会写进 CUIX —— 实测生成的 PopMenuRoot.cui 里所有 PopMenuItem 都是
#   IsSeparator="false"。所以别把 Separator 算进分组大小，否则会多插一条、出现双分隔线。
#
# 分隔线只影响观感，增删分组时同步这个数组即可（漏改只会少一条分隔线，不会影响命令可用性）。
$separatorBefore = @(1, 2, 3, 4)

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

$touched = 0
$mountedAlias = ''      # 供第 3 步写/删配置文件布局条目使用
foreach ($dir in $supportDirs) {
    $pnp = Join-Path $dir 'PnP.cuix'
    if (-not (Test-Path $pnp)) { continue }
    Write-Output ("support 目录：{0}" -f $dir)

    # 先备份全部 CUIx：ZipFile 以 Update 模式打开时会独占文件，之后 Copy-Item 会失败
    foreach ($f in (Get-ChildItem $dir -File | Where-Object { $_.Name -match '\.(cuix|cui)$' -and $_.Name -ne 'PnP.bak.cuix' })) {
        Copy-Item $f.FullName (Join-Path $backupRoot $f.Name) -Force -ErrorAction SilentlyContinue
    }

    # ---------- 1) PnP.cuix：弹出菜单 + 宏 ----------
    $zip = [System.IO.Compression.ZipFile]::Open($pnp, 'Update')
    try {
        $pop = Get-ZipEntryText $zip 'PopMenuRoot.cui'
        $grp = Get-ZipEntryText $zip 'MenuGroup.cui'
        if ($null -eq $pop -or $null -eq $grp) { Write-Warning '  PnP.cuix 缺少 PopMenuRoot.cui / MenuGroup.cui，跳过'; continue }

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
            Write-Output ("  PnP.cuix：已移除挂载菜单（原先存在={0}）" -f $hadMounted)
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
            if ($close -lt 0) { throw 'PnP.cuix PopMenuRoot.cui 结构异常' }
            $pop = $pop.Substring(0, $close) + (New-FlatPopMenu $alias $items) + $pop.Substring($close)

            $grpClose = $grp.IndexOf('</MacroGroup>')
            if ($grpClose -lt 0) { throw 'PnP.cuix MenuGroup.cui 里找不到 </MacroGroup>' }
            $macroXml = ($macros | ForEach-Object { '    ' + $_.Trim() }) -join "`r`n"
            $grp = $grp.Substring(0, $grpClose) + $macroXml + "`r`n  " + $grp.Substring($grpClose)

            $touched++
            Set-ZipEntryText $zip 'PopMenuRoot.cui' $pop
            Set-ZipEntryText $zip 'MenuGroup.cui' $grp
            Write-Output ("  PnP.cuix：已写入扁平菜单 {0}（{1} 项含分隔线）与 {2} 个宏" -f $menuTitle, ($items.Count + $separatorBefore.Count), $macros.Count)
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
    $wsPop = @"
      <WSPop MenuGroup="PNP" pUID="$rootUid" Display="1" UID="WSPU_250_CFPNP">
        <ModifiedRev MajorVersion="25" MinorVersion="0" UserVersion="1" />
      </WSPop>
"@

    # 功能区选项卡引用（插到每个工作空间 WSRibbonRoot 的末尾，选项卡排在最后）
    $wsRibbon = ''
    if (-not $Uninstall -and $ribbonPanelIds.Count -gt 0) {
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
    foreach ($file in (Get-ChildItem $dir -File | Where-Object { $_.Name -match '\.(cuix|cui)$' -and $_.Name -ne 'PnP.bak.cuix' })) {
        $zip = $null
        try { $zip = [System.IO.Compression.ZipFile]::Open($file.FullName, 'Update') } catch { continue }
        try {
            $ws = Get-ZipEntryText $zip 'WorkspaceRoot.cui'
            if ($null -eq $ws) { continue }
            $orig = $ws
            # 清掉旧挂载引用与历史遗留的 CHEMFORGE 引用（后者会导致菜单栏丢项）
            $ws = [regex]::Replace($ws, '(?s)[ \t]*<WSPop\b[^>]*pUID="' + $rootUid + '"[^>]*>.*?</WSPop>[ \t]*\r?\n?', '')
            $ws = [regex]::Replace($ws, '(?s)[ \t]*<WSPop\b[^>]*MenuGroup="CHEMFORGE"[^>]*>.*?</WSPop>[ \t]*\r?\n?', '')
            if (-not $Uninstall) {
                $eval = { param($m) return $m.Value + "`r`n" + $wsPop }
                $ws = [regex]::Replace($ws, '(?s)(<WSPop\s+MenuGroup="EXPRESS"[^>]*>\s*<ModifiedRev[^>]*/>\s*</WSPop>)', $eval)
            }
            # 功能区选项卡引用：先清旧（幂等），再插到每个 WSRibbonRoot 末尾
            $ws = [regex]::Replace($ws, '(?s)\r?\n?[ \t]*<WSRibbonTabSourceReference\b[^>]*TabId="' + [regex]::Escape($ribbonTabId) + '"[^>]*>.*?</WSRibbonTabSourceReference>[ \t]*\r?\n?', '')
            $ws = [regex]::Replace($ws, '(?s)\r?\n?[ \t]*<WSRibbonTabSourceReference\b[^>]*MenuGroup="CHEMFORGE"[^>]*>.*?</WSRibbonTabSourceReference>[ \t]*\r?\n?', '')
            if (-not $Uninstall -and $wsRibbon) {
                $evalRibbon = { param($m) return "`r`n" + $wsRibbon + $m.Value }
                $ws = [regex]::Replace($ws, '</WSRibbonRoot>', $evalRibbon)
            }
            if ($ws -ne $orig) {
                $touched++
                Set-ZipEntryText $zip 'WorkspaceRoot.cui' $ws
                $cnt = ([regex]::Matches($ws, 'pUID="' + $rootUid + '"')).Count
                $rcnt = ([regex]::Matches($ws, 'TabId="' + [regex]::Escape($ribbonTabId) + '"')).Count
                Write-Output ("  {0}：工作空间 WSPop(MenuGroup=PNP) x{1}、功能区选项卡引用 x{2}" -f $file.Name, $cnt, $rcnt)
            }
        }
        finally { $zip.Dispose() }
    }
}

# ---------- 3) 配置文件布局：Pop<下一个连续槽> = PNP pop<n> ----------
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
                    $want = '^PNP\s+pop' + $num + '$'
                    foreach ($n in (Get-Item $key).GetValueNames()) {
                        if ($n -notmatch '^Pop5\d\d$' -and (Get-Item $key).GetValue($n) -match $want) {
                            Remove-ItemProperty -Path $key -Name $n -ErrorAction SilentlyContinue
                            Write-Output ("  {0}：已删除布局条目 {1} = PNP pop{2}" -f $prof.PSChildName, $n, $num)
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
                $want = '^PNP\s+pop' + $num + '$'
                $keyItem = Get-Item $key
                $existing = @($keyItem.GetValueNames() | Where-Object { $_ -notmatch '^Pop5\d\d$' -and $keyItem.GetValue($_) -match $want })
                if ($existing.Count -gt 0) {
                    Write-Output ("  {0}：布局条目已存在（{1} = PNP pop{2}），跳过" -f $prof.PSChildName, ($existing -join ','), $num)
                }
                else {
                    $slot = 'Pop' + ($maxSlot + 1)
                    Set-ItemProperty -Path $key -Name $slot -Value ('PNP pop' + $num)
                    Write-Output ("  {0}：写入布局条目 {1} = {2}" -f $prof.PSChildName, $slot, (Get-Item $key).GetValue($slot))
                }
            }
        }
    }
}

Write-Output ''
if ($Uninstall) {
    Write-Output ("已撤销 ChemForge 菜单挂载（改动 {0} 个文件），备份目录：{1}" -f $touched, $backupRoot)
}
else {
    Write-Output ("已完成 ChemForge 菜单挂载（改动 {0} 个文件），备份目录：{1}" -f $touched, $backupRoot)
    Write-Output '下次启动 AutoCAD 后，经典菜单栏（MENUBAR=1）会出现 ChemForge，切换工作空间不掉。'
}
