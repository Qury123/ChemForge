# CHANGELOG

所有 ChemForge 项目的显著变更均记录于此文件。

格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
版本号遵循 [Semantic Versioning](https://semver.org/lang/zh-CN/)。

## [1.2.0] - 2026-10-19

### Added
- **批量改色 TCOLOR 跑完弹「处理结果」提示框**
  （2026-10-19 需求：命令执行完成后，除命令行输出外另需以弹窗呈现处理结果，形式与 `BLKATTCHANGE`
  的收尾提示保持一致）。
  ① **弹的就是 AutoCAD 原生提示框**（`Application.ShowAlertDialog`，即 BLKATTCHANGE 收尾那个
  「AutoCAD 消息」窗口）。内容是**短版报告**：结论、目标颜色、范围、跳过统计（有才写）、撤销提示 ——
  与 BLKATTCHANGE 的收尾提示同一个体量；完整报告（样例、核对信息、刷新提示）仍打印在命令行里。
  ② **短版报告 `TextColorReport.BuildBrief()`**：与完整报告 `Build()` 共用同一批拼装函数，
  开头三行逐字一致，只砍掉样例/核对/REGEN 提示（`Build()` 的输出保持不变）。
  ③ **`CommandHost.ShowAlertSafely` 改为返回 bool**：弹失败（异常）时返回 false 并写日志
  「ShowAlertDialog 失败：…」；TCOLOR 据此兜底弹自建窗体 `TextColorResultForm`
  （一段短文字 + 一个「确定」，窗口大小跟着文字走）—— 保证即使原生提示框失败，
  命令行之外仍有可见的处理结果，不会出现「点击确定后无任何反馈」的情况。
  ④ **实现取舍**：早前依据「`acmgd.dll` 中检索不到 `ShowAlertDialog`」与「插件日志里开始执行与完成仅相隔
  1 秒」两点，判断该方法在 AutoCAD 2025 上不可用，遂改用自建窗体，并逐步扩展为白底、蓝色大标题、
  大 logo 的品牌版式。实际 `ShowAlertDialog` 由 `accoremgd` 的 `Core.Application` 定义、经继承调用，
  工作正常，`BLKATTCHANGE` 一直使用该方法；品牌版式因信息量与视觉权重过大，最终回到原生提示框。
  教训：**不能以「检索不到方法名字符串」作为「API 不可用」的依据**，同机另一个命令正在使用同一个
  API 即为反证；日志时间差只能说明未等待交互，不能说明未弹出。
  ⑤ **回归**：`TextColorReportTests` 5 个用例（短版只留结论、跳过统计不丢、与完整报告逐字一致、
  完整报告仍带样例/核对/消息、null 不抛）+ `TextColorResultFormTests` 4 个用例
  （只有「确定」一个按钮、无大标题、尺寸跟着文字走、超长行折行、换行原样保留、空正文不抛），
  单元测试 **79/79 通过**。

### Changed
- **面板版式参数集中至 `DialogStyle`**
  品牌色、8px 窗体边距、组间 6px、按钮高 28 及 `NewGroup`/`NewButton`/`NewCaption` 三个工厂统一由
  `UI/Forms/Common/DialogStyle.cs` 提供，BLKATTCHANGE、编号选项、关于、TCOLOR 四个面板共用。
  迁移前后四个窗口渲染为 PNG 逐字节比对，SHA256 一致，无像素级变化。

- **`TextColorForm` 拆分为三个 partial，`MinimumSize` 改为按比例计算**
  907 行窗体按 布局 / 状态 / 事件 拆为 `TextColorForm.Layout.cs`、`.State.cs`、`.Events.cs`，
  仅调整代码组织，不改变行为；`MinimumSize` 改为在 `OnLoad` 中按 `ClientSize` 比例计算，
  高 DPI 与字体缩放下不再低于设计尺寸。

- **`TextColorReport` 合并为 `BuildCore`，`TextColorEngine` 不再拼装报告**
  短版与完整报告共用同一套行拼装逻辑，输出逐字不变；报告拼装统一由命令层负责，Core 不反向依赖展示层。

- **`TextColorResultForm` 移除换行归一化**
  `Label` 本身支持 LF，原有 CRLF 归一化及其测试一并移除。

- **批量改色 TCOLOR 改成「先弹面板，面板上按钮回图纸框选」**（照 `BLKATTCHANGE` 的流程；
  2026-10-21 设计：命令进入后直接呈现面板，面板顶部提供按钮，点击后返回图纸框选目标范围）。
  ① **不再用命令行提示拦人**：命令一进来直接弹面板；「先选好文字再敲命令」的预选集照旧优先使用。
  ② **面板顶部新增「① 在图纸中选择要改色的文字」按钮**：点它带着当前颜色选项关窗 → 命令行里框选
  （窗口/窗交都行，自动只留多行文字 / 单行文字 / 多重引线）→ 选完再弹回面板，顶部计数刷新。
  框选时按 Esc 或空选，保留上一轮的选择；**去框选一次，已经调好的颜色不会丢**。
  ③ **没选文字就点确定**：命令行提示「请点面板上的『① 在图纸中选择要改色的文字』」，
  面板再弹回来，不执行、也不弹结果提示框。
  ④ **版式**：整套改成 `BLKATTCHANGE`、图块编号面板（`NumberingOptionsForm`）的体例 ——
  窗体 8px 外边距 + 一个根 `TableLayoutPanel` 纵排五个区（① 选择 / 基本颜色 / 索引色 / 处理选项 / 底部），
  组框之间 6px；按钮统一 28 高、「确定」100 宽 / 「取消」90 宽靠右下；「当前选择」与说明文字走 DimGray，
  选择计数与组标题走品牌蓝。**窗体可拖动缩放**（`Sizable`，最小 500×560）：
  470×552 → 520×604，索引色列表框吃掉富余高度；各组不再写死 `Left/Top`，组框 `Dock=Fill`，
  拉大时一并铺满，最长那条复选框独占整行、怎么都不会折行。
  顶部计数为 0 个时显示「尚未选择文字：请点上面的『① 在图纸中选择要改色的文字』。」。
  ⑤ **回归**：排版用例扩到新按钮（在窗体内、不与其他区重叠、文字不被裁），
  并新增一个用例：点该按钮后断言动作是 `PickObjects`、且把面板上的颜色选项原样带回
  （否则去框选一次颜色就丢）。用例本身也跟着改：坐标沿 `Parent` 链换算到共同祖先再比
  （嵌套布局后兄弟控件不再共用一个父坐标系），查找改成递归。单元测试 **80/80 通过**。

### Fixed
- **批量改色 TCOLOR：面板显示失败时不再以默认颜色修改图形**
  面板动作枚举未显式指定默认值（`Apply = 0`）。当 `ShowModalDialog` 抛出异常并被 `ShowDialogSafely`
  捕获时，`form.Action` 仍取 `Apply`，命令会以默认设置（索引色 1 红）修改图中文字；若此时没有选择集，
  还会进入无休止循环。现将默认值改为 `Cancel`（与 BLKATTCHANGE 等窗体一致），`ShowDialogSafely`
  返回对话框是否实际显示；TCOLOR 与 BLKATTCHANGE 均在失败时中止并输出
  「面板没能显示出来，命令已中止（原因已写入插件日志）」，异常原因同时写入插件日志。

- **批量改色 TCOLOR：7 处异常被静默忽略的位置收窄并记录日志**
  读取预选集、读取对象类型、吸取对象颜色、标记重画失败等操作不再无声消失；标记重画失败汇总为
  「有 N 个改动对象没能标脏重画（执行 REGEN 可刷新显示）」。

- **打包脚本：Release 构建前强制全量重编译**
  Debug 与 Release 共用输出目录时，存在将 Debug 版 DLL 打入安装包的隐患；打包脚本现于生成安装包前，
  对两个目标框架执行 `dotnet build -c Release -t:Rebuild`。

- **批量改色 TCOLOR：去掉「未选择到文字对象。是否处理【全图】所有文字？」这一步**
  （2026-10-21 调整：未选中文字时不再追问，直接提示后退出）。
  没选中文字就直接提示「未选择到文字对象，未做任何修改。」并退出 ——
  前面那个选择提示本来就能框选、也能全选，一步做得完的事不必再追问一遍；
  `SelectWholeDrawing()` 随之删除（要处理全图，在选择提示里全选即可）。

- **批量改色 TCOLOR：「处理选项」第 4 个复选框叠字、被组框下边框裁掉**
  （2026-10-19 问题：处理选项区域的复选框文字折行后相互重叠，被组框下边框裁切）。
  ① **根因**：「处理选项」组框只有 60px 高，第 4 个复选框「深入块定义内部（影响所有同名块参照）」
  被放进 210px 宽的第二列 —— 该文字要 216px（扣掉复选框方块后可用 188px）才排得下一行，
  于是折成两行，而控件没设 `AutoSize=false`、高度不撑开，第二行与第一行、组框下边框叠在一起被裁掉。
  ② **修法**：组框加高到 104px，四个复选框改成「两列 + 整行」三行排布（前三个各 200px，
  长句独占整行 430px）；窗体 470×470 → 470×514，「当前选择」与确定/取消仍在最下方。
  ③ **回归**：新增排版用例 `TextColorFormLayoutTests`（真实创建窗体后逐控件量像素：
  文字实测宽度 ≤ 可用宽度、控件四边都在组框内、底部按钮不被窗体裁掉）。
  **并做了「修复前」对照**：把复选框宽度改回 210px，用例如期 FAIL 并报出
  「宽 210px，扣掉复选框方块只剩 188px，文字要 216px 才排得下一行，会被折行」，还原后复跑全绿 ——
  证明用例真的能抓住这个 bug，而不是「改前改后都通过」的空转用例。

- **PIPEBREAK / INSTBREAK 不再把「端接（T 型）」误判成交叉、在接点处挖出缺口**
  （2026-10-18 问题：T 型端接被误判为交叉，主管在接点处被挖出缺口）。
  ① **根因**：端点判定用的是 `Tolerance.Global.EqualPoint * 2 = 2e-10` —— 那是 AutoCAD 判定「是不是同一个点」的量级。
  真实图纸里支管端点只要不是**逐位精确**落在主管上（捕捉没对准、图纸转换、大坐标下的双精度噪声），
  就会被当成普通交叉，在接点处把主管挖出缺口（表现为竖管在横管端接处被断成两截）。
  ② **两档判定**（`PipeBreakEngine.CollectIntersections`）：**第一档** 交点与端点精确重合（容差由 `2e-10` 改为 `1e-6`，
  仍远小于任何肉眼可见误差）→ 静默按端接跳过；**第二档** 交点落在**另一条**管线的端点/顶点附近（距离 < 缺口半宽）→
  按端接跳过**并单独报出**。第二档只查「另一条」：避让管线自己的端头贴近交点仍走原来的「端头太短」分支，
  避免两种原因混为一谈。
  ③ **提示**：新增 `[端点没接准]` 输出块，点名交点坐标、所属图层、端点坐标与偏差（最多 5 条，其余从略），
  并提示「端点应精确捕捉到另一条管线上；若确实要交叉，请把管线画过交叉点再打断」。
  ④ **回归**：新增端到端场景 `pipebreak-tjunction`（夹具 `lisp\FixtureT.lsp`）：支管端点过冲主管中心线 `0.4` →
  主管必须整根保留；同图另设一处真交叉 → 仍按宽度 100 正常挖缺口（防止新规则把打断整个关掉）。
  `Run-IntegrationTests.ps1` 场景表新增 `Fixture` 字段（默认 `Fixture.lsp`，既有 4 个场景行为不变）。
  ⑤ **验证**（2026-10-19 实测）：两支 DLL 均 0 警告 0 错误；**单元测试 69/69**；
  **端到端 5 场景 32 断言全绿**（含新增场景），脚本末尾注册表自检确认 `/isolate` 生效、真实 AutoCAD 配置未被改动。
  **并做了「修复前」对照**：把容差临时改回 `2e-10`、同时关掉第二档后重跑，新场景 2 条断言如期 FAIL
  —— T1 主干被断成 `[-200,50][150,200]`（6 条线），正是该缺陷的接点缺口；还原后复跑全绿。
  这条对照证明新增回归用例真的能抓住该 bug，而不是「改前改后都通过」的空转用例。
  accoreconsole 实测报出文字：`[跳过明细] 交点贴近管线端点(没接准) 1` +
  `[端点没接准] 1 处交点落在管线的端点/顶点附近 …… · 交点 (500, 100)：图层 PIPE 的管线端点/顶点 (499.6, 100) 偏离 0.4`。

## [1.1.0] - 2026-10-16

### Added
- **支持纯 AutoCAD（2019–2024，含 2022），安装器按 CAD 版本分流两支 DLL**
  （2026-10-16 目标：确认在纯 AutoCAD 2019–2024（含 2022）环境下的可用性，并在修复完成后产出可交付的
  exe 供实机验证）。
  核查结论：交接消息声称的两条根因里，**「缺少 `[assembly: ExtensionApplication]` 导致初始化不跑」被证伪**
  （全仓没有该特性，但 `Initialize()` 每次启动都执行——AutoCAD 会扫描 `IExtensionApplication` 实现，
  唯一实现时不需要该特性）；**「菜单挂载脚本写死 PnP.cuix」成立**；另发现第三条**更致命**的问题：
  安装器把所有 CAD 版本的加载登记都指向 `Contents\net8.0\ChemForge.dll`，2022 会去加载 .NET 8 程序集。
  ① **安装器分流**：新增 `DllSubDirForCad(CadKey)`（R25.0+ → `net8.0`，R21.0–R24.3 → `net48`）与
  `AppDllPathFor(CadKey)`，`RegisterWithAutoCad` 按 `CadKeys[i]` 逐版本写 `LOADER`；
  `WithTrustedPaths` 同时加入 `Contents\net8.0\` 与 `Contents\net48\` 两个受信任目录。
  ② **菜单脚本认宿主**：新增 `Get-HostCuix(Support目录)` —— 优先 `PnP.cuix`（Plant 3D 垂直产品），
  否则 `acad.CUIX` / `acad.cuix`（纯 AutoCAD），并读出该宿主的**菜单组名**（PNP / ACAD）；
  `Mount-ChemForgeMenu.ps1` 的弹出菜单、工作空间 `WSPop`、功能区引用、配置文件布局条目
  （`Pop<n> = <宿主组> pop<m>`）全部改用宿主组名；`Register-ChemForgeMenu.ps1` 的
  `$isGroupFile` 判据从「文件名是不是 PnP.cuix」改成「是不是本目录选中的宿主文件」。
  纯 AutoCAD 没有 EXPRESS 组，插入锚点退化为「本工作空间的第一个 WSPop 之后」（只替换 1 处）。
  ③ **补上 `[assembly: ExtensionApplication]`**：`src\Shared\Application\PluginInitializer.cs` 加上该特性
  （两支 DLL 现在都带 `ExtensionApplicationAttribute`），保证 2022 这类只认特性的加载路径也能初始化。
  ④ **顺带修掉**：`Test-MountUpToDate` 里工作空间引用用裸字符串比较（对 CRLF/缩进敏感），
  会让「升级跳过重写」永远失效 —— 改为与同函数其它判据一致的 `Normalize-Xml` 比较。
  ⑤ **验证**（本机无 2022，用「真 `acad.CUIX` + 重定向 `%APPDATA%` 的假支持目录」做端到端模拟）：
  安装 → `Header.cui` 写入 `PartialMenuFile>ChemForge.cuix`、`PopMenuRoot.cui` 写入扁平菜单、
  `MenuGroup.cui` 6 个 `ID_CF_*` 宏、`WorkspaceRoot.cui` 1 个 `WSPop MenuGroup="ACAD"` + 3 处功能区引用、
  布局条目 `Pop14 = ACAD pop13`；再装一次 → 「菜单内容已是最新，跳过重写与备份」（0 文件改写）；
  卸载 → WSPop 与功能区引用清零、布局条目删除。回归：单测 69/69、两支 DLL 0 警告 0 错误。

### Changed
- **卸载确认改成卸载窗口里的「一页」，点「取消」一个文件都不删**
  （2026-10-16 需求：日志删除的询问应前置到卸载确认页，由用户勾选后再执行；确认界面并入卸载窗口本身，
  不再另行弹出小窗口）。
  ① **顺序**：`AskDeleteUserData` 原来挂在 `usPostUninstall` —— 文件删除完成后才询问，选择已无意义。
  现改挂 `usUninstall` 且排在所有破坏性动作**之前**，用户的选择先存进局部变量，等清理动作全部做完
  再执行删除（即便中间某步失败，用户的选择依然算数）。
  ② **形态**：确认界面从 440×216 的小弹窗改成**与卸载窗口同尺寸、同位置的一页**：
  `CreateCustomForm(UninstallProgressForm.ClientWidth, .ClientHeight, True, True)`，底色 / 字体 /
  右上角品牌小图 / 标题与正文的坐标全部**借卸载器自己的控件**（`PageNameLabel`、
  `PageDescriptionLabel`、`WizardSmallBitmapImage`、`CancelButton`），所以用户点完「是」之后看到的
  是窗口内容换了一页，而不是又蹦出一个风格不搭的小窗；右下角两个按钮「取消」/「确认卸载」
  （`CalculateButtonWidth` 取等宽、与卸载器按钮同高）。
  **点「取消」或用 × 关窗 = `Abort`，一个文件都不删**；点「确认卸载」才继续。
  ③ **实测**（免 UAC 的测试包：`PrivilegesRequired=lowest` + 临时安装目录 + 把 `RunRegistration` /
  `LinkUserBundle` / `RegisterWithAutoCad` / `UnregisterWithAutoCad` / `UnlinkUserBundle` /
  `SetUninstallerElevation` / `ClearUninstallerElevation` 全部换成空桩，避免碰真实 AutoCAD 状态）
  三个分支各跑一遍：点「取消」→ 安装目录 **14 个文件原样**、卸载登记原样 ⇒ 中止生效、零改动；
  点「确认卸载」（不勾）→ 目录与登记消失、出现「已顺利从您的计算机中移除」⇒ 正常走完；
  勾上再点「确认卸载」→ 卸载日志 `TEST: 继续卸载，DeleteUserData=1` ⇒ 勾选状态确实传到了删除分支。
  ④ **顺手修掉一个会误报的真 bug**：`Register-ChemForgeMenu.ps1` 在 `$ErrorActionPreference='Stop'` 下
  **裸调 `Remove-Item`** 删自己产出的文件，任何一个被占住都会抛异常、整个脚本以退出码 1 收场 ——
  而安装/卸载程序是用 `Exec(..., SW_HIDE)` 调它的，用户端只看到一句「菜单注册撤销未完成（退出码 1）」，
  现场全丢（2026-10-16 卸载实测踩到）。现改为**单个文件删不掉降级为警告**（其余文件照常清理、
  不再让整个卸载失败），并新增**顶层 `trap` 兜底**：任何未捕获错误都连**出错位置**一起写进
  `%TEMP%\ChemForge-register-error.log`。
  ⑤ 安装包更新为 **2,686,471 B**，`sha256=a0156b8deaab81aef4c16d8f6aa4da1d7b21027babf34c25204e0a0176711fd0`。
- **安装与卸载体验重做：品牌化排版 + AutoCAD 开着时不再默默装坏**
  （2026-10-16 需求：安装与卸载界面重新排版以统一品牌样式，并对「AutoCAD 正在运行导致安装或卸载失败」
  给出明确处置）。
  ① **排版**：安装包换用品牌图标（`SetupIconFile`，从 `branding\ico\ChemForge-v3.ico` 取最大帧）；
  向导左侧大图与右上角小图改为按品牌色板程序化生成的 `Assets\Wizard-100.bmp`(202×386) /
  `Wizard-150.bmp`(336×643) / `WizardSmall-100.bmp`(58×58) / `WizardSmall-150.bmp`(97×97)
  —— 尺寸依据 Inno 官方文档（6.6+ 默认字号下 100% 为 202×386 / 58×58，多尺寸写法是
  「逗号分隔的文件名列表」，不写数字，由 Inno 按 DPI 自选）；新增
  `deploy\Installer\make-assets.ps1` 一键重新生成（**图源必须用 `.ico`，不能用
  `branding\png\*.png`** —— 本机装了透明加密，那几个 PNG 读出来是密文，GDI+ 会报
  "Out of memory"）；`[Messages]` 覆盖了十几条生硬直译的向导文案；快捷方式与
  `UninstallDisplayIcon` 统一改用 `.ico`（原来指向 `index.html`，系统只能给通用图标）。
  ② **CAD 开着时的应对（核心）**：新增 `IsAutoCadRunning`（走 `tasklist` 而非窗口标题，
  无界面的 `accoreconsole` 也能查到）与 `EnsureAutoCadClosed`；**安装**在 `InitializeSetup`
  与 `PrepareToInstall` 各拦一道（`InitializeSetup` 是最早的钩子 —— 升级时**旧版本的卸载器
  可能先于 `PrepareToInstall` 运行**，到那时才拦，旧版可能已经被卸掉一半），**卸载**在
  `InitializeUninstall` 拦一道；静默模式（`WizardSilent` / `UninstallSilent`）下不弹窗，
  直接以非零退出码拒绝，以免升级过程中旧卸载器突然弹框。
  （2026-10-16 需求补充：卸载时若 AutoCAD 正在运行，应提示先关闭并中止，不执行任何文件操作 ——
  据此把卸载侧原有的「重试 / **跳过** / 放弃」改为**只有「重试 / 取消」**：跳过会让卸载删掉一半文件、
  装出半成品，安装侧也一并去掉跳过，
  两侧行为一致。**点「取消」零改动**：安装不写一个文件、卸载不删一个文件。）
  ③ **其它**：「准备安装」页新增「附加说明」（读卸载登记键的 `DisplayVersion` 说明这是升级、
  要给几个 AutoCAD 启用、CAD 当前是否还开着）；`SetupMutex` 防止两个向导同时改 CUIx 与注册表；
  新增 `[UninstallDelete]` 清掉运行期日志 `install-link.log` / `install-registry-error.txt`
  并删除空目录（原来卸载后会留下一整个空文件夹）。
  ④ **实测揪出一个升级判定 bug**：`GetPrevVersion` 原来只查 `HKEY_CURRENT_USER`，但
  `PrivilegesRequired=admin` 下 Inno 把卸载登记写在 HKLM，而 Setup.exe 是 32 位进程、
  `HKEY_LOCAL_MACHINE` 走 WOW6432Node 视图 —— 本机 v1.0.0 的登记键确实在
  `HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{AppId}_is1`
  （`DisplayName=ChemForge · 工艺匠造工坊`、`DisplayVersion=1.0.0`）。现改为 HKCU → HKLM →
  HKLM64 依次查。
  ⑤ **验证**：`IsAutoCadRunning` 的判定原语用假 `acad.exe`（复制 `ping.exe` 改名）实测
  「无→退出码 1 / 有→0 / 杀掉后→1」；`ISCC` 编译 0 错误；`Build-Installer.ps1` 全流程通过
  （manifest 补丁 + 提权探针 740）。安装包更新为 2,686,471 B，
  `sha256=a0156b8deaab81aef4c16d8f6aa4da1d7b21027babf34c25204e0a0176711fd0`。

### Added
- **版本号统一升到 `1.1.0`，并产出安装包 `ChemForge_Setup_v1.1.0.exe`**
  （2026-10-16 版本规划：新增功能对应次版本号，统一升至 1.1.0 并产出安装包）。
  ① **四处版本号一次改齐**：`deploy\Installer\ChemForge.iss:30` 的 `#define MyAppVersion`（安装包
  文件名与「关于」页版本都取自它，是单一来源）→ `deploy\Bundle\ChemForge.bundle\PackageContents.xml`
  的 `AppVersion` 与**两个** `ComponentEntry` 的 `Version` → 新增
  `src\Build\ChemForge.Net80.csproj` 的 `<Version>1.1.0</Version>`（SDK 工程据此自动生成
  `AssemblyInfo`）→ 新增 `src\Build\AssemblyInfo.Net48.cs`（老式 net48 工程**不认 `<Version>` 属性**，
  实测 DLL 一直报 `0.0.0.0`，必须显式写 `AssemblyVersion` / `AssemblyFileVersion` /
  `AssemblyInformationalVersion` 三个特性）。两份 DLL 现已都报 `FileVersion=1.1.0.0`。
  ② **`AssemblyInfo.Net48.cs` 放在 `src\Build\` 下会被 SDK 工程的通配符自动编进 net80**，与自动生成的
  `AssemblyInfo` 撞出 `CS0579: 特性重复`；已在 `ChemForge.Net80.csproj` 里加
  `<Compile Remove="AssemblyInfo.Net48.cs" />` 显式排除（注释说明原因）。
  ③ **`net48` 目标其实可以本机编译**（推翻早前"仓库没有 `lib\` 目录所以编不了"的判断——那是查错了路径，
   AutoCAD 参考程序集在 `src\Build\lib\Acad2015\`，另有 `Acad2020\`；.NET Framework 4.6 参考程序集
  由 `ChemForge.Net48.csproj:80-81` 的 `FrameworkPathOverride` 指向 NuGet 缓存
  `microsoft.netframework.referenceassemblies.net46\1.0.3`）。本次 net48 与 net8.0 **两支都编译通过
  （0 警告 0 错误）且都含 `INSTBREAK`**，安装包同时带两支。
  ④ **验证**：单元测试 69/69；端到端验收 4 场景 27 条断言全通过、注册表自检干净；安装包
  `deploy\Installer\Output\ChemForge_Setup_v1.1.0.exe`（2,686,471 B，`FileVersion=1.1.0`，
  manifest 已由 `Build-Installer.ps1` 补丁为 `requireAdministrator` 并通过提权探针 740 校验），
  `sha256=a0156b8deaab81aef4c16d8f6aa4da1d7b21027babf34c25204e0a0176711fd0`，
  发布说明见同目录 `RELEASE_NOTES_v1.1.0.md`。

- **仪表线自动打断（`INSTBREAK` = INSTRument + BREAK）：管线打断的镜像命令，固定打断仪表线**
  （2026-10 需求：与管线自动打断一致，但图层需点选两次（仪表线图层 + 管线图层），打断方向固定朝向仪表线，
  因此不再询问缺口方向）。
  ① **交互**：缺口/架桥总宽度 → 模式 `[缺口(D)/架桥(H)]` → 架桥时问半圆凸起方向 →
  **点选仪表线图层**（回车用上次）→ **点选管线图层**（回车用上次）→ 框选仪表线。
  与 `PIPEBREAK` 的唯一差别就在这里：图层点两次，且**不再询问缺口方向**（该步骤已删除，
  `PIPEBREAK` 保留）。两图层同名时直接提示并退出，不做无意义的自交。
  ② **实现**：不复制引擎。`PipeBreakEngine` 抽出私有 `BreakRequest`（宽度 / 模式 / 凸起方向 /
  避让侧 `AvoidLayer` / 另一侧 `OtherLayer` / 提示词），`Execute()`（单图层，按几何方向分组）
  与新增的 `ExecuteInstrument()`（双图层，按图层分组）各组装一份请求，处理段 `RunCore` 完全共用；
  `CollectIntersections` 里避让侧固定为仪表线一侧（`avoidIdx` 直接取仪表线那组），
  因此求交、分割、架桥、跳过统计的行为与 `PIPEBREAK` 逐字一致。
  ③ **记忆**：沿用 `PipeBreak.settings`，新增键 `LastInstrumentLayer`（宽度与管线图层与
  `PIPEBREAK` 共用一份记忆）。④ **入口**：菜单「管线与仪表」新增一项，功能区按钮用新图标
  `breakinst_16/32.png`（由 `breakpipe_*.png` 沿对角线转置而来：十字对称，转置只把红色缺口箭头
  从横线搬到竖线，一眼看出"被打断的是另一条线"）；`PackageContents.xml` 两个组件段都登记。
  ⑤ **验证**：`dotnet test` 69 项全通过（含菜单断言从"工具菜单只有 1 项"改为 2 项、
  命令宏常量表新增 `ID_BreakInstrument`）；另有新增的**自动化端到端验收**
  `src\Tests\Integration\Run-IntegrationTests.ps1`（Core Console 无界面跑真实命令链路，
  4 场景 27 条几何断言全通过，含 `PIPEBREAK` 单图层回归）——
  这套验收在开发中**抓到过一次真实回归**：重构时 `PIPEBREAK` 的 `BreakRequest.AvoidLayer`
  曾置空，导致单图层模式下所有图元都被判为"非管线图层"、命令静默不打断；
  单元测试测不到（引擎重度依赖 AutoCAD API），是端到端脚本兜住的，故 `TwoLayer`
  改为显式字段而非由 `AvoidLayer != null` 推导。

- **ChemForge 安装包（Inno Setup）：中文向导 + 自动检测本机 AutoCAD 版本 + 可选安装位置**
  （`deploy\Installer\ChemForge.iss`，产物 `deploy\Installer\Output\ChemForge_Setup_v1.0.0.exe`）。
  ① **向导**：欢迎 → **选择 AutoCAD 版本**（枚举 `HKLM64\SOFTWARE\Autodesk\AutoCAD\<版本>\<产品键>`，
  只收 `ProductName` 与 `AcadLocation` 都非空的；`ACAD-8101`、`ACAD-8117` 这类空壳键自动过滤；
  复选框默认全选）→ 选择安装位置（默认 `%LOCALAPPDATA%\Programs\ChemForge`，取消
  `DisableDirPage=yes`）→ 附加任务（桌面帮助快捷方式，默认不勾）→ 安装进度 → 完成
  （「查看帮助」与「立即启动 AutoCAD」两个复选框**都默认不勾**）。
  ② **装完自动做的事**：拷 `ChemForge.bundle` 到安装目录 → 在
  `%APPDATA%\Autodesk\ApplicationPlugins\ChemForge.bundle` 建**目录联接**（`mklink /J`，失败回退
  `xcopy`；已存在则原样保留，重装不误删）→ 向 `TRUSTEDPATHS` 追加 4 条真实路径（安装目录两个 +
  自动加载目录两个；**值不存在时创建**）→ 写 `HKCU\...\Applications\ChemForge_Net80` 加载登记
  （LOADER / MANAGED=1 / LOADCTRLS=6 / DESCRIPTION）→ 后台跑 `Register-ChemForgeMenu.ps1` 完成菜单注册
  （**`SW_HIDE`，不再弹 PowerShell 窗口**，安装与卸载共用同一函数）。
  ③ **卸载**：反向清理（摘联接用 `[IO.Directory]::Delete($p,$false)` —— **绝不递归删联接**，
  否则会连安装目录一起删）→ 开始菜单 `卸载 ChemForge` 快捷方式 + Windows「设置 → 应用」登记。
  ④ **`PrivilegesRequired` 由 `lowest` 改为 `admin`**：实测**提权是绕过本机火绒企业版拦截的前提**
  （普通双击被拒在"创建 `unins000.exe`"这一步：错误 5 拒绝访问），详见
  `KnownIssues.md`《火绒企业版（ESEndpoint）拦截"未提权"的安装/卸载程序》。
  ⑤ **只写注册表登记不足以让插件加载**（2026-10-13 实测）：`AcMr` / `App Manager` / `CloudAccess`
  三条能工作的登记背后都有真实 bundle 目录躺在 `ApplicationPlugins` 里，注册表条目只是扫描的副产物；
  因此安装包必须同时建立自动加载目录（联接）。
  ⑥ 注册表写入改为**逐项收集失败**（`RegErrors`，Inno 的 `Reg*` 写失败只返回 False 不抛异常），
  失败则落盘 `{app}\install-registry-error.txt` 并弹窗告知 —— 避免"文件装好了但 CAD 里什么都没有"这类最难查的故障。
  验证：装完 `ChemForgePlugin.log` 出现完整加载链（`运行时目录` → `CUIx 菜单文件已存在` →
  `菜单加载完成` → `插件初始化完成` → `功能区选项卡已创建`），命令行 `BATATTCHANGE` 可用；
  帮助页命令名与 `CommandNames` 一致（`TCOLOR/RENBLK/BLKCNT/BATATTCHANGE/BLKNUM/PIPEBREAK/TAGNO/CHEMFORGE`，
  旧的 `CF_*` 前缀 0 处）。简体中文语言包放在 `deploy\Installer\Languages\ChineseSimplified.isl`
  （Inno 6.7.3 自带语言包**不含**简体中文）；按 2026-10 的公开仓库保密决定，`deploy\Installer\`
  下的安装脚本与语言包**只在本机保留、不进公开仓库**，编好的 `ChemForge_Setup_v1.0.0.exe`
  作为 GitHub Release 附件分发。

### Fixed
- **TRUSTEDPATHS 每次升级都会重复追加 4 条（AutoCAD 的 `%UserProfile%` 写法没被识别，已修复）**：
  AutoCAD 会把受信任路径规范化成 `%UserProfile%\AppData\...` 形式，并把 `TRUSTEDPATHS`
  的值类型改成 `REG_EXPAND_SZ`；而 Inno 的 `RegQueryStringValue` 读回来的是**未展开**的原始文本
  （PowerShell 的 `Get-ItemProperty` 会自动展开，只看 PowerShell 会被骗过去）。原来的
  `PathEntryPresent` 是纯字符串比较，拿 `C:\Users\xxx\...` 去比 `%UserProfile%\...` 永远比不中，
  于是**每次安装都再追加 4 条**，卸载侧 `WithoutAppPaths` 同样比不中、摘不掉那 4 条。
  实测现场：`HKCU\...\Profiles\<<未命名配置>>\Variables\TRUSTEDPATHS` 里同 4 条路径出现两遍
  （`%UserProfile%` 形式 4 条 + 字面量 4 条，共 625 字符）。
  修法：新增 `ExpandEnvVars`（用 `GetEnv` 展开 `%VAR%`，认不出来的原样保留）+ `NormalizeForCompare`
  （展开环境变量 → 统一斜杠 → 去尾反斜杠 → 转小写），`PathEntryPresent` / `SamePath` /
  `WithoutAppPaths` 全部改用归一化后比较。
  用只读探针（`PrivilegesRequired=lowest`，不写注册表、不需要 UAC）实测：
  ① 列表里只有 `%UserProfile%` 形式时，4 个 `PathEntryPresent` 全为 `True`、`WithTrustedPaths` 不追加；
  ② 8 条混合形式的列表经 `WithoutAppPaths` 后剩下**空列表**（8 条全摘掉）。
  现场同时做了一次性清理：备份原值后把 8 条去重成 4 条（`Set-ItemProperty -Type ExpandString`），
  备份在 `%TEMP%\ChemForge-TrustedPaths-bak-20261016-204352\`。
  （踩坑记录：Inno **没有** `userprofile` 这个常量，`ExpandConstant` 取它会直接抛异常把整个脚本打断；
  而且这个常量名写在 `{ }` 注释里会提前闭合注释，报 `String error`。）

- **卸载确认框把应用名显示成字面量 `[name]`（Inno 消息占位符用错，已修复）**：
  `deploy\Installer\ChemForge.iss` 的 `[Messages]` 段里，`ConfirmUninstall` 与
  `UninstallStatusLabel` 写成了 `[name]`，而 Inno 对这两条消息用的是**参数占位符 `%1`**
  （`WelcomeLabel1` / `SelectDirLabel3` / `FinishedLabel` 之类才是 `[name]`）——
  实测卸载时弹框原文是「确定要卸载 **[name]** 吗？」。改成 `%1` 后实测为
  「确定要卸载 **ChemForge · 工艺匠造工坊** 吗？」。核对了语言包里其余 11 条被覆盖的消息，
  占位符均与 `Default.isl` 一致。

- **安装 / 卸载在 AutoCAD 开着时不再可能"改一半"（硬性拦截，已逐项实测）**：
  详见上面 `### Changed` 的 ② —— 卸载侧去掉「跳过」按钮、新增 `InitializeSetup` 最早拦截；
  本轮用「同 `[Code]`、免 UAC、去掉 AutoCAD 注册副作用」的测试包 + 假 `acad.exe`
  （复制 `ping.exe` 改名，`IsAutoCadRunning` 只看 `tasklist` 的映像名）跑了 7 个用例，全部通过：
  ① 静默安装 + CAD 开着 → 退出码 1、`InitializeSetup returned False; aborting.`、**零写入**；
  ② CAD 关着静默安装 → 退出码 0、装出 14 个文件；③ 静默卸载 + CAD 开着 → 退出码 1、
  日志「静默卸载已取消 —— 未删除任何文件」、**文件数与卸载登记原样**；④ 交互式卸载 + CAD 开着，
  自动点「取消」→ **14 个文件与登记仍在**（弹框按钮实测只有 `重试(&R)` / `取消`，**没有跳过**）；
  ⑤ CAD 关着交互式卸载 → 点「是」后正常卸干净（目录与登记都消失）；
  ⑥ 交互式安装 + CAD 开着点「取消」→ **零写入**；
  ⑦ CAD 开着启动安装 → 关掉 CAD → 点「重试」→ 向导正常走完（下一步×3 → 安装 → 完成）→
  **装出 14 个文件**（证明"关掉 CAD 就能继续"这条路通）。

- **升级到 v1.1.0 后菜单栏与功能区看不到「仪表线自动打断」（CUIx 永不重建的真 bug，已修复）**：
  实测装完 v1.1.0，菜单里只有 5 项、`INSTBREAK` 不出现。
  ① **根因链**：`deploy\Dev\Mount-ChemForgeMenu.ps1` 第 2/4 步用 `ZipFile 'Update'` 重写各 `.cuix`
  的工作空间，这一写会把支持目录 `ChemForge.cuix` 的 `LastWriteTimeUtc` 刷新到**挂载时刻**；
  而 `src\Shared\CAD\Impl\MenuService_Net80.cs` 判断"要不要重建菜单文件"用的是**纯时间戳判据**
  （`IsOlderThanPlugin`：cuix 时间 < 插件 DLL 时间 → 才重建）。于是"编译 → 挂载"之后 CUIx 恒比
  DLL 新，插件永远跳过重建，**改了 `MenuDefinition` 也传不进 CUIx**：挂载收割到的仍是旧菜单。
  该陷阱 2026-10-13 就被记录在挂载脚本头部，当时只能靠"手工删掉 CUIx 再启动一次"绕过。
  ② **修复**：改为**内容判据** —— 新增 `MenuFileNeedsRebuild(string cuixPath)`：把 CUIx 当 ZIP 打开、
  读 `MenuGroup.cui`、抽出全部 `<MenuMacro UID="...">` 的 UID 集合，与 `MenuDefinition` 的命令项
  别名集合（新增递归收集器 `CollectCommandAliases`）做**集合相等**比较；少一个、多一个、
  缺 `MenuGroup.cui`、读取失败都重建。时间戳不再参与判断。
  ③ **验证（决定性）**：先让插件重建出权威 CUIx（21,915 B → 23,065 B，宏 6 个，含
  `chemforge_breakinst_16/32.png`），把 `deploy\Dev\ChemForge.cuix` 引导副本同步为权威版本、
  重跑挂载（收割"菜单项 6 个、宏 6 个"，`PnP.cuix` 写入 6 个宏含 `ID_CF_BreakInstrument`）；
  然后**复现 bug 的原始现场**——把 5 项旧 CUIx 盖回支持目录并**把时间戳刷到比 DLL 更新**
  （17:42:29 > 17:38:37，旧判据在此必然跳过重建），启动 AutoCAD：插件仍识别出内容不符并自动
  重建（日志 `ChemForge CUIx 菜单文件已生成`；23,067 B、宏 6 个含 `ID_BreakInstrument`），
  即修复生效。④ 挂载脚本头部的"时间戳陷阱"说明同步改写为**已在插件侧根治**，不再需要手工删 CUIx。

- **端到端验收脚本会把系统变量写回用户的 AutoCAD 配置（已修复：改用 `/isolate`）**：
  现象：`FILEDIA` 与 `DYNMODE` 在测试运行后被写回默认值。查证结果：
  ① `accoreconsole.exe` 默认使用当前用户的配置目录与注册表键，**退出时会把本次会话动过的系统变量
  落成 `HKCU\Software\Autodesk\AutoCAD\R25.0\ACAD-8117:804\FixedProfile\...` 下的"偏离记录"**；
  实测脚本里设过的 `FILEDIA` / `CMDDIA` / `SECURELOAD` / `ATTREQ` 四条**全部落盘**，与现场发现的
  四条偏离记录逐条对上 —— 这部分责任在脚本，已修。
  ② `DYNMODE` **不是这套脚本写的**：脚本从不设它，且对照实验（跑一次不设任何系统变量的
  `accoreconsole`）显示它也不会被写回；`port\InstrumentNumbering\tools\cad-test\` 的 COM 测试脚本
  注释里已记录“动态输入（DYNMODE）在早前的测试运行后变回默认值”，即**该现象早于本次开发**。
  ③ **修复**：`src\Tests\Integration\Run-IntegrationTests.ps1` 改为
  `accoreconsole /isolate CFE2E <临时目录> /s`（语法为 `/isolate <regkey> <userDataFolder>`，
  两个参数都要给、且放在 `/s` 之前），实测真实配置完全不被写；脚本末尾新增**注册表自检**，
  一旦发现 `FileDialog` / `CommandDialogs` 偏离记录就报红并 `exit 1`。
  ④ **验证**：同一套脚本加 `/isolate` 后重跑，**4 场景 27 条断言全通过、exit 0**，跑前跑后
  `FileDialog` / `CommandDialogs` / `DYNMODE` / `SECURELOAD` 均仍为"无偏离记录"、`Attreq` 保持 1。
  现场被改的四条已用备份（`%TEMP%\ChemForge-SysvarFix-20261016-123257\AutoCAD-HKCU-full.reg`）
  核对后删除，AutoCAD 恢复出厂默认。

- **启动弹「安全性 - 未签名的可执行文件」（原 KI-1）已修复，并按要求从 `KnownIssues.md` 移除**：
  安装包与安装脚本现在会在 `TRUSTEDPATHS` **不存在时创建**它并写入插件真实路径。实测：
  **重建 DLL（改内容 + 换时间戳，正是以前必弹的条件）后启动 AutoCAD，枚举该进程全部顶层窗口，
  `#32770` 安全对话框 = 0 个**。另一条等价路线：把包放 `%ProgramData%\Autodesk\ApplicationPlugins\`
  （默认受信任根，零注册表 —— 信任判定看的是加载路径字符串，实测真身在 `D:\` 也不弹框）。
  原条目的现象 / 判据 / 取证方法已并入 `docs\DeveloperGuide\ManualTesting.md` 同名小节。

### 已知问题（记录待办）
- 新增 `docs/DeveloperGuide/KnownIssues.md`：记录两个**已查清根因、本版暂不修复**的问题（记录待办，后续版本处理）：
  **KI-1** 启动时弹"安全性 - 未签名的可执行文件"确认框 —— 插件 DLL 无签名且 `TRUSTEDPATHS`
  实际未写入（`Install-Dev.ps1` 在值不存在时静默跳过），不点确认框 AutoCAD 就卡在启动、.NET 插件
  完全不加载（此时经典菜单栏/功能区仍显示 ChemForge，因为那是 CUIX 声明式挂载）；
  **KI-2** 经 Junction 部署时"向上找仓库"失败，运行时目录落到 `%LOCALAPPDATA%\ChemForge`。
  两条都含现象/证据/根因/建议修复，并附本机透明加密环境下的取证约束
  （日志需使用者按时间范围粘贴；时间戳必须用第二种方法复核）。
  配套代码改动：`PluginInitializer.ResolveRealPath` 改为**用目录句柄**解析真实路径
  （`TryResolveDirectoryHandle`，只请求属性权限、不触碰已加载的程序集），
  并删掉无效的 `ResolveLinkTarget` 分支 —— Net80/Net48 构建 0 警告 0 错误、26 个单元测试通过，
  **未在 CAD 进程内验证**。

### Removed
- **移除 `docs/architecture/` 整个目录**（该目录可随时由 archify 从当前代码重新生成，不需要随仓库体现）：原含 799 KB 的 archify 生成页 `ChemForge-Architecture.html` 与 19 KB 的规格源
  `chemforge-architecture.json`。两者都**不是插件运行所需**，且随时可由 archify 从当前代码重新生成，
  故不再随仓库分发（含未推送历史一并清除，本地备份留在 `TEMP/_backup_before_rewrite/`）。
  `docs/DeveloperGuide/Architecture.md` 顶部指向该 HTML 的链接已改为"按需生成"的说明。

### Changed
- **文字格式处理组精简：移除「批量修改多行文字倾角」与「批量修改多行文字宽度因子」；「编号用法说明」改为「属性块批量编号」对话框右上角的按钮弹窗**（精简文字格式处理组菜单；用法说明改为对话框内按钮触发的弹窗）。
  ① **菜单**（`src\Shared\Application\MenuDefinition.cs`）：删掉 文字格式处理 组的 `TANGLE` / `TWIDTH` 两行与 图块编号 组的 `BLKNUMHELP` 一行 —— 文字格式处理组只剩 `TCOLOR`，图块编号组只剩 `BLKNUM`；分组与顺序不变，仍是五组四条分隔线。
  ② **命令与代码一并删除**（命令与入口一并移除，不保留别名）：`CommandNames.TextOblique` / `TextWidth` 常量、`RibbonBuilder.IconMap` 两条图标映射、`PackageContents.xml` 两条 `<Command>`、`Shared.projitems` 两条 Compile 项、以及 `src\Shared\Commands\Text\TextObliqueCommand.cs` 与 `TextWidthCommand.cs` 两个文件全部移除。`TextFormatter` / `TextFormatOptions` 保留（`TCOLOR` 仍在用），`ExtensionMethods.ToRadians` 随之失去唯一调用者但按原决定保留（见下方 2026-10 条目）。
  ③ **用法说明单一来源**：新增 `src\Shared\UI\Forms\Block\Numbering\NumberingHelpText.cs`（`CommandLineText` = 原 `BLKNUMHELP` 打印的整段文案，逐字搬运；`DialogText` = 去掉开头换行的同一份文案），`src\Shared\BlockNumbering\Commands.cs` 里 `BLKNUMHELP` 改为 `doc.Editor.WriteMessage(ChemForge.UI.Forms.Block.Numbering.NumberingHelpText.CommandLineText)` —— 命令行输出与弹窗**逐字一致，不会再各自漂移**。
  ④ **对话框右上角按钮**：新增 `NumberingHelpForm.cs`（只读等宽文本框 + 关闭按钮，`ShowDialog` 模态弹窗）与 `NumberingHelpButton.cs`（在「选择要编号的块（两步）」分组框右侧挂 112×28 的「编号用法说明」按钮，文字与「① 在图纸中选一个样板块」按钮顶边对齐，跟着窗口缩放走 `box.Layout` 重算）；按钮只调纯 WinForms，**不碰 `AcadApp.ShowModalDialog`**（该 API 在 accoreconsole 里会原生 AV，见集成文档维护注意事项第 2 条）。
  ⑤ **同步层**：`deploy\Dev\Sync-BlockNumbering.ps1` 记录的偏差由 1 处增加到 3 处 —— 偏差 2 = `Commands.cs` 的用法说明字面量换成常量引用，偏差 3 = `UI\NumberingOptionsForm.cs` 在 `box.Controls.Add(flow);` 之后插入一行 `NumberingHelpButton.Attach(box);`；两处都先校验锚点恰好匹配 1 次，否则报错退出。
  验证：Net80 构建 0 警告 0 错误；`Sync-BlockNumbering.ps1 -Verify` → 施加偏差后 **18 个源文件 + 自测程序与源仓库逐字节一致**、算法自测 **65/65 通过**；单元测试 **30/30 通过**（原 27 个用例 + 新增 `NumberingHelpTextTests` 3 个）。**仍需在 AutoCAD 内人工确认**：菜单三行消失、按钮弹窗、`BLKNUMHELP` 命令行输出不变。
  **2026-10-13 现场确认**：菜单三行已消失（`PnP.cuix` 挂载复核 = 8 项 + 4 条分隔线、8 个宏；插件重建的支持目录 CUIx = 8 个 PopMenuItem / 8 个 MenuMacro / 8 个 RibbonCommandButton，旧标识符残留 0），右上角「编号用法说明」按钮弹窗可用；弹窗正文换行缺陷见下方 `### Fixed`（已修，单元测试 32/32）。
- **批量修改块属性：命令与全部标识由 `ATTRREP` 改名为 `BATATTCHANGE`**（原名称辨识度不足，统一更名，覆盖命令名、文件名与文件内容）。
  ①命令名 `CommandNames.BatchAttrChange = "BATATTCHANGE"`（`src\Shared\Application\CommandNames.cs:31`），**不保留 `ATTRREP` 别名**；
  ②**22 个文件改名**：`AttributeReplace*` → `BatchAttrChange*`（含命令/采集器/引擎/写入器/报告/记忆存储/9 个模型/窗体），图标 `attrreplace_16/32.png` → `batattchange_16/32.png`，
  集成文档 → `docs\DeveloperGuide\BatchAttrChange-Integration.md`，自测工程目录 → `tests\BatchAttrChangeSelfTest\`（该工程不在 `ChemForge.sln` 里，无需改 sln）；
  ③**31 个文件内容改写**（`AttributeReplace`→`BatchAttrChange`、`attrreplace`→`batattchange`、正则 `ATTRREP(?![:\-])`→`BATATTCHANGE`），
  负向先行断言**刻意保住** `ATTRREP:` 与 `ATTRREP-` 两种形式 —— 那是原 AutoLISP 插件的函数/变量名（`ATTRREP:collect-tags`、`ATTRREP-DIAG` 等），文档里作为移植出处引用必须原样保留；
  ④**记忆文件迁移**：`BatchAttrChangeMemoryStore` 的 `FileName`/根节点改为 `BatchAttrChange.xml`，`Load()` 在新文件不存在而旧的 `AttributeReplace.xml` 存在时读取旧文件、
  写成新文件名、并把旧文件改名为 `AttributeReplace.xml.migrated` 留底（此前保存的那套值不会丢）；
  ⑤**图标链路**：`RibbonBuilder.IconMap` 的基础名与嵌入资源名（`src\Shared\Shared.projitems:126` 是通配 `Icons\*.png`）随文件改名同步，`EnsureIconFile` 落盘的散图名变为 `chemforge_batattchange_16/32.png`。
  验证：Net80 构建 0 警告 0 错误（`ChemForge.dll` 195,072 字节）、纯逻辑自测 **137 项通过 0 失败**、
  部署产物 UTF-8/UTF-16 双偏移字符串核对 14/14 命中且旧图标名已消失。
  **仍需 CAD 内验收**：功能区按钮、经典菜单栏（`PnP.cuix` 快照必须重挂，见下）与新命令名一致。
- **仪表自动编号向导界面改版：文字与排版整体照 ChemForge「属性块批量编号」（`BLKNUM`）+ `编号规则` 增加后缀**（界面与排版统一对齐「属性块批量编号」；编号规则新增后缀，效果行、预览与实际写入需同步）。
  ① 版式由五步向导改为**两页**：第 1 页 = BLKNUM 式单页配置（`选择要编号的块（两步）` / `目标属性` / `仪表类型` / `编号规则` / `排序规则（默认：先上后下，同一行从左到右）` / `写入行为`），
  第 2 页 = `编号顺序示意图（点击圆圈选中，下方按钮调序）` + `块清单（多选后可上移/下移/成组/合并）`；
  ② 风格对齐 BLKNUM：不设自定义字体（系统默认 + `AutoScaleMode.Font`）、控件文字 **0 个 emoji**、分组框/字段/按钮/配色同款，窗体 `ClientSize 960×760` / `MinimumSize 780×640` / `Padding 8` / `Sizable`；"运行日志"面板改为**右下角一行状态行**；
  ③ `编号规则` 里 `编号开头：` 与 `后缀：` 并列，**后缀非空即生效**（删掉旧的「使用固定后缀」复选框），效果行、第 2 页 `新值` 列与实际写入值三处同步。
  **CAD 内实测通过**（2026-10-12 12:43，`run_wizard_test.ps1 -PickLoop -Write -CloseAcad -Suffix '-A'`，全自动无人工点击）：两页流程走通、取消拾取后同一实例重开、
  9 块实际写入 `001-A … 009-A`（`== 9 attribute value(s) changed ==`，预置的 `3899` 被覆盖）、行优先顺序与 Python 原版一致、无崩溃；脚本另修掉两处问题（`TRUSTEDPATHS` 原值要先剔除 DLL 目录再记；`-Prefix`/`-Suffix` 参数）。
  验证：UiDemo/Acad 构建 0 警告 0 错误、单元测试 41/41、差分 57/57、`--selftest` 全绿。证据见 `port\InstrumentNumbering\docs\验收说明.md` 第 6 节。
- **仪表自动编号（`port\InstrumentNumbering\`）：步骤①改为 ChemForge「属性块批量编号」同款两步选块模型**（原选择块流程易卡顿，改为「属性块批量编号」同款两步选块模型）。
  向导不再用"隐藏窗口 + 回调抢焦点"读选择：点「① 在图纸中选一个样板块」/「② 选择范围」时只置 `Action = PickSample / PickRange` 并**收起窗口**（实例复用、不 `Close`），
  命令层在 CAD 里完成拾取（`PromptEntityOptions` / `GetSelection`，不选直接回车 = 整个图形）后用 `ApplySample()` / `ApplyCandidates()` 回填、同一实例重开；
  状态行照搬 BLKNUM 三态（尚未选择 / 再点 ② 框选 / 样板 + 可编号数 + 跳过数）。新增命令 `INSTRNUM`（向导；`Modal | UsePickSet`，带预选集兜底：首块当样板、其余按同名收集），
  `INSTRNUMTEST` / `INSTRNUMINFO` 保留。**写回链路实测通过**（2026-10-12，AutoCAD 2025 Plant 3D，脚本全自动无人工点击）：9 块行优先写入 `001..009`、COM 回读一致、
  日志 `=== 编号完成 === 写入 9 个编号，图形已提交并重生成。`；同轮修掉一个真实缺陷 —— 向导成功后必须结束模态循环（否则命令层拿不到 `ShowModalDialog` 返回，`Commit()` 永不执行，
  现象是"向导报成功、属性没变"）。证据见 `port\InstrumentNumbering\docs\验收说明.md` 第 5 节。
  验证：Core/Acad/Ui/UiDemo 构建 0 警告 0 错误、单元测试 41/41、差分 57/57、`--selftest` 全绿。
- **开发环境：仓库根目录已迁移**（顺带修正了旧路径里的一处拼写错误）。
  整个仓库（`.git`、`src`、`deploy`、`docs`、`port`、`runtime`、`tests` 等）原样搬到新路径，git 历史与工作树校验通过
  （`HEAD` 仍为 `3839691`，`git status` 只剩未跟踪的 `port/`）。配套动作：
  ①每用户插件目录 Junction 已重建为指向 `<仓库根目录>\deploy\Bundle\ChemForge.bundle`
  （CAD 的加载入口仍是 `%APPDATA%\Autodesk\ApplicationPlugins\ChemForge.bundle`，插件加载方式不变）；
  ②仓库内所有指向自身的绝对路径已同步（`deploy\Dev\Mount-ChemForgeMenu.ps1`、`Register-ChemForgeMenu.ps1`、
  `docs\DeveloperGuide\*.md`、`port\InstrumentNumbering\**`、`runtime\*.ps1`）；
  ③图块编号源码仓库（私有，未入库）的引用已同步到新路径（第二阶段一并搬迁，见 ⑥）；
  ④迁移期间曾留一个指向新路径的兼容 Junction（旧路径），**同日已删除**（`cmd /c rmdir`
  只删链接，目标仓库与 `<仓库根目录>` 内容无损；删除后旧路径不再存在）。
  ⑤**第二阶段：旧工作区目录本身已彻底消失** —— 其余 4 个子目录整体搬入工作区根目录下
  （`Move-Item`，同盘改名，未复制数据），随后删除该空目录。
  搬迁副作用与处理：`net\.venv`（Python 3.14.4 + pymupdf 1.28.2 + pypdf 6.19.0）的
  `python.exe` 与 `python -m pip` 移动后照常可用（`pyvenv.cfg` 的 `command` 行已改新路径），但
  `Scripts\pip.exe`/`pip3.exe`/`pip3.14.exe`/`pymupdf.exe` 四个启动器把旧路径**内嵌在 exe 内**，
  移动后必然失效，而本机无法重生成它们（`venv --upgrade` / `pip --force-reinstall` 都需要可用临时
  目录，本机 python 的 `tempfile.gettempdir()` 在默认 TMP/TEMP 下直接抛
  `FileNotFoundError: No usable temporary directory found`，属既有环境问题），故这 4 个启动器已删除，
  改用 `<工作区根目录>\net\.venv\Scripts\python.exe -m pip`。

### Fixed
- **「编号用法说明」弹窗正文挤成一行、显示不全**（现象：弹窗正文未换行、显示不全）：
  `NumberingHelpText.DialogText` 原先只做 `TrimStart('\n')`，而整段文案是**纯 LF** 的（`CommandLineText` 每段字面量以 `\n` 开头）——
  WinForms `TextBox` 底层是原生 EDIT 控件，**只把 `\r\n` 当换行**，只给 `\n` 就整段显示成一行
  （本机实测 `SendMessage(EM_GETLINECOUNT)`：LF 版 = 1、CRLF 版 = 3；命令行那份没问题，所以现象是"命令行正常、弹窗一行"）。
  现在 `DialogText` = `CommandLineText.TrimStart('\n').Replace("\r\n", "\n").Replace("\n", "\r\n")`，**只转弹窗**；
  命令行那份保持 LF（`Editor.WriteMessage` 按 `\n` 断行）。新增 `CommandLineText_UsesLfOnly` / `DialogText_DropsLeadingNewlineKeepsSameContent` /
  `DialogText_UsesCrlfOnlyAndIsReallyMultiline` 三个用例锁住这两条，单元测试 **32/32 通过**。
- **「编号用法说明」按钮与同行按钮没对齐**（现象：按钮顶边与同行其它按钮不一致）：
  `NumberingHelpButton.AlignHelp` 把**相对分组框客户区**量出来的行高（`flow.Top + 邻居按钮.Top`）直接赋给
  **相对宿主面板**的 `help.Top`，于是整整多出一个宿主面板的高度。离线复刻该分组框实测：
  按钮顶边比「① 在图纸中选一个样板块」低 **18px**（= `host.Top`）、右边距 22px 而左边距 14px。
  现在两侧都先换算到同一个坐标系再相减（新增 `TopInBox`，不依赖句柄），并把 `RightMargin` 14 → 6
  （组框内容区本身有 8px 内边距，8 + 6 = 14 与左侧按钮的左边距对称）；实测 ΔY = 0、两按钮等高、左右边距都是 14px。
  新增排版回归测试 `NumberingHelpButtonLayoutTests`（离线复刻对话框第一分组框，把旧代码放回去会以 ΔY=18 失败），单元测试 **33/33 通过**。
- **改了菜单定义但菜单栏不变（CUIx 时间戳陷阱，2026-10-13）**：`deploy\Dev\Mount-ChemForgeMenu.ps1` 用 `ZipFile 'Update'` 重写工作空间时，
  会把支持目录 `ChemForge.cuix` 的 mtime 刷到挂载时刻；而插件判断要不要重建菜单文件用的是
  「cuix 时间 < 插件 DLL 时间」（`src\Shared\CAD\Impl\MenuService_Net80.cs:237-249` `IsOlderThanPlugin`，`:109-119` 调用）
  —— 于是「编译 → 挂载」之后 CUIx 恒比 DLL 新，插件永远跳过重建，**改了 `MenuDefinition` 也传不进 CUIx**，
  挂载收割到的还是旧菜单（表现为已删除的三行菜单仍然存在）。
  已在脚本头部 `.PARAMETER CuixSource` 写明正确顺序（① 编译部署 → ② 删/改名支持目录 CUIx 与 `chemforge.mnr`/`ChemForge_light.mnr`
  → ③ 启动 AutoCAD 一次让插件重建权威版本 → ④ 完全退出 AutoCAD 再挂载），并在收割来源处常显一行
  「请核对菜单项数 = 当前 MenuDefinition 的叶子项数」（**刻意不做"来源比插件 DLL 新"的 `Write-Warning`**：
  第 2 步重写工作空间时已把来源 CUIx 的 mtime 刷到本次挂载时刻，该判据恒真、每次挂载都会误报，实测后已删除）。
  引导副本 `deploy\Dev\ChemForge.cuix` 已用插件重建出的权威版本刷新（28698 B → 27435 B，8 个宏、8 个菜单项、8 个功能区按钮）。

### Added
- **新功能：批量修改块属性（`BATATTCHANGE`，菜单 图块批量处理 → 批量修改块属性）** —— 把
  `AttrRep3.0.lsp`（1174 行 AutoLISP，外部文件、未入库，原命令为 `ATTRREP`）
  移植为 ChemForge 内的 C# 实现。**没有新建顶层目录**：代码全部落在既有的空占位目录
  `src\Shared\{Commands,Core,Models,UI}\...\Block\BlockAttr\`（17 个文件），命名空间
  `ChemForge.<层>.Block.BlockAttr`；面板参照 BLKNUM 的 `NumberingOptionsForm` 手写 WinForms 布局。
  能力：对增强属性块的指定属性标签批量修改**文字内容**、**文字选项**（字体样式/对齐/反向/倒置/字高/
  宽度因子/旋转角/倾斜角）与**特性**（图层/颜色/线型/线宽），三个区各有独立开关。
  接线：`CommandNames.BatchAttrChange = "BATATTCHANGE"`、`MenuDefinition` 的「图块批量处理」组新增第 3 项
  （**菜单组仍是 5 组**，不破坏 `Menus_ContainsFiveGroupsInOrder`）、`Shared.projitems` 新增 17 个
  `Compile` 项（全部 `Condition="'$(UseWindowsForms)' == 'true'"`）、`PackageContents.xml` 新增
  `<Command Local="BATATTCHANGE" …>`、`RibbonBuilder.IconMap` 新增 `batattchange` 并新增
  `src\Shared\UI\Resources\Icons\batattchange_{16,32}.png`。
  **实现要点（含理由）**：
  ① **反向/倒置走 ActiveX**（`(AcadAttribute)att.AcadObject` 后设 `Backward`/`UpsideDown` 再 `Update()`）——
  托管 API 在 AutoCAD 2020 与 2025 两代都**没有** `AttributeReference.IsMirrored`/`IsUpsideDown`
  （探针实测缺失，`DBText` 同样没有）；为此 `src\Build\ChemForge.Net80.csproj` 新增
  `Autodesk.AutoCAD.Interop.dll` 与 `Autodesk.AutoCAD.Interop.Common.dll` 两个引用（`<Private>false</Private>`），
  这也是整层**只进 net80** 的原因（`src\Build\lib\Acad2020` 里没有 Interop，net48 编不过）；
  ② **颜色直接写 `AttributeReference.ColorIndex`（组码 62）**，不用 ActiveX 的 `Color` —— 块内 ATTRIB
  只有组码 62 能 100% 表达 256=随层 / 0=随块 / 1-255=ACI；
  ③ **文字对齐只在给了非 0 值时写组码 72**（原插件 `(and just (/= just 0))`）；
  ④ **目标图层恰为 `"0"` 且用户没指定颜色时，颜色按随层(256)写**（否则随块属性跟随块所在层显示成背景色，
  即此前出现的“换成 0 层却还是 000”），并在报告里补一句「（目标图层为 0，自动写入）」；
  ⑤ **有效块名用块表反查实现**（托管 API 无 `EffectiveName`）：`BlockTableRecord.IsDynamicBlock &&
  !IsAnonymous` 的记录取 `GetAnonymousBlockIds()` 建「匿名块 → 定义名」映射，「全图同名块」改用
  `GetBlockReferenceIds(true, true)` 反查而非全库扫 INSERT；
  ⑥ **记忆存到插件自己的配置文件**（`ConfigManager` 目录下的 `BatchAttrChange.xml`），不再用 `setenv`
  污染 AutoCAD 环境变量，语义与原插件一致（跨图纸跨启动；三个修改开关不记忆）；
  ⑦ **属性列表用 `CheckedListBox` 而非多选 ListBox**（DCL 只能 Ctrl/Shift 多选），代价是双击不再等于确定；
  「未启用任何修改项」改为**在面板内拦截**（原插件是关窗后命令行打印），其余文案逐字保留；
  ⑧ 面板记忆**原始 `BatchAttrChangeMemory` 而不是转换后的 `BatchAttrChangeOptions`** —— 否则分不清
  「上次选的是下拉里那一项」与「上次选的是『其他颜色号』并填了数字」（原插件 `recall-set` 也是直接回填
  字符串/索引）。
  **验证**：`dotnet build src\Build\ChemForge.Net80.csproj -c Debug` **0 警告 0 错误**，DLL 里已确认含
  `BATATTCHANGE` 与全部 `BatchAttrChange*` 类型；新增纯逻辑自测工程 `tests\BatchAttrChangeSelfTest\`
  （仿 `tests\BlockNumberingSelfTest\`，用 `<Compile Include>` + `<Link>` 只链入不依赖 AutoCAD 程序集的
  6 个文件），**137 项断言全部通过** —— 覆盖四张候选表的文本/数值/顺序、`JustifyLabel`/`LineWeightLabel`
  反查与兜底、`SafeIndex`/`TextToIndex`、`Normalize()` 降级语义、报告与回读校验文本格式；
  `MenuDefinitionTests` 已同步更新并新增断言。新增文档
  `docs\DeveloperGuide\BatchAttrChange-Integration.md`。**尚未在 CAD 进程内运行验证**（部署目录被运行中的
  AutoCAD 占用，需先退出 AutoCAD 才能部署）。
- **`CHEMFORGE` 命令（菜单「关于 ChemForge（版本信息）」）除命令行输出外弹出关于对话框，并补版权信息**：
  命令行文本与弹窗内容都含版本号与版权；版权行与 `README.md` 的「版权」一节一致
  （`Copyright © 2026 ChemForge Contributors. 保留所有权利。`），版本号取程序集 `AssemblyVersion`
  并显示成三段（`Version.ToString(3)` → `1.0.0`）。新增 `src\Shared\UI\Forms\Common\AboutDialog.cs`
  （WinForms 纯代码布局，不用设计器，字体取 `SystemFonts.MessageBoxFont`），在 `Shared.projitems` 里按
  `$(UseWindowsForms)` 条件登记 —— 只有 Net80 编译它；`CommonCommands.cs` 里的调用用 `#if NET80` 包住，
  其余目标框架仍走 `Application.ShowAlertDialog`（行为不变）。有活动文档时命令行照旧打印同一段文本。
  验证：Net80/Net48 构建 0 警告 0 错误、单元测试 27/27；**CAD 内实测**（2026-10-11，AutoCAD 2025 Plant 3D）
  键入 `CHEMFORGE` 弹窗正常渲染（标题「关于 ChemForge」，含 `ChemForge` / 工艺匠造工坊 / 版本 1.0.0 /
  标语 / 版权两行 / 确定按钮，448×283）。
- **整合图块编号功能（原 BlockNumbering 插件，源码逐字节同步）**：该插件（源仓库私有、未入库）的
  BlockNumbering（AutoCAD 变体）18 个源文件同步进 `src\Shared\BlockNumbering\`，接到 ChemForge 的
  菜单/功能区/命令清单：新增并列分组**图块编号**（属性块批量编号 `BLKNUM`、编号用法说明 `BLKNUMHELP`），
  命令 `BLKNUMINFO`（插件自检）只登记命令不进菜单；`PackageContents.xml` 同步补三条命令。
  工程侧：`Shared.projitems` 按 `$(UseWindowsForms)` 条件登记该层（**只在 Net80 编译**，与原插件一致）、
  Net80 增加 `BLOCKNUMBERING_ACAD` 宏、并按项目静默 CS1591/CS1573（同步层不带 XML 注释，避免改动源文件）。
  **唯一源码偏差**：`Commands.cs` 的 `BlockNumberingApp` 去掉 `IExtensionApplication`
  （同程序集两个实现时 AutoCAD 只挑一个调用，而 `PluginInitializer` 必须每次都跑——它负责按
  `MenuDefinition` 重新生成 CUIx），改由 `PluginInitializer.Initialize()/Terminate()` 显式转发，对外行为不变。
  新增 `deploy\Dev\Sync-BlockNumbering.ps1`（复制 + 逐字节校验 + 算法自测，带 `-Verify`）与
  `tests\BlockNumberingSelfTest`（65 项断言，链接**合并后**的源码），详见
  `docs/DeveloperGuide/BlockNumbering-Integration.md`。
  验证：Net80/Net48 构建 0 警告 0 错误、ChemForge 单元测试 27/27（原 4 个分组用例已按五分组更新）、
  图块编号算法自测 65/65；**CAD 内实测通过**（2026-10-11，AutoCAD 2025 Plant 3D）——菜单栏
  `… Express ChemForge` 渲染正常（COM `MenuBar` 14 项；PNP 组里的 ChemForge 菜单 14 项 =
  10 个命令 + 4 条分隔线，顺序与 `MenuDefinition` 分组一致）、`BLKNUMINFO` 打印自检
  （`已注册命令: BLKNUM / BLKNUMHELP / BLKNUMINFO`，程序集路径指向 bundle）、功能区面板 5 个
  （含 `CF_PANEL_BlockNumberMenu`）已写入 CUIx 与各工作空间引用。
  `BLKNUM` 的对话框交互（选样板块 → 框选 → 写入）属人工验收项。
  配套调整：`Mount-ChemForgeMenu.ps1` 的分隔线位置改为数组 `$separatorBefore = @(3,5,7,9)`
  （原为写死的 3/5/7），否则五分组会少一条分隔线。
- **一键安装包接线：Inno Setup 安装/卸载自动完成全部机器状态改动（`deploy\Installer\ChemForge.iss`）**：
  `[Files]` 把 `Register-ChemForgeMenu.ps1`、`Mount-ChemForgeMenu.ps1` 与引导副本 `ChemForge.cuix`
  一并装到 `{app}\Tools`（三者同目录，脚本按 `$PSScriptRoot` 互相查找；备份写 `%TEMP%`，与仓库路径无关）；
  `[Code]` 在 `ssPostInstall` 调用 `Register-ChemForgeMenu.ps1`（主 CUIx 的 `PartialMenuFile` 注册 +
  出厂 `PnP.cuix` 扁平菜单挂载 + 各工作空间 `WSPop`/功能区引用 + 配置文件布局条目），
  在 `usUninstall` 调用 `-Uninstall -RemoveFile` 撤销上述改动；退出码非 0 时弹框说明原因
  （**退出码 2 = 本机还没有 AutoCAD 用户支持目录**，即安装后尚未启动过 AutoCAD → 先启动一次再完全退出，
  然后运行脚本）。另加程序组快捷方式"ChemForge 菜单注册（先退出 AutoCAD）"用于补跑。
  安装包需在**使用 AutoCAD 的那个 Windows 账号**下运行（注册写 `HKCU` 与 `%APPDATA%`）。
  配套改动：`Register-ChemForgeMenu.ps1` 在找不到支持目录时不再直接抛异常 —— 安装路径打印指引并
  `exit 2`，卸载路径视为"无需撤销"`exit 0`。
  **本机未安装 Inno Setup（无 `ISCC.exe`），`.iss` 未经编译验证**；已完成的是字段级人工核对与
  UTF-8 BOM 修正（含中文的 `.iss`/`.ps1` 必须带 BOM，否则 Windows PowerShell 5.1 按 GBK 解析报错）。
- **参考文档 `docs/DeveloperGuide/MenuMounting-Reference.md`（HGCAD v3.0.6 拆包实证）**：
  把 HGCAD 安装包完整拆开（载荷全在 PE `.rsrc` 的自定义资源里：415 条 `FILE` 资源 =
  `uint32 原始大小` + 5 字节 LZMA 属性头 + 裸 LZMA1 流，配一条 UTF-16 的 `JSON` 索引给出真实路径，
  共还原 415 个文件），实证它的挂菜单路线：**自带局部 CUIx + 根弹出菜单别名 `POP1` +
  `(menucmd "P10=+HGCAD.POP1")`** —— 不碰宿主主 CUIx、不写配置文件布局 `Menus`、不改工作空间，
  宿主侧只改 `TRUSTEDPATHS`（它甚至把 `WSAUTOSAVE` 设回 1）。对照结论：ChemForge 的根菜单别名
  `ID_ChemForgeMenu` **不在官方规定的 POP1–POP16 区间**（"加载 CUIx 时 AutoCAD 自己上菜单栏"
  的那一档），这才是必须改宿主 `PnP.cuix` 写 `<WSPop>` 的根本原因。文档给出三个可执行选项与
  实测判据（未复现的部分已按要求标注为假设）。

### Removed
- **清理无调用死代码（外部审查 P2 中"暂缓"的那批，逐项核对引用面后执行）**：
  删除 `src\Shared\Core\Text\TextReplaceEngine.cs`（全仓库仅自身 4 处 + 工程登记，生产 0 调用者）、
  `src\Shared\UI\Resources\Strings.resx`（0 引用，连 XAML 也没有）、
  `ConfigManager.ReadText`/`WriteText`（0 调用者；`Initialize`/`GetConfigFilePath` 仍在用，保留）、
  `ExtensionMethods.ToDegrees`/`SafeString`（各 0 调用者；同文件 `ToRadians` 被 `TextObliqueCommand` 使用，保留），
  并从 `src\Shared\Shared.projitems` 摘掉 `TextReplaceEngine.cs` 与 `Strings.resx` 的登记。
  **刻意保留（不是漏删）**：`UI\` 下 6 个界面文件（TCOLOR/TANGLE/TWIDTH/RENBLK/TAGNO 尚未实现，
  它们是界面脚手架；且 `TextFormatViewModel`/`NumberingViewModel` 仍引用活类型 `TextFormatOptions`/`NumberingConfig`）、
  `Core\Block\BlockRenamer.cs`（**唯一真正实现了块改名逻辑**的代码；`BlockManager.RenameBlocks` 目前是桩 ——
  `Logger.Info` + `// 具体实现需通过 CAD 抽象层操作块表` + `return 0;`，将来应把 `BlockRenamer` 接进它）、
  `UI\Resources\Icons\*.png`（**不是死代码**：`Shared.projitems` 以 `<EmbeddedResource>` 打包，
  `RibbonBuilder.GetIconResource`/`LoadIcon`/`EnsureIconFile` 读取它并落到 CUIX 旁供功能区按钮使用）。
  验证：Net80 与 Net48 构建 0 警告 0 错误，26 个单元测试全部通过。

### Fixed
- **修复经典菜单栏 ChemForge 下拉里每项左侧的 "?" 图标**：挂载脚本从插件 CUIx 收割的 10 个宏里带着
  `<SmallImage Name="chemforge_*_16.png" />` / `<LargeImage Name="chemforge_*_32.png" />` —— 这是
  **文件名引用**，指向内嵌在 `ChemForge.cuix` 里的 PNG；宏被搬进出厂 `PnP.cuix` 后，支持目录里并不存在
  这些文件，AutoCAD 就渲染成缺图占位 "?"（工厂宏用的是 `<SmallImage Name="RCDATA_ICO_..."/>` 内嵌资源名，
  所以工厂菜单有图标；新加的两个图块编号项没有图片引用，也就没有 "?" —— 与现象完全吻合）。
  `Mount-ChemForgeMenu.ps1` 新增 `Remove-MenuImageRef`，收割菜单项与宏时删掉全部 `SmallImage`/`LargeImage`
  元素：**宁可没有图标，也不要问号**（功能区按钮图标走 CUIx 内嵌资源那条路，不受影响）。
  重挂载后校验：`CF macros: 10` / `image refs inside CF macros: 0`。
- **修复属性块批量编号（`BLKNUM`）对话框「效果」行前缀长时显示不全**：`NumberingOptionsForm` 里
  「编号规则」组的行高在 `TableLayoutPanel` 上写死 74
  （`root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74f));   // 编号规则`），小于组自身的 96
  （`NewGroup("编号规则", 96)`）→ 组被裁短；且 `_lblSample` 是 `Label` + `AutoSize=false` + `AutoEllipsis=true`，
  只能单行省略号（5 个样例约 160 字符、一行只放得下约 127 字符），于是前缀一长「效果」行就被截断。
  现改为**多行只读文本框**（`Multiline=true` / `WordWrap=true` / `ScrollBars=Vertical` /
  `BorderStyle=None` / `Dock=Fill`，保留原前景色与边距），并把行高统一为 112
  （`NewGroup(..., 112)` 与 `RowStyle(..., 112f)` 两处一致）。改动落在**源仓库**再同步，
  仍满足"逐字节一致"。
  后续按使用反馈再调（同一轮）：样例数由 6（5 个 + "..." + 末位）降为 **4**（`DescribeSample(4, probe, 4)`，
  不再出现省略号），行高再由 112 提到 128 —— 实测最小窗口（780×640）下 4 个样例一行显示完整，
  前缀再长才会换行；随后按反馈**去掉右侧滚动条**（`ScrollBars=None`）—— 4 个样例一行放得下，
  滚动条纯属多余（真装不下时仍可用滚轮/方向键滚动，悬停提示保留全文）。
  实测（2026-10-11，AutoCAD 2025 Plant 3D）：`BLKNUM` 对话框效果行 4 个样例一行完整显示、无滚动条。
- **同步源仓库路径更新**：BlockNumbering 源仓库（私有，未入库）路径已变更，
  `Sync-BlockNumbering.ps1`（改从环境变量 `CHEMFORGE_BLOCKNUM_SOURCE` 读取源路径）、`PackageContents.xml`、
  自测工程、Net80 工程、`Shared.projitems`、`CommandNames.cs`、`PluginInitializer.cs` 与相关文档里的
  17 处字面量同步替换；`-Verify` 通过（18 个源文件 + 自测程序逐字节一致，算法自测 65/65）。
- **插件运行时目录改为"仓库优先、用户目录兜底"，不再写入已安装的插件目录**：
  `PluginInitializer.GetConfigDirectory()` 原先的三级回退是"仓库根 `runtime`（向上找 `ChemForge.sln`）→
  插件 DLL 同目录 `runtime` → `%LocalAppData%\ChemForge`"。经 Junction 部署
  （`%APPDATA%\Autodesk\ApplicationPlugins\ChemForge.bundle` → `deploy\Bundle\ChemForge.bundle`）时，
  第二级会把日志/配置写进**插件安装目录**：实测日志落在
  `deploy\Bundle\ChemForge.bundle\Contents\net8.0\runtime\ChemForgePlugin.log`，而仓库
  `runtime\ChemForgePlugin.log` 自 2026-10-07 起不再更新；正式安装后同样会污染 `%PROGRAMDATA%` 下的
  bundle、卸载后留残渣。现改为：仓库 `runtime` → `%LocalAppData%\ChemForge`（**删掉"插件 DLL 同目录"这一级**）。
  同时 `ResolveRealPath` 增加退化路径：文件句柄解析失败（受管环境不允许打开已加载的程序集）时改用
  `Directory.ResolveLinkTarget` 解析所在目录（`#if NET80`，老目标框架保持原行为），并拆出
  `TryResolveFileHandle`/`StripLongPathPrefix`。启动时新增一行 `运行时目录：…` 日志，排查时不必再猜日志落在哪。
  验证：Net80 与 Net48 构建 0 警告 0 错误，26 个单元测试全部通过。
- **修复"多目标构建互相顶掉产物"：`net48` 交付目录里装的其实是 net8.0 程序集**：`src\Build` 下 6 个工程都落在默认中间目录 `obj\$(Configuration)\`（Net80 因 `AppendTargetFrameworkToOutputPath=false` 也落在 `obj\Debug\`），于是"先建 Net80、再建 Net48"时，Net48 的增量检查把 Net80 刚写出的 `obj\Debug\ChemForge.dll` 当成自己的产物 → **跳过编译**，再把它复制进 `Contents\net48\`。实测证据：两份 DLL 同为 76800 B、SHA256 完全相同，且 net48 那份里含 `.NETCoreApp,Version=v8.0` 字符串（真实应为 `.NETFramework,Version=v4.8`）——即 2019–2024 的 net48 交付物是错的。现为 6 个工程各设独立中间目录（`obj\$(Configuration)\net20|net35|net40|net45|net48|net80\`）。修复后验证：net8.0 产物 76800 B / `.NETCoreApp,Version=v8.0`，net48 产物 55296 B / `.NETFramework,Version=v4.8`，两者哈希不同；并按"先 Net80 再 Net48"的增量顺序复跑，不再互相覆盖；26 个单元测试仍全部通过。
- **外部审查整改（P0/P1/P2 逐条落实）**：按第三方对代码库的审查意见修改。**P0（构建与可见性）**：
  （1）`ChemForge.sln` 摘掉 Net20/35/40/45 四个薄壳的 `.Build.0` —— 它们的 csproj 里 AutoCAD
  引用仍是 `<!-- TODO -->`、`string.IsNullOrWhiteSpace`（.NET 4.0 起才有）也不兼容，"解决方案能编译"是假象；
  （2）`Shared.projitems` 的 6 个 UI `<Compile>` 与 2 个 `<Page>` 加
  `Condition="'$(UseWPF)' == 'true'"`（旧式 csproj 不导入 WPF 目标、`<Page>` 被忽略，Net48 必然报
  `CS0103: InitializeComponent`），并把 Net80 工程的 `Shared.projitems` 导入移到 `UseWPF` 定义之后；
  Net48 工程默认 `AcadApiLib` 由不存在的 `..\lib\Acad2020` 改为实际存在的 `lib\Acad2020`，
  并在缺少 .NET Framework 4.8 目标包时回退到 NuGet 缓存里的参考程序集 ——
  **Net48 现已可在本机直接 `dotnet build` 通过（0 警告 0 错误）**；
  （3）`CHEMFORGE` 命令原来用 `Console.WriteLine`（AutoCAD 里永远看不到输出），改为有文档时
  `Editor.WriteMessage`、启动页无文档时 `ShowAlertDialog`。**P1（正确性）**：
  （4）`TextFormatOptions.ObliqueAngle` 由 `double` 改 `double?` —— 原来用 0 当"未设置"哨兵
  （`HasObliqueAngle => ObliqueAngle != 0.0`），倾角永远无法改回 0°（`WidthFactor` 保留哨兵写法：
  0 不是合法宽度因子）；
  （5）`PipeBreakEngine` 的图层比较改用 `StringComparison.OrdinalIgnoreCase`；
  （6）`PluginInitializer.ResolveRealPath` 不再硬编码 `\\?\D:\`/`\\?\C:\`，改为通用剥 `\\?\`
  （含 `\\?\UNC\` 还原为 `\\`）前缀；
  （7）`MenuService_Net48` 整篇重写：删除运行时 COM 建菜单 / `InsertInMenuBar` / Idle 重试
  （与声明式 CUIx 路线冲突、崩溃高风险），只保留 `MENUBAR=1` 复查与日志，
  挂载统一交给声明式 CUIx 路线。**P2（可维护性）**：
  （8）新增 `Application/CommandNames.cs`：命令名原本三处维护（`[CommandMethod]`、菜单宏、
  `RibbonBuilder.IconMap`），现统一引用常量，并加单元测试把菜单宏锁到常量上；
  （9）`ICADServiceFactory`/`CADServiceFactory` 删除 `IsSupportedVersion`（0 调用点，工厂是编译期
  `#if` 选择）与死变量；`MenuBuilder.UnloadMenu` 复用 `CreateMenuService()`；
  （10）`NumberingEngine` 增加 `Increment == 0` / `Digits <= 0` 参数校验（原来会生成重复号或抛
  `FormatException`）；
  （11）`Logger` 增加运行期轮转（每 200 条查一次文件大小，超 4MB 即轮转；原来只在启动时检查，
  长会话可无限增长，本机出过 5.28MB 垃圾日志）；
  （12）`MenuDefinition.Menus` 改 `IReadOnlyList`；`IMenuService` 注释澄清 `cuixPath` 只是提示路径。
  文档漂移同步修正：`API_Reference.md`（`ObjectId`→`CadObjectId`、`PipelineBreaker`→`PipeBreakEngine`、
  工厂改为"编译期选择"）、`Architecture.md`、`Features.md`（菜单树四分组、未实现项如实标注）、
  `README.md`（版本支持现状与构建命令）。测试新增 `NumberingEngineTests`（5 例）与
  `TextFormatterTests`（4 例，含"倾角显式置 0 必须写入"的回归用例），合计 **26 例全部通过**。
  死代码清理见上方 `### Removed`（UI 层与 `BlockRenamer` 经引用面核对后刻意保留）；"关于"分组首项分隔线
  继续保留（扁平挂载靠它做分组）。
- **修复"一键安装测试.bat 实际什么都没执行"与"菜单挂载被静默跳过"（部署脚本两处缺陷）**：
  （1）`deploy\Dev\Install-Dev.ps1`、`Register-ChemForgeMenu.ps1`、`Uninstall-Dev.ps1` 原来都是**无 BOM**
  的 UTF-8 文件，而本机系统 ANSI 代码页为 936 —— Windows PowerShell 5.1 读取无 BOM 脚本时按 GBK 解码，
  中文串被撕坏，解析直接报 `Unexpected token '}'` 一类语法错误，于是 `deploy\Dev\一键安装测试.bat`
  **从未真正装成功过**（实测：把脚本复制到 `%TEMP%` 单独运行即复现，退出码 1）；现补 UTF-8 BOM，
  同一副本已可正常解析执行。
  （2）`Install-Dev.ps1` / `Register-ChemForgeMenu.ps1` 原来用 `[scriptblock]::Create((Get-Content -Raw …))`
  调用子脚本，而 `[scriptblock]` 内的 `$PSScriptRoot` 是**空值** → `Register-ChemForgeMenu.ps1` 中
  `Join-Path $PSScriptRoot 'Mount-ChemForgeMenu.ps1'` 出来的路径失效，该异常被外层 catch 吞成一条警告，
  **菜单挂载这一步从未执行**；现改为子 PowerShell 进程 + `-File` 调用（保住 `$PSScriptRoot`），
  并按 `$LASTEXITCODE` 判失败后 `throw`，不再静默跳过。
  部署脚本的解析已复验；菜单挂载步骤的实测仍待关闭 CAD 后随下一次安装一起做。
- **修复"切换工作空间卡顿 / 连续切换几次后 CAD 崩溃"（插件侧根因：每次空闲一次 COM 调用 + 切换后反复删注册表）**：
  `MenuServiceNet80` 的 Idle 钩子原来**每帧**都调用 `TrackWorkspaceSwitch()`，即每次空闲发一次
  `Application.GetSystemVariable("WSCURRENT")` COM 调用；Idle 触发极密集（鼠标移动即触发），
  这是切换工作空间（以及日常操作）卡顿的主要来源。更糟的是检测到切换后它会开一个 20 秒
  "补挂窗口"，窗口内每 5 秒执行一次 `RemoveChemForgeLayoutEntry()`（以可写方式打开
  `HKCU\...\Profiles\<配置>\Menus` 删值）——而此刻 AutoCAD 正在按新工作空间重写同一批键值，
  两边抢同一批注册表值；叠加上探测到功能区缺失就重建 WPF 对象，多次切换后表现为进程崩溃
  （`0xc0000374` ntdll 堆损坏）。本次改动：
  （1）**删除 `TrackWorkspaceSwitch()` 与 20 秒补挂窗口**——菜单栏由工作空间定义原生驱动，
  切换本就不丢菜单（见下方 PNP 静态挂载条目），轮询与删值没有任何收益；
  （2）**整个删除 `RemoveChemForgeLayoutEntry()`**（含启动时的那一次调用）：根菜单改用
  `POP16` 别名后，AutoCAD 自己写进配置文件的布局条目（如 `Pop14 = PNP pop13`）是**合法**的，
  插件再去删就是与宿主抢同一批键值；升级遗留的 CHEMFORGE 条目改由部署脚本
  `deploy\Dev\Install-Dev.ps1` 的 `Clear-StaleProfileMenus` 在 CAD 关闭时清理；
  （3）删除死代码 `EnsureMenuInBar()` / `CreateRootMenuInGroup()`：其中
  `InsertInMenuBar` + `Application.ReloadAllMenus()` 是进程内 COM 挂菜单的最后调用点
  （自 `bdae121` 起已不可达）；
  （4）Idle 钩子只剩轻量复查、都按 5 秒节流且统一用 `DateTime.UtcNow`：功能区探测 +
  `MENUBAR` 复查（`EnsureClassicMenuBarVisible`）+ 主窗口置顶复查（`EnsureMainWindowNotTopmost`）；
  （5）功能区**连续重建超过 3 次**后探测间隔自动放大到 60 秒并打一条警告日志，避免
  "缺失 → 重建 → 再被清掉"的循环每 5 秒创建一套 WPF 对象（堆压力来源之一）。
  结果：插件代码中已无任何 `GetSystemVariable("WSCURRENT")` 调用（从"每帧一次"降为 0 次），
  `ChemForge.Net80` 编译 0 警告 0 错误。CAD 界面/长稳验收待本机环境恢复后补做（本机 CAD 受安全管控软件影响无法启动）。
- **菜单挂载改为"`POP16` 别名 + CUIx 声明式功能区"（摘掉运行时挂 UI 的最后一段代码）**：
  依据两条实证（官方规则 + HGCAD v3.0.6 拆包，见 `docs/DeveloperGuide/MenuMounting-Reference.md`）：
  官方规定"加载自定义 CUIx 时，别名 `POP1`–`POP16` 的下拉菜单会**自动**出现在菜单栏上，
  其余菜单必须加入工作空间才显示"，而 ChemForge 的根菜单别名原本只有 `ID_ChemForgeMenu`
  （不在该区间）——这才是必须改宿主 `PnP.cuix` 写 `<WSPop>` 的根本原因。本次改动：
  （1）`MenuDefinition` 新增 `RootMenuBarAlias = "POP16"`（本机实测 `POP0`–`POP13` 已被
  PnP/acad/acetmain/dbcon 占用，`POP14`–`POP16` 空闲，取末尾的 `POP16`）与
  `RibbonTabAlias = "ID_ChemForgeTab"`；
  （2）`MenuServiceNet80.CreateMenuFile()` 生成的根 `PopMenu` 现在带**两个**别名
  （`ID_ChemForgeMenu` + `POP16`）；
  （3）功能区改为在 CUIx 里**声明式**定义（AcCui 的 `RibbonPanelSource` + `RibbonRow` +
  `RibbonCommandButton` + `RibbonTabSource`，选项卡 `ElementID` 用 `RibbonBuilder.TabId`，
  `WorkspaceBehavior = MergeOrAddTab`），由 AutoCAD 随 CUIx 加载/工作空间切换自己重建——
  官方 App Manager 的局部 CUIx 就是这么做的（本机 `AppManager.cuix` 实证其声明式
  `RibbonTabSource` 会被画进系统"附加模块"选项卡）；
  （4）运行时 `RibbonBuilder` 保留为兜底：`FindTab()` 现在同时按 `Id` 与标题识别，
  声明式选项卡存在时不再另建一套 WPF 对象；`RemoveRibbon()` 只拆**本会话自建**的那个
  （新增 `_runtimeTabCreated` 标记），不会误拆 AutoCAD 自己的声明式选项卡；
  （5）图标为 best-effort：`CreateMenuMacro` 的图片参数走 `RibbonBuilder.EnsureIconFile()`
  把嵌入 PNG 释放到 CUIx 同目录并按文件名引用；该路径依赖 `CustomizationSection.HostServices`，
  无宿主服务时会抛异常（本机独立试跑实测），故 `CreateMenuFile` 拆成
  "带图标 → 失败则用**全新的** `CustomizationSection` 以纯文字按钮重来一次"，
  保证菜单文件一定生成成功。
  验证：一个独立的离线试跑装置（不启动 CAD，反射调用 `CreateMenuFile`）产出的
  `ChemForge.cuix` 已核对——根菜单双别名（`ID_ChemForgeMenu` + `POP16`）、4 个
  `RibbonPanelSource`、`RibbonTabSource UID="CF_RIBBON_TAB_CHEMFORGE"` 与 4 条
  `RibbonPanelSourceReference` 齐全，8 个宏与面板按钮的 `MacroID` 一一对应。
  待 CAD 内实测确认（菜单栏是否出现第二个 ChemForge = `POP16` 自动上栏生效的证据，
  确认后可撤掉 `deploy\Dev\Mount-ChemForgeMenu.ps1` 写宿主工作空间那一步）。
- **修复 CAD 主窗口"总在最前"（盖住其它软件）**：AutoCAD 会话中模态对话框异常关闭后，
  主窗口会残留 `WS_EX_TOPMOST` 标志，导致其它应用点不上去。插件新增
  `MenuServiceNet80.EnsureMainWindowNotTopmost()`，在启动、文档激活与每 5 秒空闲探测时
  检查该标志：**只有标志存在时**才 `SetWindowPos(HWND_NOTOPMOST,
  SWP_NOSIZE|SWP_NOMOVE|SWP_NOACTIVATE)` 清除，不存在时不做任何事（不移动/缩放/激活窗口，
  不影响其它软件使用）。实测：正常启动不干预；人为置顶后 3 秒内自动恢复。
  手动兜底脚本 `deploy\Dev\Fix-AutoCADTopmost.ps1` 保留。
- **菜单栏最终形态：把菜单挂进出厂 PNP 组（扁平结构），重启与切换工作空间都不掉**：
  实测（Plant 3D 2025）经典菜单栏真正能稳定画出的只有一种形态——把弹出菜单定义写进**出厂
  主定制文件 `PnP.cuix` 的 PopMenuRoot（PNP 组）**，并满足三个条件：
  （1）菜单必须是**扁平**的（根菜单 + `<PopMenuRef>` 子菜单即使能加载进 PNP 组也**不会被画出来**，
  实测 COM 里 `[54] ChemForge count=4` 但界面空白），故把四个分组改成**分隔线**分组；
  （2）工作空间 `<WSPop>` 的 `MenuGroup` 必须写**被挂载菜单所在组的真实名字 `PNP`**
  （写 `ACAD`/`CHEMFORGE` 均不生效）；
  （3）配置文件布局条目必须是 `Pop<n> = PNP pop<n>` 且落在 `Pop1..PopN` **连续块**内
  （缺号如 `Pop5` 会让其后条目全部被忽略，`Pop20+` 大号槽位被忽略）。
  改为该形态后实测：启动出现 `… 帮助(H) Express ChemForge`，**重启、切换工作空间
  （草图与注释 / 三维建模 / 三维管道）后均保持**；`MenuBar` COM 集合与实际界面一致。
  新增部署脚本 `deploy\Dev\Mount-ChemForgeMenu.ps1`（幂等、可 `-Uninstall`）：从插件生成的
  `ChemForge.cuix` 收割菜单项与宏（标签/命令单一来源），改写成扁平菜单写进 `PnP.cuix`，
  同步写宏定义、工作空间 `WSPop(MenuGroup="PNP")` 与布局条目；安装时清理历史遗留的
  `MenuGroup="CHEMFORGE"` WSPop 与 `^CHEMFORGE` 连续块布局条目（后两者是丢项/切换丢菜单的根源）。
  已接入 `Register-ChemForgeMenu.ps1` → `Install-Dev.ps1` / `Uninstall-Dev.ps1`（安装包路径），
  子脚本用 scriptblock 执行以兼容禁止 `& *.ps1` 的执行策略。
- **根治经典菜单栏不显示 ChemForge（真根因：配置文件布局里的 CHEMFORGE 条目）**：
  AutoCAD 2025 的经典菜单栏要让本插件菜单出现，只需要两件事——`ChemForge.cuix` 作为
  `PnP.cuix` Header 的 `<PartialMenuFile>` 局部菜单加载，以及各工作空间定义里的
  `<WSPop MenuGroup="CHEMFORGE" pUID="ID_ChemForgeMenu">` 引用；满足这两点后
  **AutoCAD 自己会把该弹出菜单追加到菜单栏末尾并渲染**（界面实测 `… 帮助(H) Express ChemForge`）。
  但前提是 Profile 布局（`HKCU\...\Profiles\<配置>\Menus`）里**没有任何指向 CHEMFORGE 的
  连续块条目**（`Pop1..PopN`）：只要存在 `Pop<n> = CHEMFORGE id_chemforgemenu`（或
  `CHEMFORGE pop1`），AutoCAD 恢复菜单栏时就报"不能加载菜单项: CHEMFORGE:ChemForge"
  并丢掉该项——表现为 COM 的 `MenuBar` 集合里明明有 ChemForge、界面上却只画到 Express。
  历史上"补写布局条目"和"清掉布局条目"两种做法轮流'失败'，正是因为补写本身就在制造这个
  毒条目。现 `MenuServiceNet80.EnsureMenuBarLayoutEntry` 改为 `RemoveChemForgeLayoutEntry`：
  **只删不写**（删除所有 `Pop<500` 且值以 `CHEMFORGE` 开头的条目；AutoCAD 自己写在 5xx
  命名空间的 `Pop504 = CHEMFORGE id_chemforgemenu` 无害，保留），
  且**只在插件启动时执行一次**（2026-10 起不再在文档激活/工作空间切换后重复执行——
  切换后反复删值会与 AutoCAD 重写布局的线程抢同一批键值，见上方"切换工作空间卡顿"条目）。
  另新增 `EnsureClassicMenuBarVisible`：`MENUBAR` 随图纸
  保存、新建图纸与「草图与注释」工作空间下默认 0，而 ChemForge 项只在 AutoCAD**构建菜单栏
  那一刻**从工作空间追加，之后再打开"显示菜单栏"不会补进来（实测栏里只有 12 个出厂菜单 +
  Express），故启动/换图/空闲探测时统一置 `MENUBAR=1`。
  实测：干净布局 + `MENUBAR=1` 启动 → 菜单栏出现 `… Express ChemForge`，连续多次重启可复现；
  人为写入毒条目后启动，插件即删除该条目且当次会话菜单栏仍可见 ChemForge。
  `EnsureMenuInBar`/`CreateRootMenuInGroup`（COM 在 PNP 组建菜单 + `InsertInMenuBar` +
  `Application.ReloadAllMenus()`）曾"保留为会话内兜底"，2026-10 已**删除**：静态挂载已足够，
  且该分支自 `bdae121` 起就不可达，进程内 COM 挂菜单只会徒增崩溃风险
- **NETLOAD/打开/保存退化为命令行提示并立即取消（FILEDIA=0）根治**：本机存在 FILEDIA 被置 0
  且逐会话稳定的残留（来源排查：注册表/配置文件均无该值、模板与图纸皆读出 0，未找到写入方）。
  插件现于**启动时与每张图纸创建时强制 `FILEDIA=1`**（`EnsureFileDialogsEnabled` +
  `DocumentCreated` 钩子），保证文件对话框始终可用；另清除了遗留的
  Editor Configuration `LogFilePath`（此前指向仓库根目录，导致 Drawing1_*.log 落在仓库内）


### Added
- **命令更名：去除 `CF_` 前缀，改用短英文连写命令名**（避开原生命令/Express 冲突，≤8 字符）：
  `TCOLOR`（文字颜色）/ `TANGLE`（文字倾角）/ `TWIDTH`（文字宽度因子）/
  `RENBLK`（统一块名）/ `BLKCNT`（图块统计）/ `TAGNO`（仪表位号编号）/
  `CHEMFORGE`（关于，原生 ABOUT/VERSION 已被占用）；管线打断统一为 `PIPEBREAK`，
  **删除 `CF_BREAKPIPE` 别名命令注册**。菜单宏、功能区图标映射、单元测试与全部文档同步更新；
  支持目录 CUIx 将在下次启动时按新宏自动重生成
- **功能总说明（docs/UserGuide/Features.md）**：按"埋单分组 × 命令 × 实现状态"如实梳理
  大插件内各功能的定位、菜单位置、交互流程与完成度——核对源码后确认目前**仅管线自动打断
  完整可用**（引擎直接调用 AutoCAD API），文字/图块/仪表等命令入口已挂但依赖的
  CAD 抽象层（CADServiceNet80 等）尚未实现，点击暂无实际效果；README 功能表与
  用户手册首页同步更新并纠正了旧文档中已不存在的 `CF_BLOCKATTR` 等内容
- **管线自动打断并入主插件**：PipeBreak v2 逻辑自 `src/Plugins/PipeBreak` 移植进共享层
  （`Commands/Pipe/PipeBreakCommand.cs` + `Core/Pipe/PipeBreakEngine.cs`），
  以**静态** `[CommandMethod]` 注册 `CF_BREAKPIPE`（菜单别名，与 `MenuDefinition` 宏对应）
  与 `PIPEBREAK`（历史别名），随 `ChemForge.dll` Net48/Net80 构建发布；
  菜单 工具 → 管线自动打断 现已可直接使用，无需 NETLOAD 独立插件
  （顺带修复独立插件版本 `CommandMethod` 挂在实例方法上、AutoCAD 实际不注册命令的问题）
- **菜单宏改为纯命令名**：实测 AutoCAD 2025（PNP 组 COM 挂载 / CUIx 生成）环境中，
  菜单宏的 `^C^C` 前缀未被 AutoCAD 解释为取消序列，整串 `^C^C_CF_BREAKPIPE` 被当作
  字面命令名送入命令行（错误"未知命令 ^C^C_CF_BREAKPIPE"）。MenuDefinition 菜单宏
  统一改为纯命令名 + 尾随空格（如 `CF_BREAKPIPE `，空格即 AutoLISP 宏的回车确认，
  否则点击后需手动回车才能执行），与 RibbonBuilder.ToCommandLine 剥离后的
  实际发送形式一致，菜单栏/功能区两入口行为统一
- **跳过原因分类提示**：管线打断不再笼统输出"跳过 N"，
  新增 `[跳过明细]` 输出（高程不一致 / 交点位于管端点 / 投影不在管线 / 间距不足分割失败 / 分割点贴端点），
  并在分割失败时提示最小所需间距（半宽），便于用户判断是宽度不够还是管线过短
- 初始化项目骨架（解决方案、共享项目、薄壳项目）
- 建立 CAD 抽象层核心接口
- 建立按菜单层级组织的命令层与业务逻辑层子目录结构
- 菜单栏骨架：启动时由 MenuBuilder 自动生成并挂载 CUIx 下拉菜单（文字 / 块 / 工具，其余位置预留空位）
- 菜单结构单一数据源 MenuDefinition 与按版本隔离的 IMenuService 实现
- 新增功能的六层占位目录（Commands / Core / Models / UI·Forms / UI·Views / UI·ViewModels）
- 开发期手动测试工具：deploy/Dev 一键编译部署与卸载脚本
- 迁移管线打断插件 PipeBreak v2 至 src/Plugins/PipeBreak（v2 性能重构源码 + 2020 版 API 参考程序集 + 说明文档），
  保留 `PIPEBREAK` 命令并新增菜单别名 `CF_BREAKPIPE`；插件版本记录并入本文件，小插件不再单独记录
  （该独立插件目录随后已删除：代码并入共享层，仅保留其 2020 版 API 参考程序集，见 Removed）
- 编译环境落地：本机安装 .NET 8 SDK（8.0.425，用户目录）；Net80 薄壳接入 AutoCAD 2025 托管 API
  （acmgd/acdbmgd/accoremgd/AcCui，路径可用 /p:AcadSdkDir 覆盖）；Net48 薄壳接入 PipeBreak 共享的 2020 版 API
- ChemForge.Net80 与 PipeBreak 编译通过（PipeBreak 0 警告 0 错误）；单元测试 11 个全部通过
  （Tests 重定向 net8.0-windows 并引用 Net80 工程）
- Install-Dev.ps1 部署后自动清除 Autoloader 历史加载标记（保证每次部署后下次启动重新加载）；
  Uninstall-Dev.ps1 同时清理 Loaded 标记、Applications 注册与配置文件中的残留菜单
- 交互式插件架构图（docs/architecture/ChemForge-Architecture.html + 同目录 JSON 规格源），
  展示启动链、命令执行链与 PipeBreak 独立插件三条主线，节点带仓库源码引用
- 功能区入口 RibbonBuilder：插件加载后在 AutoCAD 功能区创建 ChemForge 选项卡
  （文字 / 块 / 工具 三个面板，按钮走 SendStringToExecute），与菜单栏互为备份。
  采用运行时 API 而非 CUIx 定义——官方文档指出写在 CUIx 里的 Ribbon 选项卡必须并入
  工作空间且重载后才显示，运行时构建随插件加载即时可见（csproj 新增 AdWindows 引用）
- 开发版部署改为注册表加载方式：DLL 留在仓库内，通过
  `HKCU\...\Applications\ChemForge`（LOADER / **LOADCTRLS=2** / MANAGED=1）登记，
  AutoCAD 启动时按表自动加载，**C 盘不再需要任何插件文件**；
  Install-Dev.ps1 / Uninstall-Dev.ps1 相应重写（按 R 版本号过滤写入、自动清理旧
  Junction 部署残留与旧加载标记、维护 TRUSTEDPATHS）
- `.gitignore` 忽略 `runtime/`：CUIx / MNR / 日志由运行时生成，且本机装有透明加密软件，
  这些文件在磁盘上是密文，不得入库

### Fixed
- PackageContents.xml 按 Autodesk 官方 Bundle 样例补齐必要属性（AutodeskProduct / ProductType /
  UpgradeCode / 25.0 系列号格式 / 顶层 RuntimeRequirements），修复整包被 Autoloader 拒收的问题；
  仅保留实际存在构建产物的 Net80 条目（指向缺失 DLL 的条目会导致整包不加载）
- MenuService_Net80 挂载幂等化：菜单已在菜单栏（配置文件恢复）时跳过重复插入，修复二次启动报错
- 修复 13 处 Exception 类型二义性（Autodesk.AutoCAD.Runtime.Exception vs System.Exception）、
  BlockManagerTests 具名参数笔误、Net80 输出路径重复追加目标框架名问题
- 修复启动报"不能加载菜单项： CHEMFORGE:ChemForge"：根因是 CAD 退出时把会话菜单写进配置文件，
  下次启动在插件加载前恢复必然失败；新增 Terminate → UnloadMenu（终止时卸载菜单组），
  Install-Dev.ps1 增加配置残留清理与 TRUSTEDPATHS 受信任位置写入（DLL 加入受信任位置后
  不再弹"安全性 - 未签名可执行文件"确认框，已实测静默自动加载）；
  部署遇 DLL 占用时提示用户关闭 AutoCAD 并自动等待，不再强制杀进程
- 菜单栏挂载增加菜单定义重载（`Application.ReloadAllMenus`）：AutoCAD 菜单栏内容由工作空间
  定义驱动，注入的菜单项需重载后才进入菜单栏；重载后仍未出现则走 COM 兜底插入
  （每会话仅重载一次），全过程写入 `runtime\ChemForgePlugin.log`
- 修正注册表自动加载的 `LOADCTRLS`：**12 → 2**（12 表示"打开图纸时加载"，2 才是"随
  AutoCAD 启动加载"）。此前插件始终未随 CAD 启动而加载，这也是菜单栏 ChemForge 需要
  手动 NETLOAD 才出现的原因；改为 2 后实测：`ChemForge.dll` 出现在 acad.exe 模块列表、
  菜单栏第 14 项为 ChemForge，全程无需任何手动操作
- 修复插件在无文档状态下刷日志：CAD 启动早期（尚无打开图纸）时 COM 菜单接口会报
  "当前状态下，该特性不可用"，原逻辑把它当作"菜单未挂载"并在 `Idle` 事件里反复重试，
  单次会话可刷出 60KB+ 日志。现改为：无文档时直接跳过菜单探测（新增 `HasDocument()`）、
  空闲重试节流至 5 秒（`MountRetryInterval`）、同类告警每会话仅记录一次
- 新增菜单栏残留自清理（`ClearStaleMenuBarEntries`，在 `EnsureMenuLoaded` 启动早期执行）：
  AutoCAD 每次打开图纸会把当前菜单栏布局（含运行时挂载的 ChemForge 项）写进注册表，
  下次启动恢复该布局时菜单组尚未就绪，命令行便打印"不能加载菜单项: CHEMFORGE:ChemForge"
  ——手工删一次没用（下次打开图纸又写回），故改由插件启动时自动清掉，报错与功能两不相扰
- **修复菜单栏显示（关键）**：AutoCAD 2025 的菜单栏只渲染出厂登记过的菜单组（PNP / EXPRESS /
  CUSTOM 等），运行时新建的独立菜单组（CHEMFORGE）既不显示、还会在菜单栏布局恢复时报
  "不能加载菜单项: CHEMFORGE:ChemForge"。现改为 **在出厂菜单组 PNP 内创建 ChemForge 菜单**
  并插入菜单栏（`MountAsPnpMenu`）：菜单栏出现独立的 ChemForge 菜单（与 Express 平级），
  注册表布局记为 "PNP ..."，下次启动时 PNP 组必然就绪——**显示与报错两个问题一并解决**。
  已实测验证：全新启动后菜单栏出现 ChemForge，三级结构为 文字/块/工具。
  同时移除了失效的挂载路径（独立菜单组 `MountViaCom` 与「工具菜单下子菜单」`MountUnderToolsMenu`）
- **菜单改为 Express 式定制系统注册（重构挂载方式）**：此前菜单靠运行时 COM 挂进 PNP 菜单组
  （`MountAsPnpMenu`），该方式脆弱——AutoCAD COM 派发接口未公开 `IAcadPopupMenu.Delete`，
  清理旧菜单时 `existing.Delete()` 抛 `'System.__ComObject' 不包含 Delete 的定义`，
  工作空间切换把运行时菜单清掉后补挂永远失败（表现为"切两次工作空间后插件消失"）。
  现改为与 Express（acetmain.cuix）完全一致的机制：
  1) ChemForge.cuix 由 MenuDefinition 生成到 AutoCAD 用户支持目录（`...\chs\Support\`），
     文件格式版本（0.6）与 acetmain.cuix 一致，启动时静默加载、无任何提示；
  2) `Register-ChemForgeMenu.ps1`（并接入 Install-Dev / Uninstall-Dev）把
     `<PartialMenuFile>ChemForge.cuix</PartialMenuFile>` 写进 PnP.cuix 组文件 Header，
     并在 PnP.cuix（9 个工作空间）与 acad.CUIX（3 个）的每个 `<WSPop MenuGroup="EXPRESS">`
     之后插入 `<WSPop MenuGroup="CHEMFORGE" pUID="ID_ChemForgeMenu" ...>`；
  3) 插件运行时**不再做任何菜单挂载**（删除 COM 挂载 / ReloadAllMenus / 菜单栏残留清理），
     菜单栏由工作空间定义原生驱动，切换工作空间菜单不丢失、无需轮询；
     功能区选项卡无对等注册方式，仍运行时构建并保留 5 秒空闲探测重建
     （新增 `RibbonBuilder.IsRibbonPresent()`）。
  已实测：CHEMFORGE 菜单组随定制系统启动加载（与 EXPRESS 并列），切换"三维建模"等
  工作空间菜单始终在栏，日志零错误；`ChemForge.mnr` 正常生成。

### 已知问题（本机环境，非代码缺陷）
- 本机 AutoCAD 2025 启动约 1 分钟后以 `致命错误: Unhandled Access Violation Reading 0x0008
  Exception at xxxx5938h` 崩溃，崩溃模块为 `AcWebView.dll`（AutoCAD 的 WebView2 宿主）。
  已用对照实验确认与 ChemForge 无关：删除插件注册项、还原全部受信任路径/开关改动后仍崩；
  带图纸启动、关闭开始页/InfoCenter、清空 WebView2 缓存同样无效。
  根因方向为安全管控软件（天锐绿盾）阻止 acad.exe 进程内创建 WebView2
  （CER dialog.log 报 `0x80070005 拒绝访问`）。

### Removed
- 移除管线功能占位桩（BreakPipeCommand / PipelineBreaker / PipelineSettings / PipelineBreakerTests），由真实插件取代
- **移除 PipeBreak 独立插件项目**（`src/Plugins/PipeBreak/`，含 BreakPipeCommands.cs / BreakPipePlugin.csproj /
  README.md 及 .sln 条目）：其 v2 算法早已完整并入共享层（`Core/Pipe/PipeBreakEngine.cs` 为超集），
  且该插件的 `[CommandMethod]` 挂在实例方法上、AutoCAD 从不注册，实际不可用。
  仅保留其 **AutoCAD 2020 API 参考程序集**（acmgd/acdbmgd/accoremgd），
  从 `src/Plugins/PipeBreak/lib/` 迁到 `src/Build/lib/Acad2020/`（仍未入库），
  `ChemForge.Net48.csproj` 的 `AcadApiLib` 与 `.gitignore` 规则同步更新；
  独立插件 README 说明已**归档**至 `docs/DeveloperGuide/PipeBreak-standalone.md`
  （v2 算法/性能对照/已知风险仍为 PipeBreakEngine 的权威参考）
- 仓库清理：删除临时工作区与构建中间产物（约 181 MB，含 .reasonix 会话目录、
  src/**/obj|bin、runtime 旧 CUIx/MNR、架构图 visual-check 校验产物、根目录 AutoCAD 日志）

## [1.0.0] - 计划发布日期

### Added
- 文本批量修改：颜色、倾角、宽度因子
- 图块管理：统一块名、图块统计
- 化工专业工具：管线交叉自动断线、仪表块自动编号
- 基于 CUIx 的 Express Tools 风格下拉菜单
- 支持 AutoCAD 2006 - 2026 的多目标编译
- Inno Setup 一键安装包

[Unreleased]: https://github.com/your-org/chemforge/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/your-org/chemforge/releases/tag/v1.0.0
