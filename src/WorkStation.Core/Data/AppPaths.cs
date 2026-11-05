namespace WorkStation.Core.Data;

/// <summary>
/// 跨平台路径解析。三平台各自的「应用数据目录」语义不同，但都由
/// Environment.SpecialFolder.ApplicationData 统一给出：
/// <list type="bullet">
///   <item>Windows：%APPDATA%</item>
///   <item>macOS：~/Library/Application Support</item>
///   <item>Linux：~/.config（跟随 $XDG_CONFIG_HOME）</item>
/// </list>
/// 一律用 Path.Combine 拼接，禁止手拼路径分隔符。
/// </summary>
public static class AppPaths
{
    public const string AppFolderName = "work-station";

    public const string DatabaseFileName = "data.db";

    /// <summary>打包后的数据库路径：应用数据目录 / work-station / data.db。</summary>
    public static string DefaultDatabasePath() =>
        Path.Combine(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppFolderName),
            DatabaseFileName);

    /// <summary>备份目录：数据库同级的 backups/。</summary>
    public static string BackupDirectory(string databasePath) =>
        Path.Combine(Path.GetDirectoryName(databasePath) ?? AppFolderName, "backups");

    /// <summary>
    /// 开发期的数据库路径：仓库根 .data\data.db（与 Electron 版同路径）。
    /// 找不到仓库根时回落到打包路径。
    /// </summary>
    public static string DevelopmentDatabasePath(string repositoryRoot) =>
        Path.Combine(Path.Combine(repositoryRoot, ".data"), DatabaseFileName);

    /// <summary>
    /// 从可执行文件所在目录向上找仓库根（认 .git 目录）。
    /// 找不到返回 null。
    /// </summary>
    public static string? FindRepositoryRoot(string? startDirectory = null)
    {
        var current = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git"))) return current.FullName;
            current = current.Parent;
        }

        return null;
    }

    /// <summary>
    /// 运行期实际使用的数据库路径。
    ///
    /// 开发期（输出目录在 bin\ 下）用仓库内的 .data\data.db；
    /// 打包后用应用数据目录 —— 与 Electron 版完全一致，旧数据可以直接复用。
    /// </summary>
    public static string ResolveDatabasePath()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var inBuildOutput = baseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);

        if (inBuildOutput)
        {
            var root = FindRepositoryRoot(baseDirectory);
            if (root is not null) return DevelopmentDatabasePath(root);
        }

        return DefaultDatabasePath();
    }
}
