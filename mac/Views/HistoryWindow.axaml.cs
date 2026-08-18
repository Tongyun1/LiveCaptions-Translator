using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

using LiveCaptionsTranslator.Mac.Models;
using LiveCaptionsTranslator.Mac.Services;

namespace LiveCaptionsTranslator.Mac.Views;

/// <summary>
/// 翻译历史窗口：分页浏览、搜索、导出 CSV、清空。
/// 对应 Windows 版 HistoryPage（简化、独立实现）。
/// </summary>
public partial class HistoryWindow : Window
{
    private const int PageSize = 20;

    private int _page = 1;
    private int _totalPages = 1;
    private bool _busy;

    /// <summary>搜索输入防抖，避免每敲一个字都查库。</summary>
    private CancellationTokenSource? _searchDebounce;

    public HistoryWindow()
    {
        InitializeComponent();

        PrevButton.Click += async (_, _) => await GoToPageAsync(_page - 1);
        NextButton.Click += async (_, _) => await GoToPageAsync(_page + 1);
        ExportButton.Click += async (_, _) => await ExportAsync();
        ClearButton.Click += async (_, _) => await ClearAsync();
        SearchBox.TextChanged += (_, _) => ScheduleSearch();

        Opened += async (_, _) => await LoadAsync();
    }

    private void ScheduleSearch()
    {
        _searchDebounce?.Cancel();
        _searchDebounce = new CancellationTokenSource();
        var token = _searchDebounce.Token;

        DispatcherTimer.RunOnce(async () =>
        {
            if (token.IsCancellationRequested)
                return;
            _page = 1;
            await LoadAsync();
        }, TimeSpan.FromMilliseconds(300));
    }

    private async Task GoToPageAsync(int page)
    {
        if (page < 1 || page > _totalPages)
            return;
        _page = page;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            (List<TranslationHistoryEntry> entries, int totalPages) =
                await HistoryStore.LoadAsync(_page, PageSize, SearchBox.Text);

            _totalPages = totalPages;
            _page = Math.Clamp(_page, 1, _totalPages);

            HistoryList.ItemsSource = entries;
            PageText.Text = $"{_page} / {_totalPages}";
            PrevButton.IsEnabled = _page > 1;
            NextButton.IsEnabled = _page < _totalPages;
            StatusText.Text = entries.Count == 0 ? "暂无记录" : $"本页 {entries.Count} 条";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"读取失败: {ex.Message}";
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ExportAsync()
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出翻译历史",
                SuggestedFileName = $"translation_history_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                DefaultExtension = "csv",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("CSV 文件") { Patterns = new[] { "*.csv" } }
                }
            });

            string? path = file?.TryGetLocalPath();
            if (string.IsNullOrEmpty(path))
                return;

            await HistoryStore.ExportCsvAsync(path);
            StatusText.Text = "已导出";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"导出失败: {ex.Message}";
        }
    }

    private async Task ClearAsync()
    {
        // 二次确认：首次点击变为“确认清空”，再点一次才真正执行
        if (!Equals(ClearButton.Content, "确认清空"))
        {
            ClearButton.Content = "确认清空";
            DispatcherTimer.RunOnce(() => ClearButton.Content = "清空", TimeSpan.FromSeconds(4));
            return;
        }

        ClearButton.Content = "清空";
        try
        {
            await HistoryStore.ClearAsync();
            _page = 1;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"清空失败: {ex.Message}";
        }
    }
}
