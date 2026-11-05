# 工作台 Work Station · M1 骨架与数据层 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 搭起 Electron + React + TypeScript 的工程骨架，建立基于 `node:sqlite` 的完整数据层与仓储层，做出可用的项目列表页与任务列表页，并打通 Windows portable exe 打包链路。

**Architecture:** 主进程独占 SQLite（`node:sqlite`），渲染进程无任何文件系统权限，两者通过 preload 的 contextBridge + 类型化 IPC 通信。数据层用「仓储工厂 + 依赖注入」组织，仓储函数接收 `DatabaseSync` 实例，测试时注入 `:memory:` 库。渲染进程用 Zustand 管全局状态，本地库直读不使用缓存层。

**Tech Stack:** Electron 44.10 / electron-vite 5 / Vite 7.3 / React 19.3 / TypeScript 5.9 / node:sqlite（Node 24 内置） / Zustand 5 / Vitest 4 / electron-builder 26

**设计依据：** `docs/superpowers/specs/2026-11-05-work-station-design.md`

---

## 实施进度

> **维护说明：** 本节由控制器在每个任务评审通过后更新。逐轮的完整记录（每条发现、每轮修复、每条延后 Minor）在 SDD 账本 `.superpowers/sdd/2026-11-05-work-station-m1-foundation/progress.md`，该账本是 scratch，不入库。
>
> **最近更新：** 2026-11-05 · Task 6 完成（6/13）

**当前状态：M1 骨架与数据层进行中。13 个任务已完成 6 个，Task 6 标签仓储经 1 轮修复后复审通过（VERDICT: PASS）。Task 7–13 与 M1 收尾未开始。M2/M3/M4 刻意未提前拆解 —— 任务拆分依赖 M1 产出的真实代码结构，提前写必然与实现脱节。**

### 里程碑总览

| 里程碑 | 范围 | 状态 |
| --- | --- | --- |
| M1 | 骨架与数据层（13 任务） | 进行中 · 6/13 |
| M2 | 看板与拖拽 | 未拆解 |
| M3 | 提醒与报告 | 未拆解 |
| M4 | 打磨与发布 | 未拆解 |

### 任务进度

| # | 任务 | 状态 | 提交 | 评审 | 用例 |
| --- | --- | --- | --- | --- | --- |
| 1 | 工程骨架与构建打包链路 | ✅ 完成 | `0081ce7` | 通过 | — |
| 2 | 共享类型与工具函数 | ✅ 完成 | `9fe2c69` | 通过 | 7 |
| 3 | 数据库连接、迁移与完整性检查 | ✅ 完成 | `2aac476` `0412c8a` `f830e26` | 通过（2 轮修复） | 17 |
| 4 | 项目仓储与进度计算 | ✅ 完成 | `1660c77` `4b6d88c` | 通过（1 轮修复） | 18 + 6 |
| 5 | 任务仓储 | ✅ 完成（1 轮修复） | `8ab6a27` `c7a7043` | 通过（4 个 Important 全部修复，复审 PASS） | 19 |
| 6 | 标签仓储 | ✅ 完成（1 轮修复） | `4836699` `8f6eb9d` | 通过（1 个 Important 修复，复审 PASS） | 14 |
| 7 | 设置仓储 | ⬜ 未开始 | — | — | — |
| 8 | IPC 路由与 preload 桥接 | ⬜ 未开始 | — | — | — |
| 9 | 渲染进程基础（API 客户端 / 主题 / 布局） | ⬜ 未开始 | — | — | — |
| 10 | 通用 UI 组件集 | ⬜ 未开始 | — | — | — |
| 11 | 项目管理页 | ⬜ 未开始 | — | — | — |
| 12 | 任务列表页与快速添加 | ⬜ 未开始 | — | — | — |
| 13 | 打包配置、README、M1 手测清单 | ⬜ 未开始 | — | — | — |
| — | M1 收尾：全分支最终评审 + 修复波 | ⬜ 未开始 | — | — | — |

合计 **81** 条用例全绿（6 个测试文件），`npm run typecheck` 与 `npm run build` 通过。

### 已交付代码（Task 1–5）

```
electron/
├─ db.ts                     连接、迁移、完整性检查、事务
├─ migrations.ts             MIGRATIONS v1（7 表 + 6 索引 + CHECK 约束）
├─ lib/result.ts             ok / fail / toBool / nullable
├─ util/time.ts              nowIso / toLocalDate / toLocalDateTime / isLocalDateTime
├─ util/id.ts                newId
├─ repositories/projects.ts  项目仓储
├─ repositories/tasks.ts     任务仓储
├─ repositories/tags.ts      标签仓储
├─ main.ts                   窗口生命周期 + 损坏恢复 + 每日备份
└─ preload.ts                脚手架（Task 8 重写）
src/
├─ types/entities.ts         12 个导出类型
├─ lib/progress.ts           computeProgress（全项目唯一实现）
└─ App.tsx / main.tsx / index.html   渲染端脚手架
tests/                       time 7 · db 17 · projects 18 · progress 6 · tasks 19 · tags 14
```

### 实施中发现并修掉的真实缺陷

这些不是风格建议，是会让程序跑不对的问题，均已带测试固化：

1. **Windows 上损坏恢复句柄泄漏** → `renameSync` 抛 EBUSY → 启动时未处理的 rejection → **窗口根本不弹**。
2. **`db.location()` 在 `@types/node` 里是 `string | null`**，照抄过不了 typecheck。
3. **dev 模式数据库落到 `out/.data/`**（构建产物目录）而不是项目根。
4. **`runMigrations` 按数组序执行** → 乱序追加迁移会让 `user_version` 回退 → **第二次启动建库失败**。
5. **`withTransaction` / `runMigrations` 的 ROLLBACK 自身抛错会掩盖原始异常** → 磁盘满时用户看到 "cannot rollback" 而不是真实原因。
6. **`openDatabase` 在 PRAGMA 失败时泄漏文件句柄**，连 unlink 都是 EBUSY。
7. **`app.whenReady().then()` 没有 `.catch()`** → DB 失败则进程活着、无窗口、零提示。
8. **`src/lib/progress.ts` 用了 `@/types/entities` 别名**，主进程 program 无 `@` 别名 → TS2307。
9. **`due_at` 无格式校验** → 误传 ISO UTC 串会让当天 18:00 到期的任务被判逾期（评审用 node:sqlite 实证了边界翻转）→ 现已在写入前 `isLocalDateTime` 校验并抛错。

### 关键决策

产品层面的 13 条决策见设计文档；以下是实施阶段新增的 5 条：

| # | 决策 | 理由 |
| --- | --- | --- |
| 12 | 逾期判定全项目统一为墙钟口径 `due_at < toLocalDateTime(now)` | 原三处算法互相打架：同一任务会在列表标红却不计入项目徽标 |
| 13 | `TaskFilter` 增加 `tagIds?: string[]`（EXISTS 子查询） | M2 看板「按标签筛选」需要；标签仓储只有 task→tags 方向，替代不了 |
| 14 | 进度计算只保留一份于 `src/lib/progress.ts` | 主进程与渲染进程共用；逐字重复会被评审判为缺陷 |
| 15 | `dueAt` 写入前必须格式校验 | 静默错判比显式失败危险得多 |
| 16 | 不提前写 M2/M3/M4 计划 | 任务拆分依赖前一里程碑的真实代码结构 |

### 跨任务事实（已实测，勿重新踩）

- `node:sqlite` **默认开启外键约束**（Node 22.22.2 与 Electron 内置 Node 24.22.2 一致）→ 显式 `PRAGMA foreign_keys = ON` 冗余但无害。
- `node:sqlite` 的 `SUM(CASE...)` 在无匹配行时返回 `null` → 聚合查询里的 `?? 0` 是**承重分支**，不是防御性冗余。
- `node:sqlite` 的文本字典序在两种时间格式混用时**边界方向会翻转** → 所有时间比较必须两侧同格式。
- `isLocalDateTime` 只校验格式不校验日历有效性（`2026-99-99T99:99` 会通过）—— Task 2 冻结的契约，不改。
- 迁移版本号有「空洞」语义：先发 v3 再补 v2，`filter(version > current)` 会**静默丢弃 v2 且不报错**。补版本号不会补跑缺失的迁移。
- Electron 二进制直连 GitHub 仅 45–105 KB/s，需 `ELECTRON_MIRROR` 指向 npmmirror（已用 `checksums.json` 校验 sha256 与官方 Release 一致，未改仓库文件）。
- 本机 npm 10.9.7 有已知 arborist 崩溃 bug（npm/cli#9787），**仅在无锁文件时触发**；锁文件已生成，后续任务不再受影响。

### 验证方式

```powershell
cd "E:\Joont\AI Agent Workspace\work-station"
npm test                            # 81 用例全绿
npm run typecheck                   # tsc 双 program 无错
npm run build                       # electron-vite 三端构建
npm run build:win                   # electron-builder portable exe（Task 1 实测 104.7MB 可启动）
npx vitest run tests/tasks.test.ts  # 单文件聚焦
```

### 当前待办

1. **Task 7 设置仓储** —— 照抄 Task 4/5 的定稿仓储模板（`type XxxRow`、闭包不依赖 `this`、可空字段 update 语义、冻结时钟）。
2. **Task 8 IPC 与 preload** —— 收尾必须重跑 `npm run typecheck`：`src/lib/progress.ts` 会在这一步首次进入主进程 program，此前 `@` 别名问题不会触发。`TaskFilter.tagIds` 是 Task 5 新增的对外签名，IPC 层需按新签名接入；仓储抛的裸 `Error`（无错误码）需在此映射为用户可见错误。**另需落实 Task 6 评审的 4 条裁决**：handler 不开外层事务（`withTransaction` 不可重入）、迁移先于仓储构造、`setTaskTags` 未知 id 抛错而非静默忽略、渲染进程只能经 `window.api` 访问数据。
3. **Task 9–13** 渲染端与打包。
4. **M1 收尾**：全分支最终评审 + 修复波。

### 延后 Minor 清单

记账于 SDD 账本，暂不处理，留到 M1 收尾统一评估：

- **Task 1**：electron-builder 把 react/react-dom/zustand/@dnd-kit 打进 asar（exe 104.7MB）→ 可移入 devDependencies 瘦身；`electron/main.ts` 有模块作用域副作用 → Task 8 顺手收进 `main()`。
- **Task 2**：`isLocalDateTime` 不校验日历有效性（冻结契约）；`toLocalDate` 年份不补零到 4 位；`entities.ts` 的 channel 是内联字面量、缺 `ReminderChannel` 命名类型；`newId` / `nowIso` 无直接断言。
- **Task 3**：损坏快照 stamp 只到天、同日二次损坏会覆盖上次快照；`copyFileSync` 备份非原子；`reminders.kind='task_due'` 但 `task_id` 可空、缺跨列约束；`db.close()` 自身抛错被吞；回滚失败原因完全不可观测；`runMigrations`（包装+cause）与 `withTransaction`（原样重抛）错误契约不一致；`main.ts` 用 `String(error)` 丢弃堆栈；测试里 PRAGMA foreign_keys 断言恒真、给虚假安全感；测试的 `rmSync` 在 Windows 遇未关闭句柄会把单条失败升级为套件级失败。
- **Task 4**：`countTasks` 的类型断言漏 `| undefined`；`create` 硬编码 `sort_order=0` → 「把项目置顶」目前无法表达（留给 M2）；`projects:remove` 是级联硬删且无二次确认（Task 8 的 UI 需要知道这是破坏性操作）；`projects.dueDate` 无格式校验 —— 存的是本地日历日，当前无人把它与墙钟串比较暂无错判，但 M2 项目到期徽标落地前必须明确格式约定。
- **Task 5**：`includeDone:false` 只排除 done 不排除 canceled，与本仓储对 canceled 的处理不一致；`status` 与 `includeDone` 不可组合，后者被 `else if` 静默吞掉；`countOverdue` 入参无格式护栏（误传日期串会少算）；**读取侧仍无格式护栏** —— `dueBefore`/`dueAfter`/`countOverdue` 传日期串或 ISO 串仍会静默比错（写入侧同类风险已修掉，读取侧是另一半）；`row.c ?? 0` 是无效防御；`ALL_STATUSES.filter(...)` 计算两次；`countByStatus` 对未知 status 直接写键；测试里有恒真断言；`create` 不能指定 `sortOrder`，排序要两次写入；`update` 对不存在的任务 + 非法 `dueAt` 返回 null 而非抛错，两条错误契约不对称（Task 8 的 IPC 错误映射需注意）；`assertLocalDateTime` 抛裸 `Error` 无错误码，Task 8 需自行映射为用户可见错误；测试里 `clock.nowIsoCalls` 未在 `beforeEach` 重置，当前无断言依赖它。
- **Task 6**：`setTaskTags` 的失败路径无用例钉死也无契约文档 —— 未知 id 时抛原始 `ERR_SQLITE_ERROR: FOREIGN KEY constraint failed`（已实测，事务回滚后旧关联保留）；`listTagTaskIds` 无 `ORDER BY`，当前靠索引碰巧有序但不是契约；`create`/`update` 不校验空名与重名，校验应落在 Task 8 的 IPC 层；`listTaskTags`/`listTagTaskIds` 每次调用重新 prepare，与上提工厂闭包的风格不一致；`tags.remove()` 对不存在 id 返回 `false` 的路径无断言（房屋模式 `projects.test.ts` 同时覆盖 true/false）。

---

## Global Constraints

这些约束适用于本计划的每一个任务，任务描述里不再重复。

### 版本矩阵（已实测验证，勿随意升级）

```jsonc
"dependencies": {
  "react": "^19.3.1",
  "react-dom": "^19.3.1",
  "zustand": "^5.0.16",
  "@dnd-kit/core": "^6.3.1"
},
"devDependencies": {
  "electron": "^44.10.2",
  "electron-vite": "^5.0.0",
  "vite": "^7.3.7",
  "@vitejs/plugin-react": "^5.0.0",
  "typescript": "^5.9.3",
  "electron-builder": "^26.15.3",
  "vitest": "^4.0.0",
  "@types/react": "^19.3.0",
  "@types/react-dom": "^19.3.0",
  "@types/node": "^24.19.2"
}
```

升级这些版本前必须重跑本计划的自检步骤：`electron-vite@5` 要求 `vite ^5||^6||^7`；`vitest@4` 要求 `vite ^6||^7`；`@vitejs/plugin-react@5` 要求 `vite ^4||^5||^6||^7`。

### `node:sqlite` 硬约束（踩过就记住）

1. **不能绑定 boolean**。绑定 `true`/`false` 抛 `Provided value cannot be bound to SQLite parameter N`。一律转成 `1`/`0`。
2. **不能绑定 undefined**。一律转成 `null`。
3. **不能绑定 bigint**（除 `0n`/`1n`）。本项目不涉及。
4. `stmt.get()` 找不到行时返回 `undefined`，不是 `null`。
5. `stmt.run()` 返回 `{ lastInsertRowid, changes }`，`lastInsertRowid` 是 number 或 bigint，取 `Number()` 强制转换。
6. `db.exec()` 支持一次执行多条以分号分隔的语句。
7. 事务用 `db.exec('BEGIN')` / `db.exec('COMMIT')` / `db.exec('ROLLBACK')`，**不要用** `db.exec` 包裹 `BEGIN` 之外的隐式事务。

### 时间格式约定（三类，不可混用）

| 用途 | 格式 | 示例 | 生成函数 |
| --- | --- | --- | --- |
| 记录时间戳（created/updated/started/completed/fired） | ISO 8601 UTC | `2026-11-05T04:05:00.000Z` | `nowIso()` |
| 日期（start_date / due_date / period_start / period_end） | 本地日历日 | `2026-11-05` | `toLocalDate()` |
| 本地日期时间（due_at / remind_at） | 本地墙钟，无时区 | `2026-11-05T14:30` | `toLocalDateTime()` |

所有「到期」「截止」类比较都用**本地墙钟字符串**做字典序比较——因为 `YYYY-MM-DDTHH:mm` 格式定长且本地有序，字符串比较等价于时间比较。**禁止**用 `new Date('2026-11-05T14:30')` 解析（会被当成本地时区，且不带秒的字符串在不同引擎行为不一致）。

**逾期判定全项目统一为墙钟口径**：`due_at < 当前本地墙钟时刻` 即为逾期。当前时刻用 `toLocalDateTime(new Date())` 取。**不要**用 `toLocalDate(new Date())`（只到天 —— 会让「今天 09:00 到期、现在 10:00」的任务漏判），**更不要**用 `nowIso().slice(0, 10)`（UTC 日期，与本地墙钟差最多一天，跨时区会错）。项目卡片徽标、看板概览条、任务列表标红三处口径必须一致，否则同一个任务会在列表标红却不计入项目徽标。

### 仓储模板定稿（Task 4 评审后修正，Task 5/6/7 照此写）

1. **行映射用类型别名 `type XxxRow = { ... }`，不要用 `interface`。** `interface` 声明没有隐式索引签名，`db.prepare(...).all() as XxxRow[]` 会触发 TS2352。`electron/db.ts` 已有同样写法先例。
2. **仓储方法内部不要依赖 `this` 绑定。** 在工厂闭包里定义局部 `getById(id)`（用闭包里的 `selectById`），公开方法 `get(id)` 委托给它，其余方法一律调 `getById(id)`。这样方法被解构或当回调传也不会炸。**对外方法签名不许变** —— Task 8 的 IPC 层按此冻结。
3. **逾期判定统一墙钟口径**（见上）。
4. **测试断言与当前时间相关的场景时，冻结时钟**：`vi.mock('../electron/util/time')` 冻结 `toLocalDate` / `toLocalDateTime`，避免午夜边界 flake。冻结值要与真实实现自洽（`toLocalDateTime(d)` = `toLocalDate(d)` + `T` + 时分）。
5. **可空字段的 update 语义**：区分「没传」与「传了 null」用 `patch.x === undefined ? existing.x : patch.x`；非空字段用 `??`。合并后的记录类型用 `Required<UpdateInput>`，否则 `merged.x` 带 `undefined`，`node:sqlite` 的绑定参数不接受。
6. **`dueAt` 写入前必须校验格式**：用 `electron/util/time.ts` 的 `isLocalDateTime(value)` 检查。全项目的「到期/截止」判定都建立在「`YYYY-MM-DDTHH:mm` 定长且有序」的字符串契约上 —— 若有人误传 ISO UTC 串（`2026-11-05T06:30:00.000Z`），字典序比较会把当天 18:00 到期的任务判成逾期。**静默错判必须变成显式失败**。
7. **`TaskFilter` 必须能按标签筛选**：`tagIds?: string[]`，SQL 用 `EXISTS (SELECT 1 FROM task_tags tt WHERE tt.task_id = tasks.id AND tt.tag_id IN (...))` 实现。M2 看板的「按标签筛选」依赖它；标签仓储只有 task→tags 方向，不能替代。

### 命名约定

- 数据库列：`snake_case`
- TypeScript 实体与接口：`camelCase`
- 转换由仓储层显式 mapper 负责，不引入 ORM、不引入自动转换库
- 所有 ID：`crypto.randomUUID()`，无需第三方库

### 架构约束

- 渲染进程**不得**直接访问文件系统或数据库，一律经 `window.api` → IPC
- 主进程 IPC 处理器**永远不向外 throw**，统一包装成 `Result<T>`
- `package.json` **不要**加 `"type": "module"`（保持 CJS，`__dirname` 可用；electron-vite 主进程产物默认 CJS）
- 不引入 `zod`、`react-query`、`date-fns`、UI 组件库、`@electron-toolkit/*`
- `electron-vite` 的 `@swc/core` 是可选 peer，**不要**安装

### 提交约定

每个任务末尾执行 `git add` + `git commit`，提交信息格式 `<type>: <描述>`，type 取 `feat` / `fix` / `test` / `chore` / `refactor`。

### 命令约定

在 `E:\Joont\AI Agent Workspace\work-station` 目录下执行。PowerShell 环境，用 `&&` 串联。

---

## File Structure

本计划（M1）创建的文件及其职责：

```
work-station/
├─ package.json                    依赖与脚本
├─ tsconfig.json                   渲染进程 + 共享代码的 TS 配置
├─ tsconfig.node.json              主进程 / preload / 测试 的 TS 配置
├─ electron.vite.config.ts         三端构建配置
├─ electron-builder.yml            Windows portable 打包配置
├─ vitest.config.ts                测试配置
├─ .gitignore
├─ README.md
├─ build/.gitkeep                  electron-builder 资源目录
├─ electron/                       主进程
│  ├─ main.ts                      窗口生命周期
│  ├─ preload.ts                   contextBridge 暴露
│  ├─ ipc.ts                       IPC 路由集中注册
│  ├─ db.ts                        连接、迁移、完整性检查、事务
│  ├─ migrations.ts                版本化 SQL 迁移
│  ├─ lib/
│  │  └─ result.ts                 Result 包装与错误码
│  ├─ util/
│  │  ├─ id.ts                     UUID 生成
│  │  └─ time.ts                   三类时间格式化
│  └─ repositories/
│     ├─ projects.ts
│     ├─ tasks.ts
│     ├─ tags.ts
│     └─ settings.ts
├─ src/                            渲染进程
│  ├─ index.html
│  ├─ main.tsx
│  ├─ App.tsx                      布局骨架 + 路由
│  ├─ styles/global.css            主题变量与基础样式
│  ├─ types/
│  │  ├─ entities.ts               领域实体类型
│  │  └─ api.ts                    IPC 契约与 Window 类型
│  ├─ lib/
│  │  ├─ api.ts                    IPC 客户端封装
│  │  ├─ format.ts                 日期/工时格式化
│  │  └─ progress.ts               进度计算（纯函数，主进程与渲染进程共用）
│  ├─ hooks/useAsync.ts            异步状态 hook
│  ├─ store/ui.ts                  主题、当前页面、Toast
│  ├─ components/                  通用 UI
│  │  ├─ Drawer.tsx
│  │  ├─ Modal.tsx
│  │  ├─ Badge.tsx
│  │  ├─ ProgressRing.tsx
│  │  ├─ EmptyState.tsx
│  │  └─ Field.tsx
│  └─ features/
│     ├─ projects/ProjectPage.tsx
│     ├─ tasks/TaskPage.tsx
│     └─ settings/SettingsPage.tsx
└─ tests/
   ├─ db.test.ts
   ├─ progress.test.ts
   ├─ projects.test.ts
   ├─ tasks.test.ts
   ├─ tags.test.ts
   ├─ settings.test.ts
   └─ time.test.ts
```

