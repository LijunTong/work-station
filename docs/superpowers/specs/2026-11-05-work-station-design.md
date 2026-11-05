# 工作台 Work Station · 设计文档

- 日期：2026-11-05
- 状态：已评审通过，待写实施计划
- 作者：mavis（与 joont 共同确认）

---

## 1. 目标

做一个**个人工作台**桌面程序，把「项目、任务、进度、提醒、总结、周报月报」这几件每天都要处理的事收进一个工具里。

核心诉求不是功能多，而是**用得顺**：打开就在手边，状态一眼可见，记一条不用跳转，月底写报告不用回忆。

### 1.1 成功标准

| 标准 | 判定方式 |
| --- | --- |
| 打开即用 | 冷启动到看到看板 ≤ 3 秒（本地 SSD） |
| 记录成本低 | 新增一条任务 ≤ 3 次按键（`n` → 输入 → Enter） |
| 不漏事 | 逾期任务、阻塞任务、待处理提醒在首页可见或可一键筛选到 |
| 报告省事 | 周报自动段直接可用，手填段只写文字，一键复制 |
| 数据可控 | 单文件数据库，复制即备份，损坏可自动恢复 |
| 离线可用 | 无任何网络依赖，全部本地 |

---

## 2. 非目标（明确不做）

- 多人协作、账号与登录体系
- 云同步、多设备
- 手机端
- 富文本编辑器（周报月报用纯 Markdown）
- 任务依赖关系图 / 甘特图
- 自动抓取外部数据源（飞书、GitHub、邮件等）
- 与现有 `Todo` 应用的任何数据耦合（两个应用并存，互不读写）

---

## 3. 技术选型

### 3.1 环境事实（已实测）

| 事实 | 值 | 验证方式 |
| --- | --- | --- |
| .NET SDK | **10.0.401** | `dotnet --info` |
| .NET 运行时 | **10.0.12** | `dotnet --list-runtimes` |
| Avalonia 版本 | **12.1.5** | NuGet 版本清单；nuspec 含 **net10.0** 目标框架组 |
| Avalonia 跨平台覆盖 | Windows / macOS / 桌面 Linux(X11+Wayland)，iOS/Android/WASM 为实验性 | 官方文档 |
| SQLite 驱动 | **Microsoft.Data.Sqlite 10.0.11** + SQLitePCLRaw 2.1.12 | NuGet nuspec 依赖组 |
| 测试框架 | xUnit 2.9.3 + Avalonia.Headless.XUnit 12.1.5 | NuGet 版本清单 |
| 打包工具 | `dotnet publish`（多 RID，单文件） | — |

**关键结论**：Avalonia 是普通 NuGet 包，**不依赖 WindowsDesktop 运行时**，目标机只需 .NET 10 运行时；数据库驱动 `Microsoft.Data.Sqlite` 随包分发原生库，**不需要任何原生编译工具链**。三平台共用同一份代码，无平台专属 API。

### 3.2 选型清单

| 层 | 选型 | 理由 |
| --- | --- | --- |
| 运行时 | .NET 10 | 当前 LTS（至 2028-11），与 `Mes` 项目一致 |
| UI | **Avalonia 12.1.5** | 唯一同时满足「XAML 生态」「跨平台三桌面端」「MIT 免费」的方案；内置 `ThemeVariant` 主题与 `TrayIcon`（M3 用） |
| MVVM | CommunityToolkit.Mvvm 8.2.2 | 源生成器免样板；Avalonia 不强制 ReactiveUI |
| 数据库 | Microsoft.Data.Sqlite 10.0.11 | 官方驱动，WAL 可用，零原生编译 |
| 构建/打包 | `dotnet build` / `dotnet publish -r <rid>` | 一个 RID 一个单文件产物 |
| 测试 | xUnit + Avalonia.Headless.XUnit | UI 层可无头自动化，根治「页面零测试」缺口 |
| 日期 | 自写工具函数 | 只需周一为周首的周/月边界，无需引入 date-fns |

### 3.3 依赖清单

运行时依赖：

