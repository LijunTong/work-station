# 日报 / 周报 / 月报 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** 给工作台加一套可编辑、可导出的周期报告（日报 / 周报 / 月报），并把「每日站会提醒」换成三类报告提醒。

**Architecture:** 复用现有 `reports` 表，迁移 v3 重建表以放开 `kind` 约束并加唯一索引；Core 层新增纯函数（周期计算、Markdown 汇总、Markdown 解析、提醒判定）；服务层新增 `Reports*` 系列方法（`Result` 契约）；UI 层新增独立「报告」页（侧边栏第六项，快捷键 `6`，设置顺延到 `7`）。「重新汇总」只覆盖 `AutoMarkdown`，**永不覆盖 `ManualText`**。

**Tech Stack:** .NET 10 / C# / Avalonia 12.1.5 / SukiUI 7.0.1 / CommunityToolkit.Mvvm 8.2.2 / SQLite (Microsoft.Data.Sqlite 2.1.12) / xunit v3 3.2.2 + Avalonia.Headless

**设计文档：** `docs/superpowers/specs/2026-11-08-report-feature-design.md`

## Global Constraints

以下约束**每个任务都适用**，逐条照做：

- **依赖方向 `App → Core → SQLite`**：Core 层零 UI 依赖（不引用 Avalonia / SukiUI），UI 只经 `WorkStationService` 取数据。
- **服务层永不向外 `throw`**：一律 `Guard(...)` 包裹，失败转 `Ok=false` + `ErrorInfo(Code, Message)`；`Code` 机器可读，`Message` 是可直接展示的中文文案。
- **C# 源码 UTF-8 无 BOM**（三个 csproj 已有 `<CodePage>65001</CodePage>`，新文件同样要求无 BOM）。
- **编译绑定默认开启**：XAML 必须写 `x:DataType` 与正确的绑定路径，绑定路径写错要在 build 期报错，**不许关掉 `x:CompileBindings` 绕错**。
- **颜色一律用 Suki 主题键**（`SukiText` / `SukiMuteText` / `SukiLowText` / `SurfaceGlassRaised` / `SurfaceGlassSunken` / `SurfaceDivider` / `SukiPrimaryColor50`），控件一律用 SukiUI + `PathIcon`；**不自绘图标与进度环** —— 新图标加进 `AppIcons.cs` 的几何表（Material Design Icons，与既有图标同源同网格）。
- **版式令牌只写在 `App.axaml` 的 `Application.Resources`**，复用现有 8 档字号（`FontSizeDisplay/Metric/Title/Heading/Subhead/Body/Caption/Micro`）与 `PageGutter` / `PageGutterBottom` / `PageGutterBelowHeader` / `PageHeaderMargin` 系列留白，**不在 XAML 里硬编码字号与留白数值**。
- **任务 / 项目 / 提醒三页工具栏统一 `Border Classes="card"` + `PageGutterBelowHeader`** —— 报告页照此办理。
- **下拉框选中项用对象引用绑定**（照 `ProjectOption` 的做法），不要用 `SelectedValue` + `SelectedValueBinding`。
- **不新增任何 NuGet 依赖**（Markdown 渲染自己按行解析，不引第三方库）。
- **不写死分支名**；提交前确认当前分支，推送后用 `git ls-remote --heads origin` 核对 SHA。
- **不碰 `.workbuddy/`、`.superpowers/`、`.data/`、`.shots/`、`dist/`**。
- **仓库公开**：不写密钥、令牌、本地绝对路径。
- **每个任务结束必须 `dotnet test` 全绿再提交** —— 任务顺序已排好，任何一个任务结束时仓库都是可编译可测试的。

## 与设计文档的偏差（实现期间按本计划走，并回改设计文档）

| 位置 | 设计文档写法 | 本计划写法 | 原因 |
| --- | --- | --- | --- |
| 唯一索引 | 新增 `idx_reports_kind_period` | **把已有的 `idx_reports_period` 改成 UNIQUE** | 同一组列不需要两个索引；索引名不变，`MigrationsTests.RunMigrations_CreatesAllSixIndexes` 也不用改 |
| Markdown 统计行 | `完成 12 项 · 遗留未完成 5 项 · 逾期 2 项` | `完成 12 项 · 遗留未完成 5 项（其中逾期 2 项）` | 「逾期」是「遗留未完成」的子集，原写法两个数并列会被读成 5+2；括号表达把包含关系写明 |
| 报告统计取值 | 未写 | 新增 `WorkStationService.ReportsStats(kind)` | 通知正文要用周期统计，服务层是唯一数据入口 |
| Markdown 渲染 | 「分段 TextBlock 渲染」 | Core 纯函数 `ReportMarkdown.Parse` + VM 行集合 | 解析逻辑可单测，XAML 只做渲染 |
| 周报「未完成」的口径 | 未细分 | 「未完成」含逾期；清单里逾期的那几条**单列在「逾期」小节** | 两处数字必须各自对得上清单，不能重复计入 |
| `ReportStats.CompletionRate` | `public int CompletionRate { get; init; }` | 改成**计算属性**（`Total == 0 ? 0 : Math.Round(...)`），`Total` 也改成计算属性 | 完成率与总数都由已统计的三项推出，让调用方能改写其中一项却忘了重算另一个 |
| 「遗留未完成」行的补充说明 | 直接写 `ShortDate(dueAt)` → `（11-05）` | 改成 `PendingLabel(task)` → `（截止 11-05）` | 原写法漏了「截止」二字，与设计文档 `:197` 的示例、与本计划自带的测试期望都冲突 |
| `ReportMarkdown.Parse` 对空行的处理 | 未明确（测试断言 5 行，按空行占位索引） | **空行不产生行**，测试改成断言 4 行 | 解析器只认标题、列表项与普通行三种；原测试的样例是 4 行内容 + 2 个空行，**没有任何自洽实现能得出 5 行** |
| 报告预览的渲染 | `AutoLines` / `FinalLines` 直接是 `ObservableCollection<ReportMarkdownLine>`，XAML 一律按正文渲染 | App 层加一个显示用的 `ReportLineItem`（`Text` / `IsBullet` / `IsHeading` / `IsTitle`），XAML 按层级与列表标记分别渲染 | `ReportMarkdownLine` 特意带了 `Level` 与 `IsBullet`，全按正文渲染会把「### 统计」和「- 完成 1 项」画成同一行 —— 报告页是这功能的脸面，不能是一堵等大的字 |
| 统计口径的实现方式 | `ComputeStats` 与 `Compose` 各自写一遍 Where 谓词 | 抽一个私有 `Classify(...)` 返回三堆（completed / pending / overdue），统计与正文共用 | 同一份口径写两遍，提醒消息里的数字迟早和报告正文对不上，且没有任何测试会红 |
| 统计行的用词 | 统计行写「未完成 M 项」，小节标题写「遗留未完成」 | 统计行也写「**遗留未完成** M 项（其中逾期 K 项）」 | 同一概念两种叫法，读者会以为是两回事；括号仍保留「逾期 ⊂ 遗留未完成」的语义 |
| 「报告不存在」的错误码 | `ArgumentException` → `VALIDATION_FAILED` | `KeyNotFoundException` → `NOT_FOUND`，并给 `Guard` 加一条 catch | `ErrorCodes.NotFound` 此前定义了却没人用过；「找不到」不是「参数不合法」，机器可读的码要能区分这两件事 |
| 周期起点的格式校验 | 未写 | `ReportsEnsure` 前置 `TryParseExact("yyyy-MM-dd")` 校验，不合格抛 `ArgumentException` → `VALIDATION_FAILED` + 中文文案 | 不校验的话非法起点会落到兜底 catch，变成 `HANDLER_FAILED` + 英文的 `FormatException` 消息，违反「Message 是可直接展示的中文文案」 |

---

### Task 1: 数据层 —— `daily` 类型、迁移 v3、唯一索引

**Files:**
- Modify: `src/WorkStation.Core/Models/Enums.cs`（`ReportKind` 加 `Daily`）
- Modify: `src/WorkStation.Core/Models/DbEnum.cs`（`ToDb` / `ParseReportKind` 加 `daily`）
- Modify: `src/WorkStation.Core/Data/Migrations.cs`（新增迁移 `3`）
- Test: `tests/WorkStation.Tests/MigrationsTests.cs`

**Interfaces:**
- Consumes: 无（起点任务）
- Produces:
  - `enum ReportKind { Daily, Weekly, Monthly }`
  - `DbEnum.ToDb(this ReportKind)` → `"daily"` / `"weekly"` / `"monthly"`
  - `DbEnum.ParseReportKind(string)` → `ReportKind`
  - 迁移 `3`（名字 `reports-daily-kind`）落地后：`reports.kind` CHECK 含 `daily`；`idx_reports_period` 为 UNIQUE 索引 `(kind, period_start)`

- [x] **Step 1: 写失败的测试**

在 `tests/WorkStation.Tests/MigrationsTests.cs` 末尾追加：

```csharp
    /// <summary>日报是新增的类型：迁移 v3 之后 kind='daily' 必须能写进库。</summary>
    [Fact]
    public void RunMigrations_AcceptsDailyReportKind()
    {
        using var connection = TestDb.OpenMigrated();

        TestDb.Execute(connection, """
            INSERT INTO reports (id, kind, period_start, period_end, created_at, updated_at)
            VALUES ('r-daily', 'daily', '2026-11-08', '2026-11-08', '2026-11-08T00:00:00.000Z', '2026-11-08T00:00:00.000Z')
            """);

        Assert.Equal("daily", TestDb.Scalar(connection, "SELECT kind FROM reports WHERE id = 'r-daily'"));
    }

    /// <summary>一个周期只允许一条报告：唯一索引必须真的拦得住。</summary>
    [Fact]
    public void RunMigrations_RejectsTwoReportsInSamePeriod()
    {
        using var connection = TestDb.OpenMigrated();

        TestDb.Execute(connection, """
            INSERT INTO reports (id, kind, period_start, period_end, created_at, updated_at)
            VALUES ('r-1', 'daily', '2026-11-08', '2026-11-08', '2026-11-08T00:00:00.000Z', '2026-11-08T00:00:00.000Z')
            """);

        var error = Assert.Throws<SqliteException>(() => TestDb.Execute(connection, """
            INSERT INTO reports (id, kind, period_start, period_end, created_at, updated_at)
            VALUES ('r-2', 'daily', '2026-11-08', '2026-11-08', '2026-11-08T00:00:00.000Z', '2026-11-08T00:00:00.000Z')
            """));

        Assert.Contains("UNIQUE", error.Message);
    }

    /// <summary>不同类型的同一周期互不冲突：日报与周报可以落在同一天。</summary>
    [Fact]
    public void RunMigrations_AllowsSamePeriodAcrossDifferentKinds()
    {
        using var connection = TestDb.OpenMigrated();

        TestDb.Execute(connection, """
            INSERT INTO reports (id, kind, period_start, period_end, created_at, updated_at)
            VALUES ('r-1', 'daily', '2026-11-02', '2026-11-02', '2026-11-02T00:00:00.000Z', '2026-11-02T00:00:00.000Z')
            """);
        TestDb.Execute(connection, """
            INSERT INTO reports (id, kind, period_start, period_end, created_at, updated_at)
            VALUES ('r-2', 'weekly', '2026-11-02', '2026-11-08', '2026-11-02T00:00:00.000Z', '2026-11-02T00:00:00.000Z')
            """);

        Assert.Equal(2L, TestDb.Scalar(connection, "SELECT COUNT(*) FROM reports"));
    }
```

