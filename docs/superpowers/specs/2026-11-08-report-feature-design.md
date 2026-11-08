# 日报 / 周报 / 月报 —— 设计文档

- 日期：2026-11-08
- 状态：已确认，待实现
- 关联分支：`feature/m4-foundation`

## 1. 目标

给工作台加一套**周期报告**：日报、周报、月报。核心产出是可编辑、可导出的 Markdown 文字报告，
而不是又一张统计图表。

三条产品决策（已与用户确认）：

1. **报告页是独立页面**，侧边栏新增一项，页内切换日 / 周 / 月三个周期。
2. **报告内容 = 已完成 + 遗留未完成 + 逾期 + 统计数字**，全部进自动汇总 Markdown。
3. **产出后复制到剪贴板 / 导出 .md 文件**，纯本地，不依赖任何外部服务。

补充决策：

- 「每日站会提醒」泛化成**报告提醒**：日报、周报、月报各有开关与时间。
- 每个周期**自动生成一条记录**，可回看、可修改。
- 周报统计**本周一 ~ 本周日**；周五提醒时汇总到当天为止，周末做完可再「重新汇总」。
- 「重新汇总」**覆盖 `AutoMarkdown`、保留 `ManualText`**。

## 2. 现状

代码里已经有地基，但没有可用的功能：

| 已有 | 位置 | 状态 |
| --- | --- | --- |
| `reports` 表 | `src/WorkStation.Core/Data/Migrations.cs` | 已建表，`kind` CHECK 只允许 `weekly` / `monthly` |
| `Report` 模型 | `src/WorkStation.Core/Models/Report.cs` | 字段齐全：`AutoMarkdown` / `ManualText` / `FinalMarkdown` |
| `ReportKind` 枚举 | `src/WorkStation.Core/Models/Enums.cs` | 只有 `Weekly` / `Monthly` |
| `DbEnum` 映射 | `src/WorkStation.Core/Models/DbEnum.cs` | 已写好两个方向的映射 |
| 索引 | 同上 | `idx_reports_period (kind, period_start)` |

**没有**：`ReportsRepository`、`WorkStationService` 里的报告方法、任何界面。
`docs/功能说明.md` 已把「周报 / 月报与统计：数据库已有 `reports` 表与模型，但还没有仓储、服务与界面」
列为已知限制 —— 本功能完成后删掉这条。

`Report` 现有字段恰好覆盖设计需要，**不新增字段**：

| 字段 | 含义 | 谁写 |
| --- | --- | --- |
| `AutoMarkdown` | 自动汇总出的正文 | `ReportComposer` 生成，「重新汇总」时覆盖 |
| `ManualText` | 用户手写的补充说明 | 用户编辑，**任何自动流程都不覆盖** |
| `FinalMarkdown` | 最终稿 = 自动 + 补充 | 保存补充时重算 |

## 3. 数据层

### 3.1 迁移 v3

`reports.kind` 的 CHECK 约束要放开成 `('daily','weekly','monthly')`。
SQLite 不能 `ALTER` 一个 CHECK 约束，只能重建表：

```sql
CREATE TABLE reports_new (
  id             TEXT PRIMARY KEY,
  kind           TEXT NOT NULL CHECK (kind IN ('daily','weekly','monthly')),
  period_start   TEXT NOT NULL,
  period_end     TEXT NOT NULL,
  auto_markdown  TEXT NOT NULL DEFAULT '',
  manual_text    TEXT NOT NULL DEFAULT '',
  final_markdown TEXT NOT NULL DEFAULT '',
  created_at     TEXT NOT NULL,
  updated_at     TEXT NOT NULL
);
INSERT INTO reports_new SELECT id, kind, period_start, period_end,
       auto_markdown, manual_text, final_markdown, created_at, updated_at FROM reports;
DROP TABLE reports;
ALTER TABLE reports_new RENAME TO reports;
CREATE UNIQUE INDEX idx_reports_period ON reports(kind, period_start);
```

> **实现偏差**：唯一约束直接落在 `idx_reports_period` 上（UNIQUE），不再另建
> `idx_reports_kind_period` —— 同一个 `(kind, period_start)` 上挂两条索引只是重复维护。
> 索引必须在重建表**之后**建：先建的话会被 `DROP TABLE` 一起删掉。

> 部署环境目前没有老用户数据，直接重建即可；但迁移仍按「老库可升」写，
> 并用测试覆盖「v1/v2 老库升上来数据不丢」，避免将来有人用旧库时踩坑。

