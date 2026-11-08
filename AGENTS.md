# AGENTS.md — Work Station

> 给 AI 编码代理的**硬约束**。事实源优先级：代码与测试断言 > 本文件 > 其它文档。
> 本文件不复制功能 / UI / 发布正文，只写不可违背的规则和「改代码要同步哪些文档」。
> 功能与快捷键 → [功能说明](docs/功能说明.md)；UI 规范 → [设计系统](docs/设计系统.md)；
> 发布、数据、结构、测试 → [README](README.md)。

## 1. 架构不变量

1. **依赖方向 `App → Core → SQLite`**：Core 零 UI 依赖（不引用 Avalonia），
   UI 只经 `WorkStationService` 取数据，不碰仓储与 `SqliteConnection`。
2. **`Result<T>` 契约**：服务层永不向外 `throw`，失败转 `Ok=false` + `ErrorInfo(Code, Message)`；
   `Code` 机器可读，`Message` 是可直接展示的中文文案。新增方法沿用 `Guard(...)`，
   参数校验用中文 `ArgumentException`。
3. **`AppServices.Start` 顺序不可换**：损坏探测（独立短连接，Windows 上句柄会占用改名）
   → 打开正式连接 → 迁移 → 每日备份 → 构造服务。备份失败不阻塞启动。
4. **提醒是任务的派生数据**：关掉提前提醒、清空截止时间、任务完成或取消，
   对应提醒都要作废（**含已弹过的**）；调度器 20 秒一拍，**先落状态再发通知**。
5. **外壳行为**：单实例（第二实例只抬已有窗口）；托盘建不出来要**提示用户**并降级为关窗即退出；
   通知走窗口内 `WindowNotificationManager`，不做系统 Toast（未打包 exe 无 AUMID，会静默失败）。
6. **编译绑定默认开启**：`x:DataType` 路径写错应在 build 期报错 —— 不许关掉它绕错，改绑定路径。

## 2. 技术栈基线

- 三个 csproj 都是 `net10.0`；C# 源码 **UTF-8 无 BOM** + `<CodePage>65001</CodePage>`；
  App 的 `AssemblyName=WorkStation` 保持 ASCII（中文 exe 名会让资源 URI 失效）。
- 三个 csproj 都写了 `TaskStatus` 别名（与 `System.Threading.Tasks.TaskStatus` 二义），新增项目照加。
- 测试统一 **xunit v3**（`OutputType=Exe`）+ `Avalonia.Headless`，不要引 v2。
- 依赖版本以 csproj 为准：Avalonia 12.1.5 / SukiUI 7.0.1 / CommunityToolkit.Mvvm 8.2.2 /
  `Microsoft.Data.Sqlite` + `SQLitePCLRaw.bundle_e_sqlite3` 2.1.12。
- 仓库根标志文件是 `work-station.slnx`：截图输出与测试定位仓库根都认它，别改名。

## 3. UI 硬规则

- 颜色一律用 Suki 主题键，控件一律用 SukiUI（`GlassCard` / `CircleProgressBar` / `PathIcon` /
  `SukiDialog`）；图标与进度环不自绘，日期用 `CalendarDatePicker`。
- 版式令牌**只写在 `App.axaml` 的 `Application.Resources`**，不另建令牌或样式文件；
  复用现有 8 档字号、三档圆角与 `PageGutter` 系列留白，不要在 XAML 里硬编码数值。
- 换字体同时改 `DefaultFontFamily` 与 `UiFontFamily`；取色器皮肤必须排在 Fluent 版
  `ColorPicker` 样式之后；视图模型取主题画刷必须带 `ActualThemeVariant`。
- 任务 / 项目 / 提醒三页工具栏统一 `Border Classes="card"` + `PageGutterBelowHeader`。
- 规则与「为什么」见 [设计系统](docs/设计系统.md)。

## 4. 数据与路径硬规则

- 路径一律 `Path.Combine`。优先级：自定义目录（`app-config.json` 的 `dataDirectory`）
  > 开发期仓库根 `.data/data.db`（判定条件是**输出目录**在 `bin/` 下）
  > 发布后应用数据目录。
