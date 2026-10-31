# ChemForge · 工艺匠造工坊

<p align="center">
  <em>锻造化工工艺，精绘设计蓝图</em><br>
  <sub>面向化工、新能源行业的 AutoCAD 辅助设计工具箱，产品定位为工艺设计师的「效率伙伴」</sub>
</p>

<p align="center">
  <img alt="支持 AutoCAD 2017–2026" src="https://img.shields.io/badge/AutoCAD-2017--2026-blue?style=flat-square">
  <img alt="Windows 64 位" src="https://img.shields.io/badge/Windows-64--bit-lightgrey?style=flat-square">
  <img alt="最新版本" src="https://img.shields.io/github/v/release/Qury123/ChemForge?style=flat-square&label=version&color=orange">
  <img alt="许可 MIT" src="https://img.shields.io/badge/license-MIT-green?style=flat-square">
</p>

把工艺设计里天天重复的活儿收进一个插件：批量改多行文字颜色、批量改块属性、属性块批量编号、管线交叉自动打断。装上就有功能区和菜单，不用记命令。

## 支持环境

| 项目 | 要求 |
|------|------|
| AutoCAD | **2017 – 2026**：支持（安装时自动识别本机装了哪些版本，由你勾选） |
| 系统 | Windows 10 / 11，64 位 |
| 权限 | 安装时点一次 UAC 授权（和装 QQ、微信一样，弹窗里选「是」） |

## 功能一览

| 菜单分组 | 功能 | 命令 |
|------|------|------|
| 文字格式处理 | 批量修改多行文字颜色 | `TCOLOR` |
| 图块批量处理 | 批量修改块属性 | `BLKATTCHANGE` |
| 图块编号 | 属性块批量编号 | `BLKNUM` |
| 管线与仪表 | 管线自动打断 | `PIPEBREAK` |
| 管线与仪表 | 仪表线自动打断 | `INSTBREAK` |
| 关于 ChemForge | 版本信息 | `CHEMFORGE` |

> [!NOTE]
> 还有两条命令菜单里没有入口，需要时直接在 AutoCAD 命令行敲：
> `BLKNUMHELP`（编号用法说明，等同「属性块批量编号」对话框右上角那个按钮）、
> `BLKNUMINFO`（插件自检，排查问题时用）。

## 安装

### 方式一：安装包（推荐）

从 [最新版本页面](https://github.com/Qury123/ChemForge/releases/latest) 下载 `ChemForge_Setup_vX.Y.Z.exe`，双击运行。

> [!IMPORTANT]
> 安装程序需要管理员权限，UAC 弹窗里选「是」就可以了。

向导会自动完成四件事：

1. 检测本机装了哪些 AutoCAD 版本，让你勾选装给哪几个
2. 让你选择安装位置
3. 把插件包挂到 AutoCAD 的自动加载目录
4. 注册到 AutoCAD，并把菜单挂上

装完启动 AutoCAD：功能区出现 **ChemForge** 选项卡，经典菜单栏出现 **ChemForge** 下拉菜单。

**卸载**：开始菜单 → `ChemForge` → 卸载 ChemForge（或 Windows「设置 → 应用」里卸载，同样要同意一次 UAC）。

### 方式二：免安装绿色包

把 `ChemForge.bundle` 整个目录复制进下面任一目录即可，不写注册表、不需要管理员权限：

| 目录 | 适用范围 | 首次加载 |
|------|----------|----------|
| `%ProgramData%\Autodesk\ApplicationPlugins\` | 本机所有用户 | 不弹提示（推荐） |
| `%APPDATA%\Autodesk\ApplicationPlugins\` | 当前用户 | 弹一次，选「始终加载」即可 |

<details>
<summary><b>从源码构建（开发者）</b></summary>

> 这一节只有想自己编译的人需要看。**普通用户请直接用上面的安装包或绿色包** ——
> 不需要装 Visual Studio，也不需要准备任何"程序集"。

### 开发环境要求

- Visual Studio 2022 或更高版本（含 .NET Framework 开发工作负荷）
- 编译 AutoCAD 2017–2024 对应的那个目标（Net48）时，要自备 Autodesk 的托管程序集
  `acmgd.dll` / `acdbmgd.dll` / `accoremgd.dll`，放到 `src\Build\lib\<版本>\` 下
  （该目录不入库，版权归 Autodesk）。默认用 `lib\Acad2015`，手上只有别的版本时，
  编译时加 `/p:AcadApiLib=<托管程序集目录>` 覆盖即可。

### 编译

```bash
# 编译解决方案（当前只构建 Net80 / Net48 / Tests 三个项目）
msbuild ChemForge.sln /p:Configuration=Release

# 只编译某一版
msbuild src\Build\ChemForge.Net80.csproj /p:Configuration=Release
msbuild src\Build\ChemForge.Net48.csproj /p:Configuration=Release /p:AcadApiLib=<AutoCAD 托管程序集目录>
```

编译产物直接输出到 `deploy\Bundle\ChemForge.bundle\Contents\`。

### 源码部署

1. 按上面的命令编译
2. 把 `deploy\Bundle\ChemForge.bundle` 复制到 AutoCAD 插件目录 `%APPDATA%\Autodesk\ApplicationPlugins\`
3. 启动 AutoCAD，菜单栏里会出现 **ChemForge** 下拉菜单

### 项目结构

```
ChemForge/
├── src/
│   ├── Shared/          # 共享源代码（所有 CAD 版本共用）
│   ├── Build/           # 各版本薄壳项目（参与构建：Net48 / Net80）
│   │   └── lib/         # 自备的 AutoCAD 托管程序集（不入库）
│   └── Tests/           # 单元测试项目
└── deploy/              # 安装包、Autoloader Bundle 与开发脚本
```

</details>

## 许可与反馈

- **许可**：[MIT](LICENSE)
- **反馈**：用着有问题、或者想要个新功能，去 [Issues](https://github.com/Qury123/ChemForge/issues) 提一条