迁移编号：`3`，名字 `reports-daily-kind`。

### 3.2 枚举与模型

- `ReportKind` 加 `Daily`（顺序放在 `Weekly` 前：`Daily, Weekly, Monthly`）。
- `DbEnum` 加 `Daily => "daily"` 与 `ParseReportKind("daily")`。
- 新增 `ReportPeriod` 值对象。数据模型一律放 `src/WorkStation.Core/Models/`
  （与 `Report` / `ReminderItem` 等同目录），逻辑放 `src/WorkStation.Core/Reports/`：

```csharp
public sealed record ReportPeriod
{
    public required ReportKind Kind { get; init; }
    /// <summary>本地日历日 YYYY-MM-DD。</summary>
    public required string PeriodStart { get; init; }
    /// <summary>本地日历日 YYYY-MM-DD。</summary>
    public required string PeriodEnd { get; init; }
}
```

## 4. Core 层

新增目录 `src/WorkStation.Core/Reports/`，全部是**纯函数**，可直接单测、不依赖数据库与 UI。

### 4.1 `ReportPeriods` —— 周期计算

| 周期 | `PeriodStart` | `PeriodEnd` |
| --- | --- | --- |
| 日报 | 今天 | 今天 |
| 周报 | 本周一 | 本周日 |
| 月报 | 本月 1 号 | 本月最后一天 |

周口径的**结束日**复用 `TaskGrouping.WeekEndDate`（现有代码里周口径的唯一真源，周日收尾），
起始日由它减 6 天得出 —— 这样看板的「本周」和周报的「本周」在周日那天不会差一天。
不另立一套算法。

```csharp
public static class ReportPeriods
{
    public static ReportPeriod Daily(DateTime now);
    public static ReportPeriod Weekly(DateTime now);
    public static ReportPeriod Monthly(DateTime now);

    /// <summary>按 kind 算当前周期。</summary>
    public static ReportPeriod Current(ReportKind kind, DateTime now);

    /// <summary>前一个 / 后一个周期（日报按天，周报按周，月报按月）。</summary>
    public static ReportPeriod Shift(ReportPeriod period, int delta);

    /// <summary>周期标题，如「2026-11-03 ~ 2026-11-09」。</summary>
    public static string Title(ReportPeriod period);
}
```

`Shift` 的月位移用 `DateTime.AddMonths`，跨年自然处理（11 月 +2 = 1 月）。

### 4.2 `ReportStats` —— 周期统计数字

```csharp
public sealed record ReportStats
{
    public int Completed { get; init; }      // 周期内完成
    public int CarriedOver { get; init; }    // 周期内到期但未完成（含已逾期）
    public int Overdue { get; init; }        // 遗留里截至今天已过期的
    public int Total => Completed + CarriedOver;
    public int CompletionRate => Total == 0 ? 0 : (int)Math.Round(Completed * 100.0 / Total);
}
```

> **实现偏差**：`Total` 与 `CompletionRate` 是**计算属性**（`=>`），不是 `init` 属性。
> 它们完全由 `Completed` / `CarriedOver` 推出，允许写入就会出现「分母改了、百分比没改」的脏数据。

### 4.3 `ReportComposer` —— 自动汇总 Markdown

```csharp
public static class ReportComposer
{
    public static string Compose(
        ReportPeriod period,
        IReadOnlyList<TaskItem> tasks,
        IReadOnlyList<Project> projects,
        DateTime now);
}
```

**口径**（与仪表盘 `DashboardStats` 的口径刻意不同 —— 仪表盘答「要扛多少活」，
报告答「这段时间干了什么、还欠什么」）：

| 指标 | 判定 |
| --- | --- |
| 已完成 | `status = Done` 且 `completed_at` 的本地日期落在 `[PeriodStart, PeriodEnd]` |
| 遗留未完成 | `status` 不是 Done / Canceled，且 `due_at` 的本地日期落在 `[PeriodStart, PeriodEnd]` |
| 逾期 | 遗留之中，`due_at` 的本地日期 **早于今天** 的那部分 |

关键取舍：

- **完成按完成时间落窗**，到期按到期时间落窗 —— 一件上周到期、上周做完的事，算上周的完成，
  不因为它今天才被勾掉就跳到今天。跟 `DashboardStats.CompletedInRange` 同源原则。
- **已取消不计入任何一项**。
- **无截止日期的任务不计入遗留** —— 没法说它「欠在哪个周期」。
- **月报周期含未来日期**：遗留按 `due_at` 落窗（含未来），逾期只算截至 `now` 已过期的。
  否则月报刚生成时会把月底才到期的任务算成逾期。
