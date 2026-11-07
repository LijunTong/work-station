using System.Text;

namespace WorkStation.Tests;

/// <summary>
/// Windows 安装包（tools\installer\WorkStation.iss，Inno Setup 6）。
///
/// 这套东西的价值全在「目标机没装 .NET 运行时」这个场景上。
/// 现在的解法是**自包含发布**：exe 里带着运行时，安装过程不需要检测运行时、
/// 不需要联网下载任何东西 —— 装完就能用。
///
/// 这里把几条容易踩的点钉住：
///   · 自包含：publish 脚本必须带 --self-contained true，否则安装包在
///     没装运行时的机器上照样起不来；
///   · 安装包引用的 exe 路径要和 publish 的输出目录一致，路径写错的话
///     ISCC 会报「找不到文件」，但报的是相对路径、很容易看漏；
///   · 语言包必须存在，否则安装向导退回英文；
///   · 卸载要问一句「数据要不要一起删」，而且默认是保留 ——
///     误卸载不该丢工作台里的任务和项目。
/// </summary>
public class InstallerTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string InstallerDir => Path.Combine(RepoRoot, "tools", "installer");

    private static string IssPath => Path.Combine(InstallerDir, "WorkStation.iss");

    private static string PublishScript => Path.Combine(RepoRoot, "tools", "publish.ps1");

    private static string BuildInstallerScript => Path.Combine(RepoRoot, "tools", "build-installer.ps1");

    [Fact]
    public void Installer_FilesExist()
    {
        Assert.True(File.Exists(IssPath), "缺少 WorkStation.iss");
        Assert.True(
            File.Exists(Path.Combine(InstallerDir, "lang", "ChineseSimplified.isl")),
            "缺少中文语言包 —— 没有它安装向导会退回英文");
        Assert.True(
            File.Exists(Path.Combine(InstallerDir, "wizard", "wizard.bmp")),
            "缺少向导横幅图");
        Assert.True(
            File.Exists(Path.Combine(InstallerDir, "wizard", "wizard-small.bmp")),
            "缺少向导小图标");
        Assert.True(File.Exists(PublishScript), "缺少 publish.ps1");
        Assert.True(File.Exists(BuildInstallerScript), "缺少 build-installer.ps1");
    }

    /// <summary>
    /// 语言包必须是 UTF-8 —— Inno Setup 按 BOM 判断编码，
    /// 没 BOM 就按 ANSI 读，中文全变乱码。
    /// </summary>
    [Fact]
    public void LanguageFile_IsUtf8()
    {
        var path = Path.Combine(InstallerDir, "lang", "ChineseSimplified.isl");
        var bytes = File.ReadAllBytes(path);

        Assert.True(bytes.Length >= 3, "语言包为空");
        Assert.True(
            bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            "ChineseSimplified.isl 必须是 UTF-8 with BOM —— Inno Setup 没 BOM 会按 ANSI 解析，中文会乱码");
    }

    /// <summary>
    /// 向导图片必须是 24 位无压缩 BMP。PIL 默认存成 32 位带 alpha 的，
    /// Inno 的 WizardImageFile 不接受那种格式。
    /// </summary>
    [Fact]
    public void WizardImages_Are24BitBmp()
    {
        foreach (var name in new[] { "wizard.bmp", "wizard-small.bmp" })
        {
            var path = Path.Combine(InstallerDir, "wizard", name);
            var bytes = File.ReadAllBytes(path);

            Assert.True(bytes.Length > 54, $"{name} 不像 BMP 文件");
            Assert.Equal((byte)'B', bytes[0]);
            Assert.Equal((byte)'M', bytes[1]);
            // BITMAPINFOHEADER 的 biBitCount 在偏移 28 处（文件头 14 + 14）
            var bitCount = BitConverter.ToUInt16(bytes, 28);
            Assert.True(
                bitCount == 24,
                $"{name} 必须是 24 位 BMP（当前 {bitCount} 位）—— Inno 的 WizardImageFile 不接受带 alpha 的 32 位");
        }
    }

    /// <summary>
    /// 整个方案的地基：发布必须是**自包含**的。
    /// 框架依赖的 exe 在 CLR 加载之前就失败（缺运行时时 apphost 自己报错退出），
    /// 「启动时判断有没有运行时」这件事没法写在应用代码里 ——
    /// 能跑到代码里就说明运行时已经在了。自包含把这个问题从根上消掉。
    /// </summary>
    [Fact]
    public void Publish_IsSelfContained()
    {
        var script = File.ReadAllText(PublishScript);

        Assert.Contains("--self-contained true", script);
        Assert.Contains("PublishSingleFile=true", script);
        // 裁剪对 Avalonia 不安全：SukiUI 的 SettingsLayout 用反射 Binding，
        // System.Text.Json 用反射序列化，裁掉就崩。而且裁剪并不省多少体积。
        Assert.DoesNotContain("PublishTrimmed=true", script);
    }

    /// <summary>
    /// 安装包引用的 exe 必须和 publish 的输出目录对得上。
    /// 路径写错时 ISCC 报的是「找不到指定的文件」这种泛泛的话，很容易看漏。
    /// </summary>
    [Fact]
    public void Iss_ReferencesPublishedExecutable()
    {
        var iss = File.ReadAllText(IssPath);

        // .iss 里用 {#AppExeName} 宏，值是 WorkStation.exe
        Assert.Contains("#define AppExeName \"WorkStation.exe\"", iss);
        Assert.Contains(@"Source: ""..\..\dist\win-x64\{#AppExeName}""", iss);
    }

    /// <summary>
    /// 装到用户目录、写 HKCU，不需要管理员权限 —— 团队内部工具自己用就够了，
    /// 装到 Program Files 反而要弹 UAC。
    /// </summary>
    [Fact]
    public void Iss_InstallsPerUserWithoutElevation()
    {
        var iss = File.ReadAllText(IssPath);

        Assert.Contains("PrivilegesRequired=lowest", iss);
        Assert.Contains(@"DefaultDirName={localappdata}\Programs\{#AppNameEn}", iss);
    }

    /// <summary>
    /// 卸载时要问一句数据要不要一起删，且默认保留。
    /// </summary>
    [Fact]
    public void Iss_AsksBeforeDeletingUserData()
    {
        var iss = File.ReadAllText(IssPath);

        Assert.Contains("CurUninstallStepChanged", iss);
        Assert.Contains("UninstallDeleteData", iss);
        Assert.Contains("DelTree(DataDir, True, True, True)", iss);
        // 数据目录必须跟 AppPaths.cs 对得上，删错了等于没删
        Assert.Contains("{userappdata}\\work-station", iss);
    }

    /// <summary>
    /// 「设置 → 应用 → 已安装的应用」里要能看、能卸载：
    /// 卸载显示名、图标、版本都得给。
    /// </summary>
    [Fact]
    public void Iss_RegistersUninstallEntry()
    {
        var iss = File.ReadAllText(IssPath);

        Assert.Contains("UninstallDisplayName={#AppName}", iss);
        Assert.Contains("UninstallDisplayIcon={app}\\{#AppExeName}", iss);
        Assert.Contains(@"Name: ""{group}\{#AppName}""", iss);
        Assert.Contains(@"Name: ""{group}\卸载 {#AppName}""", iss);
    }

    /// <summary>
    /// 应用配了 rollForward=LatestMajor：装了更高 major（如 12.x）的机器也能跑，
    /// 不用再单独装 10.0。开发期用框架依赖发布时靠的就是这条。
    /// </summary>
    [Fact]
    public void App_UsesLatestMajorRollForward()
    {
        var csproj = File.ReadAllText(
            Path.Combine(RepoRoot, "src", "WorkStation.App", "WorkStation.App.csproj"));

        Assert.Contains("<RollForward>LatestMajor</RollForward>", csproj);
    }

    /// <summary>
    /// 构建脚本自身：PowerShell 5.1 没 BOM 就按 ANSI 读 .ps1 —— 中文会整段乱码，
    /// 带 BOM 是硬要求。
    /// </summary>
    [Theory]
    [InlineData("publish.ps1")]
    [InlineData("build-installer.ps1")]
    public void BuildScripts_HaveUtf8Bom(string fileName)
    {
        var path = Path.Combine(RepoRoot, "tools", fileName);
        var bytes = File.ReadAllBytes(path);

        Assert.True(bytes.Length >= 3, $"{fileName} 为空");
        Assert.True(
            bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            $"{fileName} 必须是 UTF-8 with BOM —— PowerShell 5.1 没 BOM 就按 ANSI 解析，中文会乱码");
    }

    /// <summary>从测试程序集往上找仓库根（凭 work-station.slnx 认）。</summary>
    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "work-station.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("找不到仓库根（work-station.slnx）");
    }
}