```
Avalonia
Avalonia.Desktop
Avalonia.Themes.Fluent
CommunityToolkit.Mvvm
Microsoft.Data.Sqlite
SQLitePCLRaw.bundle_e_sqlite3
```

开发依赖：

```
Avalonia.Diagnostics          （仅 Debug）
Avalonia.Headless
Avalonia.Headless.XUnit
xunit
xunit.runner.visualstudio
Microsoft.NET.Test.Sdk
coverlet.collector
```

**刻意不引入**：ReactiveUI（Avalonia 非必需，INPC + RelayCommand 足够）、zod（入参只做基础校验，自研）、date-fns、第三方 UI 组件库（自写轻量控件，样式统一由浅/深双主题控制）。

---

## 4. 架构

### 4.1 进程职责

单进程应用（不再有主进程 / 渲染进程分离）：

```
WorkStation.App（Avalonia UI，net10.0）
├─ Program.cs / App.axaml        入口、窗口、托盘、单实例、生命周期
├─ Views/                        看板 / 任务 / 项目 / 提醒 / 统计 / 周报月报 / 设置
├─ ViewModels/                   MVVM 状态管理（CommunityToolkit.Mvvm）
├─ Controls/                     通用 UI（卡片、抽屉、模态、徽标、进度环）
└─ Services/WorkStationService   UI 唯一入口，聚合全部仓储
        ↕ 直接方法调用（同进程，无 IPC）
WorkStation.Core（net10.0，零 UI 依赖）
├─ Data/                         SQLite 连接、迁移、完整性检查、备份
├─ Data/Repositories/            projects / tasks / tags / settings
├─ Time/Ids/Formatting/ProgressCalculator/TaskGrouping
└─ 未来的 scheduler / reports 也落在这一层
```

**约束**：`WorkStation.Core` 不引用任何 UI 包，保持框架无关；所有平台差异收敛到 `App/Platform/` 下的少量适配类。代码层禁止 `System.Windows.*`、`Microsoft.Win32.Registry`、`Microsoft.WindowsDesktop.App`。

### 4.2 目录结构

```
work-station/
├─ src/
│  ├─ WorkStation.Core/           纯逻辑层（net10.0）
│  │  ├─ Models/                  实体与输入类型
│  │  ├─ Data/                    连接、迁移、完整性检查
│  │  ├─ Data/Repositories/       projects / tasks / tags / settings
│  │  ├─ Services/                WorkStationService
│  │  ├─ Time/Ids.cs/Formatting.cs/ProgressCalculator.cs/TaskGrouping.cs
│  │  └─ 未来的 scheduler / reports 也落在这一层
│  └─ WorkStation.App/            Avalonia UI 层（net10.0）
│     ├─ Program.cs               AppBuilder 入口
│     ├─ App.axaml                Application 子类 + 主题资源
│     ├─ Views/                   看板 / 任务 / 项目 / 提醒 / 统计 / 周报月报 / 设置
│     ├─ ViewModels/              MVVM
│     ├─ Controls/                通用 UI
│     ├─ Styles/                  设计令牌与控件样式
│     └─ Platform/                平台差异适配（路径、通知、托盘）
├─ tests/
│  └─ WorkStation.Tests/          xUnit + Avalonia Headless
├─ tools/
│  └─ publish.ps1                 三平台发布脚本
├─ assets/fonts/                  中文字体（Linux 必需）
├─ docs/
│  ├─ superpowers/specs/
│  └─ superpowers/plans/
├─ work-station.sln
└─ README.md
```

### 4.3 服务层约定

```csharp
type Result<T> = { ok: true; data: T } | { ok: false; error: { code: string; message: string } };
```

- 服务层方法**永远不向外 throw**，统一 catch 后转成 `{ ok:false, error }`
- `error.code` 为机器可读常量（如 `DB_WRITE_FAILED`、`NOT_FOUND`、`VALIDATION_FAILED`）
- `error.message` 为可直接展示给用户的中文文案
- ViewModel 侧解包 `Result`，失败时统一 toast 展示

---

## 5. 数据模型

数据库文件：应用数据目录下的 `work-station/data.db`（开发期 `.data/data.db`）。
迁移版本号存于 `PRAGMA user_version`，每次结构变更递增并在事务中执行。