---

### Task 1: 工程骨架与构建打包链路

**Files:**
- Create: `package.json`, `tsconfig.json`, `tsconfig.node.json`, `electron.vite.config.ts`, `electron-builder.yml`, `vitest.config.ts`, `.gitignore`, `build/.gitkeep`, `electron/main.ts`, `electron/preload.ts`, `src/index.html`, `src/main.tsx`, `src/App.tsx`, `src/types/env.d.ts`
- Modify: 无

**Interfaces:**
- Consumes: 无（起点任务）
- Produces:
  - npm scripts：`dev` / `build` / `build:win` / `test` / `typecheck`
  - `electron/main.ts` 导出的 `createMainWindow(): BrowserWindow`
  - `electron/preload.ts` 向 `window` 暴露 `window.api`（M1 先放占位对象，Task 8 补齐）
  - 构建产物路径：`out/main/index.js`、`out/preload/index.js`、`out/renderer/index.html`

- [x] **Step 1: 初始化 git 仓库**

```bash
cd "E:\Joont\AI Agent Workspace\work-station"
git init
git symbolic-ref HEAD refs/heads/main
```

- [x] **Step 2: 写 `package.json`**

```json
{
  "name": "work-station",
  "version": "0.1.0",
  "description": "工作台 Work Station · 个人项目管理与报告工作台",
  "private": true,
  "author": "joont",
  "main": "./out/main/index.js",
  "scripts": {
    "dev": "electron-vite dev",
    "build": "electron-vite build",
    "preview": "electron-vite preview",
    "build:win": "npm run build && electron-builder --win portable",
    "test": "vitest run",
    "test:watch": "vitest",
    "typecheck": "tsc --noEmit -p tsconfig.json && tsc --noEmit -p tsconfig.node.json"
  },
  "dependencies": {
    "@dnd-kit/core": "^6.3.1",
    "react": "^19.3.1",
    "react-dom": "^19.3.1",
    "zustand": "^5.0.16"
  },
  "devDependencies": {
    "@types/node": "^24.19.2",
    "@types/react": "^19.3.0",
    "@types/react-dom": "^19.3.0",
    "@vitejs/plugin-react": "^5.0.0",
    "electron": "^44.10.2",
    "electron-builder": "^26.15.3",
    "electron-vite": "^5.0.0",
    "typescript": "^5.9.3",
    "vite": "^7.3.7",
    "vitest": "^4.0.0"
  }
}
```

- [x] **Step 3: 写 TS 配置**

`tsconfig.json`（渲染进程 + 共享代码）：

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "ESNext",
    "moduleResolution": "bundler",
    "lib": ["ES2023", "DOM", "DOM.Iterable"],
    "jsx": "react-jsx",
    "strict": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "noFallthroughCasesInSwitch": true,
    "esModuleInterop": true,
    "resolveJsonModule": true,
    "isolatedModules": true,
    "skipLibCheck": true,
    "noEmit": true,
    "types": ["node"],
    "baseUrl": ".",
    "paths": {
      "@/*": ["src/*"]
    }
  },
  "include": ["src/**/*.ts", "src/**/*.tsx", "src/**/*.d.ts", "tests/**/*.ts", "electron/lib/**/*.ts", "electron/util/**/*.ts"]
}
```

`tsconfig.node.json`（主进程、preload、构建脚本）：

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "ESNext",
    "moduleResolution": "bundler",
    "lib": ["ES2023"],
    "strict": true,
    "esModuleInterop": true,
    "skipLibCheck": true,
    "noEmit": true,
    "types": ["node"]
  },
  "include": ["electron/**/*.ts", "electron.vite.config.ts", "vitest.config.ts"]
}
```

- [x] **Step 4: 写构建配置**

`electron.vite.config.ts`：

```ts
import { resolve } from 'node:path';
import { defineConfig, externalizeDepsPlugin } from 'electron-vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  main: {
    plugins: [externalizeDepsPlugin()],
    build: {
      rollupOptions: {
        input: { index: resolve(__dirname, 'electron/main.ts') }
      }
    }
  },
  preload: {
    plugins: [externalizeDepsPlugin()],
    build: {
      rollupOptions: {
        input: { index: resolve(__dirname, 'electron/preload.ts') }
      }
    }
  },
  renderer: {
    root: resolve(__dirname, 'src'),
    plugins: [react()],
    resolve: {
      alias: { '@': resolve(__dirname, 'src') }
    },
    build: {
      rollupOptions: {
        input: { index: resolve(__dirname, 'src/index.html') }
      }
    }
  }
});
```

`vitest.config.ts`：

```ts
import { defineConfig } from 'vitest/config';
import { resolve } from 'node:path';

export default defineConfig({
  resolve: {
    alias: { '@': resolve(__dirname, 'src') }
  },
  test: {
    environment: 'node',
    include: ['tests/**/*.test.ts']
  }
});
```

`electron-builder.yml`：

```yaml
appId: com.joont.workstation
productName: WorkStation
asar: true
directories:
  output: dist
  buildResources: build
files:
  - out/**
  - package.json
win:
  target:
    - target: portable
      arch:
        - x64
  artifactName: ${productName}-${version}-portable.${ext}
```

`.gitignore`：

```gitignore
node_modules/
out/
dist/
.data/
.superpowers/
*.log
build/icon.ico
build/icon.png
```

- [x] **Step 5: 写最小主进程与渲染进程**

`electron/main.ts`：

```ts
import { app, BrowserWindow } from 'electron';
import { join } from 'node:path';

export function createMainWindow(): BrowserWindow {
  const win = new BrowserWindow({
    width: 1280,
    height: 800,
    minWidth: 1024,
    minHeight: 640,
    show: false,
    title: '工作台 Work Station',
    backgroundColor: '#14161a',
    webPreferences: {
      preload: join(__dirname, '../preload/index.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: false
    }
  });

  win.once('ready-to-show', () => win.show());

  const rendererUrl = process.env['ELECTRON_RENDERER_URL'];
  if (rendererUrl) {
    void win.loadURL(rendererUrl);
  } else {
    void win.loadFile(join(__dirname, '../renderer/index.html'));
  }

  return win;
}

app.whenReady().then(() => {
  createMainWindow();

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createMainWindow();
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});
```

`electron/preload.ts`（M1 占位，Task 8 替换）：

```ts
import { contextBridge } from 'electron';

contextBridge.exposeInMainWorld('api', {
  ping: async (): Promise<string> => 'pong'
});
```

`src/index.html`：

```html
<!doctype html>
<html lang="zh-CN">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>工作台 Work Station</title>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/main.tsx"></script>
  </body>
</html>
```

`src/main.tsx`：

```tsx
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import App from './App';

const container = document.getElementById('root');
if (!container) throw new Error('找不到 #root 挂载点');

createRoot(container).render(
  <StrictMode>
    <App />
  </StrictMode>
);
```

`src/App.tsx`：

```tsx
export default function App(): React.ReactElement {
  return (
    <div style={{ padding: 32, color: '#e6e8eb', fontFamily: 'system-ui, sans-serif' }}>
      <h1>工作台 Work Station</h1>
      <p>M1 骨架已就绪。</p>
    </div>
  );
}
```

`src/types/env.d.ts`：

```ts
/// <reference types="vite/client" />

declare global {
  interface Window {
    api: {
      ping(): Promise<string>;
    };
  }
}

export {};
```

- [x] **Step 6: 安装依赖**

```bash
cd "E:\Joont\AI Agent Workspace\work-station"
npm install
```

Expected: 安装成功，无 peer dependency 警告；若出现 peer 冲突说明版本被改过，回查 Global Constraints。

- [x] **Step 7: 类型检查与构建验证**

```bash
npm run typecheck && npm run build
```

Expected: 两条命令都退出码 0；`out/main/index.js`、`out/preload/index.js`、`out/renderer/index.html` 三个产物存在。

- [x] **Step 8: 开发模式冒烟验证**

```bash
npm run dev
```

Expected: 弹出窗口显示「工作台 Work Station」和「M1 骨架已就绪。」，无控制台报错。手动关闭窗口结束该进程。

- [x] **Step 9: 打包验证**

```bash
npm run build:win
```

Expected: `dist/WorkStation-0.1.0-portable.exe` 生成。双击 exe 应能启动并显示同一页面（此时数据库尚未接入，页面内容不变）。

- [x] **Step 10: 提交**

```bash
git add -A
git commit -m "feat: 初始化 Electron + React + TypeScript 工程骨架与打包链路"
```

---

### Task 2: 共享类型与工具函数

**Files:**
- Create: `src/types/entities.ts`, `electron/util/id.ts`, `electron/util/time.ts`, `tests/time.test.ts`
- Modify: `src/types/env.d.ts`（暂时不动，Task 8 再改）

**Interfaces:**
- Consumes: 无
- Produces:
  - `src/types/entities.ts` 导出类型：`ProjectStatus`、`TaskStatus`、`Priority`、`ProgressMode`、`Project`、`Task`、`Tag`、`Reminder`、`Report`
  - `electron/util/id.ts` 导出 `newId(): string`
  - `electron/util/time.ts` 导出 `nowIso(): string`、`toLocalDate(d: Date): string`、`toLocalDateTime(d: Date): string`、`isLocalDateTime(value: string): boolean`

- [x] **Step 1: 写实体类型**

`src/types/entities.ts`：

```ts
export type ProjectStatus = 'planning' | 'active' | 'paused' | 'done';
export type TaskStatus = 'todo' | 'doing' | 'blocked' | 'done' | 'canceled';
export type Priority = 'high' | 'medium' | 'low' | 'none';
export type ProgressMode = 'auto' | 'manual';
export type ReminderKind = 'task_due' | 'custom';
export type ReminderStatus = 'pending' | 'fired' | 'dismissed' | 'failed' | 'done';
export type RepeatRule = 'none' | 'daily' | 'weekly';
export type ReportKind = 'weekly' | 'monthly';

export interface Project {
  id: string;
  name: string;
  description: string;
  status: ProjectStatus;
  priority: Priority;
  color: string | null;
  progressMode: ProgressMode;
  progressManual: number | null;
  startDate: string | null;
  dueDate: string | null;
  sortOrder: number;
  createdAt: string;
  updatedAt: string;
  archivedAt: string | null;
}

export interface Task {
  id: string;
  projectId: string | null;
  title: string;
  notes: string;
  status: TaskStatus;
  priority: Priority;
  dueAt: string | null;
  remindBefore: number;
  estimate: number | null;
  spent: number | null;
  blockedReason: string | null;
  sortOrder: number;
  createdAt: string;
  updatedAt: string;
  startedAt: string | null;
  completedAt: string | null;
}

export interface Tag {
  id: string;
  name: string;
  color: string | null;
}

export interface Reminder {
  id: string;
  kind: ReminderKind;
  taskId: string | null;
  title: string;
  remindAt: string;
  repeatRule: RepeatRule;
  channel: 'system' | 'inapp';
  status: ReminderStatus;
  firedAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface Report {
  id: string;
  kind: ReportKind;
  periodStart: string;
  periodEnd: string;
  autoMarkdown: string;
  manualText: string;
  finalMarkdown: string;
  createdAt: string;
  updatedAt: string;
}
```

- [x] **Step 2: 写 ID 与时间工具**

`electron/util/id.ts`：

```ts
import { randomUUID } from 'node:crypto';

export function newId(): string {
  return randomUUID();
}
```

`electron/util/time.ts`：

```ts
const pad = (n: number): string => String(n).padStart(2, '0');

export function nowIso(): string {
  return new Date().toISOString();
}

export function toLocalDate(d: Date): string {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

export function toLocalDateTime(d: Date): string {
  return `${toLocalDate(d)}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

export function isLocalDateTime(value: string): boolean {
  return /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(value);
}
```

- [x] **Step 3: 写时间工具的失败测试**

`tests/time.test.ts`：

```ts
import { describe, expect, it } from 'vitest';
import { isLocalDateTime, toLocalDate, toLocalDateTime } from '../electron/util/time';

describe('time 工具', () => {
  it('toLocalDate 输出 YYYY-MM-DD', () => {
    expect(toLocalDate(new Date(2026, 0, 5))).toBe('2026-01-05');
  });

  it('toLocalDateTime 输出 YYYY-MM-DDTHH:mm 且月份日期补零', () => {
    expect(toLocalDateTime(new Date(2026, 0, 5, 9, 7))).toBe('2026-01-05T09:07');
  });

  it('toLocalDateTime 输出下午时间不做 12 小时制转换', () => {
    expect(toLocalDateTime(new Date(2026, 10, 5, 14, 30))).toBe('2026-11-05T14:30');
  });

  it('toLocalDateTime 输出 23:59 边界', () => {
    expect(toLocalDateTime(new Date(2026, 10, 5, 23, 59))).toBe('2026-11-05T23:59');
  });

  it('isLocalDateTime 接受合法值', () => {
    expect(isLocalDateTime('2026-11-05T14:30')).toBe(true);
  });

  it('isLocalDateTime 拒绝带秒的值', () => {
    expect(isLocalDateTime('2026-11-05T14:30:00')).toBe(false);
  });

  it('isLocalDateTime 拒绝纯日期', () => {
    expect(isLocalDateTime('2026-11-05')).toBe(false);
  });
});
```

- [x] **Step 4: 运行测试确认失败**

```bash
npx vitest run tests/time.test.ts
```

Expected: FAIL —— 报 `Failed to resolve import "../electron/util/time"`。

- [x] **Step 5: 运行测试确认通过**

```bash
npx vitest run tests/time.test.ts
```

Expected: PASS，7 个用例全绿。

- [x] **Step 6: 提交**

```bash
git add -A
git commit -m "feat: 添加领域实体类型与 ID/时间工具"
```

---

### Task 3: 数据库连接、迁移与完整性检查

**Files:**
- Create: `electron/db.ts`, `electron/migrations.ts`, `electron/lib/result.ts`, `tests/db.test.ts`
- Modify: `electron/main.ts`（接入数据库，Task 1 的 main.ts 改为使用 `openDatabase`）

**Interfaces:**
- Consumes: `electron/util/time.ts` 的 `nowIso()`
- Produces:
  - `electron/migrations.ts` 导出 `MIGRATIONS: Migration[]`（`Migration` 接口含 `version: number`、`name: string`、`sql: string`）
  - `electron/db.ts` 导出：
    - `openDatabase(filePath: string): DatabaseSync`
    - `runMigrations(db: DatabaseSync): void`
    - `getUserVersion(db: DatabaseSync): number`
    - `isHealthy(db: DatabaseSync): boolean`
    - `withTransaction<T>(db: DatabaseSync, fn: () => T): T`
  - `electron/lib/result.ts` 导出：
    - `type Result<T> = { ok: true; data: T } | { ok: false; error: { code: string; message: string } }`
    - `ok<T>(data: T): Result<T>`
    - `fail(code: string, message: string): Result<never>`
    - `toBool(value: boolean): number`（boolean → 0/1，给 node:sqlite 绑定用）
    - `nullable<T>(value: T | null | undefined): T | null`（undefined → null）

- [x] **Step 1: 写迁移定义**

`electron/migrations.ts`：

```ts
export interface Migration {
  version: number;
  name: string;
  sql: string;
}

export const MIGRATIONS: Migration[] = [
  {
    version: 1,
    name: 'init-core-schema',
    sql: `
CREATE TABLE projects (
  id              TEXT PRIMARY KEY,
  name            TEXT NOT NULL,
  description     TEXT NOT NULL DEFAULT '',
  status          TEXT NOT NULL DEFAULT 'planning'
                  CHECK (status IN ('planning','active','paused','done')),
  priority        TEXT NOT NULL DEFAULT 'none'
                  CHECK (priority IN ('high','medium','low','none')),
  color           TEXT,
  progress_mode   TEXT NOT NULL DEFAULT 'auto'
                  CHECK (progress_mode IN ('auto','manual')),
  progress_manual INTEGER CHECK (progress_manual IS NULL OR (progress_manual >= 0 AND progress_manual <= 100)),
  start_date      TEXT,
  due_date        TEXT,
  sort_order      INTEGER NOT NULL DEFAULT 0,
  created_at      TEXT NOT NULL,
  updated_at      TEXT NOT NULL,
  archived_at     TEXT
);

CREATE TABLE tasks (
  id              TEXT PRIMARY KEY,
  project_id      TEXT REFERENCES projects(id) ON DELETE CASCADE,
  title           TEXT NOT NULL,
  notes           TEXT NOT NULL DEFAULT '',
  status          TEXT NOT NULL DEFAULT 'todo'
                  CHECK (status IN ('todo','doing','blocked','done','canceled')),
  priority        TEXT NOT NULL DEFAULT 'none'
                  CHECK (priority IN ('high','medium','low','none')),
  due_at          TEXT,
  remind_before   INTEGER NOT NULL DEFAULT 0,
  estimate        REAL,
  spent           REAL,
  blocked_reason  TEXT,
  sort_order      INTEGER NOT NULL DEFAULT 0,
  created_at      TEXT NOT NULL,
  updated_at      TEXT NOT NULL,
  started_at      TEXT,
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
  kind         TEXT NOT NULL CHECK (kind IN ('task_due','custom')),
  task_id      TEXT REFERENCES tasks(id) ON DELETE CASCADE,
  title        TEXT NOT NULL,
  remind_at    TEXT NOT NULL,
  repeat_rule  TEXT NOT NULL DEFAULT 'none'
               CHECK (repeat_rule IN ('none','daily','weekly')),
  channel      TEXT NOT NULL DEFAULT 'system'
               CHECK (channel IN ('system','inapp')),
  status       TEXT NOT NULL DEFAULT 'pending'
               CHECK (status IN ('pending','fired','dismissed','failed','done')),
  fired_at     TEXT,
  created_at   TEXT NOT NULL,
  updated_at   TEXT NOT NULL
);

CREATE TABLE reports (
  id             TEXT PRIMARY KEY,
  kind           TEXT NOT NULL CHECK (kind IN ('weekly','monthly')),
  period_start   TEXT NOT NULL,
  period_end     TEXT NOT NULL,
  auto_markdown  TEXT NOT NULL DEFAULT '',
  manual_text    TEXT NOT NULL DEFAULT '',
  final_markdown TEXT NOT NULL DEFAULT '',
  created_at     TEXT NOT NULL,
  updated_at     TEXT NOT NULL
);

CREATE TABLE settings (
  key   TEXT PRIMARY KEY,
  value TEXT NOT NULL
);

CREATE INDEX idx_tasks_project  ON tasks(project_id);
CREATE INDEX idx_tasks_status   ON tasks(status);
CREATE INDEX idx_tasks_due      ON tasks(due_at);
CREATE INDEX idx_task_tags_tag  ON task_tags(tag_id);
CREATE INDEX idx_reminders_due  ON reminders(status, remind_at);
CREATE INDEX idx_reports_period ON reports(kind, period_start);
`
  }
];
```

- [x] **Step 2: 写 Result 工具**

`electron/lib/result.ts`：

```ts
export type Result<T> =
  | { ok: true; data: T }
  | { ok: false; error: { code: string; message: string } };

export function ok<T>(data: T): Result<T> {
  return { ok: true, data };
}

export function fail(code: string, message: string): Result<never> {
  return { ok: false, error: { code, message } };
}

export function toBool(value: boolean): number {
  return value ? 1 : 0;
}

export function nullable<T>(value: T | null | undefined): T | null {
  return value === undefined ? null : value;
}
```

- [x] **Step 3: 写数据库模块**

`electron/db.ts`：

```ts
import { DatabaseSync } from 'node:sqlite';
import { copyFileSync, mkdirSync } from 'node:fs';
import { dirname } from 'node:path';
import { MIGRATIONS } from './migrations';

export function openDatabase(filePath: string): DatabaseSync {
  mkdirSync(dirname(filePath), { recursive: true });
  const db = new DatabaseSync(filePath);
  db.exec('PRAGMA journal_mode = WAL');
  db.exec('PRAGMA foreign_keys = ON');
  return db;
}

export function getUserVersion(db: DatabaseSync): number {
  const row = db.prepare('PRAGMA user_version').get() as
    | { user_version?: number }
    | undefined;
  return row?.user_version ?? 0;
}