- Linux 是 `~/.config/work-station/`，不是 `~/.local/share/` 下的路径。
- WAL、每日备份保留 7 份、损坏留存重建的行为别绕过；备份失败不能挡启动。
- 细节见 [README「数据存放」](README.md#数据存放)。

## 5. 发版清单

- **版本号要同步 5 处**：`src/WorkStation.App/WorkStation.App.csproj` 的 `<Version>`、
  `tools/installer/WorkStation.iss` 的 `AppVersion`、
  `tools/linux/make-linux-packages.py` 的 `APP_VERSION`、
  `tools/linux/build-appimage.sh`、`tools/linux/verify-linux-packages.py` 的 control 版本断言。
- 发布必须**自包含**（`--self-contained true`）：win/macOS 单文件、Linux 目录式。
  **禁 `PublishTrimmed`** —— 连 `publish.ps1` 的注释里都不能出现 `PublishTrimmed=true` 字面量，
  `InstallerTests` 用 `DoesNotContain` 读脚本源码。
- Linux 打包需 Python 3 + **Pillow**（不是纯标准库）。
- `InstallerTests` / `LinuxPackageTests` 直接读脚本源码断言字符串常量、`.isl`/`.ps1` 的
  UTF-8 BOM、向导 BMP 的 24 位深。**改打包脚本必须同步改断言**，别只改一边。
- 命令与产物见 [README「发布与安装」](README.md#发布与安装)。

## 6. 代码 → 文档同步规则

| 改了什么 | 必须同步 |
| --- | --- |
| 快捷键、默认首页、功能、已知限制 | [功能说明](docs/功能说明.md) |
| `App.axaml` 令牌 / 样式、控件、主题材质 | [设计系统](docs/设计系统.md) + 本文件 §3 |
| 数据路径、备份、发布形态、依赖、目录结构、测试流程 | [README](README.md) |
| 版本号、打包脚本 | 本文件 §5 + [README](README.md) |
| `docs/images/` 增删 | README 的 9 段图片字面量 + `sync-readme-images.py` 的 `IMAGE_MAP` + 重跑截图 |
| 任何主题 / 控件改动 | `dotnet test` + `VisualCheck` + 肉眼复验 `.shots/` 浅深两套 |

## 7. 验收

- `dotnet test` 全绿；改 UI 另跑 `VisualCheck` 并看浅 / 深两套截图。
- `README.md` 必须 **UTF-8 无 BOM** 且含 9 段 `docs/images/<名>.png` 字面量
  （`sync-readme-images.py` 依赖）；`docs/images/` 的 9 个文件名与脚本 `IMAGE_MAP` 绑定，不可随意改名。
- 新文件编码：C# UTF-8 无 BOM；`.ps1` / `.isl` UTF-8 with BOM；
  `.cmd` 纯 ASCII + `cd /d "%~dp0"`。
- 别把 `dist/`、`.shots/`、`.data/`、`counter.csv` 加进提交。

## 8. 禁止事项

- 不动 `.workbuddy/`（项目数据）与根目录 `.superpowers/`；`docs/` 下的历史材料已清理，需要时查 git 历史。
- 不要为「修文档」改代码或脚本逻辑；被测试钉住的字符串常量、BOM、位深一律保持。
- 仓库是公开的：不写密钥、令牌、本地绝对路径。
- 不写死分支名；提交前确认当前分支，推送后用 `git ls-remote --heads origin` 核对 SHA
  （`push` 输出 `Everything up-to-date` 不代表推送成功）。

## 9. 文档索引

| 文件 | 职责 |
| --- | --- |
| [README.md](README.md) | 环境、界面预览、发布与安装、项目结构、数据存放、测试与截图流程 |
| [docs/功能说明.md](docs/功能说明.md) | 功能清单、完整快捷键、已知限制 |
| [docs/设计系统.md](docs/设计系统.md) | 现行 UI 令牌、组件规则、设计取舍 |
| 本文件 | 硬约束、发版清单、同步规则、验收 |
