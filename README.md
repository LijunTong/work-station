# 工作台 Work Station

个人项目管理与报告工作台。**Avalonia 12 + .NET 10 + SQLite**，跨平台桌面应用（Windows / macOS / Linux），全部数据留在本机、无网络依赖。

> 技术栈已由 Electron + React 迁移到 Avalonia + C#。迁移原因与取舍见设计文档决策 #14；旧 Electron 数据（同结构的 SQLite 库）可直接复用。

## 当前状态

**M1 骨架与数据层** —— 功能完成

- **项目**：卡片列表（进度环 / 任务统计 / 目标日期 / 优先级 / 颜色）、新建与编辑抽屉、归档与取消归档、永久删除二次确认（级联删任务）
- **任务**：日期分组（今日 / 逾期 / 本周 / 以后 / 无期限 / 已完结）、分组折叠、状态与项目筛选、关键字搜索、行内完成、编辑抽屉、删除确认
- **进度**：自动模式按「已完成 ÷（总数 − 已取消）」计算，手动模式直接取设定值
- **标签**：仓储与关联（管理界面待做）
- **主题**：浅色 / 深色 / 跟随系统，切换即时生效、重启保持
- **数据**：SQLite 单文件 + WAL、版本化迁移、启动完整性检查、损坏自动留存重建、每日备份保留 7 份
- **发布**：Windows 单文件 exe（脚本同时支持 osx-arm64 / linux-x64）

**M2 看板与快速访问** —— 功能完成

- **任务看板**：待办 / 进行中 / 已阻塞 / 已完成四列，拖拽换列即改状态并落库；已完成列默认只显示近 7 天，可一键放宽
- **项目看板**：按 规划中 / 进行中 / 暂停 / 已完成 四列排列项目卡，拖拽换列改项目状态，卡片可直接进入该项目筛选后的任务页
- **概览条**：今日 / 逾期 / 已阻塞 / 本周 四个数字，点击即筛选；逾期、阻塞在有量时才染语义色
- **筛选**：项目、优先级、关键字可与概览口径组合，一键清除
- **命令面板**：`Ctrl+K` 搜任务、搜项目、跳页面、执行动作，全键盘操作
- **全局快速添加**：顶部常驻输入框，`n` 聚焦，任意页面回车即记一条
- **侧边栏徽标**：全局逾期数，「看板」项常驻提示

M3（提醒与托盘）、M4（周报月报与统计）尚未开始；标签管理界面与提醒页是 M2/M3 之间的已知缺口，见 `docs/M2-手测清单.md` 末尾。

## 快捷键

| 键 | 作用 |
| --- | --- |
| `n` | 聚焦顶部快速添加框 |
| `/` | 聚焦当前页搜索框（项目页/设置页会先跳到任务页） |
| `Ctrl+K` | 命令面板 |
| `1` – `4` | 切到 看板 / 任务 / 项目 / 设置 |
| `Ctrl+Enter` | 保存抽屉 |
| `Esc` | 关闭抽屉 / 浮层 |
| `?` | 显示或隐藏快捷键说明 |

焦点在输入框内时不响应单个字母与数字，避免打字时误跳页。

## 环境要求

- 开发：.NET SDK **10.0** 或更高
- 运行：目标机只需 **.NET 10 运行时**（Avalonia 是普通 NuGet 包，不需要 WindowsDesktop 运行时）

## 开发

```bash
dotnet run --project src/WorkStation.App
```

开发期数据库落在仓库内 `.data/data.db`；发布版落在用户应用数据目录。

## 测试

```bash
dotnet test
```

当前 **218 个用例**，覆盖：

| 层 | 内容 |
| --- | --- |
| Core 单元 | 时间三类格式、进度计算、任务分组（含周日边界）、格式化、看板口径（列顺序 / 已完成窗口 / 概览计数） |
| 数据层 | 迁移、完整性检查、损坏恢复、备份轮转、四个仓储的 CRUD 与聚合 |
| 服务层 | Result 契约（不抛异常）、错误码映射、中文化错误文案 |
| ViewModel | 快速添加、分组顺序、折叠保持、筛选与搜索、表单校验、归档与删除确认、看板拖拽改状态、命令面板检索 |
| UI 冒烟 | 真实 XAML 加载（Headless）、主窗口导航、键盘快捷键、看板双击开抽屉、命令面板键盘滚动跟随、主题切换落盘 |

UI 层用 `Avalonia.Headless` 承载，XAML 解析失败或编译绑定路径写错会直接让测试变红。

## 发布

```powershell
tools\publish.ps1 -Rid win-x64                         # 只出 Windows
tools\publish.ps1                                      # win-x64 / osx-arm64 / linux-x64
```

产物在 `dist/<rid>/`，单文件、可直接双击。win-x64 实测 **31.4 MB**（含 Skia / HarfBuzz / SQLite 原生库），目标机需预装 .NET 10 运行时。

> Windows PowerShell 5.1 读取无 BOM 的 UTF-8 脚本会按 GBK 解码，中文注释可能破坏语法。`tools/publish.ps1` 因此保存为 UTF-8 with BOM，改动时请保持。

## 数据存放

| 场景 | 路径 |
| --- | --- |
| 开发期 | `.data/data.db`（仓库内） |
| 打包后 | `%APPDATA%\work-station\data.db`（macOS `~/Library/Application Support/work-station/`、Linux `~/.config/work-station/`） |

数据库使用 WAL 模式。**退出应用后复制 `data.db` 即完成备份**；运行中复制需连同 `data.db-wal`、`data.db-shm` 一起复制，更稳妥的做法是直接取同目录 `backups/data-<日期>.db`（启动时已 checkpoint，最多保留 7 份）。

启动时若 `PRAGMA integrity_check` 失败，原库会被改名为 `data.db.corrupt-<日期>` 留存，并自动重建空库，界面上会提示留存路径。

## 目录结构

```text
src/
├─ WorkStation.Core/           纯逻辑层，零 UI 依赖
│  ├─ Models/                  实体、更新入参（Optional<T> 三态）、过滤条件
│  ├─ Data/                    连接、迁移、完整性检查、备份、路径解析
│  ├─ Data/Repositories/       projects / tasks / tags / settings
│  ├─ Services/                WorkStationService（UI 唯一入口，Result 契约）
│  ├─ Board/                   看板列顺序、已完成窗口、概览计数
│  ├─ Projects/                项目统计与进度口径（列表卡与看板卡共用）
│  └─ Time/ Formatting/        日期格式、进度计算、任务分组
└─ WorkStation.App/            Avalonia 界面层
   ├─ AppServices.cs           启动装配：恢复 → 迁移 → 备份 → 服务
   ├─ ViewModels/              主窗口、看板、项目页、任务页、设置页、命令面板
   ├─ Views/                   MainWindow / BoardView / ProjectsView / TasksView / SettingsView / CommandPaletteView
   ├─ Controls/ProgressRing.cs 进度环自绘控件
   └─ Styles/                  设计令牌（浅/深两套）与控件样式
tests/WorkStation.Tests/       xUnit v3 + Avalonia.Headless
tools/publish.ps1              多平台发布
docs/                          设计文档、实施计划、手测清单
```

详细设计见 `docs/superpowers/specs/2026-11-05-work-station-design.md`，
人工验收项见 `docs/M1-手测清单.md`、`docs/M2-手测清单.md`，
界面的色彩/字号/间距/组件规则见 `docs/设计系统.md`（改界面前先读它）。