### 5.1 表结构

```sql
CREATE TABLE projects (
  id              TEXT PRIMARY KEY,
  name            TEXT NOT NULL,
  description     TEXT NOT NULL DEFAULT '',
  status          TEXT NOT NULL DEFAULT 'planning',   -- planning | active | paused | done
  priority        TEXT NOT NULL DEFAULT 'none',       -- high | medium | low | none
  color           TEXT,
  progress_mode   TEXT NOT NULL DEFAULT 'auto',       -- auto | manual
  progress_manual INTEGER,                           -- 0-100，仅 progress_mode='manual' 时有效
  start_date      TEXT,                               -- YYYY-MM-DD
  due_date        TEXT,                               -- YYYY-MM-DD
  sort_order      INTEGER NOT NULL DEFAULT 0,
  created_at      TEXT NOT NULL,                      -- ISO8601
  updated_at      TEXT NOT NULL,
  archived_at     TEXT                                -- 非空表示已归档（软删除）
);

CREATE TABLE tasks (
  id              TEXT PRIMARY KEY,
  project_id      TEXT REFERENCES projects(id) ON DELETE CASCADE,  -- 空 = 独立任务
  title           TEXT NOT NULL,
  notes           TEXT NOT NULL DEFAULT '',
  status          TEXT NOT NULL DEFAULT 'todo',        -- todo | doing | blocked | done | canceled
  priority        TEXT NOT NULL DEFAULT 'none',       -- high | medium | low | none
  due_at          TEXT,                               -- YYYY-MM-DDTHH:mm
  remind_before   INTEGER NOT NULL DEFAULT 0,         -- 截止前多少分钟提醒
  estimate        REAL,                               -- 预估工时（小时）
  spent           REAL,                               -- 实际工时（小时）
  blocked_reason  TEXT,                               -- status='blocked' 时的阻塞原因
  sort_order      INTEGER NOT NULL DEFAULT 0,
  created_at      TEXT NOT NULL,
  updated_at      TEXT NOT NULL,
  started_at      TEXT,                               -- 首次进入 doing
  completed_at    TEXT
);

CREATE TABLE tags (
  id    TEXT PRIMARY KEY,
  name  TEXT NOT NULL UNIQUE,
  color TEXT
);

CREATE TABLE task_tags (
  task_id TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
  tag_id  TEXT NOT NULL REFERENCES tags(id)  ON DELETE CASCADE,
  PRIMARY KEY (task_id, tag_id)
);

CREATE TABLE reminders (
  id           TEXT PRIMARY KEY,
  kind         TEXT NOT NULL,         -- task_due | custom
  task_id      TEXT REFERENCES tasks(id) ON DELETE CASCADE,
  title        TEXT NOT NULL,         -- custom 事项标题；task_due 时存任务标题快照
  remind_at    TEXT NOT NULL,         -- YYYY-MM-DDTHH:mm
  repeat_rule  TEXT NOT NULL DEFAULT 'none',   -- none | daily | weekly
  channel      TEXT NOT NULL DEFAULT 'system', -- system | inapp
  status       TEXT NOT NULL DEFAULT 'pending', -- pending | fired | dismissed | failed | done
  fired_at     TEXT,
  created_at   TEXT NOT NULL,
  updated_at   TEXT NOT NULL
);

CREATE TABLE reports (
  id             TEXT PRIMARY KEY,
  kind           TEXT NOT NULL,       -- weekly | monthly
  period_start   TEXT NOT NULL,       -- YYYY-MM-DD
  period_end     TEXT NOT NULL,       -- YYYY-MM-DD
  auto_markdown  TEXT NOT NULL DEFAULT '',  -- 自动汇总段
  manual_text    TEXT NOT NULL DEFAULT '',  -- 手填段
  final_markdown TEXT NOT NULL DEFAULT '',  -- 合并成稿
  created_at     TEXT NOT NULL,
  updated_at     TEXT NOT NULL
);

CREATE TABLE settings (
  key   TEXT PRIMARY KEY,
  value TEXT NOT NULL    -- JSON 字符串
);

CREATE INDEX idx_tasks_project    ON tasks(project_id);
CREATE INDEX idx_tasks_status     ON tasks(status);
CREATE INDEX idx_tasks_due        ON tasks(due_at);
CREATE INDEX idx_task_tags_tag    ON task_tags(tag_id);
CREATE INDEX idx_reminders_due    ON reminders(status, remind_at);
CREATE INDEX idx_reports_period   ON reports(kind, period_start);
```

