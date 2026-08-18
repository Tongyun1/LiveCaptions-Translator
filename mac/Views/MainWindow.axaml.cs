using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Threading;

using LiveCaptionsTranslator.Mac.Captions;
using LiveCaptionsTranslator.Mac.Models;
using LiveCaptionsTranslator.Mac.Services;

using SoundFlow.Structs;

namespace LiveCaptionsTranslator.Mac.Views;

public partial class MainWindow : Window
{
    private readonly TranslationService _translation = new(new TranslationSettings());

    private WhisperCaptionSource? _source;
    private DeviceInfo[] _devices = Array.Empty<DeviceInfo>();
    private bool _running;
    private bool _busy;

    private CancellationTokenSource? _translateCts;
    private string _lastTranslatedSource = string.Empty;

    public MainWindow()
    {
        InitializeComponent();

        EngineComboBox.ItemsSource = TranslationSettings.EngineNames;
        EngineComboBox.SelectedItem = _translation.Settings.EngineName;
        EngineComboBox.SelectionChanged += (_, _) =>
        {
            if (EngineComboBox.SelectedItem is string name)
                _translation.Settings.EngineName = name;
        };

        RefreshButton.Click += (_, _) => LoadDevices();
        StartStopButton.Click += async (_, _) => await ToggleAsync();
        Opened += (_, _) => LoadDevices();
        Closed += (_, _) => _source?.Dispose();
    }

    private void LoadDevices()
    {
        try
        {
            _source ??= new WhisperCaptionSource();
            _devices = _source.GetCaptureDevices();

            DeviceComboBox.ItemsSource = _devices
                .Select(d => d.Name + (d.IsDefault ? " (默认)" : string.Empty))
                .ToList();

            if (_devices.Length > 0 && DeviceComboBox.SelectedIndex < 0)
            {
                int blackHole = Array.FindIndex(_devices,
                    d => d.Name?.Contains("BlackHole", StringComparison.OrdinalIgnoreCase) == true);
                DeviceComboBox.SelectedIndex = blackHole >= 0 ? blackHole : 0;
            }

            SetStatus(_devices.Length > 0
                ? $"发现 {_devices.Length} 个采集设备。未看到 BlackHole 请先安装并配置多输出设备。"
                : "未发现采集设备。");
        }
        catch (Exception ex)
        {
            SetStatus("枚举设备失败: " + ex.Message);
        }
    }

    private async Task ToggleAsync()
    {
        if (_busy)
            return;

        if (_running)
        {
            StopCapture();
            return;
        }

        _busy = true;
        StartStopButton.IsEnabled = false;
        try
        {
            _source ??= new WhisperCaptionSource();

            if (DeviceComboBox.SelectedIndex >= 0 && DeviceComboBox.SelectedIndex < _devices.Length)
                _source.SelectedDevice = _devices[DeviceComboBox.SelectedIndex];

            var progress = new Progress<double>(p =>
                Dispatcher.UIThread.Post(() => SetStatus($"首次使用，正在下载识别模型… {p:P0}")));

            SetStatus("正在加载识别模型…");
            await _source.InitializeAsync(progress);

            _source.CaptionReceived += OnCaptionReceived;
            _source.Start();

            _running = true;
            StartStopButton.Content = "停止";
            SetStatus("识别中…（说话或播放音频试试）");
        }
        catch (Exception ex)
        {
            SetStatus("启动失败: " + ex.Message);
        }
        finally
        {
            _busy = false;
            StartStopButton.IsEnabled = true;
        }
    }

    private void StopCapture()
    {
        if (_source is not null)
        {
            _source.CaptionReceived -= OnCaptionReceived;
            _source.Stop();
        }

        _translateCts?.Cancel();
        _running = false;
        StartStopButton.Content = "开始";
        SetStatus("已停止。");
    }

    private void OnCaptionReceived(object? sender, string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            CaptionText.Text = text;
            CaptionScroll.ScrollToEnd();
        });

        _ = TranslateAsync(text);
    }

    private async Task TranslateAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text == _lastTranslatedSource)
            return;
        _lastTranslatedSource = text;

        _translateCts?.Cancel();
        var cts = new CancellationTokenSource();
        _translateCts = cts;

        try
        {
            string translated = await _translation.TranslateAsync(text, cts.Token);
            if (cts.IsCancellationRequested)
                return;

            Dispatcher.UIThread.Post(() =>
            {
                TranslationText.Text = translated;
                TranslationScroll.ScrollToEnd();
            });
        }
        catch (OperationCanceledException)
        {
            // 被新的翻译请求取代，忽略
        }
    }

    private void SetStatus(string message) => StatusText.Text = message;
}
