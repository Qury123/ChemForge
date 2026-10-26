# 端到端验收（AutoCAD Core Console）

`PIPEBREAK` / `INSTBREAK` 的**几何结果**自动化验收。单元测试（`src\Tests`，MSTest）只覆盖菜单、
命令名这类纯逻辑；打断引擎重度依赖 AutoCAD API，只能放到**真实 AutoCAD 内核**里验，这个目录就是干这个的。

## 怎么跑

```powershell
powershell -ExecutionPolicy Bypass -File src\Tests\Integration\Run-IntegrationTests.ps1
```

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `-AcadDir` | `C:\Program Files\Autodesk\AutoCAD 2025` | 需要里面的 `accoreconsole.exe` |
| `-SkipBuild` | 关 | 跳过 `dotnet build`，直接用 `deploy\Bundle` 里现有的 DLL |
| `-TimeoutSeconds` | `180` | 单场景超时；超时杀掉 `accoreconsole` 并判该场景失败 |

退出码 0 = 全部断言通过。日志与生成的脚本留在 `%TEMP%\cf_e2e_<时间戳>\`，**不删**，方便排查。

依赖：AutoCAD（`accoreconsole.exe`）、.NET 8 SDK（PATH 上没有时会自动找 `%USERPROFILE%\.dotnet\dotnet.exe`）、
Windows PowerShell 5.1（**不需要** PowerShell 7）。

## 覆盖的场景

统一夹具（`lisp\Fixture.lsp`，三层 `INST` / `PIPE` / `OTHER`）：

```
        L2(INST,竖)   O1(OTHER,竖)   P1(PIPE,竖)  P2(PIPE,竖)  P3(PIPE,横)
             x=200        x=150         x=500        x=800      y=-100
               |            |             |            |     600..900
  L1(INST,横) ─┼────────────┼─────────────┼────────────┼────────────   y=0, x=0..1000
               |            |             |            |
```

| 场景 | 命令与输入 | 断言 |
| --- | --- | --- |
| `instbreak-gap` | `INSTBREAK`，宽度 200 / 缺口 | 8 条：L1 被 P1、P2 打断成 `[0,400][600,700][900,1000]`；**同层**交叉（L2×L1、P3×P2）不断；未选图层（OTHER）不断；管线保持 3 条完整 |
| `instbreak-bridge` | `INSTBREAK`，宽度 200 / 架桥 / 凸起 U | 8 条：同上，但缺口处生成 2 个 `LWPOLYLINE`（2 顶点、|bulge| = 1 的半圆），跨度正好 `[400,600]`、`[700,900]`，图层 `INST` |
| `instbreak-scurve` | `INSTBREAK`，宽度 200 / **S 型** / 符号宽度默认 | 9 条：缺口区间同 `instbreak-gap`；**每个断点一个 S 符号 = 一条 5 拟合点 `SPLINE`**（共 4 条）；符号波长横跨管线（±100）、振幅沿管线（±25 = W/8）：`(400,-100)(375,-50)(400,0)(425,50)(400,100)`，其余三个断点 `x=600/700/900` 同构；不生成任何 `LWPOLYLINE` |
| `pipebreak-scurve` | `PIPEBREAK`，宽度 100 / **S 型** / 符号宽度默认 / **垂直** | 7 条：缺口落在竖线 P2 上（断成 `[-200,-150][-50,200]`），两个断点各一个 S 符号、每个一条 5 拟合点 `SPLINE`（共 2 条）：`(850,-150)(825,-162.5)(800,-150)(775,-137.5)(750,-150)`，另一断点 `(800,-50)` 同构；P3 横线不被打断、也不生成样条 |
| `pipebreak-scurve-width` | `PIPEBREAK`，宽度 100 / **S 型** / **符号宽度 50** / 垂直 | 7 条：缺口仍按 100 切（`[-200,-150][-50,200]`），但符号按 50 走、内部比例不变：跨度 ±25、振幅 ±6.25 → `(825,-150)(812.5,-156.25)(800,-150)(787.5,-143.75)(775,-150)`，`(800,-50)` 同构；另断言不存在按 100 生成的大符号 |
| `pipebreak-scurve-memory` | `PIPEBREAK`，宽度 100 / **S 型** / **回车（预置记忆 50）** / 垂直 | 7 条：预置 `LastSCurveWidth=50` 后直接回车，符号按 50 生成（几何同 `pipebreak-scurve-width`），记忆保持 50 |
| `instbreak-bridge-memory` | `INSTBREAK`，宽度 200 / **回车（预置记忆 H）** / 凸起 `U` | 8 条：预置 `LastMode=H` 后直接回车，走架桥路径（几何同 `instbreak-bridge`），记忆保持 `H` |
| `instbreak-bridge-side-memory` | `INSTBREAK`，宽度 200 / **回车（记忆 H）** / **回车（记忆 D）** | 6 条：预置 `LastBridgeSide=D` 后回车沿用凸起方向，半圆 bulge 为 `-1`（向下），跨度与 `instbreak-bridge` 一致；记忆键保持 `D` |
| `pipebreak-dir-memory` | `PIPEBREAK`，宽度 100 / **回车（记忆 D）** / **回车（记忆 H）** | 5 条：预置 `LastMode=D` + `LastGapDirection=H`，两句都回车 → 几何同 `pipebreak-regression-h`，两个记忆键保持原值 |
| `pipebreak-regression` | `PIPEBREAK`，宽度 100 / 缺口 / **垂直** | 6 条：单图层模式被打断的是竖线 → P2 断成 `[-200,-150][-50,200]`，P1 / P3 / INST / OTHER 全不动 |
| `pipebreak-regression-h` | `PIPEBREAK`，宽度 100 / 缺口 / **水平** | 5 条：被打断的是横线 → P3 断成 `[600,750][850,900]`，两根竖管全不动 |
| `pipebreak-tjunction` | `PIPEBREAK`，宽度 100 / 缺口 / **垂直**（夹具换 `FixtureT.lsp`） | 5 条：支管端点过冲主管中心线 **0.4**（T 型端接）→ 主管 `x=500` **整根保留、一个缺口都不许有**；同图 `x=1500` 处的**真交叉**仍按宽度 100 断成 `[-200,-50][50,200]`（防止新规则把打断整个关掉）；两条支管不动 |

> 回归场景的宽度特意用 **100**（不是 200）：半宽 50，分割点落在 `±150`，是 P2 / P3 的内部点。
> 用 200 时分割点正好压在端点上，引擎会按"分割点贴近端点"跳过该交点 —— 那是引擎的正确行为，
> 但会让回归场景变成空转（这个坑开发时踩过一次）。

## 工作原理

1. 每个场景生成一份 `run.scr`，逐行喂给 `accoreconsole.exe /isolate CFE2E <临时目录> /s`：
   关 `FILEDIA` / `CMDDIA` / `SECURELOAD`（不关会被安全策略拦）→ `(load "Harness.lsp")` →
   `(load "<场景夹具>")` 建图形（默认 `Fixture.lsp`，`pipebreak-tjunction` 用 `FixtureT.lsp`）→ `NETLOAD` 插件 DLL → **敲真实命令并逐行回答提示** →
   `(load "Check-*.lsp")` 断言 → `_.QUIT` + `Y`。
   `/isolate` 见下面「坑」的第一条 —— **没有它这个脚本会改用户的 AutoCAD 配置**。
2. 图层没法用鼠标点选（Core Console 无图形界面），所以走**记忆值**这条路：脚本先往
   `runtime\PipeBreak.settings` 写入 `LastPipeLayer=PIPE` / `LastInstrumentLayer=INST`，
   命令里对应提示**回车**即可；结束时按字节还原（原来没有就删掉）。
3. 断言用 `##CF## ` 前缀打印（`PASS | 场景/断言名 | 说明` / `SUMMARY pass=N fail=M`），
   PowerShell 侧只做提取和汇总。