export function runMigrations(db: DatabaseSync): void {
  const current = getUserVersion(db);
  for (const migration of MIGRATIONS) {
    if (migration.version <= current) continue;
    db.exec('BEGIN');
    try {
      db.exec(migration.sql);
      db.exec(`PRAGMA user_version = ${migration.version}`);
      db.exec('COMMIT');
    } catch (error) {
      db.exec('ROLLBACK');
      throw new Error(
        `迁移失败 (version=${migration.version} ${migration.name}): ${(error as Error).message}`
      );
    }
  }
}

export function isHealthy(db: DatabaseSync): boolean {
  const rows = db.prepare('PRAGMA integrity_check').all() as Array<{
    integrity_check: string;
  }>;
  return rows.length === 1 && rows[0].integrity_check === 'ok';
}

export function withTransaction<T>(db: DatabaseSync, fn: () => T): T {
  db.exec('BEGIN');
  try {
    const result = fn();
    db.exec('COMMIT');
    return result;
  } catch (error) {
    db.exec('ROLLBACK');
    throw error;
  }
}

export function backupDatabase(db: DatabaseSync, targetPath: string): void {
  db.exec('PRAGMA wal_checkpoint(TRUNCATE)');
  copyFileSync(db.location(), targetPath);
}
```

**注意**：`db.location()` 返回当前数据库文件路径（已实测）。若 `openDatabase` 传入的是 `:memory:`，`db.location()` 返回 **`null`**（不是空字符串），此时 `backupDatabase` 会因 `copyFileSync(null, ...)` 抛错 —— **测试中不要调用它**，生产路径永远传真实文件路径。

- [x] **Step 4: 写数据库的失败测试**

`tests/db.test.ts`：

```ts
import { DatabaseSync } from 'node:sqlite';
import { describe, expect, it } from 'vitest';
import { getUserVersion, isHealthy, openDatabase, runMigrations } from '../electron/db';
import { MIGRATIONS } from '../electron/migrations';

function freshDb(): DatabaseSync {
  const db = new DatabaseSync(':memory:');
  db.exec('PRAGMA foreign_keys = ON');
  return db;
}

describe('数据库迁移', () => {
  it('空库的 user_version 为 0', () => {
    expect(getUserVersion(freshDb())).toBe(0);
  });

  it('迁移后 user_version 等于最新迁移版本号', () => {
    const db = freshDb();
    runMigrations(db);
    const latest = Math.max(...MIGRATIONS.map((m) => m.version));
    expect(getUserVersion(db)).toBe(latest);
  });

  it('迁移会创建全部七张核心表', () => {
    const db = freshDb();
    runMigrations(db);
    const names = db
      .prepare("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")
      .all()
      .map((r) => (r as { name: string }).name)
      .sort();
    expect(names).toEqual(
      ['projects', 'reminders', 'reports', 'settings', 'tags', 'task_tags', 'tasks'].sort()
    );
  });

  it('重复执行迁移是幂等的', () => {
    const db = freshDb();
    runMigrations(db);
    runMigrations(db);
    const latest = Math.max(...MIGRATIONS.map((m) => m.version));
    expect(getUserVersion(db)).toBe(latest);
  });

  it('迁移后的库通过完整性检查', () => {
    const db = freshDb();
    runMigrations(db);
    expect(isHealthy(db)).toBe(true);
  });

  it('外键约束生效：删除项目会级联删除其任务', () => {
    const db = freshDb();
    runMigrations(db);
    const now = new Date().toISOString();
    db.prepare('INSERT INTO projects (id, name, created_at, updated_at) VALUES (?, ?, ?, ?)').run(
      'p1',
      '项目一',
      now,
      now
    );
    db.prepare(
      'INSERT INTO tasks (id, project_id, title, created_at, updated_at) VALUES (?, ?, ?, ?, ?)'
    ).run('t1', 'p1', '任务一', now, now);
    db.prepare('DELETE FROM projects WHERE id = ?').run('p1');
    const count = db.prepare('SELECT COUNT(*) AS c FROM tasks').get() as { c: number };
    expect(count.c).toBe(0);
  });

  it('CHECK 约束拒绝非法的任务状态', () => {
    const db = freshDb();
    runMigrations(db);
    const now = new Date().toISOString();
    expect(() =>
      db
        .prepare(
          'INSERT INTO tasks (id, title, status, created_at, updated_at) VALUES (?, ?, ?, ?, ?)'
        )
        .run('t2', '非法状态', 'archived', now, now)
    ).toThrow();
  });
});

describe('openDatabase', () => {
  it('打开内存库不抛错', () => {
    const db = openDatabase(':memory:');
    expect(isHealthy(db)).toBe(true);
    db.close();
  });
});
```

- [x] **Step 5: 运行测试确认失败**

```bash
npx vitest run tests/db.test.ts
```

Expected: FAIL —— 报 `Failed to resolve import "../electron/db"`。

- [x] **Step 6: 运行测试确认通过**

```bash
npx vitest run tests/db.test.ts
```

Expected: PASS，8 个用例全绿。

- [x] **Step 7: 主进程接入数据库，并实现损坏恢复与每日备份**

改 `electron/main.ts`：在 `app.whenReady()` 里打开数据库、执行迁移、检查完整性、做每日备份。

```ts
import { app, BrowserWindow, dialog } from 'electron';
import { existsSync, mkdirSync, readdirSync, renameSync, unlinkSync } from 'node:fs';
import { dirname, join } from 'node:path';
import type { DatabaseSync } from 'node:sqlite';
import { backupDatabase, isHealthy, openDatabase, runMigrations } from './db';
import { toLocalDate } from './util/time';

export function createMainWindow(): BrowserWindow {
  // ... 与 Task 1 相同，此处省略
}

function resolveDbPath(): string {
  return app.isPackaged
    ? join(app.getPath('userData'), 'data.db')
    : join(__dirname, '..', '.data', 'data.db');
}

/** 启动时探测数据库完整性；损坏则把原文件改名留存并重建空库 */
function recoverIfCorrupt(dbPath: string): void {
  if (!existsSync(dbPath)) return;

  let healthy = false;
  try {
    const probe = openDatabase(dbPath);
    healthy = isHealthy(probe);
    probe.close();
  } catch {
    healthy = false;
  }
  if (healthy) return;

  const stamp = toLocalDate(new Date()).replace(/-/g, '');
  for (const suffix of ['', '-wal', '-shm']) {
    const target = `${dbPath}${suffix}`;
    if (existsSync(target)) renameSync(target, `${dbPath}.corrupt-${stamp}${suffix}`);
  }

  dialog.showErrorBox(
    '数据库已损坏',
    `损坏的文件已重命名为 ${dbPath}.corrupt-${stamp}\n\n程序会新建一个空数据库继续运行。`
  );
}

/** 每天首次启动时备份一次，只保留最近 7 份 */
function runDailyBackup(db: DatabaseSync, dbPath: string): void {
  const backupDir = join(dirname(dbPath), 'backups');
  const today = toLocalDate(new Date());
  const target = join(backupDir, `data-${today}.db`);
  if (existsSync(target)) return;

  mkdirSync(backupDir, { recursive: true });
  backupDatabase(db, target);

  const files = readdirSync(backupDir)
    .filter((name) => name.startsWith('data-') && name.endsWith('.db'))
    .sort();
  while (files.length > 7) {
    const oldest = files.shift();
    if (oldest) unlinkSync(join(backupDir, oldest));
  }
}

app.whenReady().then(() => {
  const dbPath = resolveDbPath();
  recoverIfCorrupt(dbPath);

  const db = openDatabase(dbPath);
  runMigrations(db);
  runDailyBackup(db, dbPath);

  createMainWindow();

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createMainWindow();
  });
});
```

**注意**：Task 8 会在这段里追加 `registerIpc(db, dbPath)` 一行，位于 `runDailyBackup` 之后、`createMainWindow()` 之前。

- [x] **Step 8: 验证开发模式能建库**

```bash
npm run dev
```

Expected: 窗口正常弹出；`.data/data.db`、`.data/data.db-wal`、`.data/data.db-shm` 三个文件出现；`.data/backups/data-<今天日期>.db` 备份文件也生成。关闭进程。

- [x] **Step 9: 提交**

```bash
git add -A
git commit -m "feat: 添加 SQLite 连接、版本化迁移与完整性检查"
```

---

### Task 4: 项目仓储与进度计算

**Files:**
- Create: `electron/repositories/projects.ts`, `src/lib/progress.ts`, `tests/projects.test.ts`, `tests/progress.test.ts`
- Modify: 无

**Interfaces:**
- Consumes: `src/types/entities.ts` 的 `Project`、`ProjectStatus`、`Priority`、`ProgressMode`；`electron/util/id.ts` 的 `newId()`；`electron/util/time.ts` 的 `nowIso()`、`toLocalDateTime()`
- Produces:
  - `src/lib/progress.ts` 导出 `computeProgress(tasks: Array<{ status: TaskStatus }>): number` —— **全项目唯一实现**，主进程与渲染进程共用同一份，不存在副本
  - `electron/repositories/projects.ts` 导出：
    - `interface CreateProjectInput { name: string; description?: string; status?: ProjectStatus; priority?: Priority; color?: string | null; progressMode?: ProgressMode; progressManual?: number | null; startDate?: string | null; dueDate?: string | null; }`
    - `interface UpdateProjectInput { name?: string; description?: string; status?: ProjectStatus; priority?: Priority; color?: string | null; progressMode?: ProgressMode; progressManual?: number | null; startDate?: string | null; dueDate?: string | null; sortOrder?: number; archivedAt?: string | null; }`
    - `function createProjectsRepo(db: DatabaseSync): ProjectsRepo`
    - `type ProjectsRepo = ReturnType<typeof createProjectsRepo>`

`ProjectsRepo` 的方法签名（后续任务依赖，勿改）：

```ts
{
  list(options?: { includeArchived?: boolean }): Project[];
  get(id: string): Project | null;
  getByName(name: string): Project | null;
  create(input: CreateProjectInput): Project;
  update(id: string, patch: UpdateProjectInput): Project | null;
  archive(id: string): boolean;
  unarchive(id: string): boolean;
  remove(id: string): boolean;
  countTasks(projectId: string): { total: number; done: number; overdue: number; doing: number; };
}
```

- [x] **Step 1: 写进度计算纯函数**

`src/lib/progress.ts`：

```ts
// 用相对路径而不是 @ 别名：tsconfig.node.json（主进程程序）没有 paths 配置，
// 该文件一旦被 electron/** 引入，别名就会报 TS2307
import type { TaskStatus } from '../types/entities';

export function computeProgress(tasks: Array<{ status: TaskStatus }>): number {
  const counted = tasks.filter((t) => t.status !== 'canceled');
  if (counted.length === 0) return 0;
  const done = counted.filter((t) => t.status === 'done').length;
  return Math.round((done / counted.length) * 100);
}
```

**注意**：这个函数放在 `src/lib/` 而不是 `electron/lib/`，因为主进程与渲染进程都要用它。主进程从 `../../src/lib/progress` 引入，与主进程从 `../../src/types/entities` 取类型的做法一致。**全项目只此一份，不要在任何其他位置复制它。**

- [x] **Step 2: 写进度的失败测试**

`tests/progress.test.ts`：

```ts
import { describe, expect, it } from 'vitest';
import { computeProgress } from '../src/lib/progress';

describe('进度计算', () => {
  it('空集合返回 0', () => {
    expect(computeProgress([])).toBe(0);
  });

  it('全部取消时返回 0 而不是 NaN', () => {
    expect(computeProgress([{ status: 'canceled' }, { status: 'canceled' }])).toBe(0);
  });

  it('全部完成返回 100', () => {
    expect(computeProgress([{ status: 'done' }, { status: 'done' }])).toBe(100);
  });

  it('一半完成返回 50', () => {
    expect(computeProgress([{ status: 'done' }, { status: 'todo' }])).toBe(50);
  });

  it('已取消的任务不计入分母', () => {
    expect(computeProgress([{ status: 'done' }, { status: 'todo' }, { status: 'canceled' }])).toBe(50);
  });

  it('进行中与阻塞都算未完成', () => {
    expect(computeProgress([{ status: 'done' }, { status: 'doing' }, { status: 'blocked' }])).toBe(33);
  });
});
```

- [x] **Step 3: 运行测试确认失败**

```bash
npx vitest run tests/progress.test.ts
```

Expected: FAIL —— `Failed to resolve import "../electron/lib/progress"`。

- [x] **Step 4: 运行测试确认通过**

```bash
npx vitest run tests/progress.test.ts
```

Expected: PASS，6 个用例全绿。

- [x] **Step 5: 写项目仓储**

`electron/repositories/projects.ts`：

```ts
import type { DatabaseSync } from 'node:sqlite';
import type {
  Priority,
  ProgressMode,
  Project,
  ProjectStatus
} from '../../src/types/entities';
import { newId } from '../util/id';
import { nowIso, toLocalDateTime } from '../util/time';
import { nullable } from '../lib/result';

// 用 type 而非 interface：node:sqlite 的 all() 返回 Record<string, SQLOutputValue>[]，
// 类型别名（对象类型字面量）才有隐式索引签名，interface 没有，直接断言会报 TS2352
type ProjectRow = {
  id: string;
  name: string;
  description: string;
  status: string;
  priority: string;
  color: string | null;
  progress_mode: string;
  progress_manual: number | null;
  start_date: string | null;
  due_date: string | null;
  sort_order: number;
  created_at: string;
  updated_at: string;
  archived_at: string | null;
};

function mapRow(row: ProjectRow): Project {
  return {
    id: row.id,
    name: row.name,
    description: row.description,
    status: row.status as ProjectStatus,
    priority: row.priority as Priority,
    color: row.color,
    progressMode: row.progress_mode as ProgressMode,
    progressManual: row.progress_manual,
    startDate: row.start_date,
    dueDate: row.due_date,
    sortOrder: row.sort_order,
    createdAt: row.created_at,
    updatedAt: row.updated_at,
    archivedAt: row.archived_at
  };
}

export interface CreateProjectInput {
  name: string;
  description?: string;
  status?: ProjectStatus;
  priority?: Priority;
  color?: string | null;
  progressMode?: ProgressMode;
  progressManual?: number | null;
  startDate?: string | null;
  dueDate?: string | null;
}

export interface UpdateProjectInput {
  name?: string;
  description?: string;
  status?: ProjectStatus;
  priority?: Priority;
  color?: string | null;
  progressMode?: ProgressMode;
  progressManual?: number | null;
  startDate?: string | null;
  dueDate?: string | null;
  sortOrder?: number;
  archivedAt?: string | null;
}

export function createProjectsRepo(db: DatabaseSync) {
  const selectAll = db.prepare('SELECT * FROM projects ORDER BY sort_order ASC, created_at ASC');
  const selectActive = db.prepare(
    'SELECT * FROM projects WHERE archived_at IS NULL ORDER BY sort_order ASC, created_at ASC'
  );
  const selectById = db.prepare('SELECT * FROM projects WHERE id = ?');
  const selectByName = db.prepare('SELECT * FROM projects WHERE name = ?');

  // 局部查询函数：内部调用不依赖返回对象的 this 绑定，
  // 解构（const { create } = repo）或把方法当回调传（list().map(repo.get)）都不会炸
  function getById(id: string): Project | null {
    const row = selectById.get(id) as ProjectRow | undefined;
    return row ? mapRow(row) : null;
  }

  return {
    list(options?: { includeArchived?: boolean }): Project[] {
      const stmt = options?.includeArchived ? selectAll : selectActive;
      return (stmt.all() as ProjectRow[]).map(mapRow);
    },

    get(id: string): Project | null {
      return getById(id);
    },

    getByName(name: string): Project | null {
      const row = selectByName.get(name) as ProjectRow | undefined;
      return row ? mapRow(row) : null;
    },

    create(input: CreateProjectInput): Project {
      const id = newId();
      const now = nowIso();
      db.prepare(
        `INSERT INTO projects (
           id, name, description, status, priority, color,
           progress_mode, progress_manual, start_date, due_date,
           sort_order, created_at, updated_at
         ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`
      ).run(
        id,
        input.name,
        input.description ?? '',
        input.status ?? 'planning',
        input.priority ?? 'none',
        nullable(input.color),
        input.progressMode ?? 'auto',
        nullable(input.progressManual),
        nullable(input.startDate),
        nullable(input.dueDate),
        0,
        now,
        now
      );
      const created = getById(id);
      if (!created) throw new Error('项目创建后读取失败');
      return created;
    },

    update(id: string, patch: UpdateProjectInput): Project | null {
      const existing = getById(id);
      if (!existing) return null;

      // 合并后的记录每个字段都已确定，用 Required 去掉可选性：
      // 否则字段类型会带上 undefined，node:sqlite 不接受 undefined 作为绑定参数
      const merged: Required<UpdateProjectInput> = {
        name: patch.name ?? existing.name,
        description: patch.description ?? existing.description,
        status: patch.status ?? existing.status,
        priority: patch.priority ?? existing.priority,
        color: patch.color === undefined ? existing.color : patch.color,
        progressMode: patch.progressMode ?? existing.progressMode,
        progressManual:
          patch.progressManual === undefined ? existing.progressManual : patch.progressManual,
        startDate: patch.startDate === undefined ? existing.startDate : patch.startDate,
        dueDate: patch.dueDate === undefined ? existing.dueDate : patch.dueDate,
        sortOrder: patch.sortOrder ?? existing.sortOrder,
        archivedAt: patch.archivedAt === undefined ? existing.archivedAt : patch.archivedAt
      };

      db.prepare(
        `UPDATE projects SET
           name = ?, description = ?, status = ?, priority = ?, color = ?,
           progress_mode = ?, progress_manual = ?, start_date = ?, due_date = ?,
           sort_order = ?, updated_at = ?, archived_at = ?
         WHERE id = ?`
      ).run(
        merged.name,
        merged.description,
        merged.status,
        merged.priority,
        nullable(merged.color),
        merged.progressMode,
        nullable(merged.progressManual),
        nullable(merged.startDate),
        nullable(merged.dueDate),
        merged.sortOrder,
        nowIso(),
        nullable(merged.archivedAt),
        id
      );

      return getById(id);
    },

    archive(id: string): boolean {
      // archived_at 与 updated_at 语义相同，取一次时间戳复用，避免两次调用差 1ms
      const now = nowIso();
      const result = db
        .prepare('UPDATE projects SET archived_at = ?, updated_at = ? WHERE id = ?')
        .run(now, now, id);
      return Number(result.changes) > 0;
    },

    unarchive(id: string): boolean {
      const result = db
        .prepare('UPDATE projects SET archived_at = NULL, updated_at = ? WHERE id = ?')
        .run(nowIso(), id);
      return Number(result.changes) > 0;
    },

    remove(id: string): boolean {
      const result = db.prepare('DELETE FROM projects WHERE id = ?').run(id);
      return Number(result.changes) > 0;
    },

    countTasks(projectId: string): { total: number; done: number; overdue: number; doing: number } {
      // 本地墙钟当前时刻（YYYY-MM-DDTHH:mm），与 due_at 同格式，字典序即时间序。
      // 全项目统一墙钟口径：与 Task 10 的 isOverdue(dueAt, nowLocalDateTime)、Task 11 徽标一致
      const now = toLocalDateTime(new Date());
      const row = db
        .prepare(
          `SELECT
             COUNT(*) AS total,
             SUM(CASE WHEN status = 'done' THEN 1 ELSE 0 END) AS done,
             SUM(CASE WHEN status = 'doing' THEN 1 ELSE 0 END) AS doing,
             SUM(CASE WHEN status NOT IN ('done','canceled') AND due_at IS NOT NULL
                      AND due_at < ? THEN 1 ELSE 0 END) AS overdue
           FROM tasks WHERE project_id = ?`
        )
        .get(now, projectId) as {
        total: number;
        done: number;
        doing: number;
        overdue: number;
      };
      return {
        total: row.total ?? 0,
        done: row.done ?? 0,
        doing: row.doing ?? 0,
        overdue: row.overdue ?? 0
      };
    }
  };
}

export type ProjectsRepo = ReturnType<typeof createProjectsRepo>;
```

**注意**：`countTasks` 的逾期判定用 `due_at < ?` 配合 `toLocalDateTime(new Date())`（**本地墙钟当前时刻**）。`due_at` 与之同为 `YYYY-MM-DDTHH:mm` 定长格式，字典序即时间序。**不要用 `nowIso().slice(0, 10)`**（UTC 日期，会与本地墙钟差最多一天）。**也不要退回 `toLocalDate(new Date())`（只到天）** —— 那会让「今天 09:00 到期、现在 10:00」的任务不算逾期，与 Task 10 的 `isOverdue(dueAt, nowLocalDateTime)`（墙钟口径）及 Task 11 项目卡片不一致，同一个任务会在任务列表标红、在项目卡片却不计入逾期徽标。全项目统一用墙钟口径。

- [x] **Step 6: 写项目仓储的失败测试**

`tests/projects.test.ts`：

```ts
import { DatabaseSync } from 'node:sqlite';
import { beforeEach, describe, expect, it } from 'vitest';
import { runMigrations } from '../electron/db';
import { createProjectsRepo } from '../electron/repositories/projects';

let db: DatabaseSync;
let repo: ReturnType<typeof createProjectsRepo>;

beforeEach(() => {
  db = new DatabaseSync(':memory:');
  db.exec('PRAGMA foreign_keys = ON');
  runMigrations(db);
  repo = createProjectsRepo(db);
});

