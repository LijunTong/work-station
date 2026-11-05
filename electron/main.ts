import { app, BrowserWindow, dialog } from 'electron';
import { existsSync, mkdirSync, readdirSync, renameSync, unlinkSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { backupDatabase, isHealthy, openDatabase, runMigrations } from './db';
import { toLocalDate } from './util/time';

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

function resolveDbPath(): string {
  return app.isPackaged
    ? join(app.getPath('userData'), 'data.db')
    : join(__dirname, '..', '..', '.data', 'data.db');
}

/** 启动时探测数据库完整性；损坏则把原文件改名留存并重建空库 */
function recoverIfCorrupt(dbPath: string): void {
  if (!existsSync(dbPath)) return;

  // 探测用裸句柄而非 openDatabase：WAL 模式下损坏文件会让 PRAGMA 抛错，
  // 句柄会在 openDatabase 内部泄漏并锁住文件，导致后续改名报 EBUSY。
  let healthy = false;
  let probe: DatabaseSync | undefined;
  try {
    probe = new DatabaseSync(dbPath);
    healthy = isHealthy(probe);
  } catch {
    healthy = false;
  } finally {
    try {
      probe?.close();
    } catch {
      // 关闭失败不影响后续的改名留存流程
    }
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

app
  .whenReady()
  .then(() => {
    const dbPath = resolveDbPath();
    recoverIfCorrupt(dbPath);

    const db = openDatabase(dbPath);
    runMigrations(db);
    runDailyBackup(db, dbPath);

    createMainWindow();

    app.on('activate', () => {
      if (BrowserWindow.getAllWindows().length === 0) createMainWindow();
    });
  })
  .catch((error: unknown) => {
    // 启动链里任何失败都必须让用户看到，否则表现为「进程在、窗口不弹、无提示」
    dialog.showErrorBox('启动失败', String(error));
    app.quit();
  });

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});
