using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using WorkStation.App.ViewModels;

namespace WorkStation.App.Views;

/// <summary>
/// 报告页。
///
/// 剪贴板与文件选择器是平台能力，ViewModel 不直接碰；
/// 视图只负责「要求做什么、结果是什么」这两件事，把它们注进 VM。
/// </summary>
public partial class ReportsView : UserControl
{
    public ReportsView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is ReportsViewModel vm)
        {
            vm.CopyToClipboard = CopyToClipboardAsync;
            vm.ExportToFile = ExportToFileAsync;
        }
    }

    private async Task<bool> CopyToClipboardAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return false;

        await clipboard.SetTextAsync(text);
        return true;
    }

    private async Task<bool> ExportToFileAsync((string FileName, string Content) request)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return false;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = request.FileName,
            DefaultExtension = "md",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Markdown 文档") { Patterns = new[] { "*.md" } }
            }
        });

        if (file is null) return false;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(request.Content);
        return true;
    }
}