### 5.2 settings 键

| 键 | 默认值 | 说明 |
| --- | --- | --- |
| `daily_review` | `{"enabled":true,"time":"09:00"}` | 每日站会提醒开关与时间 |
| `theme` | `"system"` | `light` / `dark` / `system` |
| `close_to_tray` | `true` | 关闭窗口不退出 |
| `first_day_of_week` | `1` | 1 = 周一 |
| `template_weekly` | 见 7.5 | 周报 Markdown 模板 |
| `template_monthly` | 见 7.5 | 月报 Markdown 模板 |

### 5.3 进度计算

- **auto 模式**：`已完成数 ÷ (总任务数 − 已取消数)`，分母为 0 时进度记为 0
- **manual 模式**：直接取 `progress_manual`（0–100）
- 统计口径排除 `canceled`，避免取消操作稀释进度
- 独立任务（`project_id` 为空）不计入任何项目进度

### 5.4 提醒模型

三类提醒统一落到 `reminders` 表，调度器只有一条扫描路径：

| 类型 | 来源 | 生成时机 |
| --- | --- | --- |
| 任务截止提醒 | `kind='task_due'`，`remind_at = due_at - remind_before` | 保存任务时 upsert |
| 自定义提醒事项 | `kind='custom'`，`title` + `remind_at` + 可选重复 | 用户新建 |
| 每日站会提醒 | 不落表，存在 `settings.daily_review` | 调度器每天按设置时间合成一次并即时触发 |

每日站会提醒是「每天一次」的固定动作，不产生历史记录，因此不写入 `reminders`；提醒页只列出任务截止提醒与自定义事项两类。

---

## 6. 功能设计

### 6.1 项目

- 字段：名称、描述、状态、优先级、颜色、起止日期、进度模式
- 状态流转：`planning → active → paused → done`，可任意跳转
- 归档为软删除（写 `archived_at`），归档项目不出现在看板与列表默认视图，可筛选查看
- 永久删除需二次确认，并级联删除其任务与提醒
- 项目卡片显示：名称、进度环、逾期任务数、进行中任务数、目标日期、优先级、颜色标签
- 操作：新建、编辑、归档、永久删除、拖拽排序（`sort_order`）

### 6.2 任务

- 可归属项目，也可作为独立任务进入收件箱
- 字段：标题、备注、状态、优先级、标签、截止时间、提前提醒量、预估工时、实际工时、阻塞原因
- 状态：`todo` 待办 / `doing` 进行中 / `blocked` 已阻塞 / `done` 已完成 / `canceled` 已取消
- 交互沿用 `Todo` 项目的肌肉记忆：快速添加、日期分组折叠、已完成灰显不可反悔、`Esc` 关闭、`Ctrl+Enter` 确认
- 列表页分组：今日 / 逾期 / 本周 / 以后 / 无期限；支持按项目、优先级、标签筛选与全文搜索

### 6.3 标签

- 全局标签表，颜色可选
- 任务与标签多对多（`task_tags`），支持一个任务多个标签
- 删除标签时解除关联，不影响任务本身

### 6.4 看板

**任务看板**（全局视图 + 项目内筛选复用同一组件）

```
待办(12) │ 进行中(3) │ 已阻塞(2) │ 已完成(18)
  卡片      卡片        卡片        卡片（默认只显示近 7 天完成的）
```

- 卡片：标题、项目（带颜色点）、优先级标记、截止时间（逾期标红）、标签、预估工时
- 拖拽换列即改状态；落库时同步写 `started_at`（首次进入 doing）或 `completed_at`（进入 done）
- 「已完成」列默认只显示近 7 天，可切「显示全部」
- 顶部概览条：`今日 N` `逾期 N` `已阻塞 N` `提醒 N`，点击即筛选看板；右侧「写周报」直达