- [x] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/WorkStation.Tests/WorkStation.Tests.csproj --filter "FullyQualifiedName~MigrationsTests"`
Expected: FAIL —— `CHECK constraint failed: kind` 与缺少 UNIQUE 约束（后两条测试会失败）

- [x] **Step 3: 枚举与映射**

`src/WorkStation.Core/Models/Enums.cs`，把

```csharp
public enum ReportKind
{
    Weekly,
    Monthly
}
```

改成

```csharp
/// <summary>
/// 报告类型。数据库里存的是小写字符串（见 DbEnum）。
///
/// 说明：Electron 版只有 weekly / monthly，本项目新增 daily ——
/// 迁移 v3 重建了 reports 表并放开 CHECK 约束，老库照常能升。
/// </summary>
public enum ReportKind
{
    Daily,
    Weekly,
    Monthly
}
```

`src/WorkStation.Core/Models/DbEnum.cs`，把 `ToDb` / `ParseReportKind` 两段改成：

```csharp
    public static string ToDb(this ReportKind value) => value switch
    {
        ReportKind.Daily => "daily",
        ReportKind.Weekly => "weekly",
        ReportKind.Monthly => "monthly",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static ReportKind ParseReportKind(string value) => value switch
    {
        "daily" => ReportKind.Daily,
        "weekly" => ReportKind.Weekly,
        "monthly" => ReportKind.Monthly,
        _ => throw new ArgumentException($"未知的报告类型：{value}", nameof(value))
    };
```

- [x] **Step 4: 迁移 v3**

`src/WorkStation.Core/Data/Migrations.cs`，在 `All` 里加一条：

```csharp
        // 报告加日报类型：CHECK 约束改不了，只能重建表。
        // 顺带把 (kind, period_start) 变成唯一索引 —— 一个周期只允许一条报告。
        new Migration(3, "reports-daily-kind", ReportsDailyKind)
```

并新增常量：

```csharp
    /// <summary>
    /// 重建 reports 表：放开 kind 约束到 daily，并把 (kind, period_start) 变成唯一索引。
    ///
    /// SQLite 不能 ALTER 一个 CHECK 约束，只能建新表 → 拷数据 → 删旧表 → 改名。
    /// 拷贝时按 (kind, period_start) 去重（保留最早的一行）：唯一索引建在
    /// 有重复数据的旧库上会失败，那属于「数据本来就不合法」，不能让整个应用起不来。
    /// </summary>
    private const string ReportsDailyKind = """
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

        INSERT INTO reports_new (id, kind, period_start, period_end, auto_markdown, manual_text, final_markdown, created_at, updated_at)
        SELECT r.id, r.kind, r.period_start, r.period_end, r.auto_markdown, r.manual_text, r.final_markdown, r.created_at, r.updated_at
        FROM reports r
        WHERE r.rowid = (
          SELECT MIN(r2.rowid) FROM reports r2
          WHERE r2.kind = r.kind AND r2.period_start = r.period_start
        );

        DROP TABLE reports;

        ALTER TABLE reports_new RENAME TO reports;

        CREATE UNIQUE INDEX idx_reports_period ON reports(kind, period_start);
        """;
```

- [x] **Step 5: 运行测试确认通过**

Run: `dotnet test tests/WorkStation.Tests/WorkStation.Tests.csproj --filter "FullyQualifiedName~MigrationsTests"`
Expected: PASS（全部 8 条）

- [x] **Step 6: 全量测试**

Run: `dotnet test`
Expected: 全部 PASS（`RunMigrations_CreatesAllSixIndexes` 断言的索引名没变，不受影响）

- [x] **Step 7: 提交**

```bash
git add src/WorkStation.Core/Models/Enums.cs src/WorkStation.Core/Models/DbEnum.cs src/WorkStation.Core/Data/Migrations.cs tests/WorkStation.Tests/MigrationsTests.cs
git commit -m "feat(reports): 报告加日报类型，reports 表重建并加周期唯一索引"
```

---

### Task 2: Core 纯函数 —— 周期计算、统计、Markdown 汇总与解析

**Files:**
- Create: `src/WorkStation.Core/Models/ReportPeriod.cs`
- Create: `src/WorkStation.Core/Reports/ReportPeriods.cs`
- Create: `src/WorkStation.Core/Reports/ReportStats.cs`
- Create: `src/WorkStation.Core/Reports/ReportComposer.cs`
- Create: `src/WorkStation.Core/Reports/ReportMarkdown.cs`
- Modify: `src/WorkStation.Core/Time/LocalTime.cs`（加 `ToLocalDateOrNull`）
- Test: `tests/WorkStation.Tests/ReportPeriodsTests.cs`
- Test: `tests/WorkStation.Tests/ReportComposerTests.cs`
- Test: `tests/WorkStation.Tests/ReportMarkdownTests.cs`

**Interfaces:**
- Consumes: `ReportKind`（Task 1）、`TaskItem`、`Project`、`TaskGrouping.WeekEndDate`、`LocalTime`
- Produces:
  - `record ReportPeriod { ReportKind Kind; string PeriodStart; string PeriodEnd; }`（在 `WorkStation.Core.Models`）
  - `static class ReportPeriods`（在 `WorkStation.Core.Reports`）：
    - `ReportPeriod Daily(DateTime now)` / `Weekly(DateTime now)` / `Monthly(DateTime now)`
    - `ReportPeriod Current(ReportKind kind, DateTime now)`
    - `ReportPeriod FromStart(ReportKind kind, string periodStart)`
    - `ReportPeriod Shift(ReportPeriod period, int delta)`
    - `string Label(ReportKind kind)` → 日报 / 周报 / 月报
    - `string Range(ReportPeriod period)` → `2026-11-02 ~ 2026-11-08`（单日只写日期）
  - `record ReportStats { int Completed; int CarriedOver; int Overdue; int Total; int CompletionRate; }`
  - `static class ReportComposer`（在 `WorkStation.Core.Reports`）：
    - `ReportStats ComputeStats(ReportPeriod period, IReadOnlyList<TaskItem> tasks, DateTime now)`
    - `string Compose(ReportPeriod period, IReadOnlyList<TaskItem> tasks, IReadOnlyList<Project> projects, DateTime now)`
    - `string Merge(string autoMarkdown, string manualText)`
  - `record ReportMarkdownLine { string Text; int Level; bool IsBullet; }`
  - `static class ReportMarkdown { IReadOnlyList<ReportMarkdownLine> Parse(string markdown); }`
  - `LocalTime.ToLocalDateOrNull(string? isoUtc)` → `string?`

> **C# 原始字符串字面量的换行是 LF**：C# 规范规定 raw string literal 里的换行一律规范成 `\n`，
> 与源文件用什么换行无关。所以测试里的 `"""..."""` 期望值与实现里显式写的 `"\n"` **逐字一致**，
> Windows 上也不会差。

- [x] **Step 1: 写失败的测试（周期）**

创建 `tests/WorkStation.Tests/ReportPeriodsTests.cs`：

```csharp
using WorkStation.Core.Models;
using WorkStation.Core.Reports;

namespace WorkStation.Tests;

/// <summary>报告周期：日 / 周 / 月的起止日，以及前后翻页。</summary>
public class ReportPeriodsTests
{
    private static DateTime At(int month, int day, int hour = 12) =>
        new(2026, month, day, hour, 0, 0, DateTimeKind.Local);

    [Fact]
    public void Daily_CoversTodayOnly()
    {
        var period = ReportPeriods.Daily(At(11, 8));

        Assert.Equal(ReportKind.Daily, period.Kind);
        Assert.Equal("2026-11-08", period.PeriodStart);
        Assert.Equal("2026-11-08", period.PeriodEnd);
    }

    /// <summary>周口径以周一为首日：11-08 是周日，那一周是 11-02 ~ 11-08。</summary>
    [Fact]
    public void Weekly_StartsOnMonday()
    {
        var period = ReportPeriods.Weekly(At(11, 8));

        Assert.Equal("2026-11-02", period.PeriodStart);
        Assert.Equal("2026-11-08", period.PeriodEnd);
    }

    [Fact]
    public void Weekly_OnFridayStillCoversWholeWeek()
    {
        var period = ReportPeriods.Weekly(At(11, 6));

        Assert.Equal("2026-11-02", period.PeriodStart);
        Assert.Equal("2026-11-08", period.PeriodEnd);
    }

    [Fact]
    public void Monthly_CoversFirstToLastDay()
    {
        var period = ReportPeriods.Monthly(At(11, 8));

        Assert.Equal("2026-11-01", period.PeriodStart);
        Assert.Equal("2026-11-30", period.PeriodEnd);
    }

    [Fact]
    public void Monthly_FebruaryEndsOn28thIn2026()
    {
        var period = ReportPeriods.Monthly(At(2, 10));

        Assert.Equal("2026-02-01", period.PeriodStart);
        Assert.Equal("2026-02-28", period.PeriodEnd);
    }

    [Fact]
    public void Current_PicksByKind()
    {
        Assert.Equal(ReportKind.Daily, ReportPeriods.Current(ReportKind.Daily, At(11, 8)).Kind);
        Assert.Equal(ReportKind.Weekly, ReportPeriods.Current(ReportKind.Weekly, At(11, 8)).Kind);
        Assert.Equal(ReportKind.Monthly, ReportPeriods.Current(ReportKind.Monthly, At(11, 8)).Kind);
    }

    [Fact]
    public void FromStart_DerivesPeriodEnd()
    {
        Assert.Equal("2026-11-08", ReportPeriods.FromStart(ReportKind.Weekly, "2026-11-02").PeriodEnd);
        Assert.Equal("2026-11-30", ReportPeriods.FromStart(ReportKind.Monthly, "2026-11-01").PeriodEnd);
        Assert.Equal("2026-11-08", ReportPeriods.FromStart(ReportKind.Daily, "2026-11-08").PeriodEnd);
    }

    [Fact]
    public void Shift_MovesByOneStepOfItsKind()
    {
        var daily = ReportPeriods.Daily(At(11, 8));
        Assert.Equal("2026-11-09", ReportPeriods.Shift(daily, 1).PeriodStart);

        var weekly = ReportPeriods.Weekly(At(11, 8));
        var nextWeek = ReportPeriods.Shift(weekly, 1);
        Assert.Equal("2026-11-09", nextWeek.PeriodStart);
        Assert.Equal("2026-11-15", nextWeek.PeriodEnd);

        var monthly = ReportPeriods.Monthly(At(11, 8));
        Assert.Equal("2026-12-01", ReportPeriods.Shift(monthly, 1).PeriodStart);
        Assert.Equal("2026-12-31", ReportPeriods.Shift(monthly, 1).PeriodEnd);
    }

    [Fact]
    public void Shift_MonthlyCrossesYearBoundary()
    {
        var december = ReportPeriods.Monthly(At(12, 8));

        var january = ReportPeriods.Shift(december, 1);

        Assert.Equal("2027-01-01", january.PeriodStart);
        Assert.Equal("2027-01-31", january.PeriodEnd);
    }

    [Fact]
    public void Label_And_Range()
    {
        Assert.Equal("日报", ReportPeriods.Label(ReportKind.Daily));
        Assert.Equal("周报", ReportPeriods.Label(ReportKind.Weekly));
        Assert.Equal("月报", ReportPeriods.Label(ReportKind.Monthly));

        Assert.Equal("2026-11-08", ReportPeriods.Range(ReportPeriods.Daily(At(11, 8))));
        Assert.Equal("2026-11-02 ~ 2026-11-08", ReportPeriods.Range(ReportPeriods.Weekly(At(11, 8))));
    }
}
```

- [x] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/WorkStation.Tests/WorkStation.Tests.csproj --filter "FullyQualifiedName~ReportPeriodsTests"`
Expected: FAIL —— `WorkStation.Core.Reports` 命名空间不存在（编译错误）

- [x] **Step 3: 实现 `ReportPeriod` 与 `ReportPeriods`**

创建 `src/WorkStation.Core/Models/ReportPeriod.cs`：

```csharp
namespace WorkStation.Core.Models;

/// <summary>
/// 一个报告周期。<see cref="PeriodStart"/> 与 <see cref="PeriodEnd"/> 都是本地日历日
/// YYYY-MM-DD。日报起止同一天。
/// </summary>
public sealed record ReportPeriod
{
    public required ReportKind Kind { get; init; }

    /// <summary>本地日历日 YYYY-MM-DD。</summary>
    public required string PeriodStart { get; init; }

    /// <summary>本地日历日 YYYY-MM-DD。</summary>
    public required string PeriodEnd { get; init; }
}
```

创建 `src/WorkStation.Core/Reports/ReportPeriods.cs`：

```csharp
using System.Globalization;
using WorkStation.Core;
using WorkStation.Core.Models;
using WorkStation.Core.Time;

namespace WorkStation.Core.Reports;

/// <summary>
/// 报告周期计算 —— 纯函数，直接单测。
///
/// 周口径复用 <see cref="TaskGrouping.WeekEndDate"/>（周一起始、周日收尾），
/// 起始日由结束日减 6 天得出：另立一套算法，看板的「本周」和周报的「本周」
/// 迟早会在某个周日差一天。
/// </summary>
public static class ReportPeriods
{
    public static ReportPeriod Daily(DateTime now)
    {
        var date = LocalTime.ToLocalDate(LocalTime.AsLocal(now));
        return new ReportPeriod { Kind = ReportKind.Daily, PeriodStart = date, PeriodEnd = date };
    }

    public static ReportPeriod Weekly(DateTime now)
    {
        var end = TaskGrouping.WeekEndDate(now);
        var startDate = ParseDate(end).AddDays(-6);
        return new ReportPeriod
        {
            Kind = ReportKind.Weekly,
            PeriodStart = LocalTime.ToLocalDate(startDate),
            PeriodEnd = end
        };
    }

    public static ReportPeriod Monthly(DateTime now)
    {
        var local = LocalTime.AsLocal(now);
        var start = new DateTime(local.Year, local.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        return new ReportPeriod
        {
            Kind = ReportKind.Monthly,
            PeriodStart = LocalTime.ToLocalDate(start),
            PeriodEnd = LocalTime.ToLocalDate(end)
        };
    }

    /// <summary>按类型取当前周期。</summary>
    public static ReportPeriod Current(ReportKind kind, DateTime now) => kind switch
    {
        ReportKind.Daily => Daily(now),
        ReportKind.Weekly => Weekly(now),
        _ => Monthly(now)
    };

    /// <summary>
    /// 按周期起点反推整个周期。起点必须是该周期的首日
    /// （日报任意一天、周报的周一、月报的 1 号）—— 调用方一律用
    /// <see cref="Current"/> / <see cref="Shift"/> 产生起点，不会出错。
    /// </summary>
    public static ReportPeriod FromStart(ReportKind kind, string periodStart)
    {
        var start = ParseDate(periodStart);
        var end = kind switch
        {
            ReportKind.Daily => start,
            ReportKind.Weekly => start.AddDays(6),
            _ => start.AddMonths(1).AddDays(-1)
        };

        return new ReportPeriod
        {
            Kind = kind,
            PeriodStart = LocalTime.ToLocalDate(start),
            PeriodEnd = LocalTime.ToLocalDate(end)
        };
    }

    /// <summary>前一个 / 后一个周期：日报按天，周报按周，月报按月。</summary>
    public static ReportPeriod Shift(ReportPeriod period, int delta)
    {
        var start = ParseDate(period.PeriodStart);
        var shifted = period.Kind switch
        {
            ReportKind.Daily => start.AddDays(delta),
            ReportKind.Weekly => start.AddDays(7 * delta),
            _ => start.AddMonths(delta)
        };

        return FromStart(period.Kind, LocalTime.ToLocalDate(shifted));
    }

    /// <summary>类型中文名：日报 / 周报 / 月报。</summary>
    public static string Label(ReportKind kind) => kind switch
    {
        ReportKind.Daily => "日报",
        ReportKind.Weekly => "周报",
        _ => "月报"
    };

    /// <summary>周期的文字区间：单日只写日期，其余写「起 ~ 止」。</summary>
    public static string Range(ReportPeriod period) =>
        period.PeriodStart == period.PeriodEnd
            ? period.PeriodStart
            : $"{period.PeriodStart} ~ {period.PeriodEnd}";

    private static DateTime ParseDate(string value) =>
        DateTime.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
```

- [x] **Step 4: 运行周期测试确认通过**

Run: `dotnet test tests/WorkStation.Tests/WorkStation.Tests.csproj --filter "FullyQualifiedName~ReportPeriodsTests"`
Expected: PASS（10 条）

- [x] **Step 5: 写失败的测试（统计与汇总）**

创建 `tests/WorkStation.Tests/ReportComposerTests.cs`：

```csharp
using WorkStation.Core.Models;
using WorkStation.Core.Reports;

namespace WorkStation.Tests;

/// <summary>报告的自动汇总：完成 / 未完成 / 逾期三个口径与 Markdown 输出。</summary>
public class ReportComposerTests
{
    private static DateTime At(int month, int day, int hour = 12) =>
        new(2026, month, day, hour, 0, 0, DateTimeKind.Local);

    private static TaskItem Task(
        string title,
        TaskStatus status = TaskStatus.Todo,
        string? due = null,
        string? completed = null,
        string? projectId = null) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        ProjectId = projectId,
        Title = title,
        Status = status,
        Priority = Priority.None,
        DueAt = due,
        CompletedAt = completed,
        CreatedAt = "2026-11-01T00:00:00.000Z",
        UpdatedAt = "2026-11-01T00:00:00.000Z"
    };

    private static readonly Project Project = new()
    {
        Id = "p1",
        Name = "官网改版",
        CreatedAt = "2026-11-01T00:00:00.000Z",
        UpdatedAt = "2026-11-01T00:00:00.000Z"
    };

    [Fact]
    public void ComputeStats_CountsCompletedCarriedOverAndOverdue()
    {
        var tasks = new[]
        {
            Task("已做完", TaskStatus.Done, completed: "2026-11-05T02:00:00.000Z"),
            Task("还欠着", TaskStatus.Doing, due: "2026-11-05T18:00"),
            Task("早就过期了", TaskStatus.Todo, due: "2026-11-03T18:00")
        };

        // 「还欠着」截止 11-05、当天是 11-05 —— 截止日当天不算逾期；
        // 「早就过期了」截止 11-03 —— 早于今天才算逾期。
        var stats = ReportComposer.ComputeStats(ReportPeriods.Weekly(At(11, 5)), tasks, At(11, 5));

        Assert.Equal(1, stats.Completed);
        Assert.Equal(2, stats.CarriedOver);
        Assert.Equal(1, stats.Overdue);
        Assert.Equal(3, stats.Total);
        Assert.Equal(33, stats.CompletionRate);
    }

    [Fact]
    public void ComputeStats_IgnoresCanceledTasks()
    {
        var tasks = new[]
        {
            Task("取消的", TaskStatus.Canceled, due: "2026-11-05T18:00")
        };

        var stats = ReportComposer.ComputeStats(ReportPeriods.Weekly(At(11, 8)), tasks, At(11, 8));

        Assert.Equal(0, stats.Completed);
        Assert.Equal(0, stats.CarriedOver);
        Assert.Equal(0, stats.Overdue);
    }

    [Fact]
    public void ComputeStats_IgnoresTasksWithoutDueDate()
    {
        var tasks = new[] { Task("没期限", TaskStatus.Todo) };

        var stats = ReportComposer.ComputeStats(ReportPeriods.Weekly(At(11, 8)), tasks, At(11, 8));

        Assert.Equal(0, stats.CarriedOver);
    }

    [Fact]
    public void ComputeStats_CompletedCountsByCompletionTimeNotDueTime()
    {
        // 本周到期、本周三做完：算本周的完成
        var thisWeek = new[] { Task("本周做完的", TaskStatus.Done, due: "2026-11-03T18:00", completed: "2026-11-04T02:00:00.000Z") };

        var stats = ReportComposer.ComputeStats(ReportPeriods.Weekly(At(11, 8)), thisWeek, At(11, 8));

        Assert.Equal(1, stats.Completed);
        Assert.Equal(0, stats.CarriedOver);
    }

    [Fact]
    public void ComputeStats_MonthlyDoesNotCountFutureDueAsOverdue()
    {
        // 月报到 11-30，11-25 到期的任务在 11-08 还不该算逾期
        var tasks = new[] { Task("月底才到期", TaskStatus.Todo, due: "2026-11-25T18:00") };

        var stats = ReportComposer.ComputeStats(ReportPeriods.Monthly(At(11, 8)), tasks, At(11, 8));

        Assert.Equal(1, stats.CarriedOver);
        Assert.Equal(0, stats.Overdue);
    }

    [Fact]
    public void Compose_WritesHeaderStatsAndThreeSections()
    {
        var tasks = new[]
        {
            Task("已做完", TaskStatus.Done, completed: "2026-11-05T02:00:00.000Z", projectId: "p1"),
            Task("还欠着", TaskStatus.Doing, due: "2026-11-05T18:00", projectId: "p1"),
            Task("早就过期了", TaskStatus.Todo, due: "2026-11-03T18:00")
        };

        var markdown = ReportComposer.Compose(ReportPeriods.Weekly(At(11, 5)), tasks, [Project], At(11, 5));

        Assert.Equal("""
            ## 周报 2026-11-02 ~ 2026-11-08

            ### 统计
            - 完成 1 项 · 遗留未完成 2 项（其中逾期 1 项）· 完成率 33%

            ### 已完成
            - [官网改版] 已做完

            ### 遗留未完成
            - [官网改版] 还欠着（截止 11-05）

            ### 逾期
            - 早就过期了（截止 11-03，逾期 2 天）

            """, markdown);
    }

    [Fact]
    public void Compose_WritesNoneWhenSectionIsEmpty()
    {
        var markdown = ReportComposer.Compose(ReportPeriods.Weekly(At(11, 8)), [], [], At(11, 8));

        Assert.Contains("### 已完成\n无\n", markdown);
        Assert.Contains("### 遗留未完成\n无\n", markdown);
        Assert.Contains("### 逾期\n无\n", markdown);
    }

    [Fact]
    public void ComputeStats_IgnoresTasksCompletedOutsideThePeriod()
    {
        // 上周做完的，不算本周的完成 —— 完成按完成时间落窗
        var tasks = new[]
        {
            Task("上周做完的", TaskStatus.Done, completed: "2026-10-28T02:00:00.000Z")
        };

        var stats = ReportComposer.ComputeStats(ReportPeriods.Weekly(At(11, 5)), tasks, At(11, 5));

        Assert.Equal(0, stats.Completed);
        Assert.Equal(0, stats.Total);
    }

    [Fact]
    public void ComputeStats_IgnoresTasksDueOutsideThePeriod()
    {
        // 下周才到期的不算本周的遗留 —— 遗留按到期时间落窗
        var tasks = new[]
        {
            Task("下周才到期", TaskStatus.Doing, due: "2026-11-10T18:00")
        };

        var stats = ReportComposer.ComputeStats(ReportPeriods.Weekly(At(11, 5)), tasks, At(11, 5));

        Assert.Equal(0, stats.CarriedOver);
        Assert.Equal(0, stats.Overdue);
    }

    [Fact]
    public void Merge_JoinsAutoAndManualText()
    {
        Assert.Equal("自动", ReportComposer.Merge("自动", ""));
        Assert.Equal("人工", ReportComposer.Merge("", "人工"));
        Assert.Equal("自动\n\n人工", ReportComposer.Merge("自动", "人工"));
    }
}
```

- [x] **Step 6: 运行测试确认失败**

Run: `dotnet test tests/WorkStation.Tests/WorkStation.Tests.csproj --filter "FullyQualifiedName~ReportComposerTests"`
Expected: FAIL —— `ReportComposer` / `ReportStats` 不存在

- [x] **Step 7: 给 `LocalTime` 加一个换算方法**

`src/WorkStation.Core/Time/LocalTime.cs` 末尾追加：

```csharp
    /// <summary>
    /// ISO 8601 UTC 时间戳 → 本地日历日。解析不了返回 null。
    ///
    /// 任务的 completed_at 是 UTC 时间戳，要落到「哪一天」必须先换算成本地时间，
    /// 否则晚上完成的事会被算到第二天。
    /// </summary>
    public static string? ToLocalDateOrNull(string? isoUtc)
    {
        if (string.IsNullOrWhiteSpace(isoUtc)) return null;
        if (!DateTime.TryParse(isoUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return null;
        }

        return ToLocalDate(AsLocal(parsed));
    }
```

- [x] **Step 8: 实现 `ReportStats` 与 `ReportComposer`**

创建 `src/WorkStation.Core/Reports/ReportStats.cs`：

```csharp
namespace WorkStation.Core.Reports;

/// <summary>
/// 一个周期的统计数字。
///
/// 口径与仪表盘**刻意不同**：仪表盘答「要扛多少活」，报告答「这段时间干了什么、还欠什么」。
/// </summary>
public sealed record ReportStats
{
    /// <summary>周期内完成的任务数（按完成时间落窗）。</summary>
    public int Completed { get; init; }

    /// <summary>
    /// 周期内到期但还没完成的任务数 —— **含已逾期的那部分**。
    /// </summary>
    public int CarriedOver { get; init; }

    /// <summary><see cref="CarriedOver"/> 里截至今天已经过期的部分。</summary>
    public int Overdue { get; init; }

    /// <summary>周期内到期（或完成）的任务总数，完成率的分母。</summary>
    public int Total => Completed + CarriedOver;

    /// <summary>完成率 0–100。周期内没有到期任务时为 0。</summary>
    public int CompletionRate => Total == 0
        ? 0
        : (int)Math.Round(Completed * 100.0 / Total);
}
```

创建 `src/WorkStation.Core/Reports/ReportComposer.cs`：

```csharp
using System.Text;
using WorkStation.Core.Models;
using WorkStation.Core.Time;

namespace WorkStation.Core.Reports;

/// <summary>
/// 自动汇总：把一个周期的任务状态汇总成 Markdown。
///
/// 纯函数 —— 不碰数据库、不碰 UI，输入什么就产出什么，便于单测。
///
/// 三条口径：
/// <list type="bullet">
///   <item>已完成：status=Done 且完成时间落在周期内</item>
///   <item>未完成：还没做完（不含已取消）且截止时间落在周期内</item>
///   <item>逾期：未完成里，截止日早于今天的那些</item>
/// </list>
///
/// 「未完成」与「逾期」是包含关系（逾期 ⊂ 未完成），所以清单里分两小节写：
/// 「遗留未完成」放还没过期的，「逾期」放过期的 —— 两条不重复计入，数字与条数对得上。
/// </summary>
public static class ReportComposer
{
    /// <summary>周期统计数字。</summary>
    public static ReportStats ComputeStats(ReportPeriod period, IReadOnlyList<TaskItem> tasks, DateTime now)
    {
        var today = LocalTime.ToLocalDate(LocalTime.AsLocal(now));
        var (completed, pending, overdue) = Classify(tasks, period, today);

        return new ReportStats
        {
            Completed = completed.Count,
            CarriedOver = pending.Count + overdue.Count,
            Overdue = overdue.Count
        };
    }

    /// <summary>
    /// 生成整篇自动汇总 Markdown。
    ///
    /// 换行一律写 "\n"：导出到别处的 .md 里混进 CRLF 会多出空行。
    /// </summary>
    public static string Compose(
        ReportPeriod period,
        IReadOnlyList<TaskItem> tasks,
        IReadOnlyList<Project> projects,
        DateTime now)
    {
        var today = LocalTime.ToLocalDate(LocalTime.AsLocal(now));
        var names = projects.ToDictionary(p => p.Id, p => p.Name);

        var (completed, pending, overdue) = Classify(tasks, period, today);

        var stats = new ReportStats
        {
            Completed = completed.Count,
            CarriedOver = pending.Count + overdue.Count,
            Overdue = overdue.Count
        };

        var sb = new StringBuilder();
        sb.Append($"## {ReportPeriods.Label(period.Kind)} {ReportPeriods.Range(period)}\n\n");

        sb.Append("### 统计\n");
        sb.Append($"- 完成 {stats.Completed} 项 · 遗留未完成 {stats.CarriedOver} 项（其中逾期 {stats.Overdue} 项）· 完成率 {stats.CompletionRate}%\n\n");

        sb.Append("### 已完成\n");
        AppendItems(sb, completed.Select(t => Line(t, names, null)));
        sb.Append("\n");

        sb.Append("### 遗留未完成\n");
        AppendItems(sb, pending.Select(t => Line(t, names, PendingLabel(t))));
        sb.Append("\n");

        sb.Append("### 逾期\n");
        AppendItems(sb, overdue.Select(t => Line(t, names, OverdueLabel(t, today))));

        return sb.ToString();
    }

    /// <summary>
    /// 最终稿 = 自动汇总 + 人工补充。任一为空就只取另一个。
    /// </summary>
    public static string Merge(string autoMarkdown, string manualText)
    {
        var auto = autoMarkdown.Trim();
        var manual = manualText.Trim();

        if (auto.Length == 0) return manual;
        if (manual.Length == 0) return auto;
        return $"{auto}\n\n{manual}";
    }

    /// <summary>
    /// 按周期把任务分成三堆：已完成、还在期限内的遗留、已逾期的遗留。
    ///
    /// 统计与正文都从这里取 —— 同一份口径写两遍，提醒消息里的数字迟早会和报告正文对不上，
    /// 而且没有任何测试会红。
    /// </summary>
    private static (List<TaskItem> Completed, List<TaskItem> Pending, List<TaskItem> Overdue) Classify(
        IReadOnlyList<TaskItem> tasks,
        ReportPeriod period,
        string today)
    {
        var completed = tasks
            .Where(t => t.Status == TaskStatus.Done && InPeriod(CompletedDate(t), period))
            .OrderBy(t => t.CompletedAt ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(t => t.Title, StringComparer.Ordinal)
            .ToList();

        var carried = tasks
            .Where(t => t.Status != TaskStatus.Done
                        && t.Status != TaskStatus.Canceled
                        && InPeriod(DueDate(t), period))
            .OrderBy(t => t.DueAt ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(t => t.Title, StringComparer.Ordinal)
            .ToList();

        var overdue = carried
            .Where(t => string.CompareOrdinal(DueDate(t)!, today) < 0)
            .ToList();

        var pending = carried
            .Where(t => string.CompareOrdinal(DueDate(t)!, today) >= 0)
            .ToList();

        return (completed, pending, overdue);
    }

    private static void AppendItems(StringBuilder sb, IEnumerable<string> lines)
    {
        var any = false;
        foreach (var line in lines)
        {
            sb.Append(line).Append('\n');
            any = true;
        }

        if (!any) sb.Append("无\n");
    }

    /// <summary>一行任务：项目名（有才写）、标题、补充说明（截止日 / 逾期天数）。</summary>
    private static string Line(TaskItem task, IReadOnlyDictionary<string, string> names, string? suffix)
    {
        var prefix = task.ProjectId is not null && names.TryGetValue(task.ProjectId, out var name)
            ? $"[{name}] "
            : string.Empty;

        var tail = string.IsNullOrEmpty(suffix) ? string.Empty : $"（{suffix}）";
        return $"- {prefix}{task.Title}{tail}";
    }

    private static string OverdueLabel(TaskItem task, string today)
    {
        var due = DueDate(task);
        var days = (ParseDate(today) - ParseDate(due!)).Days;
        return $"截止 {ShortDate(task.DueAt)}，逾期 {days} 天";
    }

    /// <summary>截止日只写 MM-DD：报告是给人看的，完整年月日在前面的标题里。</summary>
    private static string ShortDate(string? dueAt) =>
        string.IsNullOrEmpty(dueAt) || dueAt.Length < 10 ? string.Empty : dueAt.Substring(5, 5);

    /// <summary>遗留未完成行的补充说明：「截止 11-05」。</summary>
    private static string PendingLabel(TaskItem task)
    {
        var date = ShortDate(task.DueAt);
        return date.Length == 0 ? string.Empty : $"截止 {date}";
    }

    private static string? CompletedDate(TaskItem task) => LocalTime.ToLocalDateOrNull(task.CompletedAt);

    private static string? DueDate(TaskItem task)
    {
        var due = task.DueAt;
        return string.IsNullOrEmpty(due) || due.Length < 10 ? null : due.Substring(0, 10);
    }

    private static bool InPeriod(string? date, ReportPeriod period) =>
        date is not null
        && string.CompareOrdinal(date, period.PeriodStart) >= 0
        && string.CompareOrdinal(date, period.PeriodEnd) <= 0;

    private static DateTime ParseDate(string value) =>
        DateTime.ParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
```

- [x] **Step 9: 运行测试确认通过**

Run: `dotnet test tests/WorkStation.Tests/WorkStation.Tests.csproj --filter "FullyQualifiedName~ReportComposerTests"`
Expected: PASS（9 条）

- [x] **Step 10: 写失败的测试（Markdown 解析）**

创建 `tests/WorkStation.Tests/ReportMarkdownTests.cs`：

```csharp
using WorkStation.Core.Reports;

namespace WorkStation.Tests;

/// <summary>把自动汇总的 Markdown 拆成可渲染的行。</summary>
public class ReportMarkdownTests
{
    [Fact]
    public void Parse_SplitsHeadingsBulletsAndParagraphs()
    {
        var lines = ReportMarkdown.Parse("""
            ## 周报 2026-11-02 ~ 2026-11-08

            ### 统计
            - 完成 1 项

            普通一行
            """);

        // 空行不产生行：解析器只认标题、列表项与普通行三种
        Assert.Equal(4, lines.Count);
        Assert.Equal(1, lines[0].Level);
        Assert.Equal("周报 2026-11-02 ~ 2026-11-08", lines[0].Text);
        Assert.Equal(2, lines[1].Level);
        Assert.Equal("统计", lines[1].Text);
        Assert.True(lines[2].IsBullet);
        Assert.Equal("完成 1 项", lines[2].Text);
        Assert.Equal(0, lines[3].Level);
        Assert.False(lines[3].IsBullet);
        Assert.Equal("普通一行", lines[3].Text);
    }

    [Fact]
    public void Parse_EmptyTextGivesNoLines()
    {
        Assert.Empty(ReportMarkdown.Parse(string.Empty));
    }

    [Fact]
    public void Parse_SkipsWhitespaceOnlyLines()
    {
        var lines = ReportMarkdown.Parse("   \n\t\n- 真的一行");

        var line = Assert.Single(lines);
        Assert.True(line.IsBullet);
        Assert.Equal("真的一行", line.Text);
    }
}
```

- [x] **Step 11: 运行测试确认失败**

Run: `dotnet test tests/WorkStation.Tests/WorkStation.Tests.csproj --filter "FullyQualifiedName~ReportMarkdownTests"`
Expected: FAIL —— `ReportMarkdown` 不存在

- [x] **Step 12: 实现 `ReportMarkdown`**

创建 `src/WorkStation.Core/Reports/ReportMarkdown.cs`：

```csharp
namespace WorkStation.Core.Reports;

/// <summary>Markdown 的一行，供界面渲染。</summary>
public sealed record ReportMarkdownLine
{
    /// <summary>行文字（已去掉标记）。</summary>
    public required string Text { get; init; }

    /// <summary>标题层级：0 普通行，1 二级标题（##），2 三级标题（###）。</summary>
    public int Level { get; init; }

    /// <summary>是不是列表项（以 "- " 开头）。</summary>
    public bool IsBullet { get; init; }
}

/// <summary>
/// 把自动汇总的 Markdown 拆成行。
///
/// 自己按行解析而不是引 Markdown 库：报告只有标题、列表项和普通行三种，
/// 一个库换不来什么，却多一份依赖与一份升级负担。
/// </summary>
public static class ReportMarkdown
{
    public static IReadOnlyList<ReportMarkdownLine> Parse(string markdown)
    {
        var result = new List<ReportMarkdownLine>();
        if (string.IsNullOrEmpty(markdown)) return result;

        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Trim().Length == 0) continue;

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                result.Add(new ReportMarkdownLine { Text = line[4..].Trim(), Level = 2 });
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                result.Add(new ReportMarkdownLine { Text = line[3..].Trim(), Level = 1 });
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                result.Add(new ReportMarkdownLine { Text = line[2..].Trim(), IsBullet = true });
            }
            else
            {
                result.Add(new ReportMarkdownLine { Text = line.Trim() });
            }
        }

        return result;
    }
}
```

- [x] **Step 13: 全量测试**

Run: `dotnet test`
Expected: 全部 PASS

- [x] **Step 14: 提交**

```bash
git add src/WorkStation.Core/Models/ReportPeriod.cs src/WorkStation.Core/Reports src/WorkStation.Core/Time/LocalTime.cs tests/WorkStation.Tests/ReportPeriodsTests.cs tests/WorkStation.Tests/ReportComposerTests.cs tests/WorkStation.Tests/ReportMarkdownTests.cs
git commit -m "feat(reports): 报告周期计算、统计口径与 Markdown 自动汇总"
```

---

### Task 3: 仓储层 —— `ReportsRepository`

**Files:**
- Create: `src/WorkStation.Core/Data/Repositories/ReportsRepository.cs`
- Test: `tests/WorkStation.Tests/ReportsRepositoryTests.cs`

**Interfaces:**
- Consumes: `Report`、`ReportKind`、`DbEnum`、`Sqlite`（内部工具类）、`Ids.NewId`、`LocalTime.NowIso`
- Produces:
  - `sealed class ReportsRepository`（`WorkStation.Core.Data.Repositories`）：
    - `ReportsRepository(SqliteConnection connection, Func<string>? nowIso = null)`
    - `IReadOnlyList<Report> List(ReportKind? kind = null)` —— 按 `period_start` 倒序
    - `Report? Get(string id)`
    - `Report? GetByPeriod(ReportKind kind, string periodStart)`
    - `Report Create(Report draft)` —— `Id` / `CreatedAt` / `UpdatedAt` 由仓储生成
    - `Report Update(Report report)` —— 只写 `auto_markdown` / `manual_text` / `final_markdown` 与 `updated_at`
    - `bool Remove(string id)`

- [x] **Step 1: 写失败的测试**

创建 `tests/WorkStation.Tests/ReportsRepositoryTests.cs`：

```csharp
using Microsoft.Data.Sqlite;
using WorkStation.Core.Data.Repositories;
using WorkStation.Core.Models;

namespace WorkStation.Tests;

/// <summary>报告仓储：一个周期一条报告，可回看可改。</summary>
public class ReportsRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ReportsRepository _reports;

    public ReportsRepositoryTests()
    {
        _connection = TestDb.OpenMigrated();
        _reports = new ReportsRepository(_connection);
    }

    public void Dispose() => _connection.Dispose();

    private static Report Draft(ReportKind kind, string start, string end) => new()
    {
        Id = string.Empty,
        Kind = kind,
        PeriodStart = start,
        PeriodEnd = end,
        AutoMarkdown = "自动",
        ManualText = string.Empty,
        FinalMarkdown = "自动",
        CreatedAt = string.Empty,
        UpdatedAt = string.Empty
    };

    [Fact]
    public void Create_StoresReportWithGeneratedId()
    {
        var created = _reports.Create(Draft(ReportKind.Daily, "2026-11-08", "2026-11-08"));

        Assert.NotEmpty(created.Id);
        Assert.Equal(ReportKind.Daily, created.Kind);
        Assert.Equal("2026-11-08", created.PeriodStart);
        Assert.Equal("自动", created.AutoMarkdown);
        Assert.NotEmpty(created.CreatedAt);
    }

    [Fact]
    public void GetByPeriod_FindsReportOfThatKindOnly()
    {
        _reports.Create(Draft(ReportKind.Daily, "2026-11-08", "2026-11-08"));
        _reports.Create(Draft(ReportKind.Weekly, "2026-11-02", "2026-11-08"));

        var found = _reports.GetByPeriod(ReportKind.Weekly, "2026-11-02");

        Assert.NotNull(found);
        Assert.Equal(ReportKind.Weekly, found!.Kind);
        Assert.Null(_reports.GetByPeriod(ReportKind.Weekly, "2026-11-09"));
    }

    [Fact]
    public void List_FiltersByKindAndSortsNewestPeriodFirst()
    {
        _reports.Create(Draft(ReportKind.Weekly, "2026-10-26", "2026-11-01"));
        _reports.Create(Draft(ReportKind.Weekly, "2026-11-02", "2026-11-08"));
        _reports.Create(Draft(ReportKind.Daily, "2026-11-08", "2026-11-08"));

        var weekly = _reports.List(ReportKind.Weekly);
        Assert.Equal(2, weekly.Count);
        Assert.Equal("2026-11-02", weekly[0].PeriodStart);

        Assert.Equal(3, _reports.List().Count);
    }

    [Fact]
    public void Update_KeepsManualTextAndRefreshesTimestamp()
    {
        var created = _reports.Create(Draft(ReportKind.Weekly, "2026-11-02", "2026-11-08"));

        var updated = _reports.Update(created with
        {
            AutoMarkdown = "新的自动汇总",
            ManualText = "我写的补充",
            FinalMarkdown = "新的自动汇总\n\n我写的补充"
        });

        Assert.Equal("新的自动汇总", updated.AutoMarkdown);
        Assert.Equal("我写的补充", updated.ManualText);
        Assert.Equal("新的自动汇总\n\n我写的补充", updated.FinalMarkdown);

        var reread = _reports.Get(created.Id);
        Assert.Equal("我写的补充", reread!.ManualText);
    }

    [Fact]
    public void Remove_DeletesReport()
    {
        var created = _reports.Create(Draft(ReportKind.Daily, "2026-11-08", "2026-11-08"));

        Assert.True(_reports.Remove(created.Id));
        Assert.Null(_reports.Get(created.Id));
        Assert.False(_reports.Remove(created.Id));
    }

    [Fact]
    public void Create_SamePeriodTwice_FailsOnUniqueIndex()
    {
        _reports.Create(Draft(ReportKind.Daily, "2026-11-08", "2026-11-08"));

        Assert.Throws<SqliteException>(() =>
            _reports.Create(Draft(ReportKind.Daily, "2026-11-08", "2026-11-08")));
    }
}
```

- [x] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/WorkStation.Tests/WorkStation.Tests.csproj --filter "FullyQualifiedName~ReportsRepositoryTests"`
Expected: FAIL —— `ReportsRepository` 不存在

- [x] **Step 3: 实现仓储**

创建 `src/WorkStation.Core/Data/Repositories/ReportsRepository.cs`：

```csharp
using Microsoft.Data.Sqlite;
using WorkStation.Core;
using WorkStation.Core.Models;
using WorkStation.Core.Time;

namespace WorkStation.Core.Data.Repositories;

/// <summary>
/// 报告仓储。一个周期一条报告 —— (kind, period_start) 上的唯一索引负责保证。
///
/// 仓储只管存取，不碰「报告里该写什么」：那是 <c>ReportComposer</c> 的事。
/// </summary>
public sealed class ReportsRepository
{
    private readonly SqliteConnection _connection;
    private readonly Func<string> _nowIso;

    public ReportsRepository(SqliteConnection connection, Func<string>? nowIso = null)
    {
        _connection = connection;
        _nowIso = nowIso ?? (() => LocalTime.NowIso());
    }

    private static Report MapRow(SqliteDataReader reader) => new()
    {
        Id = reader.Str("id"),
        Kind = DbEnum.ParseReportKind(reader.Str("kind")),
        PeriodStart = reader.Str("period_start"),
        PeriodEnd = reader.Str("period_end"),
        AutoMarkdown = reader.Str("auto_markdown"),
        ManualText = reader.Str("manual_text"),
        FinalMarkdown = reader.Str("final_markdown"),
        CreatedAt = reader.Str("created_at"),
        UpdatedAt = reader.Str("updated_at")
    };

    /// <summary>报告列表，按周期起点倒序 —— 最近的在最前。</summary>
    public IReadOnlyList<Report> List(ReportKind? kind = null)
    {
        if (kind is null)
        {
            return _connection.Command("SELECT * FROM reports ORDER BY period_start DESC, kind ASC")
                .ReadAll(MapRow);
        }

        using var command = _connection.Command(
            "SELECT * FROM reports WHERE kind = @kind ORDER BY period_start DESC")
            .Bind(("kind", kind.Value.ToDb()));
        return command.ReadAll(MapRow);
    }

    public Report? Get(string id)
    {
        using var command = _connection.Command("SELECT * FROM reports WHERE id = @id").Bind(("id", id));
        return command.ReadAll(MapRow).FirstOrDefault();
    }

    public Report? GetByPeriod(ReportKind kind, string periodStart)
    {
        using var command = _connection.Command(
            "SELECT * FROM reports WHERE kind = @kind AND period_start = @start")
            .Bind(("kind", kind.ToDb()), ("start", periodStart));
        return command.ReadAll(MapRow).FirstOrDefault();
    }

    /// <summary>新建一条报告。Id 与时间戳由仓储生成。</summary>
    public Report Create(Report draft)
    {
        var id = Ids.NewId();
        var now = _nowIso();

        using var command = _connection.Command("""
            INSERT INTO reports (
              id, kind, period_start, period_end,
              auto_markdown, manual_text, final_markdown,
              created_at, updated_at
            ) VALUES (
              @id, @kind, @periodStart, @periodEnd,
              @autoMarkdown, @manualText, @finalMarkdown,
              @createdAt, @updatedAt
            )
            """).Bind(
            ("id", id),
            ("kind", draft.Kind.ToDb()),
            ("periodStart", draft.PeriodStart),
            ("periodEnd", draft.PeriodEnd),
            ("autoMarkdown", draft.AutoMarkdown),
            ("manualText", draft.ManualText),
            ("finalMarkdown", draft.FinalMarkdown),
            ("createdAt", now),
            ("updatedAt", now));
        command.ExecuteNonQuery();

        return Get(id) ?? throw new InvalidOperationException("报告创建后读取失败");
    }

    /// <summary>
    /// 覆盖报告正文。周期与类型不动 —— 那是这条报告的身份。
    /// </summary>
    public Report Update(Report report)
    {
        using var command = _connection.Command("""
            UPDATE reports SET
              auto_markdown = @autoMarkdown,
              manual_text = @manualText,
              final_markdown = @finalMarkdown,
              updated_at = @updatedAt
            WHERE id = @id
            """).Bind(
            ("id", report.Id),
            ("autoMarkdown", report.AutoMarkdown),
            ("manualText", report.ManualText),
            ("finalMarkdown", report.FinalMarkdown),
            ("updatedAt", _nowIso()));
        command.ExecuteNonQuery();

        return Get(report.Id) ?? throw new InvalidOperationException("报告更新后读取失败");
    }

    public bool Remove(string id)
    {
        using var command = _connection.Command("DELETE FROM reports WHERE id = @id").Bind(("id", id));
        return command.ExecuteNonQuery() > 0;
    }
}
```

- [x] **Step 4: 运行测试确认通过**

Run: `dotnet test tests/WorkStation.Tests/WorkStation.Tests.csproj --filter "FullyQualifiedName~ReportsRepositoryTests"`
Expected: PASS（6 条）

- [x] **Step 5: 全量测试并提交**

Run: `dotnet test`
Expected: 全部 PASS

```bash
git add src/WorkStation.Core/Data/Repositories/ReportsRepository.cs tests/WorkStation.Tests/ReportsRepositoryTests.cs
git commit -m "feat(reports): 报告仓储，一个周期一条"
```

---

### Task 4: 服务层 —— 报告读写与「重新汇总」

**Files:**
- Modify: `src/WorkStation.Core/Services/WorkStationService.cs`
- Test: `tests/WorkStation.Tests/WorkStationServiceReportsTests.cs`

**Interfaces:**
- Consumes: `ReportsRepository`（Task 3）、`ReportComposer` / `ReportPeriods` / `ReportStats`（Task 2）
- Produces（`WorkStationService` 新增）：
  - `Result<IReadOnlyList<Report>> ReportsList(ReportKind? kind = null)`
  - `Result<Report?> ReportsGet(string id)`
  - `Result<Report> ReportsEnsure(ReportKind kind, string periodStart)` —— 幂等：已有就原样返回，没有就建一条并立即生成自动汇总
  - `Result<Report> ReportsUpdate(string id, string manualText)` —— 保存人工补充，重算最终稿
  - `Result<Report> ReportsRegenerate(string id)` —— 重算自动汇总，**保留人工补充**
  - `Result<bool> ReportsRemove(string id)`
  - `Result<ReportStats> ReportsStats(ReportKind kind)` —— 当前周期的统计（通知正文用）

- [x] **Step 1: 写失败的测试**

创建 `tests/WorkStation.Tests/WorkStationServiceReportsTests.cs`：

```csharp
using Microsoft.Data.Sqlite;
using WorkStation.Core;
using WorkStation.Core.Models;
using WorkStation.Core.Services;
using WorkStation.Core.Time;

namespace WorkStation.Tests;

/// <summary>
/// 报告服务层：幂等生成、重新汇总不覆盖人工补充、失败一律转 Result。
/// </summary>
public class WorkStationServiceReportsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WorkStationService _service;

    public WorkStationServiceReportsTests()
    {
        _connection = TestDb.OpenMigrated();
        _service = new WorkStationService(_connection, ":memory:", "0.1.0-test");
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void ReportsEnsure_CreatesReportWithAutoSummary()
    {
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "写周报",
            DueAt = LocalTime.ToLocalDateTime(DateTime.Today.AddHours(18))
        });

        var result = _service.ReportsEnsure(ReportKind.Daily, LocalTime.ToLocalDate(DateTime.Today));

        Assert.True(result.Ok);
        var report = result.Data!;
        Assert.Equal(ReportKind.Daily, report.Kind);
        Assert.Contains("### 统计", report.AutoMarkdown);
        Assert.Equal(report.AutoMarkdown, report.FinalMarkdown);
    }

    [Fact]
    public void ReportsEnsure_IsIdempotent()
    {
        var start = LocalTime.ToLocalDate(DateTime.Today);

        var first = _service.ReportsEnsure(ReportKind.Daily, start).Data!;
        var second = _service.ReportsEnsure(ReportKind.Daily, start).Data!;

        Assert.Equal(first.Id, second.Id);
        Assert.Single(_service.ReportsList(ReportKind.Daily).Data!);
    }

    [Fact]
    public void ReportsEnsure_KeepsManualTextOfExistingReport()
    {
        var start = LocalTime.ToLocalDate(DateTime.Today);
        var created = _service.ReportsEnsure(ReportKind.Daily, start).Data!;
        _service.ReportsUpdate(created.Id, "我写的补充");

        var again = _service.ReportsEnsure(ReportKind.Daily, start).Data!;

        Assert.Equal("我写的补充", again.ManualText);
    }

    [Fact]
    public void ReportsUpdate_SavesManualTextAndRebuildsFinalMarkdown()
    {
        var start = LocalTime.ToLocalDate(DateTime.Today);
        var created = _service.ReportsEnsure(ReportKind.Daily, start).Data!;

        var updated = _service.ReportsUpdate(created.Id, "我写的补充").Data!;

        Assert.Equal("我写的补充", updated.ManualText);
        Assert.EndsWith("我写的补充", updated.FinalMarkdown);
        Assert.Contains(created.AutoMarkdown, updated.FinalMarkdown);
    }

    [Fact]
    public void ReportsRegenerate_KeepsManualTextAndRefreshesAutoMarkdown()
    {
        var start = LocalTime.ToLocalDate(DateTime.Today);
        var created = _service.ReportsEnsure(ReportKind.Daily, start).Data!;
        _service.ReportsUpdate(created.Id, "我写的补充");

        // 汇总之后又加了一件事：重新汇总必须把它算进来。
        // 少了这一步，「没重算、原样返回旧记录」的实现照样能通过断言。
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "重新汇总之后才加的事",
            DueAt = LocalTime.ToLocalDateTime(DateTime.Today.AddHours(18))
        });

        var regenerated = _service.ReportsRegenerate(created.Id).Data!;

        Assert.Equal("我写的补充", regenerated.ManualText);
        Assert.Contains("重新汇总之后才加的事", regenerated.AutoMarkdown);
        Assert.Contains("### 统计", regenerated.AutoMarkdown);
        Assert.EndsWith("我写的补充", regenerated.FinalMarkdown);
    }

    [Fact]
    public void ReportsUpdate_UnknownId_ReturnsFailure()
    {
        var result = _service.ReportsUpdate("不存在", "随便写点");

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.NotFound, result.Error?.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Error?.Message));
    }

    [Fact]
    public void ReportsStats_ReturnsCurrentPeriodNumbers()
    {
        // 上周到期的不算今天的日报 —— 统计必须按当前周期取数，
        // 少了这条，用错周期（或者干脆统计全库）的实现照样能过。
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "上周到期的",
            DueAt = LocalTime.ToLocalDateTime(DateTime.Today.AddDays(-7).AddHours(18))
        });

        _service.TasksCreate(new CreateTaskInput
        {
            Title = "今天到期的",
            DueAt = LocalTime.ToLocalDateTime(DateTime.Today.AddHours(18))
        });

        var stats = _service.ReportsStats(ReportKind.Daily).Data!;

        Assert.Equal(0, stats.Completed);
        Assert.Equal(1, stats.CarriedOver);
        Assert.Equal(1, stats.Total);
        // Overdue 不断言：逾期比的是截止日历日与今天，今天到期的任务恒为 0，不断言是保守选择
    }

    [Fact]
    public void ReportsList_EmptyAtStart()
    {
        Assert.Empty(_service.ReportsList().Data!);
    }
}
```

- [x] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/WorkStation.Tests/WorkStation.Tests.csproj --filter "FullyQualifiedName~WorkStationServiceReportsTests"`
Expected: FAIL —— `ReportsEnsure` 等方法不存在

