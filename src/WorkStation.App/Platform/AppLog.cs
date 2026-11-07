using System.Text;

namespace WorkStation.App.Platform;

/// <summary>
/// 极简启动日志：只记「用户看不见的那条路径」——托盘、窗口关闭、退出。
///
/// 托盘这类东西失败时界面上什么都不会发生（图标没出现），用户能提供的线索只有
/// 「没有图标」。没有日志就只能靠猜，所以这里把关键决策和异常都落到数据目录下的
/// startup.log，出问题时直接看这个文件。
///
/// 写日志本身绝不能影响主流程：任何 IO 异常都吞掉。
/// </summary>
internal static class AppLog
{
    private const long MaxBytes = 256 * 1024;

    private static string? _path;

    public static string? FilePath => _path;

    /// <summary>日志与数据库同级：开发期是仓库 .data/，打包后是应用数据目录。</summary>
    public static void Init(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, "startup.log");

            // 超过上限就重开：这是排障用的滚动日志，不保留历史
            var info = new FileInfo(path);
            if (info.Exists && info.Length > MaxBytes)
            {
                info.Delete();
            }

            _path = path;
        }
        catch
        {
            _path = null;
        }
    }

    public static void Write(string message)
    {
        if (_path is not { } path) return;

        try
        {
            var line = new StringBuilder()
                .Append('[').Append(DateTime.Now.ToString("MM-dd HH:mm:ss")).Append("] ")
                .Append(message)
                .Append(Environment.NewLine)
                .ToString();

            File.AppendAllText(path, line);
        }
        catch
        {
            // 写不进去就算了
        }
    }
}