## 坑（改这个脚本前先看）

- **`accoreconsole` 会把系统变量写回用户的 AutoCAD 配置（最要命的一条）**：它默认用当前用户的
  配置目录与注册表键，退出时把 `FILEDIA` / `CMDDIA` / `ATTREQ` / `SECURELOAD` 等落成
  `HKCU\Software\Autodesk\AutoCAD\R25.0\ACAD-8117:804\FixedProfile\...` 下的「偏离记录」，
  用户下次开 AutoCAD 行为就被改了（内核里根本没有对话框，本不需要动这些变量）。
  **必须用 `/isolate <regkey> <userDataFolder>`**（两个参数都要给，且放在 `/s` 之前；
  少给参数会报 `Failed to create ... /isolate switch`）—— 实测隔离后真实配置完全不被写。
  脚本末尾有注册表自检：一旦发现 `FileDialog` / `CommandDialogs` 偏离记录就报红并 `exit 1`。
- **PowerShell 里逗号优先级高于 `+`**：在 `@(...)` 里写 `'a' + $x + 'b'` 会被逗号切成多个元素，
  生成的 `.scr` 一行变三行（`NETLOAD` 就会把下一行当成文件名，插件根本加载不上）。
  拼接一律走 `Lisp-Form` 这类函数或 `-f`。
- **`accoreconsole` 跑完脚本不会自己退出**：必须 `-RedirectStandardInput` 到一个空文件，
  stdin 见 EOF 才结束；否则它会停在命令提示符上一直挂着（只能超时杀掉）。
- **stdout 是 UTF-16LE，但写不写 BOM 跟启动方式有关**：`Read-ConsoleText` 先看 BOM，
  再按"前 200 字节里 NUL 的密度"兜底判断，不然中文全乱、`##CF##` 也匹配不上。
- **退出用 `_.QUIT` + `Y`**：`N` 是"不放弃修改"，会取消退出，脚本就在那儿挂住了。
- **`.ps1` 必须存成 UTF-8 带 BOM**：Windows PowerShell 5.1 读无 BOM 的 UTF-8 会按 ANSI(GBK) 解，
  中文全乱。LISP 文件则相反 —— 保持纯 ASCII，别放中文。
- 脚本改完记得重新 `dotnet build`（或去掉 `-SkipBuild`），否则测的还是旧 DLL。

## 文件

| 文件 | 作用 |
| --- | --- |
| `Run-IntegrationTests.ps1` | 驱动器：构建 → 生成 `.scr` → 跑 `accoreconsole` → 提取 `##CF##` → 汇总 |
| `lisp\Harness.lsp` | 断言助手：`cf-report` / `cf-finish` / `cf-line-pairs` / `cf-hsegs` / `cf-vsegs` / `cf-seqs-equal` / `cf-nums-equal` / `cf-poly-info` |
| `lisp\Fixture.lsp` | 统一夹具（3 图层 6 条线），每个场景开头重建 |
| `lisp\FixtureT.lsp` | T 型端接夹具（单图层 `PIPE` 4 条线：两处主干+支管，一处端点过冲 0.4、一处真交叉） |
| `lisp\Check-*.lsp` | 每个场景一份断言 |

## 不覆盖什么

菜单 / 功能区 / 图标 / CUIx、工作空间切换、对话框（`BLKNUM` 等）、安装包 —— 这些仍然按
`docs\DeveloperGuide\ManualTesting.md` 手测。