- **周报周五提醒时**，周期是周一~周日，但数据只到今天为止 —— 周末完成的任务，
  点「重新汇总」就补进来了。

输出格式（`## 标题` + `### 统计` + 三个清单）：

```markdown
## 周报 2026-11-03 ~ 2026-11-09

### 统计
- 完成 12 项 · 遗留未完成 5 项（其中逾期 2 项）· 完成率 71%

### 已完成
- [项目名] 任务标题

### 遗留未完成
- [项目名] 任务标题（截止 11-05）

### 逾期
- [项目名] 任务标题（截止 11-02，逾期 3 天）
```

清单为空时该小节写「无」。项目名前缀只在有项目时加，独立任务不写括号。

## 5. 服务层

### 5.1 `ReportsRepository`

放 `src/WorkStation.Core/Data/Repositories/ReportsRepository.cs`，与现有仓储同风格
（SQL 参数化、`MapRow` 静态映射、`nowIso` 可注入以便测试）。

```csharp
public IReadOnlyList<Report> List(ReportKind? kind = null);   // 按 kind 过滤，period_start 倒序
public Report? Get(string id);
public Report? GetByPeriod(ReportKind kind, string periodStart);
public Report Create(Report report);
public Report Update(Report report);   // 覆盖 auto_markdown / manual_text / final_markdown
public bool Remove(string id);
```

### 5.2 `WorkStationService` 新增方法

全部走 `Guard(...)`，永不向外 `throw`；失败转 `Ok=false` + `ErrorInfo(Code, Message)`。

| 方法 | 语义 |
| --- | --- |
| `ReportsList(kind?)` | 读列表 |
| `ReportsGet(id)` | 读单条 |
| `ReportsEnsure(kind, periodStart)` | **幂等**：周期已存在就返回既有记录；不存在则新建一条并立即生成 `AutoMarkdown` |
| `ReportsUpdate(id, manualText)` | 保存人工补充，重算 `FinalMarkdown` |
| `ReportsRegenerate(id)` | 用当前任务数据重算 `AutoMarkdown`（**保留 `ManualText`**），重算 `FinalMarkdown` |
| `ReportsRemove(id)` | 删除 |

**`ReportsEnsure` 与 `ReportsRegenerate` 都不得覆盖 `ManualText`** —— 这是本设计的核心约束，
「重新汇总」只动 `AutoMarkdown` 与 `FinalMarkdown`。

`FinalMarkdown` 的合成规则：`AutoMarkdown` 为空则取 `ManualText`；否则
`AutoMarkdown + "\n\n" + ManualText`（`ManualText` 为空则只取 `AutoMarkdown`）。

## 6. UI

### 6.1 导航

- `AppPage` 加 `Reports`。
- 侧边栏顺序：仪表盘 → 看板 → 任务 → 项目 → 提醒 → **报告**。设置页不在菜单里（标题栏进），
  按既有约定排在最后。
- 快捷键编号沿用既有约定「**菜单项按顺序占 1..N，设置排最后占 N+1**」
  （现在是 5 个菜单项占 1–5、设置占 6）。加一个菜单项后：**报告 = `6` / `Ctrl+6`，
  设置 = `7` / `Ctrl+7`**。
  同步改 `MainWindow.axaml.cs` 的 `PageFromDigit` 与按键分支，
  并更新该处注释里「设置单独排第 6」的说明。
- 图标：`AppIcons` 加 `Reports`，用同源 Material Design Icons 的
  `file-document-outline`（24×24 网格，与现有 `AppIcons` 风格一致）。
- 命令面板加条目「报告」，关键词 `report` / `报告` / `日报` / `周报` / `月报`。

### 6.2 页面结构

`src/WorkStation.App/Views/ReportsView.axaml` + `src/WorkStation.App/ViewModels/ReportsViewModel.cs`。

**顶部工具栏**：`Border Classes="card"` + `PageGutterBelowHeader`，
与任务 / 项目 / 提醒三页统一（AGENTS.md §3）。

- 左侧：三个分段按钮「日报 / 周报 / 月报」。
- 右侧：「重新汇总」「复制 Markdown」「导出 .md」。

**周期切换器**：`<` `>` 前后翻周期，中间是区间文字（如「2026-11-03 ~ 2026-11-09」）。
日报时前后翻即逐日。翻到未来周期不允许（最多到今天所在的周期）。

**主体左右分栏**：

