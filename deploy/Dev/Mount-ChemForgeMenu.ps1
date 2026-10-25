<#
.SYNOPSIS
    把 ChemForge 功能区选项卡引用挂进宿主 CUIx 的工作空间，并清理历史挂载残留。

    宿主文件与组名**按支持目录自动判定**（2026-10-16 起支持纯 AutoCAD）：
      * Plant 3D 垂直产品：宿主 = PnP.cuix，组名 = PNP；
      * 纯 AutoCAD（含 2019-2024）：宿主 = acad.cuix / acad.CUIX，组名 = ACAD。
    组名从宿主文件的 MenuGroup.cui 里读出来，不写死。

.DESCRIPTION
    2026-10-23 定案分工（本机 AutoCAD Plant 3D 2025 逐条实测定案）：

    **经典菜单栏**（本脚本已不负责，由 Register-ChemForgeMenu.ps1 + 插件完成）：
      * ChemForge.cuix 作为局部菜单登记进宿主 Header.cui（PartialMenuFile）；
      * 根菜单带 POP16 别名 + 宿主每个工作空间写入
        <WSPop MenuGroup="CHEMFORGE" pUID="ID_ChemForgeMenu">（与 Express 逐字同构）；
      * 根菜单为**扁平结构**（直接 PopMenuItem）。三个事实都有实测依据：
        ① POPn 别名只在 CUIx 加载那一刻自动上栏，切工作空间重建菜单栏按 WSPop 列表来，
          缺引用就掉、切回来不回来；
        ② 子项全是 <PopMenuRef> 的弹出菜单 AutoCAD 2025 永远不画（COM 模型在、画面不渲染）；
        ③ 注册表 Menus 只是 AutoCAD 自己重写的镜像，脚本写布局条目无源作用。

    **功能区选项卡**（本脚本唯一职责）：
      各工作空间 WorkspaceRoot.cui 里加 <WSRibbonTabSourceReference MenuGroup="CHEMFORGE"
      TabId="CF_RIBBON_TAB_CHEMFORGE">（含各面板的 WSRibbonPanelSourceReference）——这是
      "功能区选项卡与自带选项卡一起、开 CAD 就出现、切换工作空间不丢"的正路：Express
      Tools 就是这么做的（局部菜单 acetmain.cuix 里的 RibbonTabSource 被宿主 CUIX 的 9 个
      工作空间逐个引用）。只声明 RibbonTabSource 而不写工作空间引用时，AutoCAD 会把它
      读进来但不保证显示。面板按 "<组名>.<面板UID>" 引用，故插件生成 CUIx 时面板 UID
      必须是稳定值（CF_PANEL_*），否则每次重新生成都会让引用失效。

    安装时只写功能区引用；不再向宿主组写扁平菜单/宏、不再写注册表布局条目
    （避免菜单栏出现第二个 ChemForge、避免与 AutoCAD 的镜像机制抢键值）。

.PARAMETER Uninstall
    撤销：删除功能区选项卡引用，并清理历史遗留的扁平菜单（ID_CFPNP_ROOT）、
    ID_CF_* 宏、宿主组 WSPop（pUID=ID_CFPNP_ROOT）与配置文件布局条目。
    （CHEMFORGE 组的 WSPop 与 PartialMenuFile 由 Register-ChemForgeMenu.ps1 清理。）

