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

当前共 12 个测试文件、128 个用例，全部通过。

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

打包后实测（2026-11-05，`WorkStation-0.1.0-portable.exe` 首次启动）：`C:\Users\tongl\AppData\Roaming\work-station\data.db`，与 `%APPDATA%\work-station\data.db` 一致；同目录下还有 `data.db-wal`、`data.db-shm`、`backups\data-<日期>.db` 与损坏时的 `data.db.corrupt-<日期>`。

## 目录结构

```text
electron/     主进程：窗口、IPC、数据库、仓储
src/          渲染进程：React 界面
tests/        Vitest 测试
docs/         设计与计划文档
```

详细设计见 `docs/superpowers/specs/2026-11-05-work-station-design.md`。