describe('项目仓储', () => {
  it('创建后能按 id 取回', () => {
    const created = repo.create({ name: '重构工作台' });
    expect(created.name).toBe('重构工作台');
    expect(created.status).toBe('planning');
    expect(created.progressMode).toBe('auto');
    expect(created.archivedAt).toBeNull();
    expect(repo.get(created.id)?.id).toBe(created.id);
  });

  it('默认列表不包含已归档项目', () => {
    const a = repo.create({ name: '在做的' });
    const b = repo.create({ name: '做完的' });
    repo.archive(b.id);
    expect(repo.list().map((p) => p.id)).toEqual([a.id]);
  });

  it('带 includeArchived 时返回全部项目', () => {
    repo.create({ name: '在做的' });
    const b = repo.create({ name: '做完的' });
    repo.archive(b.id);
    expect(repo.list({ includeArchived: true })).toHaveLength(2);
  });

  it('取消归档后重新出现在默认列表', () => {
    const b = repo.create({ name: '做完的' });
    repo.archive(b.id);
    repo.unarchive(b.id);
    expect(repo.list()).toHaveLength(1);
  });

  it('更新只覆盖传入的字段', () => {
    const created = repo.create({ name: '原名', description: '原描述', priority: 'high' });
    const updated = repo.update(created.id, { name: '新名' });
    expect(updated?.name).toBe('新名');
    expect(updated?.description).toBe('原描述');
    expect(updated?.priority).toBe('high');
  });

  it('更新不存在的项目返回 null', () => {
    expect(repo.update('missing', { name: 'x' })).toBeNull();
  });

  it('删除项目返回 true，重复删除返回 false', () => {
    const created = repo.create({ name: '待删' });
    expect(repo.remove(created.id)).toBe(true);
    expect(repo.remove(created.id)).toBe(false);
  });

  it('按名称查询能命中', () => {
    repo.create({ name: '唯一名字' });
    expect(repo.getByName('唯一名字')?.name).toBe('唯一名字');
    expect(repo.getByName('不存在')).toBeNull();
  });

  it('手动进度模式可以写入百分比', () => {
    const created = repo.create({ name: '手动进度', progressMode: 'manual', progressManual: 42 });
    expect(repo.get(created.id)?.progressManual).toBe(42);
  });

  it('countTasks 统计总数、完成数与进行中数', () => {
    const project = repo.create({ name: '统计项目' });
    const now = new Date().toISOString();
    const ins = db.prepare(
      'INSERT INTO tasks (id, project_id, title, status, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?)'
    );
    ins.run('t1', project.id, '一', 'done', now, now);
    ins.run('t2', project.id, '二', 'doing', now, now);
    ins.run('t3', project.id, '三', 'todo', now, now);
    const counts = repo.countTasks(project.id);
    expect(counts.total).toBe(3);
    expect(counts.done).toBe(1);
    expect(counts.doing).toBe(1);
  });

  it('countTasks 把过期未完成的任务计为逾期', () => {
    const project = repo.create({ name: '逾期项目' });
    const now = new Date().toISOString();
    db.prepare(
      'INSERT INTO tasks (id, project_id, title, status, due_at, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?)'
    ).run('t1', project.id, '旧的', 'todo', '2000-01-01T10:00', now, now);
    expect(repo.countTasks(project.id).overdue).toBe(1);
  });
});
```

- [x] **Step 7: 运行测试确认失败**

```bash
npx vitest run tests/projects.test.ts
```

Expected: FAIL —— `Failed to resolve import "../electron/repositories/projects"`。

- [x] **Step 8: 运行测试确认通过**

```bash
npx vitest run tests/projects.test.ts
```

Expected: PASS，11 个用例全绿。

- [x] **Step 9: 提交**

```bash
git add -A
git commit -m "feat: 添加项目仓储与进度计算"
```

---

### Task 5: 任务仓储

**Files:**
- Create: `electron/repositories/tasks.ts`, `tests/tasks.test.ts`
- Modify: 无

**Interfaces:**
- Consumes: `src/types/entities.ts` 的 `Task`、`TaskStatus`、`Priority`；`electron/util/id.ts`；`electron/util/time.ts` 的 `nowIso()`
- Produces:
  - `electron/repositories/tasks.ts` 导出：
    - `interface CreateTaskInput { title: string; projectId?: string | null; notes?: string; status?: TaskStatus; priority?: Priority; dueAt?: string | null; remindBefore?: number; estimate?: number | null; spent?: number | null; blockedReason?: string | null; }`
    - `interface UpdateTaskInput { title?: string; projectId?: string | null; notes?: string; status?: TaskStatus; priority?: Priority; dueAt?: string | null; remindBefore?: number; estimate?: number | null; spent?: number | null; blockedReason?: string | null; sortOrder?: number; }`
    - `interface TaskFilter { projectId?: string | null; status?: TaskStatus[]; priority?: Priority[]; tagIds?: string[]; dueBefore?: string; dueAfter?: string; includeDone?: boolean; search?: string; }`
    - `function createTasksRepo(db: DatabaseSync): TasksRepo`

`TasksRepo` 方法签名（后续任务依赖，勿改）：

```ts
{
  list(filter?: TaskFilter): Task[];
  get(id: string): Task | null;
  create(input: CreateTaskInput): Task;
  update(id: string, patch: UpdateTaskInput): Task | null;
  setStatus(id: string, status: TaskStatus): Task | null;
  remove(id: string): boolean;
  countByStatus(): Record<TaskStatus, number>;
  countOverdue(nowLocalDateTime: string): number;
  listByProject(projectId: string): Task[];
}
```

- [x] **Step 1: 写任务仓储**

`electron/repositories/tasks.ts`：

```ts
import type { DatabaseSync } from 'node:sqlite';
import type { Priority, Task, TaskStatus } from '../../src/types/entities';
import { newId } from '../util/id';
import { nowIso } from '../util/time';
import { nullable } from '../lib/result';

// 用 type 而非 interface：node:sqlite 的 all() 返回 Record<string, SQLOutputValue>[]，
// 类型别名（对象类型字面量）才有隐式索引签名，interface 没有，直接断言会报 TS2352
type TaskRow = {
  id: string;
  project_id: string | null;
  title: string;
  notes: string;
  status: string;
  priority: string;
  due_at: string | null;
  remind_before: number;
  estimate: number | null;
  spent: number | null;
  blocked_reason: string | null;
  sort_order: number;
  created_at: string;
  updated_at: string;
  started_at: string | null;
  completed_at: string | null;
};

function mapRow(row: TaskRow): Task {
  return {
    id: row.id,
    projectId: row.project_id,
    title: row.title,
    notes: row.notes,
    status: row.status as TaskStatus,
    priority: row.priority as Priority,
    dueAt: row.due_at,
    remindBefore: row.remind_before,
    estimate: row.estimate,
    spent: row.spent,
    blockedReason: row.blocked_reason,
    sortOrder: row.sort_order,
    createdAt: row.created_at,
    updatedAt: row.updated_at,
    startedAt: row.started_at,
    completedAt: row.completed_at
  };
}

export interface CreateTaskInput {
  title: string;
  projectId?: string | null;
  notes?: string;
  status?: TaskStatus;
  priority?: Priority;
  dueAt?: string | null;
  remindBefore?: number;
  estimate?: number | null;
  spent?: number | null;
  blockedReason?: string | null;
}

export interface UpdateTaskInput {
  title?: string;
  projectId?: string | null;
  notes?: string;
  status?: TaskStatus;
  priority?: Priority;
  dueAt?: string | null;
  remindBefore?: number;
  estimate?: number | null;
  spent?: number | null;
  blockedReason?: string | null;
  sortOrder?: number;
}

export interface TaskFilter {
  projectId?: string | null;
  status?: TaskStatus[];
  priority?: Priority[];
  tagIds?: string[];
  dueBefore?: string;
  dueAfter?: string;
  includeDone?: boolean;
  search?: string;
}

const ALL_STATUSES: TaskStatus[] = ['todo', 'doing', 'blocked', 'done', 'canceled'];

export function createTasksRepo(db: DatabaseSync) {
  const selectById = db.prepare('SELECT * FROM tasks WHERE id = ?');

  // 局部查询函数：内部调用不依赖返回对象的 this 绑定，
  // 解构（const { create } = repo）或把方法当回调传（list().map(repo.get)）都不会炸
  const getById = (id: string): Task | null => {
    const row = selectById.get(id) as TaskRow | undefined;
    return row ? mapRow(row) : null;
  };

  // 局部查询函数：同理不依赖 this，list / listByProject 都委托给它
  const listTasks = (filter?: TaskFilter): Task[] => {
    const where: string[] = [];
    const params: Array<string | number> = [];

    if (filter?.projectId !== undefined) {
      if (filter.projectId === null) {
        where.push('project_id IS NULL');
      } else {
        where.push('project_id = ?');
        params.push(filter.projectId);
      }
    }

    if (filter?.status && filter.status.length > 0) {
      where.push(`status IN (${filter.status.map(() => '?').join(', ')})`);
      params.push(...filter.status);
    } else if (filter?.includeDone === false) {
      where.push(`status IN (${ALL_STATUSES.filter((s) => s !== 'done').map(() => '?').join(', ')})`);
      params.push(...ALL_STATUSES.filter((s) => s !== 'done'));
    }

    if (filter?.priority && filter.priority.length > 0) {
      where.push(`priority IN (${filter.priority.map(() => '?').join(', ')})`);
      params.push(...filter.priority);
    }

    if (filter?.dueBefore) {
      where.push('due_at IS NOT NULL AND due_at < ?');
      params.push(filter.dueBefore);
    }

    if (filter?.dueAfter) {
      where.push('due_at IS NOT NULL AND due_at >= ?');
      params.push(filter.dueAfter);
    }

    if (filter?.search) {
      where.push('(title LIKE ? OR notes LIKE ?)');
      const like = `%${filter.search}%`;
      params.push(like, like);
    }

    const sql = `SELECT * FROM tasks ${
      where.length > 0 ? `WHERE ${where.join(' AND ')}` : ''
    } ORDER BY sort_order ASC, created_at ASC`;

    return (db.prepare(sql).all(...params) as TaskRow[]).map(mapRow);
  };

  // 局部更新函数：setStatus 与 update 共享同一套状态迁移与时间戳语义，
  // 且都不依赖 this 绑定
  const updateTask = (id: string, patch: UpdateTaskInput): Task | null => {
    const existing = getById(id);
    if (!existing) return null;

    const nextStatus = patch.status ?? existing.status;
    const now = nowIso();
    // 首次进入 doing 时写 started_at，已有则保留原值
    const startedAt = nextStatus === 'doing' ? (existing.startedAt ?? now) : existing.startedAt;
    // 进入 done 时写 completed_at；从 done 退回其他状态时清空
    const completedAt = nextStatus === 'done' ? (existing.completedAt ?? now) : null;

    db.prepare(
      `UPDATE tasks SET
         title = ?, project_id = ?, notes = ?, status = ?, priority = ?, due_at = ?,
         remind_before = ?, estimate = ?, spent = ?, blocked_reason = ?, sort_order = ?,
         updated_at = ?, started_at = ?, completed_at = ?
       WHERE id = ?`
    ).run(
      patch.title ?? existing.title,
      patch.projectId === undefined ? existing.projectId : patch.projectId,
      patch.notes ?? existing.notes,
      nextStatus,
      patch.priority ?? existing.priority,
      patch.dueAt === undefined ? existing.dueAt : patch.dueAt,
      patch.remindBefore ?? existing.remindBefore,
      patch.estimate === undefined ? existing.estimate : patch.estimate,
      patch.spent === undefined ? existing.spent : patch.spent,
      patch.blockedReason === undefined ? existing.blockedReason : patch.blockedReason,
      patch.sortOrder ?? existing.sortOrder,
      now,
      startedAt,
      completedAt,
      id
    );

    return getById(id);
  };

  return {
    list(filter?: TaskFilter): Task[] {
      return listTasks(filter);
    },

    get(id: string): Task | null {
      return getById(id);
    },

    create(input: CreateTaskInput): Task {
      const id = newId();
      const now = nowIso();
      const status = input.status ?? 'todo';
      db.prepare(
        `INSERT INTO tasks (
           id, project_id, title, notes, status, priority, due_at, remind_before,
           estimate, spent, blocked_reason, sort_order, created_at, updated_at, started_at, completed_at
         ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`
      ).run(
        id,
        nullable(input.projectId),
        input.title,
        input.notes ?? '',
        status,
        input.priority ?? 'none',
        nullable(input.dueAt),
        input.remindBefore ?? 0,
        nullable(input.estimate),
        nullable(input.spent),
        nullable(input.blockedReason),
        0,
        now,
        now,
        status === 'doing' ? now : null,
        status === 'done' ? now : null
      );
      const created = getById(id);
      if (!created) throw new Error('任务创建后读取失败');
      return created;
    },

    update(id: string, patch: UpdateTaskInput): Task | null {
      return updateTask(id, patch);
    },

    setStatus(id: string, status: TaskStatus): Task | null {
      return updateTask(id, { status });
    },

    remove(id: string): boolean {
      const result = db.prepare('DELETE FROM tasks WHERE id = ?').run(id);
      return Number(result.changes) > 0;
    },

    countByStatus(): Record<TaskStatus, number> {
      const rows = db
        .prepare('SELECT status, COUNT(*) AS c FROM tasks GROUP BY status')
        .all() as Array<{ status: TaskStatus; c: number }>;
      const result = { todo: 0, doing: 0, blocked: 0, done: 0, canceled: 0 } as Record<
        TaskStatus,
        number
      >;
      for (const row of rows) result[row.status] = row.c;
      return result;
    },

    countOverdue(nowLocalDateTime: string): number {
      // 本地墙钟口径：due_at 与入参同为 YYYY-MM-DDTHH:mm 本地墙钟字符串，
      // 字典序即时间序；已完成/已取消不计入逾期
      const row = db
        .prepare(
          `SELECT COUNT(*) AS c FROM tasks
           WHERE status NOT IN ('done','canceled')
             AND due_at IS NOT NULL AND due_at < ?`
        )
        .get(nowLocalDateTime) as { c: number };
      return row.c ?? 0;
    },

    listByProject(projectId: string): Task[] {
      return listTasks({ projectId });
    }
  };
}

export type TasksRepo = ReturnType<typeof createTasksRepo>;
```

**注意**：本仓储**不提供**「按时间区间查已完成任务」的方法。因为 `completed_at` 存的是 ISO UTC 时间戳，而用户看到的周/月周期是本地日历日，两者不可直接比较，写一个参数含义含糊的方法只会埋坑。M1 没有任何调用方需要这个查询；M4 报告引擎会按 ISO UTC 边界单独实现一个语义明确的查询。

- [x] **Step 2: 写任务仓储的失败测试**

`tests/tasks.test.ts`：

```ts
import { DatabaseSync } from 'node:sqlite';
import { beforeEach, describe, expect, it } from 'vitest';
import { runMigrations } from '../electron/db';
import { createProjectsRepo } from '../electron/repositories/projects';
import { createTasksRepo } from '../electron/repositories/tasks';

let db: DatabaseSync;
let tasks: ReturnType<typeof createTasksRepo>;
let projects: ReturnType<typeof createProjectsRepo>;

beforeEach(() => {
  db = new DatabaseSync(':memory:');
  db.exec('PRAGMA foreign_keys = ON');
  runMigrations(db);
  tasks = createTasksRepo(db);
  projects = createProjectsRepo(db);
});

describe('任务仓储', () => {
  it('创建后能按 id 取回，默认状态为 todo', () => {
    const created = tasks.create({ title: '写周报' });
    expect(created.title).toBe('写周报');
    expect(created.status).toBe('todo');
    expect(created.projectId).toBeNull();
    expect(created.startedAt).toBeNull();
    expect(created.completedAt).toBeNull();
  });

  it('独立任务的项目 id 为 null', () => {
    const project = projects.create({ name: 'P' });
    const task = tasks.create({ title: '独立' });
    expect(task.projectId).toBeNull();
    expect(project.id).not.toBe(task.projectId);
  });

  it('可关联到项目', () => {
    const project = projects.create({ name: 'P' });
    const task = tasks.create({ title: '关联任务', projectId: project.id });
    expect(task.projectId).toBe(project.id);
    expect(tasks.list({ projectId: project.id })).toHaveLength(1);
  });

  it('按项目筛选与按 null 筛选互不干扰', () => {
    const project = projects.create({ name: 'P' });
    tasks.create({ title: 'A', projectId: project.id });
    tasks.create({ title: 'B' });
    expect(tasks.list({ projectId: null })).toHaveLength(1);
    expect(tasks.list({ projectId: project.id })).toHaveLength(1);
  });

  it('切到 doing 会写入 started_at', () => {
    const task = tasks.create({ title: '开始' });
    const updated = tasks.setStatus(task.id, 'doing');
    expect(updated?.status).toBe('doing');
    expect(updated?.startedAt).not.toBeNull();
  });

  it('切到 done 会写入 completed_at', () => {
    const task = tasks.create({ title: '完成' });
    const updated = tasks.setStatus(task.id, 'done');
    expect(updated?.completedAt).not.toBeNull();
  });

  it('从 done 退回 todo 会清空 completed_at', () => {
    const task = tasks.create({ title: '反悔' });
    tasks.setStatus(task.id, 'done');
    const updated = tasks.setStatus(task.id, 'todo');
    expect(updated?.completedAt).toBeNull();
  });

  it('按状态筛选', () => {
    tasks.create({ title: 'A', status: 'todo' });
    tasks.create({ title: 'B', status: 'doing' });
    expect(tasks.list({ status: ['doing'] })).toHaveLength(1);
  });

  it('includeDone 为 false 时排除已完成', () => {
    tasks.create({ title: 'A', status: 'done' });
    tasks.create({ title: 'B', status: 'todo' });
    expect(tasks.list({ includeDone: false })).toHaveLength(1);
  });

  it('搜索匹配标题与备注', () => {
    tasks.create({ title: '写周报' });
    tasks.create({ title: '别的', notes: '顺便写周报' });
    expect(tasks.list({ search: '周报' })).toHaveLength(2);
  });

  it('countByStatus 统计各状态数量', () => {
    tasks.create({ title: 'A', status: 'todo' });
    tasks.create({ title: 'B', status: 'todo' });
    tasks.create({ title: 'C', status: 'done' });
    const counts = tasks.countByStatus();
    expect(counts.todo).toBe(2);
    expect(counts.done).toBe(1);
  });

  it('countOverdue 统计过期未完成任务', () => {
    const now = new Date().toISOString();
    db.prepare(
      'INSERT INTO tasks (id, title, status, due_at, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?)'
    ).run('t1', '过期', 'todo', '2000-01-01T10:00', now, now);
    db.prepare(
      'INSERT INTO tasks (id, title, status, due_at, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?)'
    ).run('t2', '未过期', 'todo', '2099-01-01T10:00', now, now);
    db.prepare(
      'INSERT INTO tasks (id, title, status, due_at, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?)'
    ).run('t3', '已完成过期', 'done', '2000-01-01T10:00', now, now);
    expect(tasks.countOverdue('2026-11-05T12:00')).toBe(1);
  });

  it('删除任务返回 true', () => {
    const task = tasks.create({ title: '待删' });
    expect(tasks.remove(task.id)).toBe(true);
    expect(tasks.get(task.id)).toBeNull();
  });

  it('更新不存在的任务返回 null', () => {
    expect(tasks.update('missing', { title: 'x' })).toBeNull();
  });
});
```

- [x] **Step 3: 运行测试确认失败**

```bash
npx vitest run tests/tasks.test.ts
```

Expected: FAIL —— `Failed to resolve import "../electron/repositories/tasks"`。

- [x] **Step 4: 运行测试确认通过**

```bash
npx vitest run tests/tasks.test.ts
```

Expected: PASS，15 个用例全绿。

- [x] **Step 5: 提交**

```bash
git add -A
git commit -m "feat: 添加任务仓储"
```

---

### Task 6: 标签仓储

**Files:**
- Create: `electron/repositories/tags.ts`, `tests/tags.test.ts`
- Modify: 无

**Interfaces:**
- Consumes: `src/types/entities.ts` 的 `Tag`；`electron/util/id.ts`；`electron/util/time.ts`
- Produces:
  - `electron/repositories/tags.ts` 导出：
    - `function createTagsRepo(db: DatabaseSync): TagsRepo`
    - `TagsRepo` 方法：`list(): Tag[]`、`get(id: string): Tag | null`、`getByName(name: string): Tag | null`、`create(input: { name: string; color?: string | null }): Tag`、`update(id: string, patch: { name?: string; color?: string | null }): Tag | null`、`remove(id: string): boolean`
    - 任务标签关联：`setTaskTags(taskId: string, tagIds: string[]): void`、`listTaskTags(taskId: string): Tag[]`、`listTagTaskIds(tagId: string): string[]`

- [ ] **Step 1: 写标签仓储**

`electron/repositories/tags.ts`：

```ts
import type { DatabaseSync } from 'node:sqlite';
import type { Tag } from '../../src/types/entities';
import { newId } from '../util/id';
import { nullable } from '../lib/result';

type TagRow = {
  id: string;
  name: string;
  color: string | null;
};

function mapRow(row: TagRow): Tag {
  return { id: row.id, name: row.name, color: row.color };
}

