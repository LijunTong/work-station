using System.Text;

namespace WorkStation.Tests;

/// <summary>
/// Linux 安装包（tools\linux）。
///
/// 这台机器上没有 WSL、没有 Docker daemon、也没有 dpkg-deb / ar / mksquashfs，
/// 所以 .deb 是**手写**出来的 —— .deb 本身就是「ar 归档 + 两个 tar.gz」，
/// ar 头格式固定、tar/gzip Python 原生支持，在 Windows 上完全能拼出来。
///
/// 于是格式正确性只能靠**自检**保证：这里把关键约束钉住，
/// 免得以后改打包脚本时无声产出 dpkg 读不了的包。
/// </summary>
public class LinuxPackageTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string LinuxToolsDir => Path.Combine(RepoRoot, "tools", "linux");

    private static string MakeScript => Path.Combine(LinuxToolsDir, "make-linux-packages.py");

    private static string VerifyScript => Path.Combine(LinuxToolsDir, "verify-linux-packages.py");

    private static string AppImageScript => Path.Combine(LinuxToolsDir, "build-appimage.sh");

    [Fact]
    public void Scripts_Exist()
    {
        Assert.True(File.Exists(MakeScript), "缺少 make-linux-packages.py");
        Assert.True(File.Exists(VerifyScript), "缺少 verify-linux-packages.py");
        Assert.True(File.Exists(AppImageScript), "缺少 build-appimage.sh");
        Assert.True(
            File.Exists(Path.Combine(RepoRoot, "tools", "linux", "build-linux-packages.ps1")),
            "缺少 build-linux-packages.ps1");
    }

    /// <summary>
    /// ar 归档的魔数只在**整个文件开头出现一次**。
    /// 早先每个成员前都写了一遍，第一个成员位置刚好对、第二个开始全部错位，
    /// dpkg 直接读不了这个包 —— 这种错在 Windows 上完全看不出来，
    /// 只能靠格式自检拦住。
    /// </summary>
    [Fact]
    public void ArMagic_WrittenOnlyOnce()
    {
        var script = File.ReadAllText(MakeScript);

        var magicCount = CountOccurrences(script, "b\"!<arch>\\n\"");
        Assert.Equal(1, magicCount);

        // 魔数必须在循环之外写：断言它的缩进比成员头浅
        var magicLine = script.Split('\n')
            .First(l => l.Contains("b\"!<arch>\\n\""));
        var headerLine = script.Split('\n')
            .First(l => l.Contains("name_bytes.ljust"));
        Assert.True(
            IndentOf(magicLine) < IndentOf(headerLine),
            "ar 魔数要写在成员循环外面（缩进比成员头浅）");
    }

    /// <summary>
    /// ar 头是固定 60 字节，字段宽度写错一个字节整个归档就废了：
    ///   name 16 + mtime 12 + uid 6 + gid 6 + mode 8 + size 10 + 结束符 2 = 60
    /// uid/gid 写成 8 就会变成 64 字节（早先踩过）。
    /// </summary>
    [Fact]
    public void ArHeader_LayoutIs60Bytes()
    {
        var script = File.ReadAllText(MakeScript);

        Assert.Contains(".ljust(16, b\" \")", script);   // name
        Assert.Contains(".ljust(12, b\" \")", script);   // mtime
        Assert.Contains(".ljust(6, b\" \")", script);    // uid / gid
        Assert.Contains(".ljust(8, b\" \")", script);    // mode
        Assert.Contains(".ljust(10, b\" \")", script);   // size
        Assert.Contains("assert len(header) == 60", script);
    }

    /// <summary>
    /// 符号链接必须**写成 tar 的符号链接条目**，不能在 Windows 上先建好再打进去 ——
    /// Windows 建符号链接要特权，而且路径会带盘符。
    /// </summary>
    [Fact]
    public void Symlink_WrittenAsTarEntry()
    {
        var script = File.ReadAllText(MakeScript);

        Assert.Contains("tarfile.SYMTYPE", script);
        Assert.Contains("usr/bin/workstation", script);
        Assert.Contains("/opt/workstation/WorkStation", script);
    }

    /// <summary>
    /// 权限必须手动写进 tar：Windows 的文件系统不存 Unix 权限位，
    /// os.chmod 改了也白改，tarfile.gettarinfo 只会拿到 0o666。
    /// </summary>
    [Fact]
    public void TarPermissions_AreSetExplicitly()
    {
        var script = File.ReadAllText(MakeScript);

        // tar.gz 的权限：可执行 755、其余 644，都是写死的
        Assert.Contains("info.mode = 0o755", script);
        Assert.Contains("else 0o644", script);
        // .deb 里的文件权限来自打 tar 时逐个指定的 mode
        Assert.Contains("info.mode = mode", script);
        // 打包 tar.gz 时不能用 gettarinfo（它读的是 Windows 的权限位）
        Assert.DoesNotContain("tar.gettarinfo(", script);
    }

    /// <summary>
    /// 图标文件名里必须带尺寸：全部叫 workstation.png 会互相覆盖，
    /// 只剩下最后一个尺寸。
    /// </summary>
    [Fact]
    public void IconFiles_CarrySize()
    {
        var script = File.ReadAllText(MakeScript);

        Assert.Contains("workstation-{size}.png", script);
        // 尺寸取自路径倒数第三段（.../16x16/apps/workstation.png）
        Assert.Contains("name.split(\"/\")[-3]", script);
    }

    /// <summary>
    /// 依赖清单是从发布产物的 ELF DT_NEEDED 里实际解析出来的，
    /// 不是拍脑袋写的。libfontconfig1（Skia 找字体）和 liblttng-ust1
    /// 缺了应用直接起不来。
    /// </summary>
    [Fact]
    public void Depends_IncludeCriticalLibraries()
    {
        var script = File.ReadAllText(MakeScript);

        foreach (var pkg in new[]
                 {
                     "libc6", "libgcc-s1", "libstdc++6",
                     "libfontconfig1", "liblttng-ust1"
                 })
        {
            Assert.Contains(pkg, script);
        }

        // ICU 的 soname 带版本号，各发行版不同，必须用「或」列出候选，
        // 写死一个版本会让别的发行版装不上。
        Assert.Contains("libicu74 | libicu73 | libicu72", script);
    }

    /// <summary>
    /// Linux 走**目录式**发布：单文件 + EnableCompressionInSingleFile
    /// 每次启动都要把原生库解压到 /tmp，桌面应用不能接受这个启动延迟。
    /// </summary>
    [Fact]
    public void Publish_UsesDirectoryLayoutForLinux()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot, "tools", "publish.ps1"));

        Assert.Contains("$isLinux = $runtime -like 'linux-*'", script);
        Assert.Contains("-p:PublishSingleFile=false", script);
    }

    /// <summary>
    /// AppImage 需要 mksquashfs（只有 Linux 上有），
    /// 所以这一步只能在 Linux 上跑，脚本得跟着绿色版一起发出去。
    /// </summary>
    [Fact]
    public void AppImageScript_IsShippedWithTarball()
    {
        var script = File.ReadAllText(MakeScript);

        Assert.Contains("build-appimage.sh", script);
        Assert.Contains("mksquashfs", File.ReadAllText(AppImageScript));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    private static int IndentOf(string line)
    {
        return line.Length - line.TrimStart().Length;
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
