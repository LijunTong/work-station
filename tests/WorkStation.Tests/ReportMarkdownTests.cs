using WorkStation.Core.Reports;

namespace WorkStation.Tests;

/// <summary>把自动汇总的 Markdown 拆成可渲染的行。</summary>
public class ReportMarkdownTests
{
    [Fact]
    public void Parse_SplitsHeadingsBulletsAndParagraphs()
    {
        var lines = ReportMarkdown.Parse("""
            ## 周报 2026-11-02 ~ 2026-11-08

            ### 统计
            - 完成 1 项

            普通一行
            """);

        // 空行不产生行 —— 解析结果只有标题、列表项和普通行三种
        Assert.Equal(4, lines.Count);
        Assert.Equal(1, lines[0].Level);
        Assert.Equal("周报 2026-11-02 ~ 2026-11-08", lines[0].Text);
        Assert.Equal(2, lines[1].Level);
        Assert.Equal("统计", lines[1].Text);
        Assert.True(lines[2].IsBullet);
        Assert.Equal("完成 1 项", lines[2].Text);
        Assert.Equal(0, lines[3].Level);
        Assert.False(lines[3].IsBullet);
        Assert.Equal("普通一行", lines[3].Text);
    }

    [Fact]
    public void Parse_EmptyTextGivesNoLines()
    {
        Assert.Empty(ReportMarkdown.Parse(string.Empty));
    }
}