- [x] **Step 3: 实现服务层方法**

`src/WorkStation.Core/Services/WorkStationService.cs`：

1. 顶部 using 增加 `using WorkStation.Core.Reports;`
2. 字段区增加 `private readonly ReportsRepository _reports;`
3. 构造函数里增加 ` _reports = new ReportsRepository(connection);`
4. **给 `Guard` 加一条 catch**：`KeyNotFoundException` → `ErrorCodes.NotFound`。
   `ErrorCodes.NotFound` 此前定义了却没人用过，「报告不存在」这类错误现在有了机器可读的码。
   注意 catch 顺序：`KeyNotFoundException` 要排在兜底的 `catch (Exception)` **之前**。
   用 `System.Collections.Generic.KeyNotFoundException`（BCL 里没有 `System.NotFoundException`；
   `System.IO.NotFoundException` 是文件语义，用在这里不合适）。
   `Guard` 是所有服务方法共用的私有静态方法，现有代码里没有任何一条路径会抛
   `KeyNotFoundException`（字典访问全用 `TryGetValue`，没有裸下标读）—— 加这条 catch 不改变既有行为。
   **这条 catch 的 Message 不要透传 `error.Message`**：将来任何裸字典下标读抛出的 BCL 消息都是英文
   （`The given key 'x' was not present in the dictionary.`），会违反「Message 是可直接展示的中文文案」。
   用固定中文模板，例如 `$"{operation}失败：未找到对应记录"`。