**项目看板**

```
规划中(2) │ 进行中(5) │ 暂停(1) │ 已完成(4)
  项目卡片    项目卡片     项目卡片    项目卡片
```

- 项目卡片：名称、进度环、逾期任务数、进行中任务数、目标日期、优先级、颜色标签
- 拖拽换列即改项目状态
- 卡片底部「进入任务看板 →」直达该项目筛选后的任务看板

### 6.5 提醒

| 类型 | 触发条件 | 通知内容 | 点击通知后 |
| --- | --- | --- | --- |
| 任务截止提醒 | `remind_at` 到点 | 任务标题 + 项目名 | 打开应用并定位该任务 |
| 每日站会提醒 | `settings.daily_review` 的时间 | 实时查库生成：昨日完成 N 项、逾期 N 项、今晚到期 N 项 | 打开看板并套用「今日」筛选 |
| 自定义提醒事项 | 事项时间到点 | 事项标题 | 打开提醒页 |

- 托盘常驻，关主窗口不退出（`close_to_tray=false` 时改为退出）
- 重复规则：`daily` / `weekly` 触发后自动排下一次
- 已触发的提醒进入提醒页，可标记「已处理」/「忽略」
- 通知发送失败只把 `reminders.status` 置为 `failed`，不崩溃

### 6.6 统计页

- 本周完成趋势柱状图（近 12 周）
- 各项目进度条与周期内进度变化
- 项目健康：逾期任务最多的项目、有进行中任务但 7 天无更新的僵尸项目
- 历史报告入口

### 6.7 周报月报

**周期定义**：周报 = 周一至周日；月报 = 自然月。

**自动汇总段**（由 SQL 聚合生成）

1. 周期范围
2. 本周期完成任务（按项目分组，含完成日期与耗时）
3. 统计数字：新增任务数、完成数、完成率、平均完成时长（`completed_at − started_at`）
4. 项目进度变化：周期内各项目进度起止对比
5. 风险项：已阻塞任务（含 `blocked_reason`）+ 逾期未完成任务
6. 下周期待办：截止日落在下周期内的任务

**手填段**

- 本周期总结 / 关键收获
- 下周期计划
- 其他说明

**月报差异**：周期为自然月；额外增加「按标签统计的完成数分布」与「各项目累计进度」。

**产出**：预览 → 一键复制 Markdown → 可选导出 `.md` 文件。生成结果存档于 `reports` 表，可回看再编辑；重新生成自动段**不会覆盖**已写的手填段。

### 6.8 设置

- 主题（浅色/深色/跟随系统）
- 每日站会提醒开关与时间
- 关闭窗口行为（驻留托盘 / 直接退出）
- 周报月报 Markdown 模板编辑
- 数据：导出 db 副本、导出可读 JSON 快照、查看数据路径、备份目录管理

---

## 7. 信息架构与界面

### 7.1 侧边栏（7 项）

| 项 | 内容 | 徽标 |
| --- | --- | --- |
| **看板**（默认首页） | 任务看板 / 项目看板 Tab | — |
| 任务 | 列表视图，筛选与批量操作 | — |
| 项目 | 项目卡片列表 | — |
| 提醒 | 提醒列表 + 新建自定义事项 | 未处理数 |
| 统计 | 趋势、项目健康 | — |
| 周报月报 | 报告生成器 + 历史存档 | — |
| 设置 | 主题、提醒、模板、备份 | — |

**看板为默认首页**。「今日」不再是独立页面，它是看板顶部概览条上的一个筛选预设。

### 7.2 布局

```
┌──────────────────────────────────────────────────────┐
│ 侧边栏      │ ┌ 看板  [任务看板|项目看板] ─────────┐ │
│ ● 看板      │ │ [今日 5][逾期 2][已阻塞 2][提醒 3] │ │
│ ○ 任务      │ │        [写周报]                   │ │
│ ○ 项目      │ ├───────────────────────────────────┤ │
│ ○ 提醒  3   │ │ 筛选: 项目▾ 优先级▾ 标签▾ 时间▾  │ │
│ ○ 统计      │ ├──────┬────────┬────────┬─────────┤ │
│ ○ 周报月报  │ │ 待办 │ 进行中 │ 已阻塞 │ 已完成  │ │
│ ○ 设置      │ │ 卡片 │  卡片  │  卡片  │  卡片   │ │
│             │ └──────┴────────┴────────┴─────────┘ │
└──────────────────────────────────────────────────────┘
  顶部常驻快速添加条 [ 记一件事…              ]
  底部状态栏：数据路径 · 提醒器状态 · 下次提醒时间
```