export function createTagsRepo(db: DatabaseSync) {
  const selectById = db.prepare('SELECT * FROM tags WHERE id = ?');
  const getById = (id: string): Tag | null => {
    const row = selectById.get(id) as TagRow | undefined;
    return row ? mapRow(row) : null;
  };

  return {
    list(): Tag[] {
      const rows = db.prepare('SELECT * FROM tags ORDER BY name ASC').all() as TagRow[];
      return rows.map(mapRow);
    },

    get(id: string): Tag | null {
      return getById(id);
    },

    getByName(name: string): Tag | null {
      const row = db.prepare('SELECT * FROM tags WHERE name = ?').get(name) as TagRow | undefined;
      return row ? mapRow(row) : null;
    },

    create(input: { name: string; color?: string | null }): Tag {
      const id = newId();
      db.prepare('INSERT INTO tags (id, name, color) VALUES (?, ?, ?)').run(
        id,
        input.name,
        nullable(input.color)
      );
      const created = getById(id);
      if (!created) throw new Error('标签创建后读取失败');
      return created;
    },

    update(id: string, patch: { name?: string; color?: string | null }): Tag | null {
      const existing = getById(id);
      if (!existing) return null;
      db.prepare('UPDATE tags SET name = ?, color = ? WHERE id = ?').run(
        patch.name ?? existing.name,
        patch.color === undefined ? existing.color : patch.color,
        id
      );
      return getById(id);
    },

    remove(id: string): boolean {
      const result = db.prepare('DELETE FROM tags WHERE id = ?').run(id);
      return Number(result.changes) > 0;
    },

    setTaskTags(taskId: string, tagIds: string[]): void {
      db.exec('BEGIN');
      try {
        db.prepare('DELETE FROM task_tags WHERE task_id = ?').run(taskId);
        const insert = db.prepare(
          'INSERT OR IGNORE INTO task_tags (task_id, tag_id) VALUES (?, ?)'
        );
        for (const tagId of new Set(tagIds)) insert.run(taskId, tagId);
        db.exec('COMMIT');
      } catch (error) {
        db.exec('ROLLBACK');
        throw error;
      }
    },

    listTaskTags(taskId: string): Tag[] {
      const rows = db
        .prepare(
          `SELECT t.* FROM tags t
           JOIN task_tags tt ON tt.tag_id = t.id
           WHERE tt.task_id = ?
           ORDER BY t.name ASC`
        )
        .all(taskId) as TagRow[];
      return rows.map(mapRow);
    },

    listTagTaskIds(tagId: string): string[] {
      const rows = db
        .prepare('SELECT task_id FROM task_tags WHERE tag_id = ?')
        .all(tagId) as Array<{ task_id: string }>;
      return rows.map((r) => r.task_id);
    }
  };
}

export type TagsRepo = ReturnType<typeof createTagsRepo>;
```

- [ ] **Step 2: 写标签仓储的失败测试**

`tests/tags.test.ts`：

```ts
import { DatabaseSync } from 'node:sqlite';
import { beforeEach, describe, expect, it } from 'vitest';
import { runMigrations } from '../electron/db';
import { createTagsRepo } from '../electron/repositories/tags';
import { createTasksRepo } from '../electron/repositories/tasks';

let db: DatabaseSync;
let tags: ReturnType<typeof createTagsRepo>;
let tasks: ReturnType<typeof createTasksRepo>;

beforeEach(() => {
  db = new DatabaseSync(':memory:');
  db.exec('PRAGMA foreign_keys = ON');
  runMigrations(db);
  tags = createTagsRepo(db);
  tasks = createTasksRepo(db);
});

describe('标签仓储', () => {
  it('创建后能按 id 取回', () => {
    const created = tags.create({ name: '后端' });
    expect(created.name).toBe('后端');
    expect(created.color).toBeNull();
  });

  it('名称唯一，重复插入会抛错', () => {
    tags.create({ name: '前端' });
    expect(() => tags.create({ name: '前端' })).toThrow();
  });

  it('按名称查询能命中', () => {
    tags.create({ name: '设计' });
    expect(tags.getByName('设计')?.name).toBe('设计');
    expect(tags.getByName('没有')).toBeNull();
  });

  it('列表按名称排序', () => {
    tags.create({ name: 'b' });
    tags.create({ name: 'a' });
    expect(tags.list().map((t) => t.name)).toEqual(['a', 'b']);
  });

  it('给任务设置多个标签', () => {
    const task = tasks.create({ title: '任务' });
    const a = tags.create({ name: 'a' });
    const b = tags.create({ name: 'b' });
    tags.setTaskTags(task.id, [a.id, b.id]);
    expect(tags.listTaskTags(task.id).map((t) => t.name)).toEqual(['a', 'b']);
  });

  it('重复设置同一标签不会产生重复关联', () => {
    const task = tasks.create({ title: '任务' });
    const a = tags.create({ name: 'a' });
    tags.setTaskTags(task.id, [a.id, a.id]);
    expect(tags.listTaskTags(task.id)).toHaveLength(1);
  });

  it('重新设置标签会替换旧关联', () => {
    const task = tasks.create({ title: '任务' });
    const a = tags.create({ name: 'a' });
    const b = tags.create({ name: 'b' });
    tags.setTaskTags(task.id, [a.id]);
    tags.setTaskTags(task.id, [b.id]);
    expect(tags.listTaskTags(task.id).map((t) => t.name)).toEqual(['b']);
  });

  it('清空标签关联后列表为空', () => {
    const task = tasks.create({ title: '任务' });
    const a = tags.create({ name: 'a' });
    tags.setTaskTags(task.id, [a.id]);
    tags.setTaskTags(task.id, []);
    expect(tags.listTaskTags(task.id)).toHaveLength(0);
  });

  it('删除标签会解除任务关联但不删任务', () => {
    const task = tasks.create({ title: '任务' });
    const a = tags.create({ name: 'a' });
    tags.setTaskTags(task.id, [a.id]);
    expect(tags.remove(a.id)).toBe(true);
    expect(tags.listTaskTags(task.id)).toHaveLength(0);
    expect(tasks.get(task.id)).not.toBeNull();
  });

  it('listTagTaskIds 返回关联的任务 id', () => {
    const task = tasks.create({ title: '任务' });
    const a = tags.create({ name: 'a' });
    tags.setTaskTags(task.id, [a.id]);
    expect(tags.listTagTaskIds(a.id)).toEqual([task.id]);
  });
});
```

- [ ] **Step 3: 运行测试确认失败**

```bash
npx vitest run tests/tags.test.ts
```

Expected: FAIL —— `Failed to resolve import "../electron/repositories/tags"`。

- [ ] **Step 4: 运行测试确认通过**

```bash
npx vitest run tests/tags.test.ts
```

Expected: PASS，10 个用例全绿。

- [ ] **Step 5: 提交**

```bash
git add -A
git commit -m "feat: 添加标签仓储与任务标签关联"
```

---

### Task 7: 设置仓储

**Files:**
- Create: `electron/repositories/settings.ts`, `tests/settings.test.ts`
- Modify: 无

**Interfaces:**
- Consumes: `node:sqlite` 的 `DatabaseSync`
- Produces:
  - `electron/repositories/settings.ts` 导出 `createSettingsRepo(db: DatabaseSync): SettingsRepo`
  - `SettingsRepo` 方法：
    - `get<T>(key: string): T | null`
    - `set(key: string, value: unknown): void`
    - `remove(key: string): boolean`
    - `getAll(): Record<string, unknown>`
    - `getString(key: string, fallback: string): string`
    - `getBoolean(key: string, fallback: boolean): boolean`

- [ ] **Step 1: 写设置仓储**

`electron/repositories/settings.ts`：

```ts
import type { DatabaseSync } from 'node:sqlite';
import { nowIso } from '../util/time';

export function createSettingsRepo(db: DatabaseSync) {
  const selectOne = db.prepare('SELECT value FROM settings WHERE key = ?');
  const selectAll = db.prepare('SELECT key, value FROM settings');
  const upsert = db.prepare(
    `INSERT INTO settings (key, value) VALUES (?, ?)
     ON CONFLICT(key) DO UPDATE SET value = excluded.value`
  );
  const readValue = (key: string): unknown => {
    const row = selectOne.get(key) as { value: string } | undefined;
    if (!row) return null;
    try {
      return JSON.parse(row.value) as unknown;
    } catch {
      return null;
    }
  };

  return {
    get<T>(key: string): T | null {
      return readValue(key) as T | null;
    },

    set(key: string, value: unknown): void {
      upsert.run(key, JSON.stringify(value));
    },

    remove(key: string): boolean {
      const result = db.prepare('DELETE FROM settings WHERE key = ?').run(key);
      return Number(result.changes) > 0;
    },

    getAll(): Record<string, unknown> {
      const rows = selectAll.all() as Array<{ key: string; value: string }>;
      const result: Record<string, unknown> = {};
      for (const row of rows) {
        try {
          result[row.key] = JSON.parse(row.value);
        } catch {
          // 损坏的值直接跳过，不影响其他设置
        }
      }
      return result;
    },

    getString(key: string, fallback: string): string {
      const value = readValue(key);
      return typeof value === 'string' ? value : fallback;
    },

    getBoolean(key: string, fallback: boolean): boolean {
      const value = readValue(key);
      return typeof value === 'boolean' ? value : fallback;
    }
  };
}

export type SettingsRepo = ReturnType<typeof createSettingsRepo>;
```

- [ ] **Step 2: 写设置仓储的失败测试**

`tests/settings.test.ts`：

```ts
import { DatabaseSync } from 'node:sqlite';
import { beforeEach, describe, expect, it } from 'vitest';
import { runMigrations } from '../electron/db';
import { createSettingsRepo } from '../electron/repositories/settings';

let db: DatabaseSync;
let settings: ReturnType<typeof createSettingsRepo>;

beforeEach(() => {
  db = new DatabaseSync(':memory:');
  db.exec('PRAGMA foreign_keys = ON');
  runMigrations(db);
  settings = createSettingsRepo(db);
});

describe('设置仓储', () => {
  it('未设置的键返回 null', () => {
    expect(settings.get('missing')).toBeNull();
  });

  it('可以写入并读回对象', () => {
    settings.set('daily_review', { enabled: true, time: '09:00' });
    expect(settings.get('daily_review')).toEqual({ enabled: true, time: '09:00' });
  });

  it('可以写入并读回布尔值', () => {
    settings.set('close_to_tray', false);
    expect(settings.get<boolean>('close_to_tray')).toBe(false);
  });

  it('重复写入覆盖旧值', () => {
    settings.set('theme', 'light');
    settings.set('theme', 'dark');
    expect(settings.get('theme')).toBe('dark');
  });

  it('getAll 返回全部设置', () => {
    settings.set('a', 1);
    settings.set('b', 'two');
    expect(settings.getAll()).toEqual({ a: 1, b: 'two' });
  });

  it('损坏的 JSON 值被当作不存在', () => {
    db.prepare('INSERT INTO settings (key, value) VALUES (?, ?)').run('broken', '{oops');
    expect(settings.get('broken')).toBeNull();
  });

  it('getString 在类型不符时返回兜底值', () => {
    settings.set('theme', 123);
    expect(settings.getString('theme', 'system')).toBe('system');
  });

  it('getBoolean 在类型不符时返回兜底值', () => {
    settings.set('close_to_tray', 'yes');
    expect(settings.getBoolean('close_to_tray', true)).toBe(true);
  });

  it('remove 能删除设置', () => {
    settings.set('x', 1);
    expect(settings.remove('x')).toBe(true);
    expect(settings.get('x')).toBeNull();
  });
});
```

- [ ] **Step 3: 运行测试确认失败**

```bash
npx vitest run tests/settings.test.ts
```

Expected: FAIL —— `Failed to resolve import "../electron/repositories/settings"`。

- [ ] **Step 4: 运行测试确认通过**

```bash
npx vitest run tests/settings.test.ts
```

Expected: PASS，9 个用例全绿。

- [ ] **Step 5: 提交**

```bash
git add -A
git commit -m "feat: 添加设置仓储"
```

---

### Task 8: IPC 路由与 preload 桥接

**Files:**
- Create: `electron/ipc.ts`
- Modify: `electron/preload.ts`（替换占位实现）, `src/types/api.ts`（新建）, `src/types/env.d.ts`（重写）, `src/lib/api.ts`（新建）, `electron/main.ts`（注册 IPC）

**Interfaces:**
- Consumes: Task 4–7 的全部仓储；`electron/lib/result.ts` 的 `Result` / `ok` / `fail`
- Produces:
  - `src/types/api.ts` 导出 `WorkStationApi` 接口与 `Result<T>` 类型（渲染端复用自己的副本）
  - `src/lib/api.ts` 导出 `ApiError` 类与 `call<T>(fn: () => Promise<Result<T>>): Promise<T>`
  - `window.api` 的完整形状（下方定义，后续所有 UI 任务依赖）

`window.api` 契约：

```ts
interface WorkStationApi {
  projects: {
    list(options?: { includeArchived?: boolean }): Promise<Result<Project[]>>;
    get(id: string): Promise<Result<Project | null>>;
    create(input: CreateProjectInput): Promise<Result<Project>>;
    update(id: string, patch: UpdateProjectInput): Promise<Result<Project | null>>;
    archive(id: string): Promise<Result<boolean>>;
    unarchive(id: string): Promise<Result<boolean>>;
    remove(id: string): Promise<Result<boolean>>;
    counts(projectId: string): Promise<Result<TaskCounts>>;
  };
  tasks: {
    list(filter?: TaskFilter): Promise<Result<Task[]>>;
    get(id: string): Promise<Result<Task | null>>;
    create(input: CreateTaskInput): Promise<Result<Task>>;
    update(id: string, patch: UpdateTaskInput): Promise<Result<Task | null>>;
    setStatus(id: string, status: TaskStatus): Promise<Result<Task | null>>;
    remove(id: string): Promise<Result<boolean>>;
    countsByStatus(): Promise<Result<Record<TaskStatus, number>>>;
    overdueCount(nowLocalDateTime: string): Promise<Result<number>>;
  };
  tags: {
    list(): Promise<Result<Tag[]>>;
    create(input: { name: string; color?: string | null }): Promise<Result<Tag>>;
    update(id: string, patch: { name?: string; color?: string | null }): Promise<Result<Tag | null>>;
    remove(id: string): Promise<Result<boolean>>;
    setTaskTags(taskId: string, tagIds: string[]): Promise<Result<void>>;
    listTaskTags(taskId: string): Promise<Result<Tag[]>>;
  };
  settings: {
    get<T>(key: string): Promise<Result<T | null>>;
    set(key: string, value: unknown): Promise<Result<void>>;
    getAll(): Promise<Result<Record<string, unknown>>>;
  };
  app: {
    info(): Promise<Result<{ version: string; dbPath: string; userDataPath: string }>>;
  };
}
```

其中 `TaskCounts` 定义为 `{ total: number; done: number; doing: number; overdue: number }`。

- [ ] **Step 1: 定义渲染端类型**

`src/types/api.ts`：

```ts
import type {
  Project,
  ProjectStatus,
  Priority,
  ProgressMode,
  Tag,
  Task,
  TaskStatus
} from './entities';

export type Result<T> =
  | { ok: true; data: T }
  | { ok: false; error: { code: string; message: string } };

export interface TaskCounts {
  total: number;
  done: number;
  doing: number;
  overdue: number;
}

export interface CreateProjectInput {
  name: string;
  description?: string;
  status?: ProjectStatus;
  priority?: Priority;
  color?: string | null;
  progressMode?: ProgressMode;
  progressManual?: number | null;
  startDate?: string | null;
  dueDate?: string | null;
}

export interface UpdateProjectInput {
  name?: string;
  description?: string;
  status?: ProjectStatus;
  priority?: Priority;
  color?: string | null;
  progressMode?: ProgressMode;
  progressManual?: number | null;
  startDate?: string | null;
  dueDate?: string | null;
  sortOrder?: number;
  archivedAt?: string | null;
}

export interface CreateTaskInput {
  title: string;
  projectId?: string | null;
  notes?: string;
  status?: TaskStatus;
  priority?: Priority;
  dueAt?: string | null;
  remindBefore?: number;
  estimate?: number | null;
  spent?: number | null;
  blockedReason?: string | null;
}

export interface UpdateTaskInput {
  title?: string;
  projectId?: string | null;
  notes?: string;
  status?: TaskStatus;
  priority?: Priority;
  dueAt?: string | null;
  remindBefore?: number;
  estimate?: number | null;
  spent?: number | null;
  blockedReason?: string | null;
  sortOrder?: number;
}

export interface TaskFilter {
  projectId?: string | null;
  status?: TaskStatus[];
  priority?: Priority[];
  tagIds?: string[];
  dueBefore?: string;
  dueAfter?: string;
  includeDone?: boolean;
  search?: string;
}

export interface WorkStationApi {
  projects: {
    list(options?: { includeArchived?: boolean }): Promise<Result<Project[]>>;
    get(id: string): Promise<Result<Project | null>>;
    create(input: CreateProjectInput): Promise<Result<Project>>;
    update(id: string, patch: UpdateProjectInput): Promise<Result<Project | null>>;
    archive(id: string): Promise<Result<boolean>>;
    unarchive(id: string): Promise<Result<boolean>>;
    remove(id: string): Promise<Result<boolean>>;
    counts(projectId: string): Promise<Result<TaskCounts>>;
  };
  tasks: {
    list(filter?: TaskFilter): Promise<Result<Task[]>>;
    get(id: string): Promise<Result<Task | null>>;
    create(input: CreateTaskInput): Promise<Result<Task>>;
    update(id: string, patch: UpdateTaskInput): Promise<Result<Task | null>>;
    setStatus(id: string, status: TaskStatus): Promise<Result<Task | null>>;
    remove(id: string): Promise<Result<boolean>>;
    countsByStatus(): Promise<Result<Record<TaskStatus, number>>>;
    overdueCount(nowLocalDateTime: string): Promise<Result<number>>;
  };
  tags: {
    list(): Promise<Result<Tag[]>>;
    create(input: { name: string; color?: string | null }): Promise<Result<Tag>>;
    update(id: string, patch: { name?: string; color?: string | null }): Promise<Result<Tag | null>>;
    remove(id: string): Promise<Result<boolean>>;
    setTaskTags(taskId: string, tagIds: string[]): Promise<Result<void>>;
    listTaskTags(taskId: string): Promise<Result<Tag[]>>;
  };
  settings: {
    get<T>(key: string): Promise<Result<T | null>>;
    set(key: string, value: unknown): Promise<Result<void>>;
    getAll(): Promise<Result<Record<string, unknown>>>;
  };
  app: {
    info(): Promise<Result<{ version: string; dbPath: string; userDataPath: string }>>;
  };
}
```

- [ ] **Step 2: 写 IPC 路由**

`electron/ipc.ts`：

```ts
import { ipcMain, app } from 'electron';
import type { DatabaseSync } from 'node:sqlite';
import { fail, ok, type Result } from './lib/result';
import { createProjectsRepo } from './repositories/projects';
import { createTasksRepo } from './repositories/tasks';
import { createTagsRepo } from './repositories/tags';
import { createSettingsRepo } from './repositories/settings';

function handle<TArgs extends unknown[], TResult>(
  channel: string,
  fn: (...args: TArgs) => TResult
): void {
  ipcMain.handle(channel, async (_event, ...args: TArgs): Promise<Result<TResult>> => {
    try {
      return ok(fn(...args));
    } catch (error) {
      const message = (error as Error).message ?? '未知错误';
      console.error(`[ipc] ${channel} 失败:`, error);
      return fail('HANDLER_FAILED', `${channel} 执行失败：${message}`);
    }
  });
}

export function registerIpc(db: DatabaseSync, dbPath: string): void {
  const projects = createProjectsRepo(db);
  const tasks = createTasksRepo(db);
  const tags = createTagsRepo(db);
  const settings = createSettingsRepo(db);

  handle('projects:list', () => projects.list({ includeArchived: false }));
  handle('projects:get', (id: string) => projects.get(id));
  handle('projects:create', (input: CreateProjectInput) => projects.create(input));
  handle('projects:update', (id: string, patch: UpdateProjectInput) => projects.update(id, patch));
  handle('projects:archive', (id: string) => projects.archive(id));
  handle('projects:unarchive', (id: string) => projects.unarchive(id));
  handle('projects:remove', (id: string) => projects.remove(id));
  handle('projects:counts', (id: string) => projects.countTasks(id));

  handle('tasks:list', (filter?: TaskFilter) => tasks.list(filter));
  handle('tasks:get', (id: string) => tasks.get(id));
  handle('tasks:create', (input: CreateTaskInput) => tasks.create(input));
  handle('tasks:update', (id: string, patch: UpdateTaskInput) => tasks.update(id, patch));
  handle('tasks:setStatus', (id: string, status: TaskStatus) => tasks.setStatus(id, status));
  handle('tasks:remove', (id: string) => tasks.remove(id));
  handle('tasks:countsByStatus', () => tasks.countByStatus());
  handle('tasks:overdueCount', (nowLocalDateTime: string) => tasks.countOverdue(nowLocalDateTime));

  handle('tags:list', () => tags.list());
  handle('tags:create', (input: { name: string; color?: string | null }) => tags.create(input));
  handle('tags:update', (id: string, patch: { name?: string; color?: string | null }) =>
    tags.update(id, patch)
  );
  handle('tags:remove', (id: string) => tags.remove(id));
  handle('tags:setTaskTags', (taskId: string, tagIds: string[]) => {
    tags.setTaskTags(taskId, tagIds);
  });
  handle('tags:listTaskTags', (taskId: string) => tags.listTaskTags(taskId));

  handle('settings:get', (key: string) => settings.get(key));
  handle('settings:set', (key: string, value: unknown) => {
    settings.set(key, value);
  });
  handle('settings:getAll', () => settings.getAll());

  handle('app:info', () => ({
    version: app.getVersion(),
    dbPath,
    userDataPath: app.getPath('userData')
  }));
}
```

并在文件顶部补齐类型导入：

```ts
import type {
  CreateProjectInput,
  CreateTaskInput,
  TaskFilter,
  UpdateProjectInput,
  UpdateTaskInput
} from '../src/types/api';
import type { TaskStatus } from '../src/types/entities';
```

- [ ] **Step 3: 重写 preload**

`electron/preload.ts`：

```ts
import { contextBridge, ipcRenderer } from 'electron';
import type {
  CreateProjectInput,
  CreateTaskInput,
  TaskFilter,
  UpdateProjectInput,
  UpdateTaskInput,
  WorkStationApi
} from '../src/types/api';
import type { TaskStatus } from '../src/types/entities';