5. 在「---------- 提醒 ----------」这一段**之前**插入新的一段：

```csharp
    // ---------- 报告 ----------

    /// <summary>报告列表。kind 为 null 时返回全部类型，按周期起点倒序。</summary>
    public Result<IReadOnlyList<Report>> ReportsList(ReportKind? kind = null) =>
        Guard("读取报告列表", () => _reports.List(kind));

    public Result<Report?> ReportsGet(string id) => Guard("读取报告", () => _reports.Get(id));

    /// <summary>
    /// 取某个周期的报告，没有就建一条并立即生成自动汇总。
    ///
    /// **幂等**：周期已存在就原样返回（人工补充必须留着，绝不能被自动汇总覆盖）。
    /// </summary>
    public Result<Report> ReportsEnsure(ReportKind kind, string periodStart) =>
        Guard("生成报告", () =>
        {
            // 周期起点必须形如 YYYY-MM-dd：格式不对时兜底 catch 会给出英文报错，
            // 那不是能直接展示给用户的中文文案
            if (!DateTime.TryParseExact(periodStart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                throw new ArgumentException("周期起点应形如 YYYY-MM-DD");
            }

            var existing = _reports.GetByPeriod(kind, periodStart);
            if (existing is not null) return existing;

            var period = ReportPeriods.FromStart(kind, periodStart);
            var auto = ReportComposer.Compose(period, _tasks.List(), _projects.List(true), DateTime.Now);

            return _reports.Create(new Report
            {
                Id = string.Empty,
                Kind = kind,
                PeriodStart = period.PeriodStart,
                PeriodEnd = period.PeriodEnd,
                AutoMarkdown = auto,
                ManualText = string.Empty,
                FinalMarkdown = auto,
                CreatedAt = string.Empty,
                UpdatedAt = string.Empty
            });
        });

    /// <summary>保存人工补充说明，并重算最终稿。</summary>
    public Result<Report> ReportsUpdate(string id, string manualText) =>
        Guard("保存报告补充说明", () =>
        {
            var existing = _reports.Get(id);
            if (existing is null) throw new KeyNotFoundException("报告不存在");

            return _reports.Update(existing with
            {
                ManualText = manualText,
                FinalMarkdown = ReportComposer.Merge(existing.AutoMarkdown, manualText)
            });
        });

    /// <summary>
    /// 重新汇总：用当前任务数据重算自动汇总。
    ///
    /// **只动自动汇总与最终稿，人工补充一律保留** —— 周末做完几件事后
    /// 点一下这个按钮，不该把手写的总结冲掉。
    /// </summary>
    public Result<Report> ReportsRegenerate(string id) =>
        Guard("重新汇总报告", () =>
        {
            var existing = _reports.Get(id);
            if (existing is null) throw new KeyNotFoundException("报告不存在");

            var period = ReportPeriods.FromStart(existing.Kind, existing.PeriodStart);
            var auto = ReportComposer.Compose(period, _tasks.List(), _projects.List(true), DateTime.Now);

            return _reports.Update(existing with
            {
                AutoMarkdown = auto,
                FinalMarkdown = ReportComposer.Merge(auto, existing.ManualText)
            });
        });

    public Result<bool> ReportsRemove(string id) => Guard("删除报告", () => _reports.Remove(id));

    /// <summary>当前周期的统计数字，报告提醒的通知正文用它。</summary>
    public Result<ReportStats> ReportsStats(ReportKind kind) =>
        Guard("统计报告数据", () =>
        {
            var now = DateTime.Now;
            var period = ReportPeriods.Current(kind, now);
            return ReportComposer.ComputeStats(period, _tasks.List(), now);
        });
```

- [x] **Step 4: 运行测试确认通过**

Run: `dotnet test tests/WorkStation.Tests/WorkStation.Tests.csproj --filter "FullyQualifiedName~WorkStationServiceReportsTests"`
Expected: PASS（8 条）

- [x] **Step 5: 全量测试并提交**

Run: `dotnet test`
Expected: 全部 PASS

```bash
git add src/WorkStation.Core/Services/WorkStationService.cs tests/WorkStation.Tests/WorkStationServiceReportsTests.cs
git commit -m "feat(reports): 报告服务层，重新汇总保留人工补充"
```

---

### Task 5: Core —— 报告提醒的设置模型与触发判定

**Files:**
- Create: `src/WorkStation.Core/Models/ReportReminderSettings.cs`
- Create: `src/WorkStation.Core/Reports/ReportReminderPlanner.cs`
- Modify: `src/WorkStation.Core/Services/WorkStationService.cs`（加提醒设置的读写方法）
- Test: `tests/WorkStation.Tests/ReportReminderPlannerTests.cs`
- Test: `tests/WorkStation.Tests/ReportReminderSettingsTests.cs`

> **本任务只做「加」不做「删」**：站会提醒还在用着，删它要连 App 层一起换（Task 8）。
> 这样每个任务结束时仓库都是可编译可测试的。

**Interfaces:**
- Consumes: `ReportKind`、`ReportPeriods`（Task 2）
- Produces:
  - `record ReportReminderEntry { bool Enabled; string Time; int DayOfWeek; int DayOfMonth; }`（`WorkStation.Core.Models`）
  - `record ReportReminderSettings { ReportReminderEntry Daily; ReportReminderEntry Weekly; ReportReminderEntry Monthly; }`
  - `static class ReportReminderPlanner`（`WorkStation.Core.Reports`）：
    - `const string DefaultTime = "09:00"`
    - `bool IsValidTime(string? value)`
    - `int IsoDayOfWeek(DateTime date)` —— 1=周一 … 7=周日
    - `int ResolveMonthDay(int requestedDay, DateTime localNow)` —— 没有该号就落到当月最后一天
    - `string PeriodKey(ReportKind kind, DateTime now)` —— 去重键：日报=今天，周报=本周一，月报=本月 1 号
    - `bool ShouldFire(ReportKind kind, ReportReminderEntry entry, DateTime now, string? lastFiredKey)`
  - `WorkStationService` 新增：
    - `Result<ReportReminderSettings> ReportRemindersGet()`
    - `Result<bool> ReportRemindersSet(ReportReminderSettings settings)`
    - `Result<string?> ReportReminderLastFired(ReportKind kind)`
    - `Result<bool> ReportReminderMarkFired(ReportKind kind, string key)`

- [x] **Step 1: 写失败的测试（判定）**

创建 `tests/WorkStation.Tests/ReportReminderPlannerTests.cs`：

```csharp
using WorkStation.Core.Models;
using WorkStation.Core.Reports;

namespace WorkStation.Tests;

/// <summary>三类报告提醒的触发判定与去重。</summary>
public class ReportReminderPlannerTests
{
    private static DateTime At(int month, int day, int hour, int minute = 0) =>
        new(2026, month, day, hour, minute, 0, DateTimeKind.Local);

    private static ReportReminderEntry Entry(
        bool enabled = true,
        string time = "09:00",
        int dayOfWeek = 5,
        int dayOfMonth = 31) => new()
    {
        Enabled = enabled,
        Time = time,
        DayOfWeek = dayOfWeek,
        DayOfMonth = dayOfMonth
    };

    [Fact]
    public void IsValidTime_AcceptsOnlyHhMm()
    {
        Assert.True(ReportReminderPlanner.IsValidTime("09:00"));
        Assert.True(ReportReminderPlanner.IsValidTime("23:30"));
        Assert.False(ReportReminderPlanner.IsValidTime("9:00"));
        Assert.False(ReportReminderPlanner.IsValidTime("25:00"));
        Assert.False(ReportReminderPlanner.IsValidTime(null));
    }

    [Fact]
    public void IsoDayOfWeek_UsesMondayAsOne()
    {
        Assert.Equal(1, ReportReminderPlanner.IsoDayOfWeek(At(11, 2)));   // 周一
        Assert.Equal(5, ReportReminderPlanner.IsoDayOfWeek(At(11, 6)));   // 周五
        Assert.Equal(7, ReportReminderPlanner.IsoDayOfWeek(At(11, 8)));   // 周日
    }

    [Fact]
    public void ResolveMonthDay_FallsBackToLastDayOfMonth()
    {
        // 2026 年 11 月只有 30 天
        Assert.Equal(30, ReportReminderPlanner.ResolveMonthDay(31, At(11, 8)));
        Assert.Equal(28, ReportReminderPlanner.ResolveMonthDay(31, At(2, 10)));
        Assert.Equal(15, ReportReminderPlanner.ResolveMonthDay(15, At(11, 8)));
    }

    [Fact]
    public void Daily_FiresOnceAfterTime()
    {
        var entry = Entry(time: "09:00");

        Assert.False(ReportReminderPlanner.ShouldFire(ReportKind.Daily, entry, At(11, 8, 8, 59), null));
        Assert.True(ReportReminderPlanner.ShouldFire(ReportKind.Daily, entry, At(11, 8, 9, 0), null));
    }

    [Fact]
    public void Daily_DoesNotFireTwiceInSameDay()
    {
        var entry = Entry(time: "09:00");
        var key = ReportReminderPlanner.PeriodKey(ReportKind.Daily, At(11, 8));

        Assert.False(ReportReminderPlanner.ShouldFire(ReportKind.Daily, entry, At(11, 8, 18, 0), key));
        Assert.True(ReportReminderPlanner.ShouldFire(ReportKind.Daily, entry, At(11, 9, 9, 0), key));
    }

    [Fact]
    public void Weekly_FiresOnlyOnConfiguredWeekday()
    {
        // 2026-11-06 是周五
        var entry = Entry(time: "17:00", dayOfWeek: 5);

        Assert.False(ReportReminderPlanner.ShouldFire(ReportKind.Weekly, entry, At(11, 5, 17, 0), null));
        Assert.True(ReportReminderPlanner.ShouldFire(ReportKind.Weekly, entry, At(11, 6, 17, 0), null));
    }

    [Fact]
    public void Weekly_KeyIsMondayOfThatWeek()
    {
        Assert.Equal("2026-11-02", ReportReminderPlanner.PeriodKey(ReportKind.Weekly, At(11, 6)));
        Assert.Equal("2026-11-02", ReportReminderPlanner.PeriodKey(ReportKind.Weekly, At(11, 8)));
    }

    [Fact]
    public void Weekly_DoesNotFireAgainInSameWeek()
    {
        var entry = Entry(time: "17:00", dayOfWeek: 5);
        var key = ReportReminderPlanner.PeriodKey(ReportKind.Weekly, At(11, 6));

        Assert.False(ReportReminderPlanner.ShouldFire(ReportKind.Weekly, entry, At(11, 6, 20, 0), key));
        // 下一周的周五：键变了，可以再弹
        Assert.True(ReportReminderPlanner.ShouldFire(ReportKind.Weekly, entry, At(11, 13, 17, 0), key));
    }

    [Fact]
    public void Monthly_FiresOnResolvedDayOfMonth()
    {
        var entry = Entry(time: "17:00", dayOfMonth: 31);

        // 11 月只有 30 天，设定 31 号 → 落到 30 号
        Assert.True(ReportReminderPlanner.ShouldFire(ReportKind.Monthly, entry, At(11, 30, 17, 0), null));
        Assert.False(ReportReminderPlanner.ShouldFire(ReportKind.Monthly, entry, At(11, 29, 17, 0), null));
    }

    [Fact]
    public void Monthly_KeyIsFirstDayOfMonth()
    {
        Assert.Equal("2026-11-01", ReportReminderPlanner.PeriodKey(ReportKind.Monthly, At(11, 30)));
    }

    [Fact]
    public void DisabledEntry_NeverFires()
    {
        var entry = Entry(enabled: false, time: "09:00");

        Assert.False(ReportReminderPlanner.ShouldFire(ReportKind.Daily, entry, At(11, 8, 10, 0), null));
    }
}
```

- [x] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/WorkStation.Tests/WorkStation.Tests.csproj --filter "FullyQualifiedName~ReportReminderPlannerTests"`
Expected: FAIL —— `ReportReminderPlanner` 不存在

- [x] **Step 3: 实现设置模型与判定**

创建 `src/WorkStation.Core/Models/ReportReminderSettings.cs`：

```csharp
namespace WorkStation.Core.Models;

/// <summary>
/// 一类报告提醒的设置。
///
/// 三类提醒共用一个形状：日报不用星期与几号，周报不用几号，月报不用星期 ——
/// 拆成三个类型反而让设置页与存取都变复杂。
/// </summary>
public sealed record ReportReminderEntry
{
    public bool Enabled { get; init; }

    /// <summary>本地墙钟 HH:mm，24 小时制。</summary>
    public string Time { get; init; } = "09:00";

    /// <summary>周报用：ISO 星期几，1=周一 … 7=周日。</summary>
    public int DayOfWeek { get; init; } = 5;

    /// <summary>月报用：几号，1–31。没有该号的月份落到当月最后一天。</summary>
    public int DayOfMonth { get; init; } = 31;
}

/// <summary>
/// 三类报告提醒的设置（存在 settings.report_reminders）。
/// </summary>
public sealed record ReportReminderSettings
{
    /// <summary>日报：每天一次，默认 09:00。</summary>
    public ReportReminderEntry Daily { get; init; } = new() { Enabled = true, Time = "09:00" };

    /// <summary>周报：每周一次，默认周五 17:00。</summary>
    public ReportReminderEntry Weekly { get; init; } = new() { Enabled = true, Time = "17:00", DayOfWeek = 5 };

    /// <summary>月报：每月一次，默认每月 31 号 17:00（没有 31 号的月落到当月最后一天）。</summary>
    public ReportReminderEntry Monthly { get; init; } = new() { Enabled = true, Time = "17:00", DayOfMonth = 31 };
}
```

创建 `src/WorkStation.Core/Reports/ReportReminderPlanner.cs`：

```csharp
using System.Globalization;
using WorkStation.Core.Models;
using WorkStation.Core.Time;

namespace WorkStation.Core.Reports;

/// <summary>
/// 报告提醒的触发判定 —— 纯函数。
///
/// 报告提醒不落 reminders 表（每天/每周/每月一次，没有「处理」的历史意义），
/// 用「上次触发的周期键」去重：日报的键是今天、周报是本周一、月报是本月 1 号。
/// </summary>
public static class ReportReminderPlanner
{
    public const string DefaultTime = "09:00";

    public static bool IsValidTime(string? value) =>
        value is not null
        && TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out _);

    /// <summary>ISO 星期几：1=周一 … 7=周日。.NET 的 DayOfWeek 是 0=周日，要转。</summary>
    public static int IsoDayOfWeek(DateTime date) => ((int)date.DayOfWeek + 6) % 7 + 1;

    /// <summary>设定的几号在当月不存在时，落到当月最后一天（31 号 → 2 月 28 号）。</summary>
    public static int ResolveMonthDay(int requestedDay, DateTime localNow)
    {
        var clamped = Math.Clamp(requestedDay, 1, 31);
        var daysInMonth = DateTime.DaysInMonth(localNow.Year, localNow.Month);
        return Math.Min(clamped, daysInMonth);
    }

    /// <summary>
    /// 去重键：这个周期的起点。
    /// 日报是今天，周报是本周一，月报是本月 1 号。
    /// </summary>
    public static string PeriodKey(ReportKind kind, DateTime now) =>
        ReportPeriods.Current(kind, now).PeriodStart;

    /// <summary>
    /// 该不该在这一拍触发。
    ///
    /// lastFiredKey 是上次触发的周期键：与当前周期键相同就说明这个周期已经报过了。
    /// </summary>
    public static bool ShouldFire(ReportKind kind, ReportReminderEntry entry, DateTime now, string? lastFiredKey)
    {
        if (!entry.Enabled) return false;
        if (!TryParseTime(entry.Time, out var at)) return false;

        var local = LocalTime.AsLocal(now);
        if (local.TimeOfDay < at) return false;

        var key = PeriodKey(kind, local);
        if (string.Equals(lastFiredKey, key, StringComparison.Ordinal)) return false;

        return kind switch
        {
            ReportKind.Daily => true,
            ReportKind.Weekly => IsoDayOfWeek(local) == entry.DayOfWeek,
            _ => local.Day == ResolveMonthDay(entry.DayOfMonth, local)
        };
    }

    private static bool TryParseTime(string? value, out TimeSpan time)
    {
        if (value is not null
            && TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out time))
        {
            return true;
        }

        time = TimeSpan.ParseExact(DefaultTime, @"hh\:mm", CultureInfo.InvariantCulture);
        return false;
    }
}
```

- [x] **Step 4: 运行判定测试确认通过**

Run: `dotnet test tests/WorkStation.Tests/WorkStation.Tests.csproj --filter "FullyQualifiedName~ReportReminderPlannerTests"`
Expected: PASS（12 条）

- [x] **Step 5: 写失败的测试（设置存取）**

创建 `tests/WorkStation.Tests/ReportReminderSettingsTests.cs`：

```csharp
using Microsoft.Data.Sqlite;
using WorkStation.Core;
using WorkStation.Core.Models;
using WorkStation.Core.Services;

