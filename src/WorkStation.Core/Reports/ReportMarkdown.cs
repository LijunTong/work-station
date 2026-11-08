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