### 7.3 快速访问设计

| 场景 | 入口 |
| --- | --- |
| 记一条任务 | 任意页面按 `n` 聚焦顶部快速添加条 → 输入 → Enter |
| 看今日/逾期 | 看板概览条数字点击即筛选 |
| 看提醒 | 侧边栏徽标 + 托盘右键菜单直接列出未处理提醒 |
| 快速跳转 | `Ctrl+K` 命令面板：搜项目、搜任务、跳页面、执行动作 |
| 连窗口都不开 | 托盘右键菜单列出今日前 5 项任务与未处理提醒 |
| 写周报 | 看板概览条右侧「写周报」直达 |

### 7.4 快捷键

| 键 | 作用 |
| --- | --- |
| `n` | 聚焦顶部快速添加条 |
| `/` | 聚焦搜索框 |
| `Ctrl+K` | 打开命令面板 |
| `1` – `7` | 依次切换侧边栏 7 个页面 |
| `Esc` | 关闭抽屉 / 模态 / 命令面板 |
| `Ctrl+Enter` | 确认（编辑抽屉 / 报告保存） |
| `?` | 快捷键帮助 |

### 7.5 报告模板

模板为纯文本，渲染时替换占位符：

```
## {{period_range}}

### 本周期完成
{{completed_by_project}}

### 数据概览
{{stats}}

### 项目进度变化
{{progress_changes}}

### 风险与阻塞
{{risks}}

### 下周期计划
{{next_period_tasks}}

---

### 本周期总结
{{manual_summary}}

### 下周期计划
{{manual_plan}}

### 其他说明
{{manual_notes}}
```

---

## 8. 错误处理约定

| 场景 | 处理 |
| --- | --- |
| 数据库写入失败 | 返回 `DB_WRITE_FAILED`，页面 toast 提示，内存状态回滚 |
| 数据库损坏 | 启动时 `PRAGMA integrity_check` 检出后，自动把损坏文件重命名为 `data.corrupt-<时间戳>.db`，重建空库，并弹窗告知路径 |
| 迁移失败 | 迁移前自动备份 db；失败则回滚到备份并提示用户 |
| 记录不存在 | 返回 `NOT_FOUND`，页面刷新列表 |
| 通知发送失败 | `reminders.status='failed'`，写日志，不崩溃 |
| 渲染进程崩溃 | 主进程捕获 `render-process-gone`，提示并重载窗口，主进程不退出 |
| 重复启动 | 单实例锁，第二次启动聚焦已有窗口 |
| 参数非法 | 返回 `VALIDATION_FAILED` 与中文提示 |

所有 SQL 使用预处理语句参数绑定，不做字符串拼接。

---

## 9. 测试策略

| 层级 | 范围 | 工具 |
| --- | --- | --- |
| 单元测试 | 进度计算、报告聚合与模板渲染、日期边界（跨周/跨月/跨年、周一为周首）、提醒是否该触发的判定 | xUnit |
| 集成测试 | repositories 在 SQLite 内存库 `:memory:` 上的 CRUD 与聚合查询 | xUnit + Microsoft.Data.Sqlite |
| UI 层测试 | ViewModel 状态流转、分组与折叠、筛选空态、抽屉提交去重 | xUnit + Avalonia.Headless.XUnit |
| 手测清单 | 每阶段一份，覆盖关键路径 | 文档 |
| 打包真机验证 | 单实例、托盘常驻、系统通知实际弹出 | 手工 |

**不做 E2E**：个人工具，自动化端到端收益低，维护成本高。UI 层用 Headless 模式覆盖到「逻辑正确」为止，不追像素级还原。

---

## 10. 里程碑