- 左栏「自动汇总」—— 只读，灰色底，标注「可被重新汇总覆盖」。
- 右栏「补充说明」—— 多行输入框，绑定 `ManualText`。

**底部「最终报告」预览** —— 自动 + 补充的合成结果。

**历史列表**：一个下拉，列出 `ReportsList(kind)` 的已有周期，可跳回任意旧周期翻看修改。

Markdown 渲染**不引第三方库**（保持依赖清单不变），用分段 `TextBlock` 渲染标题、
列表项与统计行。

### 6.3 导出

- **复制 Markdown**：`TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(finalMarkdown)`。
- **导出 .md**：`TopLevel.GetTopLevel(this)?.StorageProvider.SaveFilePickerAsync(...)`，
  默认文件名如 `周报-2026-11-03.md`，用户选目录。

### 6.4 外壳接线

`MainWindowViewModel` 加 `Reports` 属性、`Navigate` 分支、`RefreshActivePage` 分支；
`MainWindow.axaml` 加 `DataTemplate`（`vm:ReportsViewModel` → `views:ReportsView`）
与侧边栏菜单项。

## 7. 报告提醒（替换每日站会提醒）

### 7.1 为什么替换

现有「每日站会提醒」每天到点弹一条只有三个数字的通知（昨日完成 / 逾期 / 今晚到期），
点开跳到看板今日口径。它不产出文字、不留痕 —— 与报告功能是同一件事的两端：
它只有「到点喊你」，没有「报」。

用户需要周报、月报也提醒，因此把这一套泛化成**三类报告提醒**，
而不是删掉或只改文案。

### 7.2 设置模型

新增 `ReportReminderSettings`（放 `src/WorkStation.Core/Reports/`）：

```csharp
public sealed record ReportReminderSettings
{
    public ReportReminderEntry Daily { get; init; } = ReportReminderEntry.DailyDefault();
    public ReportReminderEntry Weekly { get; init; } = ReportReminderEntry.WeeklyDefault();
    public ReportReminderEntry Monthly { get; init; } = ReportReminderEntry.MonthlyDefault();
}

public sealed record ReportReminderEntry
{
    public bool Enabled { get; init; }
    /// <summary>本地墙钟 HH:mm。</summary>
    public string Time { get; init; }
    /// <summary>周报用：星期几，1=周一 … 7=周日。</summary>
    public int DayOfWeek { get; init; }
    /// <summary>月报用：几号，1–31。没有该号的月份落到当月最后一天。</summary>
    public int DayOfMonth { get; init; }
}
```

默认值：

| 类型 | 默认 |
| --- | --- |
| 日报 | 启用，09:00 |
| 周报 | 启用，星期五 17:00 |
| 月报 | 启用，每月 31 号 17:00（无 31 号的月落到当月最后一天） |

存储：`settings` 表 JSON，key = `report_reminders`。
**兼容旧 key** `daily_review`：读不到新 key 时回落读旧 key 的 `Enabled` / `Time` 填日报项，
之后写新 key。旧 key 保留不删（无害，且避免任何回退路径出错）。

### 7.3 触发判定

新增 `ReportReminderPlanner`（纯函数）：

```csharp
public static class ReportReminderPlanner
{
    public static bool ShouldFireDaily(ReportReminderSettings s, string nowLocal, string? lastFired);
    public static bool ShouldFireWeekly(ReportReminderSettings s, string nowLocal, string? lastFired);
    public static bool ShouldFireMonthly(ReportReminderSettings s, string nowLocal, string? lastFired);

    /// <summary>月报没有 31 号时落到当月最后一天。</summary>
    public static int ResolveMonthDay(int requestedDay, DateTime localNow);

    public static string BuildMessage(ReportKind kind, ReportStats stats);
}
```

去重键（存 `settings` 表，与现有 `daily_review_last` 同思路）：

| 类型 | 去重键 | 示例 |
| --- | --- | --- |
| 日报 | `report_reminder_daily_last` | `2026-11-08` |
| 周报 | `report_reminder_weekly_last` | 该周周一日期 `2026-11-03` |
| 月报 | `report_reminder_monthly_last` | 该月 1 号 `2026-11-01` |

判定规则：

- 日报：到点（`now.time >= at`）且去重键 ≠ 今天。
- 周报：今天是设定的星期几、到点、且去重键 ≠ 本周周一日期。
- 月报：今天是「解析后的月几号」、到点、且去重键 ≠ 本月 1 号。
  「解析后的月几号」= `ResolveMonthDay(设定几号, now)`，设定 31 号而 11 月只有 30 天时解析成 30。

