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

    /// <summary>
    /// 引导配置文件名。数据目录的自定义值存在这里，**不能**存进数据库 ——
    /// 「库在哪」本身就是要读的配置，放库里就是鸡生蛋。
    /// </summary>
    public const string ConfigFileName = "app-config.json";

    /// <summary>打包后的数据库路径：应用数据目录 / work-station / data.db。</summary>
    public static string DefaultDatabasePath() =>
        Path.Combine(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppFolderName),
            DatabaseFileName);

    /// <summary>
    /// 引导配置文件路径：应用数据目录 / work-station / app-config.json。
    /// 固定不随数据目录挪动 —— 挪走数据后还得能找到这份配置。
    /// </summary>
    public static string ConfigFilePath() =>
        Path.Combine(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppFolderName),
            ConfigFileName);

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
    /// 优先级：
    /// 1. 用户自定义数据目录（app-config.json 的 dataDirectory，目录存在才算数）；
    /// 2. 开发期（输出目录在 bin\ 下）用仓库内的 .data\data.db；
    /// 3. 打包后用应用数据目录 —— 与 Electron 版完全一致，旧数据可以直接复用。
    /// </summary>
    public static string ResolveDatabasePath(string? configPath = null)
    {
        var customDirectory = ReadDataDirectoryOverride(configPath);
        if (customDirectory is not null) return Path.Combine(customDirectory, DatabaseFileName);

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

    /// <summary>
    /// 读用户自定义数据目录。没配置、目录不存在或配置损坏都返回 null（回落默认路径）。
    /// </summary>
    public static string? ReadDataDirectoryOverride(string? configPath = null)
    {
        var path = configPath ?? ConfigFilePath();
        try
        {
            if (!File.Exists(path)) return null;

            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("dataDirectory", out var element)) return null;

            var directory = element.GetString();
            return string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)
                ? null
                : Path.GetFullPath(directory);
        }
        catch (Exception)
        {
            // 配置损坏不能挡住启动：当没配置处理
            return null;
        }
    }

    /// <summary>
    /// 写（或清除）用户自定义数据目录。传 null/空串即恢复默认位置。
    /// </summary>
    public static void WriteDataDirectoryOverride(string? directory, string? configPath = null)
    {
        var path = configPath ?? ConfigFilePath();
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

        if (string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }

        var json = System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, string?> { ["dataDirectory"] = Path.GetFullPath(directory) });
        File.WriteAllText(path, json);
    }
}