namespace WorkStation.Tests;

/// <summary>报告提醒设置的存取：默认值、改完能存回来、旧的站会设置能接上。</summary>
public class ReportReminderSettingsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WorkStationService _service;

    public ReportReminderSettingsTests()
    {
        _connection = TestDb.OpenMigrated();
        _service = new WorkStationService(_connection, ":memory:", "0.1.0-test");
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void Get_ReturnsDefaultsWhenNothingStored()
    {
        var settings = _service.ReportRemindersGet().Data!;

        Assert.True(settings.Daily.Enabled);
        Assert.Equal("09:00", settings.Daily.Time);
        Assert.True(settings.Weekly.Enabled);
        Assert.Equal("17:00", settings.Weekly.Time);
        Assert.Equal(5, settings.Weekly.DayOfWeek);
        Assert.True(settings.Monthly.Enabled);
        Assert.Equal(31, settings.Monthly.DayOfMonth);
    }

    [Fact]
    public void Set_ThenGet_RoundTrips()
    {
        var saved = _service.ReportRemindersSet(new ReportReminderSettings
        {
            Daily = new ReportReminderEntry { Enabled = false, Time = "08:30" },
            Weekly = new ReportReminderEntry { Enabled = true, Time = "18:00", DayOfWeek = 3 },
            Monthly = new ReportReminderEntry { Enabled = false, Time = "10:00", DayOfMonth = 5 }
        });

        Assert.True(saved.Ok);

        var settings = _service.ReportRemindersGet().Data!;
        Assert.False(settings.Daily.Enabled);
        Assert.Equal("08:30", settings.Daily.Time);
        Assert.Equal(3, settings.Weekly.DayOfWeek);
        Assert.Equal(5, settings.Monthly.DayOfMonth);
    }

    [Fact]
    public void Set_InvalidTime_ReturnsValidationFailed()
    {
        var result = _service.ReportRemindersSet(new ReportReminderSettings
        {
            Daily = new ReportReminderEntry { Enabled = true, Time = "9:00" }
        });

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.ValidationFailed, result.Error?.Code);
        Assert.Contains("HH:mm", result.Error?.Message);
    }

    [Fact]
    public void LastFired_StartsEmptyAndStoresKey()
    {
        Assert.Null(_service.ReportReminderLastFired(ReportKind.Daily).Data);

        _service.ReportReminderMarkFired(ReportKind.Daily, "2026-11-08");

        Assert.Equal("2026-11-08", _service.ReportReminderLastFired(ReportKind.Daily).Data);
        Assert.Null(_service.ReportReminderLastFired(ReportKind.Weekly).Data);
    }

    /// <summary>老用户库里的 daily_review 设置要能接上：日报沿用原来的开关与时间。</summary>
    [Fact]
    public void Get_FallsBackToLegacyDailyReviewSetting()
    {
        TestDb.Execute(_connection, """
            INSERT INTO settings (key, value) VALUES ('daily_review', '{"Enabled":true,"Time":"07:45"}')
            """);

        var settings = _service.ReportRemindersGet().Data!;

        Assert.True(settings.Daily.Enabled);
        Assert.Equal("07:45", settings.Daily.Time);
    }
}
```

- [x] **Step 6: 运行测试确认失败**

Run: `dotnet test tests/WorkStation.Tests/WorkStation.Tests.csproj --filter "FullyQualifiedName~ReportReminderSettingsTests"`
Expected: FAIL —— `ReportRemindersGet` 等方法不存在

- [x] **Step 7: 实现服务层方法**

`src/WorkStation.Core/Services/WorkStationService.cs`：

1. using 增加 `using WorkStation.Core.Reports;`
2. 在「---------- 提醒 ----------」段末尾（`CloseToTraySet` 之前）插入：

```csharp
    // ---------- 报告提醒 ----------

    /// <summary>
    /// 三类报告提醒的设置。
    ///
    /// 读不到新键时回落到旧的 daily_review：老用户设过的站会时间不该丢，
    /// 把它填进日报那一项，其余用默认值。
    /// </summary>
    public Result<ReportReminderSettings> ReportRemindersGet() =>
        Guard("读取报告提醒设置", () =>
        {
            var stored = _settings.Get("report_reminders");
            if (stored is { } element)
            {
                return element.Deserialize<ReportReminderSettings>() ?? new ReportReminderSettings();
            }

            var legacy = _settings.Get("daily_review");
            if (legacy is not { } old) return new ReportReminderSettings();

            var enabled = old.TryGetProperty("Enabled", out var enabledValue)
                          && enabledValue.ValueKind == JsonValueKind.True;
            var time = old.TryGetProperty("Time", out var timeValue)
                       && timeValue.ValueKind == JsonValueKind.String
                ? timeValue.GetString() ?? ReportReminderPlanner.DefaultTime
                : ReportReminderPlanner.DefaultTime;

            var defaults = new ReportReminderSettings();
            return defaults with
            {
                Daily = defaults.Daily with { Enabled = enabled, Time = time }
            };
        });

    public Result<bool> ReportRemindersSet(ReportReminderSettings settings) =>
        Guard("保存报告提醒设置", () =>
        {
            Validate(settings.Daily, "日报");
            Validate(settings.Weekly, "周报");
            Validate(settings.Monthly, "月报");

            if (settings.Weekly.DayOfWeek is < 1 or > 7)
            {
                throw new ArgumentException("周报提醒的星期应为 1（周一）到 7（周日）");
            }

            if (settings.Monthly.DayOfMonth is < 1 or > 31)
            {
                throw new ArgumentException("月报提醒的几号应为 1 到 31");
            }

            _settings.Set("report_reminders", settings);
            return true;
        });

    /// <summary>某类提醒上次触发的周期键；没触发过返回 null。</summary>
    public Result<string?> ReportReminderLastFired(ReportKind kind) =>
        Guard("读取报告提醒记录", () =>
        {
            var raw = _settings.GetString(LastFiredKey(kind), string.Empty);
            return string.IsNullOrEmpty(raw) ? null : raw;
        });

    public Result<bool> ReportReminderMarkFired(ReportKind kind, string key) =>
        Guard("记录报告提醒", () =>
        {
            _settings.Set(LastFiredKey(kind), key);
            return true;
        });

    private static void Validate(ReportReminderEntry entry, string label)
    {
        if (!ReportReminderPlanner.IsValidTime(entry.Time))
        {
            throw new ArgumentException($"{label}提醒时间应形如 HH:mm");
        }
    }

    private static string LastFiredKey(ReportKind kind) => kind switch
    {
        ReportKind.Daily => "report_reminder_daily_last",
        ReportKind.Weekly => "report_reminder_weekly_last",
        _ => "report_reminder_monthly_last"
    };
```

> `WorkStationService.cs` 顶部原本就有 `using System.Text.Json;`（`JsonValueKind` 需要它），确认一下别丢。

- [x] **Step 8: 运行测试确认通过**

Run: `dotnet test tests/WorkStation.Tests/WorkStation.Tests.csproj --filter "FullyQualifiedName~ReportReminderSettingsTests"`
Expected: PASS（5 条）

- [x] **Step 9: 全量测试并提交**

Run: `dotnet test`
Expected: 全部 PASS

```bash
git add src/WorkStation.Core/Models/ReportReminderSettings.cs src/WorkStation.Core/Reports/ReportReminderPlanner.cs src/WorkStation.Core/Services/WorkStationService.cs tests/WorkStation.Tests/ReportReminderPlannerTests.cs tests/WorkStation.Tests/ReportReminderSettingsTests.cs
git commit -m "feat(reports): 三类报告提醒的设置与触发判定"
```

---

### Task 6: 报告页 ViewModel

**Files:**
- Create: `src/WorkStation.App/ViewModels/ReportsViewModel.cs`
- Test: `tests/WorkStation.Tests/ReportsViewModelTests.cs`

**Interfaces:**
- Consumes: `WorkStationService.Reports*`（Task 4）、`ReportPeriods` / `ReportMarkdown`（Task 2）、`ReportKind`
- Produces（`WorkStation.App.ViewModels`）：
  - `sealed partial class ReportsViewModel : ObservableObject`
    - 构造：`ReportsViewModel(WorkStationService service, Action<string, bool> toast, Func<DateTime>? clock = null)`
    - `ObservableCollection<ReportKindOption> Kinds` —— 日报 / 周报 / 月报三项
    - `ObservableCollection<ReportHistoryItem> History` —— 已有的周期
    - `ObservableCollection<ReportLineItem> AutoLines` —— 自动汇总（显示用行）
    - `ObservableCollection<ReportLineItem> FinalLines` —— 最终稿（显示用行）
    - `string ManualText { get; set; }` —— 人工补充（可编辑）
    - `string PeriodTitle { get; }` —— 周期区间文字
    - `string? Error { get; }`、`bool HasError { get; }`、`bool IsLoading { get; }`
    - `bool CanGoNext { get; }`
    - `bool HasHistory { get; }`
    - `ReportHistoryItem? SelectedHistory { get; set; }` —— 选中即跳到那一期
    - `Func<string, Task<bool>>? CopyToClipboard { get; set; }` —— 由视图注入
    - `Func<(string FileName, string Content), Task<bool>>? ExportToFile { get; set; }` —— 由视图注入
    - 命令：`SelectKindCommand`（参数 `ReportKind`）、`PreviousPeriodCommand`、`NextPeriodCommand`、`RegenerateCommand`、`SaveCommand`、`CopyCommand`、`ExportCommand`
    - 方法：`Reload()`
  - `sealed partial class ReportKindOption : ObservableObject` —— `ReportKind Kind`、`string Label`、`bool IsSelected`
  - `sealed class ReportHistoryItem` —— `ReportKind Kind`、`string Title`、`string PeriodStart`
  - `sealed class ReportLineItem` —— `string Text`、`bool IsBullet`、`bool IsHeading`（由 `ReportMarkdownLine` 转来）

- [x] **Step 1: 写失败的测试**

创建 `tests/WorkStation.Tests/ReportsViewModelTests.cs`：

```csharp
using Avalonia.Headless.XUnit;
using Microsoft.Data.Sqlite;
using WorkStation.App.ViewModels;
using WorkStation.Core.Models;
using WorkStation.Core.Services;

namespace WorkStation.Tests;

/// <summary>报告页：切类型、切周期、重新汇总不丢人工补充。</summary>
public class ReportsViewModelTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WorkStationService _service;

    public ReportsViewModelTests()
    {
        _connection = TestDb.OpenMigrated();
        _service = new WorkStationService(_connection, ":memory:");
    }

    public void Dispose() => _connection.Dispose();

    private ReportsViewModel NewViewModel(DateTime? now = null) =>
        new(_service, (_, _) => { }, () => now ?? DateTime.Now);

    [AvaloniaFact]
    public void Reload_LoadsTodayReport()
    {
        var vm = NewViewModel(new DateTime(2026, 11, 8, 10, 0, 0));

        vm.Reload();

        Assert.Equal(ReportKind.Daily, vm.Kinds[0].Kind);
        Assert.True(vm.Kinds[0].IsSelected);
        Assert.NotEmpty(vm.AutoLines);
        Assert.Equal("2026-11-08", vm.PeriodTitle);
        Assert.False(vm.HasError);
    }

    [AvaloniaFact]
    public void SelectKind_SwitchesPeriodKind()
    {
        var vm = NewViewModel(new DateTime(2026, 11, 8, 10, 0, 0));

        vm.Reload();
        vm.SelectKindCommand.Execute(ReportKind.Weekly);

        Assert.Equal("2026-11-02 ~ 2026-11-08", vm.PeriodTitle);
        Assert.True(vm.Kinds.Single(k => k.Kind == ReportKind.Weekly).IsSelected);
    }

    [AvaloniaFact]
    public void PreviousPeriod_MovesOneStepBack()
    {
        var vm = NewViewModel(new DateTime(2026, 11, 8, 10, 0, 0));

        vm.Reload();
        vm.PreviousPeriodCommand.Execute(null);

        Assert.Equal("2026-11-07", vm.PeriodTitle);
    }

    [AvaloniaFact]
    public void NextPeriod_IsBlockedAtCurrentPeriod()
    {
        var vm = NewViewModel(new DateTime(2026, 11, 8, 10, 0, 0));

        vm.Reload();

        Assert.False(vm.CanGoNext);
    }

    [AvaloniaFact]
    public void Save_KeepsManualTextInDatabase()
    {
        var vm = NewViewModel(new DateTime(2026, 11, 8, 10, 0, 0));

        vm.Reload();
        vm.ManualText = "今天把接口联调通了";
        vm.SaveCommand.Execute(null);

        var saved = _service.ReportsList(ReportKind.Daily).Data!;
        Assert.Single(saved);
        Assert.Equal("今天把接口联调通了", saved[0].ManualText);
        Assert.Contains("今天把接口联调通了", saved[0].FinalMarkdown);
    }

    [AvaloniaFact]
    public void Regenerate_KeepsManualText()
    {
        var vm = NewViewModel(new DateTime(2026, 11, 8, 10, 0, 0));

        vm.Reload();
        vm.ManualText = "手写的补充";
        vm.SaveCommand.Execute(null);

        vm.RegenerateCommand.Execute(null);

        var saved = _service.ReportsList(ReportKind.Daily).Data!;
        Assert.Equal("手写的补充", saved[0].ManualText);
    }

    [AvaloniaFact]
    public void History_ListsExistingReportsOfCurrentKind()
    {
        _service.ReportsEnsure(ReportKind.Daily, "2026-11-07");

        var vm = NewViewModel(new DateTime(2026, 11, 8, 10, 0, 0));
        vm.Reload();

        Assert.True(vm.HasHistory);
        Assert.Contains(vm.History, h => h.PeriodStart == "2026-11-07");
    }
}
```

- [x] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/WorkStation.Tests/WorkStation.Tests.csproj --filter "FullyQualifiedName~ReportsViewModelTests"`
Expected: FAIL —— `ReportsViewModel` 不存在

- [x] **Step 3: 实现 ViewModel**

创建 `src/WorkStation.App/ViewModels/ReportsViewModel.cs`：

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkStation.Core.Models;
using WorkStation.Core.Reports;
using WorkStation.Core.Services;
using WorkStation.Core.Time;

namespace WorkStation.App.ViewModels;

/// <summary>报告类型选项（日报 / 周报 / 月报）。</summary>
public sealed partial class ReportKindOption : ObservableObject
{
    public ReportKindOption(ReportKind kind, string label)
    {
        Kind = kind;
        Label = label;
    }

    public ReportKind Kind { get; }