### 7.4 调度

`ReminderPump.FireDailyReview` 改成 `FireReportReminders`：一个节拍里依次检查日报 / 周报 / 月报。
每类命中时**先写去重键、再发通知**（与现有「先落状态再发通知」同序）。

通知文案：

- 日报：「该写今天的日报了」
- 周报：「该写本周周报了」
- 月报：「该写本月月报了」
- 正文用该周期的统计数字合成，例如「完成 12 项 · 逾期 2 项」。
  服务层另提供 `ReportsStats(kind)` 供通知正文取数：只返回 `ReportStats`，不生成 Markdown。

点击通知 → 跳到报告页、切到对应报告类型（页面再按需生成该周期的报告）。

### 7.5 删除旧站会提醒

- 删 `DailyReviewPlanner`、`DailyReviewCounts`（`src/WorkStation.Core/Reminders/DailyReviewPlanner.cs` 整个文件）。
- `WorkStationService` 的 `DailyReviewGet` / `DailyReviewSet` / `DailyReviewLastFired` /
  `DailyReviewMarkFired` / `DailyReviewCounts` 全部删除，换成报告提醒的对应方法。
- `ReminderPumpResult` 的 `DailyReviewFired` 改成 `IReadOnlyList<ReportKind> ReportRemindersFired`。
- `BoardViewModel.ShowToday()` 保留（看板今日口径仍可能用到），但不再被站会通知引用。
- 设置页「提醒」分组：站会那两行换成日报 / 周报 / 月报三组（开关 + 时间；周报多一个星期下拉，
  月报多一个「几号」下拉）。

## 8. 测试

| 测试文件 | 覆盖 |
| --- | --- |
| `ReportPeriodsTests` | 日 / 周 / 月周期边界；周一起始；跨月与跨年 `Shift`；月末 28 / 29 / 30 / 31 号 |
| `ReportComposerTests` | 完成 / 遗留 / 逾期口径；完成按 `completed_at` 落窗；已取消不计入；无截止日期不计入遗留；月报含未来日期时逾期不误算；空清单写「无」 |
| `ReportsRepositoryTests` | 增删改查；`GetByPeriod` 命中；唯一索引拦截同周期重复 |
| `WorkStationServiceReportsTests` | `ReportsEnsure` 幂等且不覆盖 `ManualText`；`ReportsRegenerate` 只动 `AutoMarkdown`；`FinalMarkdown` 合成规则；失败返回 `Ok=false` |
| `ReportReminderPlannerTests` | 三类触发判定；月末 fallback（2 月 → 28 / 29 号）；去重跨周跨月；未到点不触发 |
| `MigrationsTests` | v3 迁移后旧库数据不丢；`kind='daily'` 可写入；唯一索引生效 |
| `ReminderPumpTests`（补） | 三类报告提醒各弹一次；先写去重键再发通知 |

## 9. 文档同步（实现完成后）

按 AGENTS.md §6 的同步规则：

| 改了什么 | 同步到 |
| --- | --- |
| 新增报告页、快捷键 `6`/`7` 调整、报告提醒替换站会提醒 | `docs/功能说明.md`（页章节、快捷键表、提醒描述；**删掉**「周报 / 月报与统计」那条已知限制） |
| 报告页用到的组件与令牌 | `docs/设计系统.md` |
| 目录结构、测试文件变化 | `README.md` |

## 10. 不做的事（YAGNI）

- 不引第三方 Markdown 渲染库，不做富文本编辑器。
- 不做报告的云端同步、导出 PDF / Word、邮件发送。
- 不做报告模板自定义、不做多语言。
- 不做「按项目过滤报告」—— 首版就是全量口径。
- 不引入「报告历史版本」—— 只有一条 `ManualText`，重新汇总不产生历史。
- 不动 `TaskBoard` / `DashboardStats` 的现有口径，避免牵动看板与仪表盘的显示。

## 11. 验收

- `dotnet test` 全绿。
- 报告页浅 / 深两套主题各看一眼（`VisualCheck` + `.shots/`）。
- 手工走一遍：新建几条任务 → 切到报告页 → 选周期 → 看自动汇总 → 写补充 → 保存 →
  复制 → 导出 → 改任务状态 → 重新汇总，确认 `ManualText` 还在、`AutoMarkdown` 已更新。
- 提醒：把日报时间调到当前分钟内，确认弹窗、点它进报告页、当天不再弹。