const api: WorkStationApi = {
  projects: {
    list: (options) => ipcRenderer.invoke('projects:list', options),
    get: (id) => ipcRenderer.invoke('projects:get', id),
    create: (input: CreateProjectInput) => ipcRenderer.invoke('projects:create', input),
    update: (id: string, patch: UpdateProjectInput) =>
      ipcRenderer.invoke('projects:update', id, patch),
    archive: (id) => ipcRenderer.invoke('projects:archive', id),
    unarchive: (id) => ipcRenderer.invoke('projects:unarchive', id),
    remove: (id) => ipcRenderer.invoke('projects:remove', id),
    counts: (id) => ipcRenderer.invoke('projects:counts', id)
  },
  tasks: {
    list: (filter?: TaskFilter) => ipcRenderer.invoke('tasks:list', filter),
    get: (id) => ipcRenderer.invoke('tasks:get', id),
    create: (input: CreateTaskInput) => ipcRenderer.invoke('tasks:create', input),
    update: (id: string, patch: UpdateTaskInput) =>
      ipcRenderer.invoke('tasks:update', id, patch),
    setStatus: (id: string, status: TaskStatus) =>
      ipcRenderer.invoke('tasks:setStatus', id, status),
    remove: (id) => ipcRenderer.invoke('tasks:remove', id),
    countsByStatus: () => ipcRenderer.invoke('tasks:countsByStatus'),
    overdueCount: (nowLocalDateTime: string) =>
      ipcRenderer.invoke('tasks:overdueCount', nowLocalDateTime)
  },
  tags: {
    list: () => ipcRenderer.invoke('tags:list'),
    create: (input) => ipcRenderer.invoke('tags:create', input),
    update: (id, patch) => ipcRenderer.invoke('tags:update', id, patch),
    remove: (id) => ipcRenderer.invoke('tags:remove', id),
    setTaskTags: (taskId: string, tagIds: string[]) =>
      ipcRenderer.invoke('tags:setTaskTags', taskId, tagIds),
    listTaskTags: (taskId: string) => ipcRenderer.invoke('tags:listTaskTags', taskId)
  },
  settings: {
    get: <T>(key: string) => ipcRenderer.invoke('settings:get', key),
    set: (key: string, value: unknown) => ipcRenderer.invoke('settings:set', key, value),
    getAll: () => ipcRenderer.invoke('settings:getAll')
  },
  app: {
    info: () => ipcRenderer.invoke('app:info')
  }
};

contextBridge.exposeInMainWorld('api', api);
```

- [ ] **Step 4: 重写全局类型声明**

`src/types/env.d.ts`：

```ts
/// <reference types="vite/client" />

import type { WorkStationApi } from './api';

declare global {
  interface Window {
    api: WorkStationApi;
  }
}

export {};
```

- [ ] **Step 5: 写渲染端 API 客户端**

`src/lib/api.ts`：

```ts
import type { Result } from '@/types/api';

export class ApiError extends Error {
  constructor(
    readonly code: string,
    message: string
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

export async function call<T>(fn: () => Promise<Result<T>>): Promise<T> {
  const result = await fn();
  if (!result.ok) {
    throw new ApiError(result.error.code, result.error.message);
  }
  return result.data;
}
```

- [ ] **Step 6: 主进程注册 IPC**

改 `electron/main.ts`，在创建窗口前注册：

```ts
import { registerIpc } from './ipc';
// ...
const dbPath = app.isPackaged
  ? join(app.getPath('userData'), 'data.db')
  : join(__dirname, '..', '.data', 'data.db');
const db = openDatabase(dbPath);
runMigrations(db);
registerIpc(db, dbPath);
```

- [ ] **Step 7: 类型检查**

```bash
npm run typecheck
```

Expected: 退出码 0，无类型错误。若 `electron/ipc.ts` 报类型不匹配，检查该文件顶部的类型导入是否包含 `CreateProjectInput`、`CreateTaskInput`、`TaskFilter`、`UpdateProjectInput`、`UpdateTaskInput` 与 `TaskStatus`。

- [ ] **Step 8: 冒烟验证**

```bash
npm run dev
```

Expected: 窗口正常弹出，无 `contextBridge` 或 `ipcRenderer` 相关报错。

- [ ] **Step 9: 提交**

```bash
git add -A
git commit -m "feat: 添加类型化 IPC 路由与 preload 桥接"
```

---

### Task 9: 渲染进程基础 —— API 客户端、主题与布局骨架

**Files:**
- Create: `src/hooks/useAsync.ts`, `src/store/ui.ts`, `src/styles/global.css`, `src/features/settings/SettingsPage.tsx`
- Modify: `src/App.tsx`, `src/main.tsx`

**Interfaces:**
- Consumes: `src/lib/api.ts` 的 `call` / `ApiError`；`src/types/api.ts` 的 `WorkStationApi`
- Produces:
  - `src/hooks/useAsync.ts` 导出 `useAsync<T>(fn: () => Promise<T>, deps: unknown[]): { data: T | null; loading: boolean; error: string | null; reload: () => void }`
  - `src/store/ui.ts` 导出 `useUiStore`（Zustand），状态：`theme: 'light' | 'dark' | 'system'`、`page: 'projects' | 'tasks' | 'settings'`、`toast: { id: number; message: string; tone: 'info' | 'error' } | null`、`setTheme`、`setPage`、`showToast`、`dismissToast`、`applyTheme(theme)`
  - `src/features/settings/SettingsPage.tsx` 导出 `SettingsPage`

- [ ] **Step 1: 写 useAsync hook**

`src/hooks/useAsync.ts`：

```ts
import { useCallback, useEffect, useState, type DependencyList } from 'react';

export function useAsync<T>(
  fn: () => Promise<T>,
  deps: DependencyList
): { data: T | null; loading: boolean; error: string | null; reload: () => void } {
  const [data, setData] = useState<T | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [nonce, setNonce] = useState(0);

  const reload = useCallback(() => setNonce((n) => n + 1), []);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);

    fn()
      .then((value) => {
        if (!cancelled) {
          setData(value);
          setLoading(false);
        }
      })
      .catch((err: unknown) => {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : String(err));
          setLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, nonce]);

  return { data, loading, error, reload };
}
```

- [ ] **Step 2: 写 UI store**

`src/store/ui.ts`：

```ts
import { create } from 'zustand';

export type Theme = 'light' | 'dark' | 'system';
export type Page = 'projects' | 'tasks' | 'settings';

interface Toast {
  id: number;
  message: string;
  tone: 'info' | 'error';
}

interface UiState {
  theme: Theme;
  page: Page;
  toast: Toast | null;
  setTheme: (theme: Theme) => void;
  setPage: (page: Page) => void;
  showToast: (message: string, tone?: 'info' | 'error') => void;
  dismissToast: () => void;
  applyTheme: (theme: Theme) => void;
}

function resolveTheme(theme: Theme): 'light' | 'dark' {
  if (theme !== 'system') return theme;
  const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
  return prefersDark ? 'dark' : 'light';
}

export const useUiStore = create<UiState>((set, get) => ({
  theme: 'system',
  page: 'projects',
  toast: null,
  setTheme: (theme) => {
    set({ theme });
    get().applyTheme(theme);
  },
  setPage: (page) => set({ page }),
  showToast: (message, tone = 'info') =>
    set({ toast: { id: Date.now(), message, tone } }),
  dismissToast: () => set({ toast: null }),
  applyTheme: (theme) => {
    document.documentElement.dataset.theme = resolveTheme(theme);
  }
}));
```

- [ ] **Step 3: 写全局样式**

`src/styles/global.css`：

```css
:root {
  --bg: #f6f7f9;
  --bg-panel: #ffffff;
  --bg-hover: #eef0f4;
  --border: #e2e5ea;
  --text: #1c1f24;
  --text-dim: #6b7280;
  --accent: #3b6ef5;
  --accent-text: #ffffff;
  --danger: #d94b4b;
  --warning: #d98a1f;
  --success: #2f9e6e;
  --radius: 8px;
  --shadow: 0 1px 3px rgba(16, 24, 40, 0.08);
  color-scheme: light;
}

:root[data-theme='dark'] {
  --bg: #14161a;
  --bg-panel: #1c1f25;
  --bg-hover: #262a31;
  --border: #2e333b;
  --text: #e6e8eb;
  --text-dim: #9aa3af;
  --accent: #5b86ff;
  --accent-text: #0d1014;
  --danger: #ef6b6b;
  --warning: #e0a04a;
  --success: #48bb87;
  --shadow: 0 1px 3px rgba(0, 0, 0, 0.4);
  color-scheme: dark;
}

* {
  box-sizing: border-box;
}

html,
body,
#root {
  height: 100%;
  margin: 0;
}

body {
  background: var(--bg);
  color: var(--text);
  font-family: 'Microsoft YaHei UI', 'Segoe UI', system-ui, sans-serif;
  font-size: 14px;
  line-height: 1.6;
}

button {
  font: inherit;
  color: inherit;
  cursor: pointer;
}

input,
textarea,
select {
  font: inherit;
  color: inherit;
  background: var(--bg-panel);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  padding: 6px 10px;
}

input:focus,
textarea:focus,
select:focus {
  outline: 2px solid var(--accent);
  outline-offset: -1px;
}
```

- [ ] **Step 4: 写设置页**

`src/features/settings/SettingsPage.tsx`：

```tsx
import { useEffect } from 'react';
import { call } from '@/lib/api';
import { useUiStore, type Theme } from '@/store/ui';

const THEMES: Array<{ value: Theme; label: string }> = [
  { value: 'light', label: '浅色' },
  { value: 'dark', label: '深色' },
  { value: 'system', label: '跟随系统' }
];

export function SettingsPage(): React.ReactElement {
  const theme = useUiStore((s) => s.theme);
  const setTheme = useUiStore((s) => s.setTheme);
  const showToast = useUiStore((s) => s.showToast);

  useEffect(() => {
    void call<Theme | null>(() => window.api.settings.get<Theme>('theme'))
      .then((saved) => {
        if (saved) setTheme(saved);
      })
      .catch(() => {
        // 读不到设置就用默认值，不打扰用户
      });
  }, [setTheme]);

  const handleThemeChange = async (next: Theme): Promise<void> => {
    setTheme(next);
    try {
      await call<void>(() => window.api.settings.set('theme', next));
    } catch (error) {
      showToast(error instanceof Error ? error.message : '保存主题失败', 'error');
    }
  };

  return (
    <section className="settings-page">
      <h2>设置</h2>
      <div className="settings-row">
        <label htmlFor="theme-select">主题</label>
        <select
          id="theme-select"
          value={theme}
          onChange={(event) => void handleThemeChange(event.target.value as Theme)}
        >
          {THEMES.map((item) => (
            <option key={item.value} value={item.value}>
              {item.label}
            </option>
          ))}
        </select>
      </div>
    </section>
  );
}
```

- [ ] **Step 5: 写布局骨架**

`src/App.tsx`：

```tsx
import { useEffect } from 'react';
import { SettingsPage } from '@/features/settings/SettingsPage';
import { useUiStore, type Page } from '@/store/ui';

const NAV: Array<{ page: Page; label: string }> = [
  { page: 'projects', label: '项目' },
  { page: 'tasks', label: '任务' },
  { page: 'settings', label: '设置' }
];

export default function App(): React.ReactElement {
  const page = useUiStore((s) => s.page);
  const setPage = useUiStore((s) => s.setPage);
  const applyTheme = useUiStore((s) => s.applyTheme);
  const theme = useUiStore((s) => s.theme);
  const toast = useUiStore((s) => s.toast);
  const dismissToast = useUiStore((s) => s.dismissToast);

  useEffect(() => {
    applyTheme(theme);
  }, [applyTheme, theme]);

  useEffect(() => {
    if (!toast) return;
    const timer = window.setTimeout(dismissToast, 3000);
    return () => window.clearTimeout(timer);
  }, [toast, dismissToast]);

  return (
    <div className="app-shell">
      <nav className="app-nav">
        <div className="app-brand">工作台</div>
        {NAV.map((item) => (
          <button
            key={item.page}
            type="button"
            className={item.page === page ? 'nav-item nav-item-active' : 'nav-item'}
            onClick={() => setPage(item.page)}
          >
            {item.label}
          </button>
        ))}
      </nav>
      <main className="app-main">
        {page === 'projects' && <div className="placeholder">项目页（Task 11 实现）</div>}
        {page === 'tasks' && <div className="placeholder">任务页（Task 12 实现）</div>}
        {page === 'settings' && <SettingsPage />}
      </main>
      {toast && (
        <div className={toast.tone === 'error' ? 'toast toast-error' : 'toast'}>
          <span>{toast.message}</span>
          <button type="button" onClick={dismissToast}>
            ×
          </button>
        </div>
      )}
    </div>
  );
}
```

在 `src/styles/global.css` 末尾追加布局样式：

```css
.app-shell {
  display: grid;
  grid-template-columns: 200px 1fr;
  height: 100%;
}

.app-nav {
  background: var(--bg-panel);
  border-right: 1px solid var(--border);
  padding: 16px 10px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.app-brand {
  font-weight: 600;
  font-size: 15px;
  padding: 4px 10px 14px;
}

.nav-item {
  background: transparent;
  border: none;
  text-align: left;
  padding: 8px 10px;
  border-radius: var(--radius);
  color: var(--text-dim);
}

.nav-item:hover {
  background: var(--bg-hover);
  color: var(--text);
}

.nav-item-active {
  background: var(--accent);
  color: var(--accent-text);
}

.app-main {
  overflow: auto;
  padding: 24px 28px;
}

.placeholder {
  color: var(--text-dim);
}

.toast {
  position: fixed;
  right: 20px;
  bottom: 20px;
  display: flex;
  gap: 12px;
  align-items: center;
  background: var(--bg-panel);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  box-shadow: var(--shadow);
  padding: 10px 14px;
}

.toast-error {
  border-color: var(--danger);
  color: var(--danger);
}

.settings-row {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 16px;
}
```

- [ ] **Step 6: 主入口挂载样式**

`src/main.tsx`：

```tsx
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import App from './App';
import './styles/global.css';

const container = document.getElementById('root');
if (!container) throw new Error('找不到 #root 挂载点');

createRoot(container).render(
  <StrictMode>
    <App />
  </StrictMode>
);
```

- [ ] **Step 7: 类型检查与冒烟**

```bash
npm run typecheck && npm run dev
```

Expected: 类型检查通过；窗口显示左侧「工作台」导航（项目/任务/设置）。点「设置」→ 切主题为深色 → 窗口立刻变深色。关闭进程。

- [ ] **Step 6: 提交**

```bash
git add -A
git commit -m "feat: 添加渲染进程 API 客户端、主题系统与布局骨架"
```

---

### Task 10: 通用 UI 组件集

**Files:**
- Create: `src/components/Drawer.tsx`, `src/components/Modal.tsx`, `src/components/Badge.tsx`, `src/components/ProgressRing.tsx`, `src/components/EmptyState.tsx`, `src/components/Field.tsx`
- Modify: `src/styles/global.css`（追加组件样式）
- Test: `tests/format.test.ts`（纯计算部分，组件本身不测）

**Interfaces:**
- Consumes: 无外部依赖，仅 React
- Produces:
  - `Drawer`：`{ open: boolean; title: string; onClose: () => void; children: React.ReactNode; footer?: React.ReactNode }`
  - `Modal`：`{ open: boolean; title: string; onClose: () => void; children: React.ReactNode; footer?: React.ReactNode }`
  - `Badge`：`{ tone?: 'default' | 'accent' | 'danger' | 'warning' | 'success'; children: React.ReactNode }`
  - `ProgressRing`：`{ value: number; size?: number; stroke?: number; label?: string }`
  - `EmptyState`：`{ title: string; hint?: string; action?: React.ReactNode }`
  - `Field`：`{ label: string; children: React.ReactNode; hint?: string }`
  - `src/lib/format.ts` 导出 `formatPercent(value: number): string`、`formatHours(value: number | null): string`、`formatLocalDateTime(value: string | null): string`、`isOverdue(dueAt: string | null, nowLocalDateTime: string): boolean`

- [ ] **Step 1: 写格式化工具**

`src/lib/format.ts`：

```ts
export function formatPercent(value: number): string {
  const clamped = Math.max(0, Math.min(100, Math.round(value)));
  return `${clamped}%`;
}

export function formatHours(value: number | null): string {
  if (value === null) return '—';
  return `${Number(value.toFixed(1))}h`;
}

export function formatLocalDateTime(value: string | null): string {
  if (!value) return '—';
  if (value.length === 16) return value.slice(5).replace('T', ' ');
  if (value.length === 10) return value.slice(5);
  return value;
}

export function isOverdue(dueAt: string | null, nowLocalDateTime: string): boolean {
  if (!dueAt) return false;
  return dueAt < nowLocalDateTime;
}
```

- [ ] **Step 2: 写格式化工具测试**

`tests/format.test.ts`：

```ts
import { describe, expect, it } from 'vitest';
import { formatHours, formatLocalDateTime, formatPercent, isOverdue } from '../src/lib/format';

describe('格式化工具', () => {
  it('formatPercent 四舍五入并夹在 0-100', () => {
    expect(formatPercent(33.4)).toBe('33%');
    expect(formatPercent(33.5)).toBe('34%');
    expect(formatPercent(-10)).toBe('0%');
    expect(formatPercent(150)).toBe('100%');
  });

  it('formatHours 对 null 返回破折号', () => {
    expect(formatHours(null)).toBe('—');
  });

  it('formatHours 保留一位小数', () => {
    expect(formatHours(2)).toBe('2h');
    expect(formatHours(2.55)).toBe('2.6h');
  });

  it('formatLocalDateTime 截取月日与时分', () => {
    expect(formatLocalDateTime('2026-11-05T14:30')).toBe('11-05 14:30');
    expect(formatLocalDateTime('2026-11-05')).toBe('11-05');
    expect(formatLocalDateTime(null)).toBe('—');
  });

  it('isOverdue 按本地墙钟字符串比较', () => {
    expect(isOverdue('2026-11-05T09:00', '2026-11-05T10:00')).toBe(true);
    expect(isOverdue('2026-11-05T11:00', '2026-11-05T10:00')).toBe(false);
    expect(isOverdue(null, '2026-11-05T10:00')).toBe(false);
  });
});
```

- [ ] **Step 3: 运行测试确认通过**

```bash
npx vitest run tests/format.test.ts
```

Expected: PASS，5 个用例全绿（`format.ts` 是纯函数，实现已在 Step 1 给出，失败请检查拼写）。

- [ ] **Step 4: 写组件**

`src/components/Badge.tsx`：

```tsx
import type { ReactNode } from 'react';

interface BadgeProps {
  tone?: 'default' | 'accent' | 'danger' | 'warning' | 'success';
  children: ReactNode;
}

export function Badge({ tone = 'default', children }: BadgeProps): React.ReactElement {
  return <span className={`badge badge-${tone}`}>{children}</span>;
}
```

`src/components/ProgressRing.tsx`：

```tsx
interface ProgressRingProps {
  value: number;
  size?: number;
  stroke?: number;
  label?: string;
}

export function ProgressRing({
  value,
  size = 44,
  stroke = 5,
  label
}: ProgressRingProps): React.ReactElement {
  const clamped = Math.max(0, Math.min(100, value));
  const radius = (size - stroke) / 2;
  const circumference = 2 * Math.PI * radius;
  const offset = circumference * (1 - clamped / 100);

  return (
    <div className="progress-ring" style={{ width: size, height: size }}>
      <svg width={size} height={size} role="img" aria-label={label ?? `进度 ${clamped}%`}>
        <circle
          className="progress-ring-track"
          cx={size / 2}
          cy={size / 2}
          r={radius}
          strokeWidth={stroke}
          fill="none"
        />
        <circle
          className="progress-ring-value"
          cx={size / 2}
          cy={size / 2}
          r={radius}
          strokeWidth={stroke}
          fill="none"
          strokeDasharray={circumference}
          strokeDashoffset={offset}
          strokeLinecap="round"
          transform={`rotate(-90 ${size / 2} ${size / 2})`}
        />
      </svg>
      <span className="progress-ring-text">{Math.round(clamped)}%</span>
    </div>
  );
}
```

`src/components/EmptyState.tsx`：

```tsx
import type { ReactNode } from 'react';

interface EmptyStateProps {
  title: string;
  hint?: string;
  action?: ReactNode;
}

export function EmptyState({ title, hint, action }: EmptyStateProps): React.ReactElement {
  return (
    <div className="empty-state">
      <p className="empty-state-title">{title}</p>
      {hint && <p className="empty-state-hint">{hint}</p>}
      {action}
    </div>
  );
}
```

`src/components/Field.tsx`：

```tsx
import type { ReactNode } from 'react';

interface FieldProps {
  label: string;
  children: ReactNode;
  hint?: string;
}

export function Field({ label, children, hint }: FieldProps): React.ReactElement {
  return (
    <label className="field">
      <span className="field-label">{label}</span>
      {children}
      {hint && <span className="field-hint">{hint}</span>}
    </label>
  );
}
```

`src/components/Drawer.tsx`：

```tsx
import { useEffect, type ReactNode } from 'react';

interface DrawerProps {
  open: boolean;
  title: string;
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
}

export function Drawer({ open, title, onClose, children, footer }: DrawerProps): React.ReactElement | null {
  useEffect(() => {
    if (!open) return;
    const onKey = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, onClose]);

  if (!open) return null;

  return (
    <div className="drawer-overlay" onClick={onClose}>
      <aside className="drawer-panel" onClick={(event) => event.stopPropagation()}>
        <header className="drawer-header">
          <h3>{title}</h3>
          <button type="button" className="drawer-close" onClick={onClose}>
            ×
          </button>
        </header>
        <div className="drawer-body">{children}</div>
        {footer && <footer className="drawer-footer">{footer}</footer>}
      </aside>
    </div>
  );
}
```

`src/components/Modal.tsx`：

```tsx
import { useEffect, type ReactNode } from 'react';

interface ModalProps {
  open: boolean;
  title: string;
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
}

export function Modal({ open, title, onClose, children, footer }: ModalProps): React.ReactElement | null {
  useEffect(() => {
    if (!open) return;
    const onKey = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, onClose]);

  if (!open) return null;

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-panel" onClick={(event) => event.stopPropagation()}>
        <header className="modal-header">
          <h3>{title}</h3>
          <button type="button" className="drawer-close" onClick={onClose}>
            ×
          </button>
        </header>
        <div className="modal-body">{children}</div>
        {footer && <footer className="modal-footer">{footer}</footer>}
      </div>
    </div>
  );
}
```

- [ ] **Step 5: 追加组件样式**

在 `src/styles/global.css` 末尾追加：

```css
.badge {
  display: inline-flex;
  align-items: center;
  padding: 1px 8px;
  border-radius: 999px;
  font-size: 12px;
  border: 1px solid var(--border);
  color: var(--text-dim);
  white-space: nowrap;
}