.PARAMETER CuixSource
    收割功能区选项卡/面板 UID 的来源 CUIx；默认为支持目录里的 ChemForge.cuix
    （插件生成的），找不到时回退到 deploy\Dev\ChemForge.cuix（仓库引导副本）。

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
#
# 2026-10-24：备份根从 %TEMP% 挪到 %LOCALAPPDATA%\ChemForge\MenuBackup。
# %TEMP% 会被清理工具删掉，插件卸载时「恢复原样」就没底稿了；这里与插件日志/配置同根，
# 卸载脚本能直接找到最近一次改动前的 PnP.cuix 快照。
$backupRoot = ''
function Get-BackupRoot {
    if (-not $script:backupRoot) {
        $base = Join-Path $env:LOCALAPPDATA 'ChemForge\MenuBackup'
        $script:backupRoot = Join-Path $base ("MenuMount-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
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

    # 功能区选项卡引用（插到每个工作空间 WSRibbonRoot 的末尾，选项卡排在最后）
    #
    # ★ UID 必须**每个工作空间各自唯一**：宿主 PnP.cuix 里 107 条 WSRibbonTabSourceReference
    #   对应 107 个互不相同的 UID（Express 的 RBN_0047 在 9 个工作空间各有一份、各自不同 UID）。
    #   9 个工作空间共用同一个 UID 时，工作空间/重写逻辑只认第一条，其余工作空间拿不到选项卡。
    #   后缀用「出现次序」而不是随机值：顺序稳定 ⇒ 重复运行结果逐字相同，仍然是幂等的。
    $wsRibbonFactory = $null
    if (-not $uninstall -and $ribbonPanelIds.Count -gt 0) {
        $wsRibbonFactory = {
            param($match)
            $script:wsRibbonIndex = [int]$script:wsRibbonIndex + 1
            $i = [int]$script:wsRibbonIndex
            $sb = New-Object System.Text.StringBuilder
            [void]$sb.AppendLine('      <WSRibbonTabSourceReference MenuGroup="CHEMFORGE" TabId="' + $ribbonTabId + '" Show="true" IsActive="false" UID="WSRU_250_CF' + $i + '">')
            [void]$sb.AppendLine('        <ModifiedRev MajorVersion="25" MinorVersion="0" UserVersion="1" />')
            $pn = 0
            foreach ($panelId in $ribbonPanelIds) {
                $pn++
                [void]$sb.AppendLine('        <WSRibbonPanelSourceReference PanelId="CHEMFORGE.' + $panelId + '" Orientation="Docked" Show="true" xval="0" yval="0" FloatingGroup="0" FloatingOrder="0" FloatingOrientation="Vertical" PanelResizeOrder="100" UID="WSRP_250_CF' + $i + '_' + $pn + '" />')
            }
            [void]$sb.AppendLine('      </WSRibbonTabSourceReference>')
            return "`r`n" + $sb.ToString().TrimEnd() + "`r`n" + $match.Value
        }
    }

    # 清掉历史扁平方案的挂载引用（pUID 指向宿主组里的 ID_CFPNP_ROOT）。
    # ★ 2026-10-23 起 CHEMFORGE 工作空间 WSPop 由 Register-ChemForgeMenu.ps1 负责写，
    #   本脚本**不得**再删它 —— 旧版这里会把它一起抹掉，等于每次跑安装脚本都把菜单栏条目清空。
    $ws = [regex]::Replace($ws, '(?s)[ \t]*<WSPop\b[^>]*pUID="' + $rootUid + '"[^>]*>.*?</WSPop>[ \t]*\r?\n?', '')
    # 扁平挂载已废弃：不再往工作空间里插 MenuGroup="<宿主组>" 的 WSPop（那条引用会被 AutoCAD
    # 重写 CUIx 时丢掉、变成悬空引用，见 docs/DeveloperGuide/MenuMounting-Reference.md）。
    # 功能区选项卡引用：先清旧（幂等），再插到每个 WSRibbonRoot 末尾
    $ws = [regex]::Replace($ws, '(?s)\r?\n?[ \t]*<WSRibbonTabSourceReference\b[^>]*TabId="' + [regex]::Escape($ribbonTabId) + '"[^>]*>.*?</WSRibbonTabSourceReference>[ \t]*\r?\n?', '')
    $ws = [regex]::Replace($ws, '(?s)\r?\n?[ \t]*<WSRibbonTabSourceReference\b[^>]*MenuGroup="CHEMFORGE"[^>]*>.*?</WSRibbonTabSourceReference>[ \t]*\r?\n?', '')
    if ($wsRibbonFactory) {
        $script:wsRibbonIndex = 0
        $ws = [regex]::Replace($ws, '</WSRibbonRoot>', $wsRibbonFactory)
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
# 2026-10-23 起本脚本只负责**功能区选项卡的工作空间引用**（经典菜单栏由
# Register-ChemForgeMenu.ps1 的 CHEMFORGE WSPop 负责），预检判据 = 每个 CUIx
# 的 WorkspaceRoot 经 Get-WorkspaceMounted（ribbon-only）变换后与现状逐字一致。
# 一致就整目录跳过 —— 不重写 CUIx、不产生 %TEMP% 备份目录。
function Test-MountUpToDate([string]$dir) {
    $ribbonTabId = 'CF_RIBBON_TAB_CHEMFORGE'
    $ribbonPanelIds = @()
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
    if ($ribbonPanelIds.Count -eq 0) { return $false }

    foreach ($file in (Get-ChildItem $dir -File | Where-Object { $_.Name -match '\.(cuix|cui)$' -and $_.Name -ne 'PnP.bak.cuix' })) {
        $ws = Read-CuixEntry $file.FullName 'WorkspaceRoot.cui'
        if ($null -eq $ws) { continue }
        # 必须用 Normalize-Xml 比较：变换会插入 CRLF 与缩进，裸字符串比较对空白敏感，
        # 会把「语义完全相同」判成「需要重写」，升级跳过就永远不生效（2026-10-16 实测）。
        if ((Normalize-Xml (Get-WorkspaceMounted $ws $ribbonTabId $ribbonPanelIds $false 'PNP')) -ne (Normalize-Xml $ws)) { return $false }
    }
    return $true
}

function Get-DefaultWorkspaceName([string]$dir) {
    # 宿主 CUIx 里标了 DefaultWorkspace="true" 的工作空间名，就是 AutoCAD 在没设
    # WSCURRENT 时会自动应用的那个；显式把它写进 WSCURRENT，等于让它按名字应用一次。
    foreach ($f in @('acad.CUIX', 'PnP.cuix')) {
        $p = Join-Path $dir $f
        if (-not (Test-Path $p)) { continue }
        $ws = Read-CuixEntry $p 'WorkspaceRoot.cui'
        if (-not $ws) { continue }
        $m = [regex]::Match($ws, '<Workspace\b[^>]*DefaultWorkspace="true"[^>]*>[\s\S]{0,800}?<Name[^>]*>([^<]+)</Name>')
        if ($m.Success) { return $m.Groups[1].Value.Trim() }
    }
    return ''
}

function Update-ProfileWorkspaceCurrent([bool]$uninstall, [string]$dir) {
    # 2026-10-24：安装/卸载全程不启动 CAD、不抢焦点（用户明确要求）。
    # 宿主的规矩是「启动时 WSCURRENT 有值 → 按该工作空间名应用引用」；为空时只恢复
    # 上一次会话快照，CUIx 里新写的 WSRibbonTabSourceReference 不会被采用。
    # 所以这里只做一件事：WSCURRENT 为空就补上默认工作空间名；卸载时还原。
    $stateFile = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'ChemForge\ws-current-orig.json'
    $profilesRoot = 'HKCU:\Software\Autodesk\AutoCAD'

    if ($uninstall) {
        if (-not (Test-Path $stateFile)) { return }
        $saved = Get-Content $stateFile -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($p in $saved.PSObject.Properties) {
            $g = Join-Path $p.Name 'General'
            if (-not (Test-Path $g)) { continue }
            Set-ItemProperty -Path $g -Name 'WSCURRENT' -Value ([string]$p.Value) -ErrorAction SilentlyContinue
            Write-Output ("  {0}：WSCURRENT 已还原为 '{1}'" -f (Split-Path $p.Name -Leaf), $p.Value)
        }
        Remove-Item $stateFile -Force -ErrorAction SilentlyContinue
        return
    }

    $default = Get-DefaultWorkspaceName $dir
    if (-not $default) { $default = '草图与注释' }
    $saved = @{}
    foreach ($ver in (Get-ChildItem $profilesRoot -ErrorAction SilentlyContinue)) {
        foreach ($prod in (Get-ChildItem $ver.PSPath -ErrorAction SilentlyContinue)) {
            $profiles = Join-Path $prod.PSPath 'Profiles'
            if (-not (Test-Path $profiles)) { continue }
            foreach ($prof in (Get-ChildItem $profiles -ErrorAction SilentlyContinue)) {
                $g = Join-Path $prof.PSPath 'General'
                if (-not (Test-Path $g)) { continue }
                $cur = [string](Get-Item $g).GetValue('WSCURRENT')
                if ($cur -and $cur.Trim()) { continue }
                $saved[$prof.PSPath] = $cur
                Set-ItemProperty -Path $g -Name 'WSCURRENT' -Value $default
                Write-Output ("  {0}：WSCURRENT 原为空，已写为 '{1}'（下次启动由宿主自己应用工作空间引用）" -f $prof.PSChildName, $default)
            }
        }
    }
    if ($saved.Count -gt 0) {
        New-Item -ItemType Directory -Force -Path (Split-Path $stateFile) | Out-Null
        ($saved | ConvertTo-Json) | Set-Content -Path $stateFile -Encoding UTF8
    }
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
    # 升级（含同版本重装）时菜单内容通常一个字都没变，没必要重写 CUIx、
    # 更没必要在 %TEMP% 里堆一个备份目录。判据是内容，不是时间戳。
    if (-not $Uninstall) {
        if (Test-MountUpToDate $dir) {
            $skipped++
            Write-Output '  功能区选项卡引用已是最新，跳过重写与备份'
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
            # 2026-10-23 定案：扁平菜单挂载已**废弃**。经典菜单栏的正确机制（本机 AutoCAD
            # 2025 逐条实测定案，缺一不可）是——CHEMFORGE 局部菜单（ChemForge.cuix，插件
            # 运行时生成）+ 根菜单 POP16 别名 + Register-ChemForgeMenu.ps1 写进宿主 CUIx
            # 每个工作空间的 <WSPop MenuGroup="CHEMFORGE" pUID="ID_ChemForgeMenu">。
            # 关键事实：① POPn 别名只在 CUIx 加载那一刻自动上栏，切工作空间重建菜单栏
            # 时按 WSPop 列表来，缺引用就掉；② 根菜单子项全是 <PopMenuRef> 时 AutoCAD
            # 2025 永远不画（模型在、画面不渲染），必须扁平直挂 PopMenuItem。
            # 故本脚本不再向宿主组写扁平菜单与宏（避免菜单栏出现第二个 ChemForge），
            # 安装侧只保留下方第 1b/2 步的功能区选项卡工作空间引用。
            Write-Output '  扁平菜单挂载已废弃（2026-10-23）：经典菜单栏由 CHEMFORGE 局部菜单 + POP16 + Register 的 WSPop 负责，本脚本跳过写宿主组。'
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
                # 2026-10-23：安装侧不再写配置文件布局条目。注册表 Menus 只是 AutoCAD
                # 自己维护的镜像（本机实测：每次启动/切工作空间都被重写），脚本写入既
                # 无源作用又会与自动机制抢键值；经典菜单栏的持久化由 WSPop 保证。
                # 保留上方两条清理：历史 CHEMFORGE 连续块条目（迁移）与卸载清理。
                continue
            }
        }
    }
}

# ---------- 4) 工作空间触发：只改注册表，不启动 CAD、不碰界面 ----------
# 见 Update-ProfileWorkspaceCurrent 的注释：WSCURRENT 为空时宿主不会应用工作空间引用。
$firstDir = ''
if ($supportDirs -and $supportDirs.Count -gt 0) { $firstDir = $supportDirs[0] }
Update-ProfileWorkspaceCurrent ([bool]$Uninstall) $firstDir

Write-Output ''$skipNote = ''
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