| 阶段 | 内容 | 验收标准 |
| --- | --- | --- |
| **M1 骨架 + 数据层** | Avalonia/.NET 10 工程骨架、浅深主题、SQLite 建表与迁移、项目/任务/标签 CRUD 列表、打通 portable exe | 能建项目、建物项；重启后数据仍在；exe 能启动并显示列表 |
| **M2 看板 + 进度** | 任务看板与项目看板拖拽、概览条、筛选、进度自动算与手动覆盖、侧边栏徽标、命令面板、快捷键 | 拖拽改状态即时落库；徽标计数准确；进度计算与口径一致 |
| **M3 提醒** | 托盘常驻、单实例、Windows 原生通知、调度器（截止/站会/自定义）、通知点击跳转、提醒页 | 设定时提醒能弹出；点击通知跳到对应任务；关窗口不退出 |
| **M4 报表 + 统计** | 报告引擎、报告编辑器（自动段 + 手填段）、一键复制 Markdown、历史存档、模板设置、统计页趋势图 | 复制出的 Markdown 包含 6 个自动段与 3 个手填段且数据正确 |

---

## 11. 数据安全

- 数据库位置：随平台而定，均为「应用数据目录 / work-station / data.db」——Windows `%APPDATA%\work-station\data.db`、macOS `~/Library/Application Support/work-station/data.db`、Linux `~/.config/work-station/data.db`（开发期统一用项目内 `.data/data.db`）
- 备份：每天首次启动时把当前库复制到 `backups/data-<日期>.db`，保留最近 7 份
- 导出：设置页可导出 db 副本与可读 JSON 快照
- 迁移与结构变更前自动备份

---

## 12. 未决事项

以下两项在实现阶段处理，不阻塞设计：

1. **应用图标**：`build/icon.ico` 需 256×256 以上。可复用 `Todo` 项目图标风格另制，或用图像生成工具产出新图标。实现阶段确定。
2. **周报模板的最终措辞**：模板骨架已定，具体文案在首次使用周报时按实际手感微调。

---

## 13. 决策记录

| # | 决策 | 选择 | 备选与否决理由 |
| --- | --- | --- | --- |
| 1 | 运行形态 | 桌面应用（框架已被 #14 取代） | 纯网页版无法后台提醒；本地服务形态过重 |
| 2 | 存储 | SQLite 单文件 | JSON 文件在聚合统计上性能与表达力都不够 |
| 3 | 提醒通道 | 应用内 + Windows 系统通知 | 飞书机器人需额外授权与网络；先不上 |
| 4 | 报告产出 | 一键复制 Markdown | 导出 PDF 需额外渲染链路 |
| 5 | 旧 Todo 数据 | 两个并存，不迁移 | 用户明确生活琐事与工作项目管理分开 |
| 6 | 进度算法 | 任务完成度自动算 + 手动覆盖 | 里程碑加权过重；纯手动不可维护 |
| 7 | 提醒范围 | 三类全要 | 统一成一张表，调度逻辑单一 |
| 8 | 报告内容 | 自动 + 手填混合 | 纯自动写不出思考；纯手填失去工具意义 |
| 9 | 前端栈 | XAML + MVVM（具体框架已被 #14 取代） | 原生多视图后状态易失控 |
| 10 | 项目结构 | 单层任务 + 标签 | 里程碑与多级子任务本期不做 |
| 11 | 看板范围 | 任务看板 + 项目看板 | 两张看板共用拖拽基础设施，成本可控 |
| 12 | 阻塞列 | 增加「已阻塞」列 | 卡住的事必须显形，且能自动喂给周报风险段 |
| 13 | 首页 | 看板 | 概览条承载今日/逾期/提醒的快速访问 |
| 14 | 技术栈 | **Avalonia 12.1.5 + .NET 10**（原 Electron + React + TypeScript） | 用户要求跨平台：WPF 架构上只能跑 Windows，而 Avalonia 覆盖 Windows/macOS/Linux 且 MIT 免费。代价是渲染层整体重写；收益是 IPC 层整层消失、exe 体积从 104.7MB 降到 1–5MB、且 UI 层首次可用 Headless 自动化测试 |
