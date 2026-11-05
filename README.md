# 工作台 Work Station

个人项目管理与报告工作台。Electron + React + TypeScript + SQLite，全部数据留在本机。

## 当前状态

**M1 骨架与数据层** —— 功能完成（13 个任务全部交付，人工验收清单见 `docs/M1-手测清单.md`）

- 项目与任务的增删改查、归档（可恢复）、删除（不可恢复）
- 标签体系的数据层与 IPC（管理界面在 M2）
- 浅色 / 深色主题
- Windows portable 单文件 exe 打包

M2（看板与进度）、M3（提醒）、M4（周报月报与统计）尚未开始。

## 环境要求

- Node.js ≥ 22.13（`node:sqlite` 自 22.13 起免 `--experimental-sqlite`；Electron 44 自身要求 ≥ 22.12）
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

首次打包需从 GitHub 下载 Electron 与 electron-builder 二进制；若网络受限，可先设置镜像再执行：

```powershell
$env:ELECTRON_MIRROR = "https://npmmirror.com/mirrors/electron/"
$env:ELECTRON_BUILDER_BINARIES_MIRROR = "https://npmmirror.com/mirrors/electron-builder-binaries/"
npm run build:win
```

或预置 `%LOCALAPPDATA%\electron\Cache` 与 `%LOCALAPPDATA%\electron-builder\Cache`。下载超时会报 `Timeout awaiting 'request' for 600000ms`，并可能在 `%TEMP%` 留下 `eb-dl-*.lock`，删掉锁文件后重试即可。

## 数据存放

| 场景 | 路径 |
| --- | --- |
| 开发期 | `.data/data.db`（项目目录下） |
| 打包后 | `%APPDATA%\work-station\data.db` |

数据库使用 WAL 模式。**退出应用后复制 `data.db` 即完成备份**；运行中复制需连同 `data.db-wal`、`data.db-shm` 一起复制，更稳妥的做法是直接取同目录 `backups\data-<日期>.db`（应用启动时已做 checkpoint，最多保留 7 份）。

打包后实测（2026-11-05，`WorkStation-0.1.0-portable.exe` 首次启动）：`C:\Users\tongl\AppData\Roaming\work-station\data.db`，与 `%APPDATA%\work-station\data.db` 一致；同目录下还有 `data.db-wal`、`data.db-shm`、`backups\data-<日期>.db` 与损坏时的 `data.db.corrupt-<日期>`。

## 目录结构

```text
electron/     主进程：窗口、IPC、数据库、仓储
src/          渲染进程：React 界面
tests/        Vitest 测试
docs/         设计与计划文档
```

详细设计见 `docs/superpowers/specs/2026-11-05-work-station-design.md`。