.badge-accent { border-color: var(--accent); color: var(--accent); }
.badge-danger { border-color: var(--danger); color: var(--danger); }
.badge-warning { border-color: var(--warning); color: var(--warning); }
.badge-success { border-color: var(--success); color: var(--success); }

.progress-ring {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
}

.progress-ring-track { stroke: var(--border); }

.progress-ring-value {
  stroke: var(--accent);
  transition: stroke-dashoffset 0.25s ease;
}

.progress-ring-text {
  position: absolute;
  font-size: 11px;
  color: var(--text-dim);
}

.empty-state {
  padding: 48px 16px;
  text-align: center;
  color: var(--text-dim);
}

.empty-state-title { margin: 0 0 6px; color: var(--text); }

.empty-state-hint { margin: 0 0 12px; font-size: 13px; }

.field {
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin-bottom: 12px;
}

.field-label { font-size: 12px; color: var(--text-dim); }

.field-hint { font-size: 12px; color: var(--text-dim); }

.drawer-overlay,
.modal-overlay {
  position: fixed;
  inset: 0;
  background: rgba(12, 14, 18, 0.42);
  z-index: 20;
}

.drawer-panel {
  position: absolute;
  top: 0;
  right: 0;
  bottom: 0;
  width: 420px;
  max-width: 92vw;
  background: var(--bg-panel);
  border-left: 1px solid var(--border);
  display: flex;
  flex-direction: column;
}

.drawer-header,
.modal-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 14px 18px;
  border-bottom: 1px solid var(--border);
}

.drawer-header h3,
.modal-header h3 { margin: 0; font-size: 15px; }

.drawer-close {
  background: transparent;
  border: none;
  font-size: 20px;
  line-height: 1;
  color: var(--text-dim);
}

.drawer-body,
.modal-body {
  flex: 1;
  overflow: auto;
  padding: 18px;
}

.drawer-footer,
.modal-footer {
  padding: 12px 18px;
  border-top: 1px solid var(--border);
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}

.modal-overlay {
  display: flex;
  align-items: center;
  justify-content: center;
}

.modal-panel {
  width: 460px;
  max-width: 92vw;
  max-height: 84vh;
  background: var(--bg-panel);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  box-shadow: var(--shadow);
  display: flex;
  flex-direction: column;
}
```

- [ ] **Step 6: 类型检查**

```bash
npm run typecheck
```

Expected: 退出码 0。

- [ ] **Step 7: 提交**

```bash
git add -A
git commit -m "feat: 添加通用 UI 组件集与格式化工具"
```

---

### Task 11: 项目管理页

**Files:**
- Create: `src/features/projects/ProjectPage.tsx`, `src/features/projects/ProjectForm.tsx`
- Modify: `src/App.tsx`（把占位换成 `<ProjectPage />`）, `src/styles/global.css`（追加项目页样式）

**Interfaces:**
- Consumes: `window.api.projects.*`；Task 10 的 `Drawer`、`Field`、`Badge`、`ProgressRing`、`EmptyState`、`Modal`；Task 4 已建立的 `src/lib/progress.ts` 的 `computeProgress`（**直接引入，不要复制实现**）
- Produces:
  - `src/features/projects/ProjectPage.tsx` 导出 `ProjectPage`
  - `src/features/projects/ProjectForm.tsx` 导出 `ProjectForm`

- [ ] **Step 1: 确认进度工具已就位**

`src/lib/progress.ts` 由 Task 4 建立，本任务直接引入使用，**不要新建或复制它**：

```tsx
import { computeProgress } from '@/lib/progress';
```

- [ ] **Step 2: 写项目表单**

`src/features/projects/ProjectForm.tsx`：

```tsx
import { useState } from 'react';
import { Field } from '@/components/Field';
import type { Priority, Project, ProjectStatus, ProgressMode } from '@/types/entities';

interface ProjectFormProps {
  initial?: Project | null;
  onSubmit: (input: {
    name: string;
    description: string;
    status: ProjectStatus;
    priority: Priority;
    color: string | null;
    progressMode: ProgressMode;
    progressManual: number | null;
    startDate: string | null;
    dueDate: string | null;
  }) => Promise<void>;
  onCancel: () => void;
  submitting: boolean;
}

const STATUS_OPTIONS: Array<{ value: ProjectStatus; label: string }> = [
  { value: 'planning', label: '规划中' },
  { value: 'active', label: '进行中' },
  { value: 'paused', label: '暂停' },
  { value: 'done', label: '已完成' }
];

const PRIORITY_OPTIONS: Array<{ value: Priority; label: string }> = [
  { value: 'high', label: '高' },
  { value: 'medium', label: '中' },
  { value: 'low', label: '低' },
  { value: 'none', label: '无' }
];