    public string Label { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>历史报告列表里的一项。</summary>
public sealed class ReportHistoryItem
{
    public required ReportKind Kind { get; init; }

    /// <summary>列表上显示的标题（周期区间文字）。</summary>
    public required string Title { get; init; }

    /// <summary>周期起点 YYYY-MM-DD。</summary>
    public required string PeriodStart { get; init; }

    public override string ToString() => Title;
}

/// <summary>
/// 报告预览里的一行。
///
/// Core 的 <see cref="ReportMarkdownLine"/> 带着标题层级与列表标记，这里转成界面直接能用的形状：
/// 标题行走标题样式，列表项前面补上项目符号 —— 全按正文渲染，报告就是一堵等大的字。
/// </summary>
public sealed class ReportLineItem
{
    public required string Text { get; init; }

    /// <summary>是不是列表项。</summary>
    public bool IsBullet { get; init; }

    /// <summary>是不是小节标题（Markdown 的 ###）。</summary>
    public bool IsHeading { get; init; }

    /// <summary>是不是整篇的标题（Markdown 的 ##）。</summary>
    public bool IsTitle { get; init; }

    public static ReportLineItem From(ReportMarkdownLine line) => new()
    {
        Text = line.IsBullet ? $"• {line.Text}" : line.Text,
        IsBullet = line.IsBullet,
        IsHeading = line.Level == 2,
        IsTitle = line.Level == 1
    };
}

/// <summary>
/// 报告页：日报 / 周报 / 月报。
///
/// 左边是自动汇总（机器算的，随时可以重新汇总），
/// 右边是人工补充（手写的，**任何自动流程都不覆盖**），
/// 底下是两者拼起来的最终稿 —— 复制与导出的就是它。
/// </summary>
public sealed partial class ReportsViewModel : ObservableObject
{
    private readonly WorkStationService _service;
    private readonly Action<string, bool> _toast;
    private readonly Func<DateTime> _clock;

    private ReportPeriod _period;

    /// <summary>
    /// 历史列表正在重建时的闸门。
    ///
    /// 重建集合会让下拉框把选中项置空、随后自动挑第一项 —— 那一下会回调
    /// <see cref="OnSelectedHistoryChanged"/>，又触发一次加载，来回打转。
    /// 重建期间关掉这个闸门。
    /// </summary>
    private bool _suppressHistorySelection;

    public ReportsViewModel(
        WorkStationService service,
        Action<string, bool> toast,
        Func<DateTime>? clock = null)
    {
        _service = service;
        _toast = toast;
        _clock = clock ?? (() => DateTime.Now);

        Kinds =
        [
            new ReportKindOption(ReportKind.Daily, "日报"),
            new ReportKindOption(ReportKind.Weekly, "周报"),
            new ReportKindOption(ReportKind.Monthly, "月报")
        ];

        Kinds[0].IsSelected = true;
        _period = ReportPeriods.Current(ReportKind.Daily, _clock());
    }

    public ObservableCollection<ReportKindOption> Kinds { get; }

    public ObservableCollection<ReportHistoryItem> History { get; } = [];

    public ObservableCollection<ReportLineItem> AutoLines { get; } = [];

    public ObservableCollection<ReportLineItem> FinalLines { get; } = [];

    /// <summary>人工补充说明。</summary>
    [ObservableProperty]
    private string _manualText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _error;

    /// <summary>历史列表里选中的那一期。选中即跳到那一期。</summary>
    [ObservableProperty]
    private ReportHistoryItem? _selectedHistory;

    public bool HasError => Error is not null;

    public bool HasHistory => History.Count > 0;

    /// <summary>周期区间文字。</summary>
    public string PeriodTitle => ReportPeriods.Range(_period);

    /// <summary>能不能再往后翻一期：最多到当前周期，未来没有报告。</summary>
    public bool CanGoNext =>
        _period.PeriodStart != ReportPeriods.Current(SelectedKind, _clock()).PeriodStart;

    /// <summary>复制到剪贴板。由视图注入（剪贴板是平台能力，VM 不该直接碰）。</summary>
    public Func<string, Task<bool>>? CopyToClipboard { get; set; }

    /// <summary>导出 .md。由视图注入（文件选择器是平台能力）。</summary>
    public Func<(string FileName, string Content), Task<bool>>? ExportToFile { get; set; }

    private ReportKind SelectedKind =>
        Kinds.FirstOrDefault(k => k.IsSelected)?.Kind ?? ReportKind.Daily;

    private Report? Current { get; set; }

    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnSelectedHistoryChanged(ReportHistoryItem? value)
    {
        if (_suppressHistorySelection) return;
        if (value is null) return;

        var period = ReportPeriods.FromStart(value.Kind, value.PeriodStart);
        if (period.PeriodStart == _period.PeriodStart) return;

        LoadPeriod(period);
    }

    /// <summary>取数刷新：进页面与切类型、切周期时都走这里。</summary>
    public void Reload()
    {
        LoadPeriod(ReportPeriods.Current(SelectedKind, _clock()));
    }

    /// <summary>切换报告类型。</summary>
    [RelayCommand]
    private void SelectKind(ReportKind kind)
    {
        foreach (var option in Kinds)
        {
            option.IsSelected = option.Kind == kind;
        }

        LoadPeriod(ReportPeriods.Current(kind, _clock()));
    }

    [RelayCommand]
    private void PreviousPeriod() => LoadPeriod(ReportPeriods.Shift(_period, -1));

    [RelayCommand]
    private void NextPeriod()
    {
        if (!CanGoNext) return;
        LoadPeriod(ReportPeriods.Shift(_period, 1));
    }

    /// <summary>重新汇总：重算自动汇总，**保留人工补充**。</summary>
    [RelayCommand]
    private void Regenerate()
    {
        if (Current is null) return;

        var result = _service.ReportsRegenerate(Current.Id);
        if (!result.Ok)
        {
            Error = result.Error?.Message ?? "重新汇总失败";
            return;
        }

        Current = result.Data;
        Apply(Current);
        _toast("已重新汇总", false);
    }

    /// <summary>保存人工补充。</summary>
    [RelayCommand]
    private void Save()
    {
        if (Current is null) return;

        var result = _service.ReportsUpdate(Current.Id, ManualText);
        if (!result.Ok)
        {
            Error = result.Error?.Message ?? "保存失败";
            return;
        }

        Current = result.Data;
        Apply(Current);
        _toast("已保存", false);
    }

    /// <summary>复制最终稿到剪贴板。</summary>
    [RelayCommand]
    private async Task CopyAsync()
    {
        if (Current is null) return;
        if (CopyToClipboard is null)
        {
            _toast("复制失败：当前环境没有剪贴板", true);
            return;
        }

        var ok = await CopyToClipboard(Current.FinalMarkdown);
        _toast(ok ? "已复制 Markdown" : "复制失败", !ok);
    }

    /// <summary>导出最终稿为 .md 文件。</summary>
    [RelayCommand]
    private async Task ExportAsync()
    {
        if (Current is null) return;
        if (ExportToFile is null)
        {
            _toast("导出失败：当前环境没有文件选择器", true);
            return;
        }

        var fileName = $"{ReportPeriods.Label(Current.Kind)}-{Current.PeriodStart}.md";
        var ok = await ExportToFile((fileName, Current.FinalMarkdown));
        _toast(ok ? "已导出" : "已取消导出", !ok);
    }

    private void LoadPeriod(ReportPeriod period)
    {
        IsLoading = true;
        Error = null;

        try
        {
            _period = period;

            var result = _service.ReportsEnsure(period.Kind, period.PeriodStart);
            if (!result.Ok || result.Data is not { } report)
            {
                Error = result.Error?.Message ?? "生成报告失败";
                return;
            }

            Current = report;
            Apply(report);
            LoadHistory(period.Kind);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(PeriodTitle));
            OnPropertyChanged(nameof(CanGoNext));
        }
    }

    private void Apply(Report report)
    {
        ManualText = report.ManualText;

        AutoLines.Clear();
        foreach (var line in ReportMarkdown.Parse(report.AutoMarkdown))
        {
            AutoLines.Add(ReportLineItem.From(line));
        }

        FinalLines.Clear();
        foreach (var line in ReportMarkdown.Parse(report.FinalMarkdown))
        {
            FinalLines.Add(ReportLineItem.From(line));
        }
    }

    private void LoadHistory(ReportKind kind)
    {
        _suppressHistorySelection = true;
        try
        {
            History.Clear();

            var result = _service.ReportsList(kind);
            if (result.Ok && result.Data is { } reports)
            {
                foreach (var report in reports)
                {
                    History.Add(new ReportHistoryItem
                    {
                        Kind = report.Kind,
                        Title = ReportPeriods.Range(ReportPeriods.FromStart(report.Kind, report.PeriodStart)),
                        PeriodStart = report.PeriodStart
                    });
                }
            }

            // 选中当前这一期：下拉框里的选中项与正在看的内容必须一致
            SelectedHistory = History.FirstOrDefault(h => h.PeriodStart == _period.PeriodStart);
        }
        finally
        {
            _suppressHistorySelection = false;
        }

        OnPropertyChanged(nameof(HasHistory));
    }
}
```

- [x] **Step 4: 运行测试确认通过**

Run: `dotnet test tests/WorkStation.Tests/WorkStation.Tests.csproj --filter "FullyQualifiedName~ReportsViewModelTests"`
Expected: PASS（7 条）

- [x] **Step 5: 全量测试并提交**

Run: `dotnet test`
Expected: 全部 PASS

```bash
git add src/WorkStation.App/ViewModels/ReportsViewModel.cs tests/WorkStation.Tests/ReportsViewModelTests.cs
git commit -m "feat(reports): 报告页视图模型"
```

---

### Task 7: 报告页界面与外壳接线

**Files:**
- Create: `src/WorkStation.App/Views/ReportsView.axaml`
- Create: `src/WorkStation.App/Views/ReportsView.axaml.cs`
- Modify: `src/WorkStation.App/Controls/AppIcons.cs`（加 `Reports` 与 `ArrowLeft`）
- Modify: `src/WorkStation.App/ViewModels/MainWindowViewModel.cs`（`AppPage.Reports`、`Reports` 属性、导航、命令面板）
- Modify: `src/WorkStation.App/Views/MainWindow.axaml`（侧边栏项、DataTemplate、快捷键帮助文字）
- Modify: `src/WorkStation.App/Views/MainWindow.axaml.cs`（数字键 6/7 映射与注释）
- Test: `tests/WorkStation.Tests/UiSmokeTests.cs`
- Test: `tests/WorkStation.Tests/MainWindowViewModelTests.cs`
- Test: `tests/WorkStation.Tests/AppIconsTests.cs`（新建，验图标几何能解析）

**Interfaces:**
- Consumes: `ReportsViewModel`（Task 6）、`AppIcons`、`SukiUI`、`App.axaml` 的版式令牌
- Produces:
  - `AppPage.Reports`
  - `MainWindowViewModel.Reports`（`ReportsViewModel?`）、`IsReportsActive`、`ShowReportsCommand`
  - 命令面板条目「报告」
  - `AppIcons.Reports`、`AppIcons.ArrowLeft`
  - 快捷键：`6` / `Ctrl+6` → 报告页，`7` / `Ctrl+7` → 设置页

- [x] **Step 1: 写失败的测试**

`tests/WorkStation.Tests/UiSmokeTests.cs` 里追加：

```csharp
    [AvaloniaFact]
    public void ReportsView_LoadsXaml()
    {
        var vm = new ReportsViewModel(_service, (_, _) => { });
        vm.Reload();

        var view = new ReportsView { DataContext = vm };
        view.Measure(new Size(1000, 700));
        view.Arrange(new Rect(0, 0, 1000, 700));

        Assert.NotNull(view.Content);
    }
```

`tests/WorkStation.Tests/MainWindowViewModelTests.cs` 里追加：

```csharp
    /// <summary>报告页是独立页面：快捷键 6 / Ctrl+6 都能到。</summary>
    [AvaloniaFact]
    public void NavigateToReports_SwitchesPage()
    {
        var vm = new MainWindowViewModel(_service);

        vm.ShowReportsCommand.Execute(null);

        Assert.Equal(AppPage.Reports, vm.Page);
        Assert.IsType<ReportsViewModel>(vm.CurrentPage);
        Assert.Equal("报告", vm.PageTitle);
        Assert.True(vm.IsReportsActive);
    }
```

新建 `tests/WorkStation.Tests/AppIconsTests.cs`：

```csharp
using Avalonia.Media;
using WorkStation.App.Controls;

namespace WorkStation.Tests;

/// <summary>
/// 图标几何表：路径写坏了会在解析时抛异常 —— 那会让整个应用在启动时崩掉
/// （AppIcons 是静态类，静态构造失败 = TypeInitializationException）。
/// 这里逐个碰一遍，把错误挡在测试里。
/// </summary>
public class AppIconsTests
{
    [Fact]
    public void AllIconGeometries_ParseWithoutThrowing()
    {
        var icons = new StreamGeometry[]
        {
            AppIcons.Edit, AppIcons.Close, AppIcons.ArrowRight, AppIcons.Plus,
            AppIcons.Calendar, AppIcons.Check, AppIcons.Trash, AppIcons.Archive,
            AppIcons.Unarchive, AppIcons.Clock, AppIcons.Bell, AppIcons.Board,
            AppIcons.Tasks, AppIcons.Projects, AppIcons.Dashboard, AppIcons.Alert,
            AppIcons.Trend, AppIcons.SettingsLine, AppIcons.MoonLine, AppIcons.SunLine,
            AppIcons.Reports, AppIcons.ArrowLeft
        };

        foreach (var icon in icons)
        {
            Assert.NotNull(icon);
            Assert.False(icon!.Bounds.IsEmpty);
        }
    }
}
```

- [x] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/WorkStation.Tests/WorkStation.Tests.csproj --filter "FullyQualifiedName~ReportsView|FullyQualifiedName~NavigateToReports|FullyQualifiedName~AppIconsTests"`
Expected: FAIL —— `ReportsView` / `ShowReportsCommand` / `AppIcons.Reports` 不存在

- [x] **Step 3: 加图标**

`src/WorkStation.App/Controls/AppIcons.cs` 追加两个几何（Material Design Icons，与既有图标同源同 24×24 网格）：

```csharp
    /// <summary>
    /// 报告页：一张带折角与正文线的文档（MDI file-document-outline）。
    ///
    /// 用 MDI 而不是自绘：既有图标全是同一套几何，混进一个自造形状，
    /// 线条粗细与比例立刻就对不上了。
    /// </summary>
    public static readonly StreamGeometry Reports = Parse(
        "M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M18,20H6V4H13V9H18V20M9,13V15H15V13H9M9,17V19H13V17H9");

    /// <summary>上一期：左箭头（与既有的 ArrowRight 成对）。</summary>
    public static readonly StreamGeometry ArrowLeft = Parse(
        "M20,11V13H8L13.5,18.5L12.08,19.92L4.16,12L12.08,4.08L13.5,5.5L8,11H20Z");
```

> 若 `AppIconsTests` 报 `Bounds.IsEmpty` 或解析异常，说明这段路径数据在
> Avalonia 的 `StreamGeometry.Parse` 下不成立 —— 换一个可用的 MDI 路径
> （或改用 `Icons.Calendar`）再跑测试。**不要**把测试改成「不报错就算过」。

- [x] **Step 4: 写 `ReportsView.axaml`**

创建 `src/WorkStation.App/Views/ReportsView.axaml`：

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:WorkStation.App.ViewModels"
             xmlns:core="using:WorkStation.Core.Reports"
             xmlns:controls="using:WorkStation.App.Controls"
             xmlns:suki="https://github.com/kikipoulet/SukiUI"
             x:Class="WorkStation.App.Views.ReportsView"
             x:DataType="vm:ReportsViewModel"
             x:CompileBindings="True">

  <UserControl.Styles>
    <!-- 类型切换按钮：选中的托亮一档，和仪表盘的周期卡同一套做法 -->
    <Style Selector="Border.kind">
      <Setter Property="Background" Value="{DynamicResource SurfaceGlassSunken}" />
      <Setter Property="BorderBrush" Value="Transparent" />
      <Setter Property="BorderThickness" Value="1" />
    </Style>
    <Style Selector="Border.kind.selected">
      <Setter Property="Background" Value="{DynamicResource SurfaceGlassRaised}" />
      <Setter Property="BorderBrush" Value="{DynamicResource SukiPrimaryColor50}" />
    </Style>

    <!-- 报告正文：标题、小节标题与普通行各走各的样式。
         全按正文画，报告就是一堵等大的字，看不出哪句是标题。 -->
    <Style Selector="TextBlock.report">
      <Setter Property="FontSize" Value="{DynamicResource FontSizeBody}" />
      <Setter Property="Foreground" Value="{DynamicResource SukiText}" />
      <Setter Property="TextWrapping" Value="Wrap" />
      <Setter Property="Margin" Value="0,2" />
    </Style>
    <Style Selector="TextBlock.report.heading">
      <Setter Property="FontSize" Value="{DynamicResource FontSizeSubhead}" />
      <Setter Property="FontWeight" Value="SemiBold" />
      <Setter Property="Margin" Value="0,8,0,2" />
    </Style>
    <Style Selector="TextBlock.report.title">
      <Setter Property="FontSize" Value="{DynamicResource FontSizeHeading}" />
      <Setter Property="FontWeight" Value="SemiBold" />
      <Setter Property="Margin" Value="0,4,0,6" />
    </Style>
  </UserControl.Styles>

  <Grid RowDefinitions="Auto,Auto,*">

    <!-- 工具栏：与任务页、项目页、提醒页同一套卡片式工具栏 -->
    <Border Grid.Row="0"
            Classes="card"
            Margin="{DynamicResource PageGutterBelowHeader}"
            Padding="10,8">
      <Grid ColumnDefinitions="Auto,*,Auto" ColumnSpacing="16">

        <ItemsControl ItemsSource="{Binding Kinds}" VerticalAlignment="Center">
          <ItemsControl.ItemsPanel>
            <ItemsPanelTemplate>
              <StackPanel Orientation="Horizontal" Spacing="6" />
            </ItemsPanelTemplate>
          </ItemsControl.ItemsPanel>
          <ItemsControl.ItemTemplate>
            <DataTemplate x:DataType="vm:ReportKindOption">
              <Border Classes="kind"
                      Classes.selected="{Binding IsSelected}"
                      Margin="0,0,6,0"
                      Padding="14,6"
                      CornerRadius="10">
                <Button Background="Transparent"
                        BorderThickness="0"
                        Padding="0"
                        Content="{Binding Label}"
                        Command="{Binding $parent[UserControl].((vm:ReportsViewModel)DataContext).SelectKindCommand}"
                        CommandParameter="{Binding Kind}" />
              </Border>
            </DataTemplate>
          </ItemsControl.ItemTemplate>
        </ItemsControl>

        <StackPanel Grid.Column="2" Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
          <Button Content="重新汇总"
                  Command="{Binding RegenerateCommand}" />
          <Button Content="复制 Markdown"
                  Command="{Binding CopyCommand}" />
          <Button Content="导出 .md"
                  Classes="Flat"
                  Command="{Binding ExportCommand}" />
        </StackPanel>
      </Grid>
    </Border>

    <!-- 周期切换：往前 / 往后翻一期，中间是区间文字 -->
    <Border Grid.Row="1"
            Classes="card"
            Margin="24,0,24,16"
            Padding="10,8">
      <Grid ColumnDefinitions="Auto,Auto,*,Auto,Auto" ColumnSpacing="10">
        <Button Classes="icon Basic"
                Command="{Binding PreviousPeriodCommand}"
                ToolTip.Tip="上一期">
          <PathIcon Data="{x:Static controls:AppIcons.ArrowLeft}" Width="18" Height="18" />
        </Button>

        <TextBlock Grid.Column="1"
                   Text="{Binding PeriodTitle}"
                   FontSize="{DynamicResource FontSizeSubhead}"
                   FontWeight="SemiBold"
                   VerticalAlignment="Center" />

        <Button Grid.Column="3"
                Classes="icon Basic"
                IsEnabled="{Binding CanGoNext}"
                Command="{Binding NextPeriodCommand}"
                ToolTip.Tip="下一期">
          <PathIcon Data="{x:Static controls:AppIcons.ArrowRight}" Width="18" Height="18" />
        </Button>

        <ComboBox Grid.Column="4"
                  Width="200"
                  VerticalAlignment="Center"
                  ItemsSource="{Binding History}"
                  SelectedItem="{Binding SelectedHistory, Mode=TwoWay}">
          <ComboBox.ItemTemplate>
            <DataTemplate x:DataType="vm:ReportHistoryItem">
              <TextBlock Text="{Binding Title}" />
            </DataTemplate>
          </ComboBox.ItemTemplate>
        </ComboBox>
      </Grid>
    </Border>

    <ScrollViewer Grid.Row="2" HorizontalScrollBarVisibility="Disabled">
      <StackPanel Margin="{DynamicResource PageGutterBottom}" Spacing="16">

        <suki:InfoBar IsVisible="{Binding HasError}" Message="{Binding Error}" Severity="Error" IsClosable="False" />

        <Grid ColumnDefinitions="*,*" ColumnSpacing="16">

          <!-- 左：自动汇总。机器算的，重新汇总会覆盖它 -->
          <Border Classes="card" Padding="20,16">
            <StackPanel Spacing="10">
              <TextBlock Text="自动汇总"
                         FontSize="{DynamicResource FontSizeHeading}"
                         FontWeight="SemiBold" />
              <TextBlock Text="按任务数据自动生成，随时可以重新汇总。"
                         FontSize="{DynamicResource FontSizeCaption}"
                         Foreground="{DynamicResource SukiMuteText}" />
              <ItemsControl ItemsSource="{Binding AutoLines}">
                <ItemsControl.ItemTemplate>
                  <DataTemplate x:DataType="vm:ReportLineItem">
                    <TextBlock Classes="report"
                               Classes.heading="{Binding IsHeading}"
                               Classes.title="{Binding IsTitle}"
                               Text="{Binding Text}" />
                  </DataTemplate>
                </ItemsControl.ItemTemplate>
              </ItemsControl>
            </StackPanel>
          </Border>

          <!-- 右：补充说明。手写的，重新汇总不会覆盖它 -->
          <Border Classes="card" Padding="20,16">
            <StackPanel Spacing="10">
              <TextBlock Text="补充说明"
                         FontSize="{DynamicResource FontSizeHeading}"
                         FontWeight="SemiBold" />
              <TextBlock Text="这里写的内容只归你管，重新汇总不会动它。"
                         FontSize="{DynamicResource FontSizeCaption}"
                         Foreground="{DynamicResource SukiMuteText}" />
              <TextBox Text="{Binding ManualText, Mode=TwoWay}"
                       AcceptsReturn="True"
                       TextWrapping="Wrap"
                       MinHeight="220" />
              <Button Content="保存补充说明"
                      HorizontalAlignment="Right"
                      Command="{Binding SaveCommand}" />
            </StackPanel>
          </Border>
        </Grid>

        <!-- 最终稿：复制与导出的就是它 -->
        <Border Classes="card" Padding="20,16">
          <StackPanel Spacing="10">
            <TextBlock Text="最终报告"
                       FontSize="{DynamicResource FontSizeHeading}"
                       FontWeight="SemiBold" />
            <ItemsControl ItemsSource="{Binding FinalLines}">
              <ItemsControl.ItemTemplate>
                <DataTemplate x:DataType="vm:ReportLineItem">
                  <TextBlock Classes="report"
                             Classes.heading="{Binding IsHeading}"
                             Classes.title="{Binding IsTitle}"
                             Text="{Binding Text}" />
                </DataTemplate>
              </ItemsControl.ItemTemplate>
            </ItemsControl>
          </StackPanel>
        </Border>

      </StackPanel>
    </ScrollViewer>
  </Grid>
</UserControl>
```

- [x] **Step 5: 写 `ReportsView.axaml.cs`**

创建 `src/WorkStation.App/Views/ReportsView.axaml.cs`：

```csharp
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using WorkStation.App.ViewModels;

namespace WorkStation.App.Views;

/// <summary>
/// 报告页。
///
/// 剪贴板与文件选择器是平台能力，ViewModel 不该直接碰：
/// 视图把两个委托注入给 VM，VM 只管「要复制什么、要导成什么名字」。
/// </summary>
public partial class ReportsView : UserControl
{
    public ReportsView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is ReportsViewModel vm)
        {
            vm.CopyToClipboard = CopyToClipboardAsync;
            vm.ExportToFile = ExportToFileAsync;
        }
    }

    private async Task<bool> CopyToClipboardAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return false;

        await clipboard.SetTextAsync(text);
        return true;
    }

    private async Task<bool> ExportToFileAsync((string FileName, string Content) request)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return false;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = request.FileName,
            DefaultExtension = "md",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Markdown 文档") { Patterns = new[] { "*.md" } }
            }
        });

        if (file is null) return false;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(request.Content);
        return true;
    }
}
```

- [x] **Step 6: 接外壳**

`src/WorkStation.App/ViewModels/MainWindowViewModel.cs`：

1. `AppPage` 枚举加 `Reports`（放在 `Settings` 之前）：

```csharp
public enum AppPage
{
    Board,
    Tasks,
    Projects,
    Reminders,
    Reports,
    Settings,
    Dashboard
}
```

2. 加属性（在 `Dashboard` 属性之后）：

```csharp
    /// <summary>报告页：日报 / 周报 / 月报。</summary>
    public ReportsViewModel? Reports { get; }
```

3. 构造函数里（`Dashboard = new DashboardViewModel(service, ShowToast);` 之后）加：

```csharp
        Reports = new ReportsViewModel(service, ShowToast);
```

4. 加 `IsReportsActive`（在 `IsDashboardActive` 之后）：

```csharp
    public bool IsReportsActive => Page == AppPage.Reports;
```

5. `Navigate(AppPage target)` 的 `OnPropertyChanged` 列表里加 `OnPropertyChanged(nameof(IsReportsActive));`
6. `Navigate` 的 `switch` 里加：

```csharp
            case AppPage.Reports:
                PageTitle = "报告";
                PageSubtitle = "日报、周报、月报，写完就能导出";
                CurrentPage = Reports;
                Reports?.Reload();
                break;