export function ProjectForm({
  initial,
  onSubmit,
  onCancel,
  submitting
}: ProjectFormProps): React.ReactElement {
  const [name, setName] = useState(initial?.name ?? '');
  const [description, setDescription] = useState(initial?.description ?? '');
  const [status, setStatus] = useState<ProjectStatus>(initial?.status ?? 'planning');
  const [priority, setPriority] = useState<Priority>(initial?.priority ?? 'none');
  const [progressMode, setProgressMode] = useState<ProgressMode>(initial?.progressMode ?? 'auto');
  const [progressManual, setProgressManual] = useState(initial?.progressManual ?? 0);
  const [startDate, setStartDate] = useState(initial?.startDate ?? '');
  const [dueDate, setDueDate] = useState(initial?.dueDate ?? '');

  const handleSubmit = async (event: React.FormEvent): Promise<void> => {
    event.preventDefault();
    if (!name.trim()) return;
    await onSubmit({
      name: name.trim(),
      description: description.trim(),
      status,
      priority,
      color: initial?.color ?? null,
      progressMode,
      progressManual: progressMode === 'manual' ? progressManual : null,
      startDate: startDate || null,
      dueDate: dueDate || null
    });
  };

  return (
    <form onSubmit={(event) => void handleSubmit(event)}>
      <Field label="项目名称">
        <input
          value={name}
          onChange={(event) => setName(event.target.value)}
          placeholder="例如：工作台上线"
          autoFocus
        />
      </Field>
      <Field label="描述">
        <textarea
          value={description}
          onChange={(event) => setDescription(event.target.value)}
          rows={3}
          placeholder="这个项目要达成什么？"
        />
      </Field>
      <Field label="状态">
        <select value={status} onChange={(event) => setStatus(event.target.value as ProjectStatus)}>
          {STATUS_OPTIONS.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      </Field>
      <Field label="优先级">
        <select value={priority} onChange={(event) => setPriority(event.target.value as Priority)}>
          {PRIORITY_OPTIONS.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      </Field>
      <Field label="进度模式" hint="自动模式按任务完成比例计算；手动模式由你直接填写">
        <select
          value={progressMode}
          onChange={(event) => setProgressMode(event.target.value as ProgressMode)}
        >
          <option value="auto">自动</option>
          <option value="manual">手动</option>
        </select>
      </Field>
      {progressMode === 'manual' && (
        <Field label="进度百分比">
          <input
            type="number"
            min={0}
            max={100}
            value={progressManual}
            onChange={(event) => setProgressManual(Number(event.target.value))}
          />
        </Field>
      )}
      <Field label="开始日期">
        <input
          type="date"
          value={startDate}
          onChange={(event) => setStartDate(event.target.value)}
        />
      </Field>
      <Field label="目标日期">
        <input type="date" value={dueDate} onChange={(event) => setDueDate(event.target.value)} />
      </Field>
      <div className="form-actions">
        <button type="button" className="btn" onClick={onCancel}>
          取消
        </button>
        <button type="submit" className="btn btn-primary" disabled={submitting || !name.trim()}>
          {submitting ? '保存中…' : '保存'}
        </button>
      </div>
    </form>
  );
}
```

- [ ] **Step 3: 写项目页**

`src/features/projects/ProjectPage.tsx`：

```tsx
import { useCallback, useState } from 'react';
import { Badge } from '@/components/Badge';
import { Drawer } from '@/components/Drawer';
import { EmptyState } from '@/components/EmptyState';
import { Modal } from '@/components/Modal';
import { ProgressRing } from '@/components/ProgressRing';
import { call } from '@/lib/api';
import { isOverdue } from '@/lib/format';
import { computeProgress } from '@/lib/progress';
import { useAsync } from '@/hooks/useAsync';
import { useUiStore } from '@/store/ui';
import type { Project, Task } from '@/types/entities';
import { ProjectForm } from './ProjectForm';

const STATUS_LABEL: Record<string, string> = {
  planning: '规划中',
  active: '进行中',
  paused: '暂停',
  done: '已完成'
};

const PRIORITY_TONE: Record<string, 'danger' | 'warning' | 'accent' | 'default'> = {
  high: 'danger',
  medium: 'warning',
  low: 'accent',
  none: 'default'
};

const PRIORITY_LABEL: Record<string, string> = {
  high: '高优先级',
  medium: '中优先级',
  low: '低优先级',
  none: '无优先级'
};

export function ProjectPage(): React.ReactElement {
  const showToast = useUiStore((s) => s.showToast);

  const projectsAsync = useAsync<Project[]>(() => call(() => window.api.projects.list()), []);
  const tasksAsync = useAsync<Task[]>(() => call(() => window.api.tasks.list()), []);

  const [editing, setEditing] = useState<Project | null>(null);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [pendingDelete, setPendingDelete] = useState<Project | null>(null);

  const projects = projectsAsync.data ?? [];
  const tasks = tasksAsync.data ?? [];

  const now = new Date();
  const nowLocalDateTime = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}T${String(now.getHours()).padStart(2, '0')}:${String(now.getMinutes()).padStart(2, '0')}`;

  const reloadAll = useCallback(() => {
    projectsAsync.reload();
    tasksAsync.reload();
  }, [projectsAsync, tasksAsync]);

  const handleSubmit = async (input: CreateInput): Promise<void> => {
    setSubmitting(true);
    try {
      if (editing) {
        await call(() => window.api.projects.update(editing.id, input));
        showToast('项目已更新');
      } else {
        await call(() => window.api.projects.create(input));
        showToast('项目已创建');
      }
      setDrawerOpen(false);
      setEditing(null);
      reloadAll();
    } catch (error) {
      showToast(error instanceof Error ? error.message : '保存失败', 'error');
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async (): Promise<void> => {
    if (!pendingDelete) return;
    try {
      await call(() => window.api.projects.remove(pendingDelete.id));
      showToast('项目已删除');
      setPendingDelete(null);
      reloadAll();
    } catch (error) {
      showToast(error instanceof Error ? error.message : '删除失败', 'error');
    }
  };

  const handleArchive = async (project: Project): Promise<void> => {
    try {
      await call(() => window.api.projects.archive(project.id));
      showToast('项目已归档');
      reloadAll();
    } catch (error) {
      showToast(error instanceof Error ? error.message : '归档失败', 'error');
    }
  };

  return (
    <section className="project-page">
      <header className="page-header">
        <h2>项目</h2>
        <button
          type="button"
          className="btn btn-primary"
          onClick={() => {
            setEditing(null);
            setDrawerOpen(true);
          }}
        >
          新建项目
        </button>
      </header>

      {projectsAsync.loading && <p className="placeholder">加载中…</p>}
      {projectsAsync.error && <p className="error-text">{projectsAsync.error}</p>}

      {!projectsAsync.loading && projects.length === 0 && (
        <EmptyState
          title="还没有项目"
          hint="建一个项目，把散落的事归到一起"
          action={
            <button
              type="button"
              className="btn btn-primary"
              onClick={() => {
                setEditing(null);
                setDrawerOpen(true);
              }}
            >
              新建项目
            </button>
          }
        />
      )}

      <div className="project-grid">
        {projects.map((project) => {
          const projectTasks = tasks.filter((task) => task.projectId === project.id);
          const progress =
            project.progressMode === 'manual'
              ? (project.progressManual ?? 0)
              : computeProgress(projectTasks);
          const overdue = projectTasks.filter(
            (task) =>
              task.status !== 'done' &&
              task.status !== 'canceled' &&
              isOverdue(task.dueAt, nowLocalDateTime)
          ).length;

          return (
            <article key={project.id} className="project-card">
              <div className="project-card-top">
                <ProgressRing value={progress} label={`${project.name} 进度`} />
                <div className="project-card-title">
                  <h3>{project.name}</h3>
                  <div className="project-card-badges">
                    <Badge>{STATUS_LABEL[project.status] ?? project.status}</Badge>
                    {project.priority !== 'none' && (
                      <Badge tone={PRIORITY_TONE[project.priority]}>
                        {PRIORITY_LABEL[project.priority]}
                      </Badge>
                    )}
                    {overdue > 0 && <Badge tone="danger">逾期 {overdue}</Badge>}
                  </div>
                </div>
              </div>
              {project.description && <p className="project-card-desc">{project.description}</p>}
              <div className="project-card-meta">
                <span>{projectTasks.length} 个任务</span>
                {project.dueDate && <span>目标 {project.dueDate}</span>}
              </div>
              <div className="project-card-actions">
                <button
                  type="button"
                  className="btn btn-sm"
                  onClick={() => {
                    setEditing(project);
                    setDrawerOpen(true);
                  }}
                >
                  编辑
                </button>
                <button type="button" className="btn btn-sm" onClick={() => void handleArchive(project)}>
                  归档
                </button>
                <button
                  type="button"
                  className="btn btn-sm btn-danger"
                  onClick={() => setPendingDelete(project)}
                >
                  删除
                </button>
              </div>
            </article>
          );
        })}
      </div>

      <Drawer open={drawerOpen} title={editing ? '编辑项目' : '新建项目'} onClose={() => setDrawerOpen(false)}>
        <ProjectForm
          initial={editing}
          submitting={submitting}
          onCancel={() => setDrawerOpen(false)}
          onSubmit={(input) => handleSubmit(input)}
        />
      </Drawer>

      <Modal
        open={pendingDelete !== null}
        title="删除项目"
        onClose={() => setPendingDelete(null)}
        footer={
          <>
            <button type="button" className="btn" onClick={() => setPendingDelete(null)}>
              取消
            </button>
            <button type="button" className="btn btn-danger" onClick={() => void handleDelete()}>
              确认删除
            </button>
          </>
        }
      >
        <p>
          确定删除「{pendingDelete?.name}」吗？该项目下的所有任务会一并删除，且无法撤销。
        </p>
      </Modal>
    </section>
  );
}

type CreateInput = {
  name: string;
  description: string;
  status: Project['status'];
  priority: Project['priority'];
  color: string | null;
  progressMode: Project['progressMode'];
  progressManual: number | null;
  startDate: string | null;
  dueDate: string | null;
};
```

- [ ] **Step 4: 接入 App 并补样式**

`src/App.tsx` 中把占位替换：

```tsx
import { ProjectPage } from '@/features/projects/ProjectPage';
// ...
{page === 'projects' && <ProjectPage />}
```

在 `src/styles/global.css` 末尾追加：

```css
.page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 18px;
}

.page-header h2 { margin: 0; font-size: 18px; }

.btn {
  background: var(--bg-panel);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  padding: 6px 12px;
}

.btn:hover { background: var(--bg-hover); }

.btn-primary {
  background: var(--accent);
  border-color: var(--accent);
  color: var(--accent-text);
}

.btn-danger {
  border-color: var(--danger);
  color: var(--danger);
}

.btn-sm { padding: 3px 9px; font-size: 12px; }

.error-text { color: var(--danger); }

.project-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 14px;
}

.project-card {
  background: var(--bg-panel);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  box-shadow: var(--shadow);
  padding: 14px;
}

.project-card-top {
  display: flex;
  gap: 12px;
  align-items: flex-start;
}

.project-card-title h3 { margin: 0 0 6px; font-size: 15px; }

.project-card-badges { display: flex; gap: 6px; flex-wrap: wrap; }

.project-card-desc {
  margin: 10px 0;
  color: var(--text-dim);
  font-size: 13px;
}

.project-card-meta {
  display: flex;
  gap: 14px;
  color: var(--text-dim);
  font-size: 12px;
}

.project-card-actions {
  display: flex;
  gap: 8px;
  margin-top: 12px;
}

.form-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 18px;
}
```

- [ ] **Step 5: 类型检查与冒烟验证**

```bash
npm run typecheck && npm run dev
```

Expected:
- 类型检查通过
- 窗口「项目」页显示「还没有项目」空状态
- 点「新建项目」→ 抽屉打开 → 填名称 → 保存 → 卡片出现，进度环显示 0%
- 点「编辑」能改名称；点「删除」弹确认框，取消后项目仍在

- [ ] **Step 6: 提交**

```bash
git add -A
git commit -m "feat: 添加项目管理页"
```

---

### Task 12: 任务列表页与快速添加

**Files:**
- Create: `src/features/tasks/TaskPage.tsx`, `src/features/tasks/TaskForm.tsx`, `src/features/tasks/QuickAdd.tsx`
- Modify: `src/App.tsx`（把占位换成 `<TaskPage />`）, `src/styles/global.css`（追加任务页样式）

**Interfaces:**
- Consumes: `window.api.tasks.*`、`window.api.projects.list`、`window.api.tags.list`；Task 10 的通用组件
- Produces:
  - `src/features/tasks/TaskPage.tsx` 导出 `TaskPage`
  - `src/features/tasks/TaskForm.tsx` 导出 `TaskForm`
  - `src/features/tasks/QuickAdd.tsx` 导出 `QuickAdd`

- [ ] **Step 1: 写快速添加条**

`src/features/tasks/QuickAdd.tsx`：

```tsx
import { useState } from 'react';
import { call } from '@/lib/api';

interface QuickAddProps {
  onCreated: () => void;
}

export function QuickAdd({ onCreated }: QuickAddProps): React.ReactElement {
  const [title, setTitle] = useState('');
  const [busy, setBusy] = useState(false);

  const handleSubmit = async (event: React.FormEvent): Promise<void> => {
    event.preventDefault();
    const trimmed = title.trim();
    if (!trimmed || busy) return;
    setBusy(true);
    try {
      await call(() => window.api.tasks.create({ title: trimmed }));
      setTitle('');
      onCreated();
    } catch {
      // 失败时保留输入内容，让用户可以重试
    } finally {
      setBusy(false);
    }
  };

  return (
    <form className="quick-add" onSubmit={(event) => void handleSubmit(event)}>
      <input
        value={title}
        onChange={(event) => setTitle(event.target.value)}
        placeholder="记一件事…"
        aria-label="快速添加任务"
      />
      <button type="submit" className="btn btn-primary" disabled={busy || !title.trim()}>
        添加
      </button>
    </form>
  );
}
```

- [ ] **Step 2: 写任务表单**

`src/features/tasks/TaskForm.tsx`：

```tsx
import { useState } from 'react';
import { Field } from '@/components/Field';
import type { Priority, Project, Task, TaskStatus } from '@/types/entities';

interface TaskFormProps {
  initial?: Task | null;
  projects: Project[];
  onSubmit: (input: {
    title: string;
    projectId: string | null;
    notes: string;
    status: TaskStatus;
    priority: Priority;
    dueAt: string | null;
    estimate: number | null;
    spent: number | null;
    blockedReason: string | null;
  }) => Promise<void>;
  onCancel: () => void;
  submitting: boolean;
}

const STATUS_OPTIONS: Array<{ value: TaskStatus; label: string }> = [
  { value: 'todo', label: '待办' },
  { value: 'doing', label: '进行中' },
  { value: 'blocked', label: '已阻塞' },
  { value: 'done', label: '已完成' },
  { value: 'canceled', label: '已取消' }
];

const PRIORITY_OPTIONS: Array<{ value: Priority; label: string }> = [
  { value: 'high', label: '高' },
  { value: 'medium', label: '中' },
  { value: 'low', label: '低' },
  { value: 'none', label: '无' }
];

export function TaskForm({
  initial,
  projects,
  onSubmit,
  onCancel,
  submitting
}: TaskFormProps): React.ReactElement {
  const [title, setTitle] = useState(initial?.title ?? '');
  const [projectId, setProjectId] = useState(initial?.projectId ?? '');
  const [notes, setNotes] = useState(initial?.notes ?? '');
  const [status, setStatus] = useState<TaskStatus>(initial?.status ?? 'todo');
  const [priority, setPriority] = useState<Priority>(initial?.priority ?? 'none');
  const [dueAt, setDueAt] = useState(initial?.dueAt ?? '');
  const [estimate, setEstimate] = useState(initial?.estimate ?? 0);
  const [spent, setSpent] = useState(initial?.spent ?? 0);
  const [blockedReason, setBlockedReason] = useState(initial?.blockedReason ?? '');

  const handleSubmit = async (event: React.FormEvent): Promise<void> => {
    event.preventDefault();
    if (!title.trim()) return;
    await onSubmit({
      title: title.trim(),
      projectId: projectId || null,
      notes: notes.trim(),
      status,
      priority,
      dueAt: dueAt || null,
      estimate: estimate > 0 ? estimate : null,
      spent: spent > 0 ? spent : null,
      blockedReason: status === 'blocked' ? blockedReason.trim() : null
    });
  };

  return (
    <form onSubmit={(event) => void handleSubmit(event)}>
      <Field label="任务标题">
        <input
          value={title}
          onChange={(event) => setTitle(event.target.value)}
          placeholder="要做什么？"
          autoFocus
        />
      </Field>
      <Field label="所属项目">
        <select value={projectId} onChange={(event) => setProjectId(event.target.value)}>
          <option value="">独立任务</option>
          {projects.map((project) => (
            <option key={project.id} value={project.id}>
              {project.name}
            </option>
          ))}
        </select>
      </Field>
      <Field label="状态">
        <select value={status} onChange={(event) => setStatus(event.target.value as TaskStatus)}>
          {STATUS_OPTIONS.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      </Field>
      <Field label="优先级">
        <select value={priority} onChange={(event) => setPriority(event.target.value as Priority)}>
          {PRIORITY_OPTIONS.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      </Field>
      <Field label="截止时间">
        <input
          type="datetime-local"
          value={dueAt}
          onChange={(event) => setDueAt(event.target.value)}
        />
      </Field>
      <Field label="预估工时（小时）">
        <input
          type="number"
          min={0}
          step={0.5}
          value={estimate}
          onChange={(event) => setEstimate(Number(event.target.value))}
        />
      </Field>
      <Field label="实际工时（小时）">
        <input
          type="number"
          min={0}
          step={0.5}
          value={spent}
          onChange={(event) => setSpent(Number(event.target.value))}
        />
      </Field>
      {status === 'blocked' && (
        <Field label="阻塞原因" hint="写清楚卡在哪里，周报会自动收录">
          <input
            value={blockedReason}
            onChange={(event) => setBlockedReason(event.target.value)}
            placeholder="等后端接口"
          />
        </Field>
      )}
      <Field label="备注">
        <textarea value={notes} onChange={(event) => setNotes(event.target.value)} rows={3} />
      </Field>
      <div className="form-actions">
        <button type="button" className="btn" onClick={onCancel}>
          取消
        </button>
        <button type="submit" className="btn btn-primary" disabled={submitting || !title.trim()}>
          {submitting ? '保存中…' : '保存'}
        </button>
      </div>
    </form>
  );
}
```

- [ ] **Step 3: 写任务页**

`src/features/tasks/TaskPage.tsx`：

```tsx
import { useCallback, useMemo, useState } from 'react';
import { Badge } from '@/components/Badge';
import { Drawer } from '@/components/Drawer';
import { EmptyState } from '@/components/EmptyState';
import { Modal } from '@/components/Modal';
import { call } from '@/lib/api';
import { formatLocalDateTime, isOverdue } from '@/lib/format';
import { useAsync } from '@/hooks/useAsync';
import { useUiStore } from '@/store/ui';
import type { Project, Task, TaskStatus } from '@/types/entities';
import { QuickAdd } from './QuickAdd';
import { TaskForm } from './TaskForm';

const STATUS_LABEL: Record<TaskStatus, string> = {
  todo: '待办',
  doing: '进行中',
  blocked: '已阻塞',
  done: '已完成',
  canceled: '已取消'
};

const PRIORITY_TONE: Record<string, 'danger' | 'warning' | 'accent' | 'default'> = {
  high: 'danger',
  medium: 'warning',
  low: 'accent',
  none: 'default'
};

type GroupKey = 'overdue' | 'today' | 'week' | 'later' | 'someday';

function groupOf(task: Task, nowDate: string, weekEndDate: string): GroupKey {
  if (!task.dueAt) return 'someday';
  const dueDate = task.dueAt.slice(0, 10);
  if (dueDate < nowDate) return 'overdue';
  if (dueDate === nowDate) return 'today';
  if (dueDate <= weekEndDate) return 'week';
  return 'later';
}

const GROUP_LABEL: Record<GroupKey, string> = {
  overdue: '逾期',
  today: '今日',
  week: '本周',
  later: '以后',
  someday: '无期限'
};

export function TaskPage(): React.ReactElement {
  const showToast = useUiStore((s) => s.showToast);

  const tasksAsync = useAsync<Task[]>(() => call(() => window.api.tasks.list()), []);
  const projectsAsync = useAsync<Project[]>(() => call(() => window.api.projects.list()), []);

  const [editing, setEditing] = useState<Task | null>(null);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [pendingDelete, setPendingDelete] = useState<Task | null>(null);
  const [statusFilter, setStatusFilter] = useState<TaskStatus[]>([]);

  const tasks = tasksAsync.data ?? [];
  const projects = projectsAsync.data ?? [];

  const now = new Date();
  const nowDate = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
  const nowDateTime = `${nowDate}T${String(now.getHours()).padStart(2, '0')}:${String(now.getMinutes()).padStart(2, '0')}`;
  // 本周以周一为首日，结束于本周六
  const daysUntilSaturday = (6 - now.getDay() + 7) % 7;
  const weekEnd = new Date(now);
  weekEnd.setDate(weekEnd.getDate() + daysUntilSaturday);
  const weekEndDate = `${weekEnd.getFullYear()}-${String(weekEnd.getMonth() + 1).padStart(2, '0')}-${String(weekEnd.getDate()).padStart(2, '0')}`;

  const visible = useMemo(
    () =>
      statusFilter.length === 0
        ? tasks
        : tasks.filter((task) => statusFilter.includes(task.status)),
    [tasks, statusFilter]
  );

  const groups = useMemo(() => {
    const map = new Map<GroupKey, Task[]>();
    for (const task of visible) {
      if (task.status === 'done' || task.status === 'canceled') continue;
      const key = groupOf(task, nowDate, weekEndDate);
      const bucket = map.get(key) ?? [];
      bucket.push(task);
      map.set(key, bucket);
    }
    return map;
  }, [visible, nowDate, weekEndDate]);

  const completed = useMemo(
    () => visible.filter((task) => task.status === 'done' || task.status === 'canceled'),
    [visible]
  );

  const reload = useCallback(() => {
    tasksAsync.reload();
    projectsAsync.reload();
  }, [tasksAsync, projectsAsync]);

  const handleCreate = async (input: {
    title: string;
    projectId: string | null;
    notes: string;
    status: TaskStatus;
    priority: Task['priority'];
    dueAt: string | null;
    estimate: number | null;
    spent: number | null;
    blockedReason: string | null;
  }): Promise<void> => {
    setSubmitting(true);
    try {
      if (editing) {
        await call(() => window.api.tasks.update(editing.id, input));
        showToast('任务已更新');
      } else {
        await call(() => window.api.tasks.create(input));
        showToast('任务已添加');
      }
      setDrawerOpen(false);
      setEditing(null);
      reload();
    } catch (error) {
      showToast(error instanceof Error ? error.message : '保存失败', 'error');
    } finally {
      setSubmitting(false);
    }
  };

  const handleToggleDone = async (task: Task): Promise<void> => {
    try {
      await call(() => window.api.tasks.setStatus(task.id, 'done'));
      reload();
    } catch (error) {
      showToast(error instanceof Error ? error.message : '操作失败', 'error');
    }
  };

  const handleDelete = async (): Promise<void> => {
    if (!pendingDelete) return;
    try {
      await call(() => window.api.tasks.remove(pendingDelete.id));
      showToast('任务已删除');
      setPendingDelete(null);
      reload();
    } catch (error) {
      showToast(error instanceof Error ? error.message : '删除失败', 'error');
    }
  };

  const projectName = (projectId: string | null): string => {
    if (!projectId) return '独立任务';
    return projects.find((p) => p.id === projectId)?.name ?? '未知项目';
  };

  const renderTask = (task: Task): React.ReactElement => (
    <li key={task.id} className="task-item">
      <button
        type="button"
        className="task-check"
        aria-label="标记为已完成"
        onClick={() => void handleToggleDone(task)}
      />
      <div className="task-body" onClick={() => {
        setEditing(task);
        setDrawerOpen(true);
      }}>
        <div className="task-title">{task.title}</div>
        <div className="task-meta">
          <span>{projectName(task.projectId)}</span>
          {task.dueAt && (
            <span className={isOverdue(task.dueAt, nowDateTime) ? 'task-due-overdue' : ''}>
              截止 {formatLocalDateTime(task.dueAt)}
            </span>
          )}
          {task.status === 'blocked' && <Badge tone="warning">已阻塞</Badge>}
          {task.priority !== 'none' && (
            <Badge tone={PRIORITY_TONE[task.priority]}>{task.priority}</Badge>
          )}
        </div>
      </div>
      <button
        type="button"
        className="btn btn-sm btn-danger"
        onClick={() => setPendingDelete(task)}
      >
        删除
      </button>
    </li>
  );

  return (
    <section className="task-page">
      <header className="page-header">
        <h2>任务</h2>
        <button
          type="button"
          className="btn btn-primary"
          onClick={() => {
            setEditing(null);
            setDrawerOpen(true);
          }}
        >
          新建任务
        </button>
      </header>

      <QuickAdd onCreated={reload} />

      <div className="filter-bar">
        {(['todo', 'doing', 'blocked', 'done'] as TaskStatus[]).map((status) => (
          <button
            key={status}
            type="button"
            className={statusFilter.includes(status) ? 'chip chip-active' : 'chip'}
            onClick={() =>
              setStatusFilter((current) =>
                current.includes(status)
                  ? current.filter((s) => s !== status)
                  : [...current, status]
              )
            }
          >
            {STATUS_LABEL[status]}
          </button>
        ))}
      </div>

      {tasksAsync.loading && <p className="placeholder">加载中…</p>}
      {tasksAsync.error && <p className="error-text">{tasksAsync.error}</p>}

      {!tasksAsync.loading && tasks.length === 0 && (
        <EmptyState title="还没有任务" hint="用上面的输入框记第一条" />
      )}

      {(Object.keys(GROUP_LABEL) as GroupKey[]).map((key) => {
        const bucket = groups.get(key);
        if (!bucket || bucket.length === 0) return null;
        return (
          <div key={key} className="task-group">
            <h3 className="task-group-title">
              {GROUP_LABEL[key]}
              <span className="task-group-count">{bucket.length}</span>
            </h3>
            <ul className="task-list">{bucket.map(renderTask)}</ul>
          </div>
        );
      })}

      {completed.length > 0 && (
        <div className="task-group">
          <h3 className="task-group-title">
            已完结<span className="task-group-count">{completed.length}</span>
          </h3>
          <ul className="task-list">{completed.map(renderTask)}</ul>
        </div>
      )}

      <Drawer
        open={drawerOpen}
        title={editing ? '编辑任务' : '新建任务'}
        onClose={() => setDrawerOpen(false)}
      >
        <TaskForm
          initial={editing}
          projects={projects}
          submitting={submitting}
          onCancel={() => setDrawerOpen(false)}
          onSubmit={(input) => handleCreate(input)}
        />
      </Drawer>

      <Modal
        open={pendingDelete !== null}
        title="删除任务"
        onClose={() => setPendingDelete(null)}
        footer={
          <>
            <button type="button" className="btn" onClick={() => setPendingDelete(null)}>
              取消
            </button>
            <button type="button" className="btn btn-danger" onClick={() => void handleDelete()}>
              确认删除
            </button>
          </>
        }
      >
        <p>确定删除「{pendingDelete?.title}」吗？此操作无法撤销。</p>
      </Modal>
    </section>
  );
}
```

- [ ] **Step 4: 接入 App 并补样式**

`src/App.tsx`：

```tsx
import { TaskPage } from '@/features/tasks/TaskPage';
// ...
{page === 'tasks' && <TaskPage />}
```

在 `src/styles/global.css` 末尾追加：

```css
.quick-add {
  display: flex;
  gap: 8px;
  margin-bottom: 14px;
}

.quick-add input { flex: 1; }

.filter-bar {
  display: flex;
  gap: 8px;
  margin-bottom: 16px;
}

.chip {
  background: var(--bg-panel);
  border: 1px solid var(--border);
  border-radius: 999px;
  padding: 3px 12px;
  font-size: 12px;
  color: var(--text-dim);
}

.chip-active {
  border-color: var(--accent);
  color: var(--accent);
}

.task-group { margin-bottom: 18px; }

.task-group-title {
  margin: 0 0 8px;
  font-size: 13px;
  color: var(--text-dim);
  font-weight: 600;
}

.task-group-count {
  margin-left: 6px;
  color: var(--text-dim);
  font-weight: 400;
}

.task-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.task-item {
  display: flex;
  align-items: center;
  gap: 10px;
  background: var(--bg-panel);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  padding: 9px 12px;
}

.task-check {
  width: 16px;
  height: 16px;
  border-radius: 50%;
  border: 1px solid var(--border);
  background: transparent;
  flex: none;
}

.task-body { flex: 1; cursor: pointer; }

.task-title { font-size: 14px; }

.task-meta {
  display: flex;
  gap: 10px;
  align-items: center;
  font-size: 12px;
  color: var(--text-dim);
  margin-top: 2px;
}

.task-due-overdue { color: var(--danger); }
```

- [ ] **Step 5: 类型检查与冒烟验证**

```bash
npm run typecheck && npm run dev
```

Expected:
- 类型检查通过
- 任务页显示快速添加条 → 输入「写周报」→ 回车 → 任务出现在「无期限」组
- 点左侧「项目」新建一个项目 → 回任务页 → 点任务打开抽屉 → 选该项目 → 保存 → 任务显示项目名
- 筛选 chip「已完成」能过滤出已完成任务
- 设置一个过期截止时间 → 任务出现在「逾期」组且截止时间为红色

- [ ] **Step 6: 提交**

```bash
git add -A
git commit -m "feat: 添加任务列表页与快速添加"
```

---

### Task 13: 打包配置、README 与 M1 手测清单

**Files:**
- Create: `README.md`, `docs/M1-手测清单.md`, `build/.gitkeep`
- Modify: `package.json`（补 `engines` 字段）

**Interfaces:**
- Consumes: 全部前序任务
- Produces: 可交付的 M1 版本

- [ ] **Step 1: 补 `engines` 字段**

`package.json` 的 `devDependencies` 之后加：

```json
"engines": {
  "node": ">=22.5.0"
}
```

理由：主进程使用 `node:sqlite`，需要 Node ≥ 22.5（Electron 44 内置 Node 24.22，实际不受限，但本机 Node 22.22 也满足）。

- [ ] **Step 2: 写 README**

`README.md`：

````markdown
# 工作台 Work Station

个人项目管理与报告工作台。Electron + React + TypeScript + SQLite，全部数据留在本机。

## 当前状态

**M1 骨架与数据层** —— 已完成

- 项目与任务的增删改查、归档、软删除
- 标签体系
- 浅色 / 深色主题
- Windows portable 单文件 exe 打包

M2（看板与进度）、M3（提醒）、M4（周报月报与统计）尚未开始。

## 环境要求

- Node.js ≥ 22.5
- Windows（打包目标为 portable exe）

## 开发

```bash
npm install
npm run dev
```

## 测试

```bash
npm run test        # Vitest 单元与集成测试
npm run typecheck   # 双 tsconfig 类型检查
```

## 打包

```bash
npm run build:win
```

产物在 `dist/`，形如 `WorkStation-0.1.0-portable.exe`，单文件、可直接双击运行。

## 数据存放

| 场景 | 路径 |
| --- | --- |
| 开发期 | `.data/data.db`（项目目录下） |
| 打包后 | `%APPDATA%\work-station\data.db` |

数据库是单个 SQLite 文件，**复制该文件即完成备份**。

## 目录结构

```text
electron/     主进程：窗口、IPC、数据库、仓储
src/          渲染进程：React 界面
tests/        Vitest 测试
docs/         设计与计划文档
```

详细设计见 `docs/superpowers/specs/2026-11-05-work-station-design.md`。
````

- [ ] **Step 3: 写手测清单**

`docs/M1-手测清单.md`：

```markdown
# M1 手测清单

打包后执行，逐项打勾。

## 启动与数据持久化

- [ ] 双击 `dist/WorkStation-0.1.0-portable.exe`，窗口正常打开
- [ ] 侧边栏显示「项目 / 任务 / 设置」
- [ ] 新建项目 → 关闭 exe → 重新打开，项目仍在
- [ ] 数据库文件位于 `%APPDATA%\work-station\data.db`

## 项目

- [ ] 新建项目，名称、描述、状态、优先级均可填写并保存
- [ ] 编辑项目，只改名称时描述保持不变
- [ ] 归档项目后项目从列表消失（新建任务不应再看到它）
- [ ] 删除项目弹出二次确认，确认后项目及其任务一并消失
- [ ] 进度环在自动模式下随任务完成比例变化

## 任务

- [ ] 快速添加条输入后回车，任务出现在「无期限」组
- [ ] 任务可归属项目，也可留为独立任务
- [ ] 切换任务状态为「进行中」后保存，重新打开仍为进行中
- [ ] 切换为「已阻塞」时出现阻塞原因输入框
- [ ] 过期截止时间的任务出现在「逾期」组且日期标红
- [ ] 状态筛选 chip 能过滤任务

## 主题

- [ ] 设置页切换浅色 / 深色 / 跟随系统，界面立即变化
- [ ] 关闭并重开 exe，主题保持上次选择

## 异常

- [ ] 断网状态下全部功能正常（无网络依赖）
- [ ] 删除 `data.db` 后重启，程序重建空库不崩溃
- [ ] 把 `data.db` 内容改成乱码后重启，程序弹出「数据库已损坏」提示，`data.db.corrupt-<日期>` 留存原文件，且能新建空库继续使用
- [ ] `backups/` 目录中生成 `data-<日期>.db`，且不超过 7 份
```

- [ ] **Step 4: 全量测试与类型检查**

```bash
npm run typecheck && npm run test
```

Expected: 类型检查 0 错误；测试全部通过，数量应不少于 60 个用例（当前约 66 个）。

- [ ] **Step 5: 打包验证**

```bash
npm run build:win
```

Expected: `dist/WorkStation-0.1.0-portable.exe` 生成。

- [ ] **Step 6: 执行手测清单**

按 `docs/M1-手测清单.md` 逐项验证。**每项必须实际验证**，不能只看代码就判定通过。发现的问题当场修复并补一条回归测试。

- [ ] **Step 7: 提交**

```bash
git add -A
git commit -m "docs: 补充 README 与 M1 手测清单"
```

---

## M1 完成标志

全部任务完成后应满足：

- [ ] `npm run typecheck` 退出码 0
- [ ] `npm run test` 全部通过
- [ ] `npm run build:win` 产出可双击运行的 exe
- [ ] `docs/M1-手测清单.md` 全部打勾
- [ ] `git log` 显示 13 个按序提交
- [ ] 项目数据在 exe 重启后完好

## 后续里程碑

| 里程碑 | 内容 | 前置 |
| --- | --- | --- |
| M2 看板与进度 | 任务看板 / 项目看板、拖拽、概览条、命令面板、快捷键、标签的界面化管理与任务打标签 | M1 |
| M3 提醒 | 托盘常驻、单实例、Windows 通知、调度器、提醒页、每日站会时间设置 | M2 |
| M4 报表与统计 | 报告引擎（需新增按 ISO UTC 边界查已完成任务的方法）、报告编辑器、一键复制 Markdown、趋势页、报告模板设置 | M3 |

M1 只交付**标签的数据层**（建标签、给任务打标签的仓储与关联表）。标签的界面化管理放到 M2，因为 M2 的看板卡片需要展示标签，拆开做会返工。

每个里程碑开始前单独编写实施计划——它们的任务拆分依赖前一里程碑产出的实际代码结构，现在写会脱离现实。