```

7. 加命令（在 `ShowDashboard` 之后）：

```csharp
    [RelayCommand]
    private void ShowReports() => Navigate(AppPage.Reports);
```

8. `RefreshActivePage()` 的 `switch` 里加：

```csharp
            case AppPage.Reports:
                Reports?.Reload();
                break;
```

9. `BuildPaletteActions()` 里加一条：

```csharp
        new("报告", "跳转", "报", ["report", "报告", "日报", "周报", "月报"], () => Navigate(AppPage.Reports)),
```

`src/WorkStation.App/Views/MainWindow.axaml`：

1. 侧边栏 `</suki:SukiSideMenu.Items>` 之前插入：

```xml
        <suki:SukiSideMenuItem Header="报告" IsContentMovable="False" Tag="Reports">
          <suki:SukiSideMenuItem.Icon>
            <PathIcon Data="{x:Static controls:AppIcons.Reports}" Width="20" Height="20" />
          </suki:SukiSideMenuItem.Icon>
        </suki:SukiSideMenuItem>
```

2. `DataTemplate` 区加：

```xml
    <DataTemplate DataType="vm:ReportsViewModel">
      <views:ReportsView />
    </DataTemplate>
```

3. 快捷键帮助里的 `1 – 6 / Ctrl + 1 – 6` 改成 `1 – 7 / Ctrl + 1 – 7`。

`src/WorkStation.App/Views/MainWindow.axaml.cs`：

1. `PageFromDigit` 改成：

```csharp
    /// <summary>
    /// 数字键与侧边栏顺序一致：仪表盘 → 看板 → 任务 → 项目 → 提醒 → 报告，
    /// 设置不在菜单里，所以排最后。
    /// </summary>
    private static AppPage? PageFromDigit(Key key) => key switch
    {
        Key.D1 or Key.NumPad1 => AppPage.Dashboard,
        Key.D2 or Key.NumPad2 => AppPage.Board,
        Key.D3 or Key.NumPad3 => AppPage.Tasks,
        Key.D4 or Key.NumPad4 => AppPage.Projects,
        Key.D5 or Key.NumPad5 => AppPage.Reminders,
        Key.D6 or Key.NumPad6 => AppPage.Reports,
        Key.D7 or Key.NumPad7 => AppPage.Settings,
        _ => null
    };
```

2. 按键 `switch` 里，把 `case Key.D6: case Key.NumPad6:` 那段改成报告页，并新增设置页：

```csharp
            case Key.D6:
            case Key.NumPad6:
                vm.ShowReportsCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.D7:
            case Key.NumPad7:
                vm.ShowSettingsCommand.Execute(null);
                e.Handled = true;
                break;
```

3. 上面那段注释「数字键与侧边栏菜单顺序一一对应：仪表盘 → 看板 → 任务 → 项目 → 提醒，设置在标题栏不在菜单里，所以排最后。」改成「数字键与侧边栏菜单顺序一一对应：仪表盘 → 看板 → 任务 → 项目 → 提醒 → 报告，设置在标题栏不在菜单里，所以排最后。」

- [x] **Step 7: 运行测试**

Run: `dotnet test tests/WorkStation.Tests/WorkStation.Tests.csproj --filter "FullyQualifiedName~ReportsView|FullyQualifiedName~NavigateToReports|FullyQualifiedName~AppIconsTests"`
Expected: PASS

- [x] **Step 8: 全量测试**

Run: `dotnet test`
Expected: 全部 PASS

- [x] **Step 9: 提交**

```bash
git add src/WorkStation.App/Views/ReportsView.axaml src/WorkStation.App/Views/ReportsView.axaml.cs src/WorkStation.App/Controls/AppIcons.cs src/WorkStation.App/ViewModels/MainWindowViewModel.cs src/WorkStation.App/Views/MainWindow.axaml src/WorkStation.App/Views/MainWindow.axaml.cs tests/WorkStation.Tests/UiSmokeTests.cs tests/WorkStation.Tests/MainWindowViewModelTests.cs tests/WorkStation.Tests/AppIconsTests.cs
git commit -m "feat(reports): 报告页与外壳接线（侧边栏第六项，快捷键 6/7）"
```

---

### Task 8: 提醒换装 —— 删站会，弹三类报告提醒，设置页改三组

**Files:**
- Modify: `src/WorkStation.App/Services/ReminderPump.cs`
- Modify: `src/WorkStation.App/ViewModels/MainWindowViewModel.cs`（通知跳转补全）
- Modify: `src/WorkStation.App/ViewModels/SettingsViewModel.cs`
- Modify: `src/WorkStation.App/Views/SettingsView.axaml`
- Create: `src/WorkStation.App/ViewModels/ReportReminderOptions.cs`
- Delete: `src/WorkStation.Core/Reminders/DailyReviewPlanner.cs`
- Modify: `src/WorkStation.Core/Models/ReminderItem.cs`（删 `DailyReviewSettings`）
- Modify: `src/WorkStation.Core/Services/WorkStationService.cs`（删站会方法）
- Test: `tests/WorkStation.Tests/ReminderPumpTests.cs`
- Test: `tests/WorkStation.Tests/ReminderPlannerTests.cs`（删站会用例）
- Test: `tests/WorkStation.Tests/MainWindowViewModelTests.cs`（改站会断言）
- Test: `tests/WorkStation.Tests/SettingsViewModelReportReminderTests.cs`（新建）

**Interfaces:**
- Consumes: `ReportReminderPlanner`、`WorkStationService.ReportRemindersGet/ReportReminderLastFired/ReportReminderMarkFired/ReportsStats`（Task 5）、`ReportsViewModel`（Task 6）、`AppPage.Reports`（Task 7）
- Produces:
  - `ReminderPump.OpenReport` —— `Action<ReportKind>?`，点通知跳到对应报告页
  - `ReminderPumpResult(IReadOnlyList<ReminderItem> Fired, IReadOnlyList<ReportKind> ReportRemindersFired)`，`Any` 随之调整
  - `sealed record ReportWeekdayOption(int Value, string Label)` / `sealed record ReportMonthDayOption(int Value, string Label)`
  - `SettingsViewModel` 新增：`ReportDailyEnabled` / `ReportDailyTime` / `ReportWeeklyEnabled` / `ReportWeeklyTime` / `ReportMonthlyEnabled` / `ReportMonthlyTime`、集合 `ReportTimeOptions` / `ReportWeekdayOptions` / `ReportMonthDayOptions`、选中项 `SelectedReportWeekday` / `SelectedReportMonthDay`

> **本任务把站会提醒整套换掉**：Core 的 `DailyReviewPlanner` / `DailyReviewSettings` /
> 服务层的五个 `DailyReview*` 方法 / App 的 `OpenDailyReview` / 设置页的站会两行，
> 连同引用它们的测试，一起删干净。**站会提醒只弹三个数字、不产出任何东西**，
> 而报告功能正好补上「到点提醒 + 产出正文」这件事。

- [x] **Step 1: 先改测试**

`tests/WorkStation.Tests/ReminderPumpTests.cs`：

1. 构造函数里关掉默认开启的站会提醒：

```csharp
        // 报告提醒默认开启，会让每个用例都多出一条通知，这里先关掉，需要它的用例自己打开
        _service.ReportRemindersSet(new ReportReminderSettings
        {
            Daily = new ReportReminderEntry { Enabled = false, Time = "09:00" },
            Weekly = new ReportReminderEntry { Enabled = false, Time = "17:00", DayOfWeek = 5 },
            Monthly = new ReportReminderEntry { Enabled = false, Time = "17:00", DayOfMonth = 31 }
        });
```

2. 删掉三条站会用例：`Tick_DailyReviewFiresOncePerDay`、
   `Tick_DailyReviewBeforeTimeOrDisabled_DoesNotFire`、`Tick_DailyReviewClickNavigatesToTodayBoard`。
3. 追加新用例：

```csharp
    [Fact]
    public void Tick_ReportReminderFiresOncePerDay()
    {
        _service.ReportRemindersSet(new ReportReminderSettings
        {
            Daily = new ReportReminderEntry { Enabled = true, Time = "09:00" },
            Weekly = new ReportReminderEntry { Enabled = false, Time = "17:00", DayOfWeek = 5 },
            Monthly = new ReportReminderEntry { Enabled = false, Time = "17:00", DayOfMonth = 31 }
        });

        var pump = PumpAt(At(5, 9, 5));
        var first = pump.Tick();

        Assert.Contains(ReportKind.Daily, first.ReportRemindersFired);
        Assert.Contains(_sink.Shown, n => n.Title == "该写今天的日报了");

        // 同一天再跑：已经记过今天，不再发
        Assert.DoesNotContain(ReportKind.Daily, pump.Tick().ReportRemindersFired);

        // 第二天到点：照常发
        Assert.Contains(ReportKind.Daily, PumpAt(At(6, 9, 1)).Tick().ReportRemindersFired);
    }

    [Fact]
    public void Tick_ReportReminderBeforeTimeOrDisabled_DoesNotFire()
    {
        _service.ReportRemindersSet(new ReportReminderSettings
        {
            Daily = new ReportReminderEntry { Enabled = true, Time = "09:00" },
            Weekly = new ReportReminderEntry { Enabled = false, Time = "17:00", DayOfWeek = 5 },
            Monthly = new ReportReminderEntry { Enabled = false, Time = "17:00", DayOfMonth = 31 }
        });

        Assert.DoesNotContain(ReportKind.Daily, PumpAt(At(5, 8, 59)).Tick().ReportRemindersFired);

        _service.ReportRemindersSet(new ReportReminderSettings
        {
            Daily = new ReportReminderEntry { Enabled = false, Time = "09:00" },
            Weekly = new ReportReminderEntry { Enabled = false, Time = "17:00", DayOfWeek = 5 },
            Monthly = new ReportReminderEntry { Enabled = false, Time = "17:00", DayOfMonth = 31 }
        });

        Assert.DoesNotContain(ReportKind.Daily, PumpAt(At(5, 10)).Tick().ReportRemindersFired);
    }

    [Fact]
    public void Tick_ReportReminderClickOpensThatReport()
    {
        _service.ReportRemindersSet(new ReportReminderSettings
        {
            Daily = new ReportReminderEntry { Enabled = true, Time = "09:00" },
            Weekly = new ReportReminderEntry { Enabled = false, Time = "17:00", DayOfWeek = 5 },
            Monthly = new ReportReminderEntry { Enabled = false, Time = "17:00", DayOfMonth = 31 }
        });

        ReportKind? opened = null;
        var pump = PumpAt(At(5, 9, 5));
        pump.OpenReport = kind => opened = kind;
        pump.Tick();

        _sink.Shown.First(n => n.Title == "该写今天的日报了").Click();

        Assert.Equal(ReportKind.Daily, opened);
    }

    [Fact]
    public void Tick_WeeklyReportReminderFiresOnConfiguredWeekday()
    {
        // 2026-11-06 是周五
        _service.ReportRemindersSet(new ReportReminderSettings
        {
            Daily = new ReportReminderEntry { Enabled = false, Time = "09:00" },
            Weekly = new ReportReminderEntry { Enabled = true, Time = "17:00", DayOfWeek = 5 },
            Monthly = new ReportReminderEntry { Enabled = false, Time = "17:00", DayOfMonth = 31 }
        });

        Assert.DoesNotContain(ReportKind.Weekly, PumpAt(At(5, 17, 0)).Tick().ReportRemindersFired);
        Assert.Contains(ReportKind.Weekly, PumpAt(At(6, 17, 0)).Tick().ReportRemindersFired);
        Assert.Contains(_sink.Shown, n => n.Title == "该写本周周报了");
    }
```

`tests/WorkStation.Tests/ReminderPlannerTests.cs`：删掉站会那三条用例
（`IsValidTime` 那条改成断言 `ReportReminderPlanner.IsValidTime`，
`ShouldFire` 那条整条删掉 —— 判定逻辑已由 `ReportReminderPlannerTests` 覆盖，
`ComputeCounts` 那条整条删掉），并删掉随之无用的 `using WorkStation.Core.Reminders;`。

`tests/WorkStation.Tests/MainWindowViewModelTests.cs`：把

```csharp
        _service.DailyReviewSet(new DailyReviewSettings { Enabled = false, Time = "09:00" });
```

换成

```csharp
        _service.ReportRemindersSet(new ReportReminderSettings
        {
            Daily = new ReportReminderEntry { Enabled = false, Time = "09:00" },
            Weekly = new ReportReminderEntry { Enabled = false, Time = "17:00", DayOfWeek = 5 },
            Monthly = new ReportReminderEntry { Enabled = false, Time = "17:00", DayOfMonth = 31 }
        });
```

并把断言 `settings.DailyReviewEnabled` / `settings.DailyReviewTime`
换成 `settings.ReportDailyEnabled` / `settings.ReportDailyTime`（值仍是 `true` / `"09:00"`）。
该文件需要 `using WorkStation.Core.Models;`（已有）。

- [x] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/WorkStation.Tests/WorkStation.Tests.csproj --filter "FullyQualifiedName~ReminderPumpTests"`
Expected: 编译失败 —— `ReminderPumpResult.ReportRemindersFired` 不存在

- [x] **Step 3: 改造 `ReminderPump`**

`src/WorkStation.App/Services/ReminderPump.cs`：

1. using 增加 `using WorkStation.Core.Reports;`
2. 把 `OpenDailyReview` 换成：

```csharp
    /// <summary>点报告提醒：跳到报告页的对应类型。</summary>
    public Action<ReportKind>? OpenReport { get; set; }
```

3. `Tick()` 改成：

```csharp
    public ReminderPumpResult Tick()
    {
        var now = _clock();
        var nowLocal = LocalTime.ToLocalDateTime(now);

        var fired = FireDueReminders(nowLocal);
        var reportFired = FireReportReminders(now);

        return new ReminderPumpResult(fired, reportFired);
    }
```

4. 把 `FireDailyReview` 整段替换成：

```csharp
    /// <summary>
    /// 三类报告提醒：日报 / 周报 / 月报。
    ///
    /// **先记去重键、再发通知** —— 与任务提醒同一个道理：
    /// 通知弹出瞬间崩了，重启后不该把这一周期再报一遍。
    /// </summary>
    private IReadOnlyList<ReportKind> FireReportReminders(DateTime now)
    {
        var fired = new List<ReportKind>();

        var settings = _service.ReportRemindersGet();
        if (!settings.Ok || settings.Data is not { } config) return fired;

        foreach (var kind in new[] { ReportKind.Daily, ReportKind.Weekly, ReportKind.Monthly })
        {
            var entry = kind switch
            {
                ReportKind.Daily => config.Daily,
                ReportKind.Weekly => config.Weekly,
                _ => config.Monthly
            };

            var lastFired = _service.ReportReminderLastFired(kind);
            if (!ReportReminderPlanner.ShouldFire(kind, entry, now, lastFired.Data)) continue;

            var key = ReportReminderPlanner.PeriodKey(kind, now);
            _service.ReportReminderMarkFired(kind, key);

            try
            {
                var stats = _service.ReportsStats(kind);
                var message = stats is { Ok: true, Data: { } data }
                    ? $"完成 {data.Completed} 项 · 逾期 {data.Overdue} 项"
                    : "该写报告了";

                Sink?.Show(TitleOf(kind), message, () => OpenReport?.Invoke(kind));
                fired.Add(kind);
            }
            catch (Exception)
            {
                // 报告提醒没有历史记录，发送失败不记状态，下一周期照常
            }
        }

        return fired;
    }

    private static string TitleOf(ReportKind kind) => kind switch
    {
        ReportKind.Daily => "该写今天的日报了",
        ReportKind.Weekly => "该写本周周报了",
        _ => "该写本月月报了"
    };
```

5. `ReminderPumpResult` 改成：

```csharp
/// <summary>一次节拍的结果：弹了哪些提醒、哪些报告提醒触发了。测试与徽标刷新都靠它。</summary>
public sealed record ReminderPumpResult(
    IReadOnlyList<ReminderItem> Fired,
    IReadOnlyList<ReportKind> ReportRemindersFired)
{
    public bool Any => Fired.Count > 0 || ReportRemindersFired.Count > 0;
}
```

- [x] **Step 4: 改 `MainWindowViewModel` 的接线**

`src/WorkStation.App/ViewModels/MainWindowViewModel.cs`：

1. `StartReminderPump` 里把 `_reminderPump.OpenDailyReview = OpenTodayBoard;` 换成
   `_reminderPump.OpenReport = OpenReportFromNotification;`
2. 把 `OpenTodayBoard` 方法换成：

```csharp
    /// <summary>点报告提醒通知：跳到报告页并切到对应的报告类型。</summary>
    private void OpenReportFromNotification(ReportKind kind)
    {
        Navigate(AppPage.Reports);
        Reports?.SelectKindCommand.Execute(kind);
    }
```

3. `PumpReminders()` 里的 `new ReminderPumpResult([], false)` 改成 `new ReminderPumpResult([], [])`。

- [x] **Step 5: 删掉 Core 的站会提醒**

`src/WorkStation.Core/Services/WorkStationService.cs`：删掉这五个方法
（`DailyReviewGet` / `DailyReviewSet` / `DailyReviewLastFired` / `DailyReviewMarkFired` / `DailyReviewCounts`）。

`src/WorkStation.Core/Models/ReminderItem.cs`：删掉 `DailyReviewSettings` 记录
（连同它的文档注释）。

删除 `src/WorkStation.Core/Reminders/DailyReviewPlanner.cs` 整个文件。

- [x] **Step 6: 改设置页的 ViewModel**

创建 `src/WorkStation.App/ViewModels/ReportReminderOptions.cs`：

```csharp
namespace WorkStation.App.ViewModels;

/// <summary>
/// 周报提醒的星期选项。Value 是 ISO 星期几：1=周一 … 7=周日。
///
/// 做成 class 而不是 record：下拉框按引用记选中项，值相等的两个实例
/// 在它眼里等于「没换过」，选中态就不会重新渲染。
/// </summary>
public sealed class ReportWeekdayOption
{
    public ReportWeekdayOption(int value, string label)
    {
        Value = value;
        Label = label;
    }

    public int Value { get; }

    public string Label { get; }

    public override string ToString() => Label;
}

/// <summary>月报提醒的几号选项。</summary>
public sealed class ReportMonthDayOption
{
    public ReportMonthDayOption(int value, string label)
    {
        Value = value;
        Label = label;
    }

    public int Value { get; }

    public string Label { get; }

    public override string ToString() => Label;
}
```

`src/WorkStation.App/ViewModels/SettingsViewModel.cs`：

1. using 增加 `using WorkStation.Core.Reports;`
2. 把站会字段与属性整段替换成：

```csharp
    // ---------- 报告提醒 ----------

    [ObservableProperty]
    private bool _reportDailyEnabled = true;

    [ObservableProperty]
    private string _reportDailyTime = "09:00";

    [ObservableProperty]
    private bool _reportWeeklyEnabled = true;

    [ObservableProperty]
    private string _reportWeeklyTime = "17:00";

    [ObservableProperty]
    private bool _reportMonthlyEnabled = true;

    [ObservableProperty]
    private string _reportMonthlyTime = "17:00";

    /// <summary>提醒时间：整点与半点最常用，避免手输 9:00 / 09:0 这类格式差异。</summary>
    public ObservableCollection<string> ReportTimeOptions { get; } = [];

    public ObservableCollection<ReportWeekdayOption> ReportWeekdayOptions { get; } = [];

    public ObservableCollection<ReportMonthDayOption> ReportMonthDayOptions { get; } = [];

    /// <summary>周报选中的星期。换成选项对象而不是整数：下拉框按引用记选中项。</summary>
    [ObservableProperty]
    private ReportWeekdayOption? _selectedReportWeekday;

    /// <summary>月报选中的几号。</summary>
    [ObservableProperty]
    private ReportMonthDayOption? _selectedReportMonthDay;

    partial void OnReportDailyEnabledChanged(bool value) => SaveReportReminders();

    partial void OnReportDailyTimeChanged(string value) => SaveReportReminders();

    partial void OnReportWeeklyEnabledChanged(bool value) => SaveReportReminders();

    partial void OnReportWeeklyTimeChanged(string value) => SaveReportReminders();

    partial void OnReportMonthlyEnabledChanged(bool value) => SaveReportReminders();

    partial void OnReportMonthlyTimeChanged(string value) => SaveReportReminders();

    partial void OnSelectedReportWeekdayChanged(ReportWeekdayOption? value) => SaveReportReminders();

    partial void OnSelectedReportMonthDayChanged(ReportMonthDayOption? value) => SaveReportReminders();
```

3. 构造函数里（`var closeToTray = service.CloseToTrayGet();` 之前）加：

```csharp
        BuildReportOptions();
        ApplyReportReminders(service.ReportRemindersGet());
```

4. `Reload()` 里（`var closeToTray = service.CloseToTrayGet();` 之前）加：

```csharp
        ApplyReportReminders(service.ReportRemindersGet());
```

5. 加上选项构造、读取与保存方法：

```csharp
    /// <summary>提醒的可选项：时间、星期、几号。</summary>
    private void BuildReportOptions()
    {
        ReportTimeOptions.Clear();
        for (var hour = 6; hour <= 22; hour++)
        {
            ReportTimeOptions.Add($"{hour:00}:00");
            ReportTimeOptions.Add($"{hour:00}:30");
        }

        ReportWeekdayOptions.Clear();
        var weekdayLabels = new[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
        for (var i = 0; i < weekdayLabels.Length; i++)
        {
            ReportWeekdayOptions.Add(new ReportWeekdayOption(i + 1, weekdayLabels[i]));
        }

        ReportMonthDayOptions.Clear();
        for (var day = 1; day <= 31; day++)
        {
            ReportMonthDayOptions.Add(new ReportMonthDayOption(day, $"{day} 号"));
        }
    }

    /// <summary>把库里存的设置摊到界面属性上。</summary>
    private void ApplyReportReminders(Result<ReportReminderSettings> result)
    {
        if (result is not { Ok: true, Data: { } config }) return;

        ReportDailyEnabled = config.Daily.Enabled;
        ReportDailyTime = config.Daily.Time;
        ReportWeeklyEnabled = config.Weekly.Enabled;
        ReportWeeklyTime = config.Weekly.Time;
        ReportMonthlyEnabled = config.Monthly.Enabled;
        ReportMonthlyTime = config.Monthly.Time;

        SelectedReportWeekday = ReportWeekdayOptions.FirstOrDefault(o => o.Value == config.Weekly.DayOfWeek);
        SelectedReportMonthDay = ReportMonthDayOptions.FirstOrDefault(o => o.Value == config.Monthly.DayOfMonth);
    }

    /// <summary>保存三类报告提醒。存失败只提示，不打断设置页其它操作。</summary>
    private void SaveReportReminders()
    {
        if (!_initialized) return;

        var result = _service.ReportRemindersSet(new ReportReminderSettings
        {
            Daily = new ReportReminderEntry { Enabled = ReportDailyEnabled, Time = ReportDailyTime },
            Weekly = new ReportReminderEntry
            {
                Enabled = ReportWeeklyEnabled,
                Time = ReportWeeklyTime,
                DayOfWeek = SelectedReportWeekday?.Value ?? 5
            },
            Monthly = new ReportReminderEntry
            {
                Enabled = ReportMonthlyEnabled,
                Time = ReportMonthlyTime,
                DayOfMonth = SelectedReportMonthDay?.Value ?? 31
            }
        });

        if (!result.Ok)
        {
            _toast(result.Error?.Message ?? "报告提醒保存失败", true);
        }
    }
```

6. **删除**站会残留：构造函数里的

```csharp
        var review = service.DailyReviewGet();
        _dailyReviewEnabled = review is { Ok: true, Data: { } config } && config.Enabled;
        _dailyReviewTime = review is { Ok: true, Data: { } conf } ? conf.Time : DailyReviewPlanner.DefaultTime;
```

以及 `_dailyReviewEnabled` / `_dailyReviewTime` 字段、`DailyReviewTimeOptions` 集合、
`BuildTimeOptions` 方法、`OnDailyReviewEnabledChanged` / `OnDailyReviewTimeChanged`、
`SaveDailyReview` 方法，全部删掉。

7. `Reload()` 里的

```csharp
        var review = service.DailyReviewGet();
        if (review is { Ok: true, Data: { } config })
        {
            DailyReviewEnabled = config.Enabled;
            if (!DailyReviewTimeOptions.Contains(config.Time)) BuildTimeOptions(config.Time);
            DailyReviewTime = config.Time;
        }
```

整段删掉。

> `SettingsViewModel` 需要 `using WorkStation.Core;` 才能用 `Result<T>` —— 确认该 using 已在。

- [x] **Step 7: 改设置页的 XAML**

`src/WorkStation.App/Views/SettingsView.axaml`：把「提醒」分组里的这两块

```xml
      <Grid ColumnDefinitions="*,Auto" ColumnSpacing="12">
        <StackPanel Spacing="2" VerticalAlignment="Center">
          <TextBlock Text="每日站会提醒" />
          <TextBlock Text="每天固定时间汇报昨日完成、逾期和今晚到期。" TextWrapping="Wrap" />
        </StackPanel>
        <ToggleSwitch Grid.Column="1"
                      IsChecked="{Binding DailyReviewEnabled, Mode=TwoWay}"
                      VerticalAlignment="Center" />
      </Grid>

      <Grid ColumnDefinitions="*,Auto" ColumnSpacing="12">
        <TextBlock Text="站会时间" VerticalAlignment="Center" />
        <ComboBox Grid.Column="1"
                  Width="112"
                  ItemsSource="{Binding DailyReviewTimeOptions}"
                  SelectedItem="{Binding DailyReviewTime, Mode=TwoWay}"
                  IsEnabled="{Binding DailyReviewEnabled}" />
      </Grid>
```

换成三块（日报 / 周报 / 月报）：

```xml
      <Grid ColumnDefinitions="*,Auto" ColumnSpacing="12">
        <StackPanel Spacing="2" VerticalAlignment="Center">
          <TextBlock Text="日报提醒" />
          <TextBlock Text="每天到点提醒你写当天的日报。" TextWrapping="Wrap" />
        </StackPanel>
        <ToggleSwitch Grid.Column="1"
                      IsChecked="{Binding ReportDailyEnabled, Mode=TwoWay}"
                      VerticalAlignment="Center" />
      </Grid>

      <Grid ColumnDefinitions="*,Auto" ColumnSpacing="12">
        <TextBlock Text="日报时间" VerticalAlignment="Center" />
        <ComboBox Grid.Column="1"
                  Width="112"
                  ItemsSource="{Binding ReportTimeOptions}"
                  SelectedItem="{Binding ReportDailyTime, Mode=TwoWay}"
                  IsEnabled="{Binding ReportDailyEnabled}" />
      </Grid>

      <Grid ColumnDefinitions="*,Auto" ColumnSpacing="12">
        <StackPanel Spacing="2" VerticalAlignment="Center">
          <TextBlock Text="周报提醒" />
          <TextBlock Text="每周固定那天到点提醒你写周报。" TextWrapping="Wrap" />
        </StackPanel>
        <ToggleSwitch Grid.Column="1"
                      IsChecked="{Binding ReportWeeklyEnabled, Mode=TwoWay}"
                      VerticalAlignment="Center" />
      </Grid>

      <Grid ColumnDefinitions="*,Auto" ColumnSpacing="12">
        <TextBlock Text="周报时间" VerticalAlignment="Center" />
        <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="8">
          <ComboBox Width="96"
                    ItemsSource="{Binding ReportWeekdayOptions}"
                    SelectedItem="{Binding SelectedReportWeekday, Mode=TwoWay}"
                    IsEnabled="{Binding ReportWeeklyEnabled}">
            <ComboBox.ItemTemplate>
              <DataTemplate x:DataType="vm:ReportWeekdayOption">
                <TextBlock Text="{Binding Label}" />
              </DataTemplate>
            </ComboBox.ItemTemplate>
          </ComboBox>
          <ComboBox Width="112"
                    ItemsSource="{Binding ReportTimeOptions}"
                    SelectedItem="{Binding ReportWeeklyTime, Mode=TwoWay}"
                    IsEnabled="{Binding ReportWeeklyEnabled}" />
        </StackPanel>
      </Grid>

      <Grid ColumnDefinitions="*,Auto" ColumnSpacing="12">
        <StackPanel Spacing="2" VerticalAlignment="Center">
          <TextBlock Text="月报提醒" />
          <TextBlock Text="每月固定几号到点提醒你写月报。没有该号的月份按最后一天算。" TextWrapping="Wrap" />
        </StackPanel>
        <ToggleSwitch Grid.Column="1"
                      IsChecked="{Binding ReportMonthlyEnabled, Mode=TwoWay}"
                      VerticalAlignment="Center" />
      </Grid>

      <Grid ColumnDefinitions="*,Auto" ColumnSpacing="12">
        <TextBlock Text="月报时间" VerticalAlignment="Center" />
        <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="8">
          <ComboBox Width="96"
                    ItemsSource="{Binding ReportMonthDayOptions}"
                    SelectedItem="{Binding SelectedReportMonthDay, Mode=TwoWay}"
                    IsEnabled="{Binding ReportMonthlyEnabled}">
            <ComboBox.ItemTemplate>
              <DataTemplate x:DataType="vm:ReportMonthDayOption">
                <TextBlock Text="{Binding Label}" />
              </DataTemplate>
            </ComboBox.ItemTemplate>
          </ComboBox>
          <ComboBox Width="112"
                    ItemsSource="{Binding ReportTimeOptions}"
                    SelectedItem="{Binding ReportMonthlyTime, Mode=TwoWay}"
                    IsEnabled="{Binding ReportMonthlyEnabled}" />
        </StackPanel>
      </Grid>
```

- [x] **Step 8: 写设置页的测试**

创建 `tests/WorkStation.Tests/SettingsViewModelReportReminderTests.cs`：

```csharp
using Microsoft.Data.Sqlite;
using WorkStation.App.ViewModels;
using WorkStation.Core.Models;
using WorkStation.Core.Services;

namespace WorkStation.Tests;

/// <summary>设置页的三类报告提醒：改完要能存回库里。</summary>
public class SettingsViewModelReportReminderTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WorkStationService _service;

    public SettingsViewModelReportReminderTests()
    {
        _connection = TestDb.OpenMigrated();
        _service = new WorkStationService(_connection, ":memory:");
    }

    public void Dispose() => _connection.Dispose();

    private SettingsViewModel NewViewModel() =>
        new(_service, _ => { }, _ => { }, (_, _) => { });

    [Fact]
    public void Load_ShowsStoredReminderSettings()
    {
        _service.ReportRemindersSet(new ReportReminderSettings
        {
            Daily = new ReportReminderEntry { Enabled = false, Time = "08:30" },
            Weekly = new ReportReminderEntry { Enabled = true, Time = "18:00", DayOfWeek = 3 },
            Monthly = new ReportReminderEntry { Enabled = false, Time = "10:00", DayOfMonth = 5 }
        });

        var settings = NewViewModel();
        settings.Reload();

        Assert.False(settings.ReportDailyEnabled);
        Assert.Equal("08:30", settings.ReportDailyTime);
        Assert.Equal("18:00", settings.ReportWeeklyTime);
        Assert.Equal(3, settings.SelectedReportWeekday?.Value);
        Assert.Equal(5, settings.SelectedReportMonthDay?.Value);
    }

    [Fact]
    public void Change_StoresReminderSettings()
    {
        var settings = NewViewModel();
        settings.Reload();

        settings.ReportDailyEnabled = false;
        settings.ReportDailyTime = "07:15";
        settings.SelectedReportWeekday = settings.ReportWeekdayOptions.First(o => o.Value == 2);
        settings.SelectedReportMonthDay = settings.ReportMonthDayOptions.First(o => o.Value == 20);

        var saved = _service.ReportRemindersGet().Data!;
        Assert.False(saved.Daily.Enabled);
        Assert.Equal("07:15", saved.Daily.Time);
        Assert.Equal(2, saved.Weekly.DayOfWeek);
        Assert.Equal(20, saved.Monthly.DayOfMonth);
    }

    [Fact]
    public void Options_CoverEveryWeekdayAndDayOfMonth()
    {
        var settings = NewViewModel();

        Assert.Equal(7, settings.ReportWeekdayOptions.Count);
        Assert.Equal(31, settings.ReportMonthDayOptions.Count);
        Assert.Contains(settings.ReportTimeOptions, t => t == "09:00");
    }
}
```

- [x] **Step 9: 运行测试确认通过**

Run: `dotnet test tests/WorkStation.Tests/WorkStation.Tests.csproj --filter "FullyQualifiedName~ReminderPumpTests|FullyQualifiedName~SettingsViewModelReportReminderTests"`
Expected: PASS

- [x] **Step 10: 全量测试**

Run: `dotnet test`
Expected: 全部 PASS

- [x] **Step 11: 提交**

```bash
git add src/WorkStation.App/Services/ReminderPump.cs src/WorkStation.App/ViewModels/MainWindowViewModel.cs src/WorkStation.App/ViewModels/SettingsViewModel.cs src/WorkStation.App/ViewModels/ReportReminderOptions.cs src/WorkStation.App/Views/SettingsView.axaml src/WorkStation.Core/Reminders/DailyReviewPlanner.cs src/WorkStation.Core/Models/ReminderItem.cs src/WorkStation.Core/Services/WorkStationService.cs tests/WorkStation.Tests/ReminderPumpTests.cs tests/WorkStation.Tests/ReminderPlannerTests.cs tests/WorkStation.Tests/MainWindowViewModelTests.cs tests/WorkStation.Tests/SettingsViewModelReportReminderTests.cs
git commit -m "feat(reports): 提醒换成日报/周报/月报三类，移除站会提醒"
```

---

### Task 9: 文档同步与截图复验

**Files:**
- Modify: `docs/功能说明.md`
- Modify: `docs/设计系统.md`
- Modify: `README.md`
- Modify: `docs/superpowers/specs/2026-11-08-report-feature-design.md`（把「与设计文档的偏差」那张表回写进去）
- Modify: `tests/WorkStation.Tests/VisualCheck.cs`（加报告页截图）

**Interfaces:**
- Consumes: 全部前序任务
- Produces: 与实现一致的文档 + `.shots/` 里的报告页浅深两套截图

- [x] **Step 1: 同步 `docs/功能说明.md`**

按 AGENTS.md §6 的同步表，逐项改：

1. **「1. 页面与模块」**：在「提醒」这一节之后、「命令面板」之前插入新的一节：

```markdown
**报告**
- 日报 / 周报 / 月报三个周期，页内切换；每个周期自动生成一条，可回看旧周期。
- 左栏是**自动汇总**（按任务数据算出来的完成、未完成、逾期与统计数字），
  右栏是**补充说明**（自己写的）。
- 「重新汇总」只重算自动汇总，**补充说明一律保留** —— 周末补完的活不会把手写总结冲掉。
- 最终稿 = 自动汇总 + 补充说明，可**复制 Markdown** 或**导出 .md 文件**。
```

2. **「提醒」这一节**：把「三类：任务截止前（由任务表单的「提前提醒」自动排期）、每日站会（固定时间，一天一条）、手动新建。」改成
「两类：任务截止前（由任务表单的「提前提醒」自动排期）、手动新建。
另有三类**报告提醒**（日报 / 周报 / 月报），到点提示该写报告了。」

3. **「2. 快捷访问」**里「通知：……点站会通知切到今日看板。」改成
「通知：……点报告提醒通知切到报告页的对应周期。」

4. **「4. 导航与快捷键」**的表格：`1` – `6` 改成 `1` – `7`，`Ctrl+1` – `Ctrl+6` 改成 `Ctrl+1` – `Ctrl+7`。

5. **「6. 已知限制」**：**删掉**这一行

```markdown
- **周报 / 月报与统计**：数据库已有 `reports` 表与模型，但还没有仓储、服务与界面。
```

6. 「设置」那一节的「站会时间」改成「日报 / 周报 / 月报提醒时间」。

- [x] **Step 2: 同步 `docs/设计系统.md`**

在现有的组件规则里补一条：

```markdown
### 报告页

- 工具栏沿用各页统一的「卡片式工具栏」（`Border Classes="card"` + `PageGutterBelowHeader`）。
- 类型切换（日报 / 周报 / 月报）与仪表盘的周期卡同一套做法：选中项托亮一档
  （更实的玻璃底 + 50% 主色描边），一眼看出下方内容属于谁。
- 自动汇总与补充说明**左右分栏**：左边是机器生成的，右边是手写的。
  两者视觉上必须能分开 —— 用户一眼要能看出「这块会被重新汇总覆盖，那块不会」。
- 周期前后翻用图标按钮（`AppIcons.ArrowLeft` / `ArrowRight`），不用文字箭头。
- 版式一律走 `App.axaml` 的令牌：`FontSizeHeading` / `FontSizeSubhead` / `FontSizeBody` /
  `FontSizeCaption`、`PageGutter` / `PageGutterBottom`。
```

- [x] **Step 3: 同步 `README.md`**

1. 「项目结构」里 `Board/ Dashboard/ Projects/ Reminders/` 改成
   `Board/ Dashboard/ Projects/ Reminders/ Reports/   看板口径、仪表盘统计、项目统计、提醒排期、报告汇总`，
   并在 `Data/Repositories/` 那行加上 `reports`：

```
│  ├─ Data/Repositories/       projects / tasks / tags / settings / reminders / reports
```

2. 「调度器」那一行改成：
   `| 调度器 | 单次节拍（先落状态后发通知）、重复顺延、通知失败落 Failed、三类报告提醒各一条 |`

3. 如果「界面预览」一节要加报告页截图：**不要动** —— README 的 9 段图片字面量与
   `tools/` 里的 `sync-readme-images.py` 的 `IMAGE_MAP` 是绑定的，
   新增图片要同步改两处并重跑截图，否则 AGENTS.md §7 的验收过不了。
   本次**不加** README 截图。

- [x] **Step 4: 回写设计文档的偏差**

`docs/superpowers/specs/2026-11-08-report-feature-design.md`：
把「唯一索引」那段改成复用 `idx_reports_period`（UNIQUE），
把 Markdown 统计行改成 `完成 N 项 · 遗留未完成 M 项（其中逾期 K 项）· 完成率 R%`，
并加一句「服务层另提供 `ReportsStats(kind)` 供通知正文取数」。
还要把 `ReportStats` 的 `CompletionRate` / `Total` 改成计算属性的说明补上（设计文档写的是 init 属性）。

- [x] **Step 5: 给 `VisualCheck` 加报告页截图**

`tests/WorkStation.Tests/VisualCheck.cs` 里，在仪表盘截图之后、任务弹框那段之前插入：

```csharp
        // 报告页：自动汇总 + 补充说明左右分栏，浅色与深色各留一张
        vm.ShowReportsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        window.UpdateLayout();
        Save(window, "s25-report-light.png");

        vm.ApplyThemeVariant("dark");
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        window.UpdateLayout();
        Save(window, "s26-report-dark.png");
        vm.ApplyThemeVariant("light");
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
```

> 这段必须排在**所有需要主窗口渲染的截图之前** —— 该文件末尾的注释说明
> 「`CaptureTemplate` 会临时换掉 window.Content，换回来之后主窗口在无头环境里就再也画不出东西」。

- [x] **Step 6: 全量测试**

Run: `dotnet test`
Expected: 全部 PASS

- [x] **Step 7: 肉眼复验截图**

Run: `dotnet test tests/WorkStation.Tests/WorkStation.Tests.csproj --filter "FullyQualifiedName~VisualCheck"`

然后打开 `.shots/`，逐张看 `s25-report-light.png` 与 `s26-report-dark.png`：

- 工具栏与周期切换器排版正常，没有硬编码的错位；
- 左右两栏都在框内，没有溢出；
- 深色下正文与卡片边框都看得清；
- 图标（报告页侧边栏图标、左右箭头）不是空白方块。

**看到不对就改，改完重跑截图** —— 这一步不能只看测试绿。

- [x] **Step 8: 提交**

```bash
git add docs/功能说明.md docs/设计系统.md README.md docs/superpowers/specs/2026-11-08-report-feature-design.md tests/WorkStation.Tests/VisualCheck.cs
git commit -m "docs: 报告功能同步到功能说明、设计系统与 README"
```

---

## 验收清单

全部做完后逐条核对：

- [x] `dotnet test` 全绿
- [x] 侧边栏第六项是「报告」，图标正常；`6` / `Ctrl+6` 到报告页，`7` / `Ctrl+7` 到设置页
- [x] 报告页能切日报 / 周报 / 月报，能前后翻周期，不能翻到未来
- [x] 人工补充写完保存 → 「重新汇总」→ 补充还在，自动汇总已更新
- [x] 「复制 Markdown」与「导出 .md」都出内容，导出的文件名形如 `周报-2026-11-02.md`
- [x] 报告提醒三类各自到点弹一次，同一周期不重复弹；点通知进报告页对应类型
- [x] 设置页三类提醒都能改、能存回来
- [x] 老库（`user_version = 2`）能升到 3，`reports` 里的旧数据不丢
- [x] `docs/功能说明.md`、`docs/设计系统.md`、`README.md` 与实现一致
- [x] `.shots/` 里报告页浅深两套截图人工看过
- [ ] 提交前确认分支；**推送后用 `git ls-remote --heads origin` 核对 SHA**
