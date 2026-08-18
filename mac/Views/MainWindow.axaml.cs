using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Threading;

using LiveCaptionsTranslator.Mac.Captions;
using LiveCaptionsTranslator.Mac.Models;
using LiveCaptionsTranslator.Mac.Services;
using LiveCaptionsTranslator.Mac.Utils;

using SoundFlow.Structs;

namespace LiveCaptionsTranslator.Mac.Views;

public partial class MainWindow : Window
{
    private readonly TranslationService _translation = new(SettingsStore.Current.Translation);

    private ICaptionSource? _source;
    private DeviceInfo[] _devices = Array.Empty<DeviceInfo>();
    private bool _running;
    private bool _busy;

    private CancellationTokenSource? _translateCts;
    private string _lastTranslatedSource = string.Empty;

    private OverlayWindow? _overlay;
    private HistoryWindow? _history;

    /// <summary>引擎选择上需要告知用户的说明（如自动退回）。</summary>
    private string? _engineNote;

    public MainWindow()
    {
        InitializeComponent();

        EngineComboBox.ItemsSource = TranslationSettings.EngineNames;
        EngineComboBox.SelectedItem = _translation.Settings.EngineName;
        EngineComboBox.SelectionChanged += (_, _) =>
        {
            if (EngineComboBox.SelectedItem is string name)
            {
                _translation.Settings.EngineName = name;
                SettingsStore.Save();
            }
        };

        SettingsButton.Click += async (_, _) => await OpenSettingsAsync();
        HistoryButton.Click += (_, _) => OpenHistory();
        RefreshButton.Click += (_, _) => LoadDevices();
        StartStopButton.Click += async (_, _) => await ToggleAsync();
        OverlayButton.Click += (_, _) => ToggleOverlay();
        Opened += (_, _) => LoadDevices();
        Closed += (_, _) =>
        {
            // 悬浮窗只隐藏不销毁：在主窗口的原生关闭流程（windowWillClose:）里
            // 销毁另一个窗口容易触发 AppKit 内部的重入问题；
            // 应用采用 OnMainWindowClose 退出模式，不会因此残留进程。
            _overlay?.Hide();
            _history?.Close();
            _source?.Dispose();
        };
    }

    /// <summary>按当前设置创建字幕来源（识别引擎 + 首选设备）。</summary>
    private ICaptionSource CreateSource()
    {
        var settings = SettingsStore.Current;
        string preferred = string.IsNullOrWhiteSpace(settings.PreferredAudioDevice)
            ? "BlackHole"
            : settings.PreferredAudioDevice!;

        _engineNote = null;

        if (settings.Engine == RecognitionEngine.AppleSpeech && OperatingSystem.IsMacOS())
        {
            if (AppPaths.IsRunningInAppBundle)
                return new AppleSpeechCaptionSource(settings.AppleSpeechLocale, preferred);

            // 从源码运行时拿不到系统授权，直接失败对开发者并不友好；
            // 改为退回 Whisper，并在状态栏说清原因（不静默切换）。
            _engineNote = "注意：从源码运行时无法使用系统语音识别（拿不到系统授权），" +
                          "本次已改用 Whisper。需要系统识别请先用 package-app.sh 打包成 .app。";
        }

        return new WhisperCaptionSource(
            settings.WhisperModel,
            preferred,
            settings.BuildModelBaseUrls());
    }

    /// <summary>
    /// 影响字幕来源的设置快照。这些值在创建识别器时就定下了，
    /// 改了就必须重建来源，否则仍沿用旧值。
    /// </summary>
    private static (RecognitionEngine Engine, string Locale, WhisperModel Model,
        ModelDownloadSource Source, string? CustomUrl) SourceSettingsSnapshot()
    {
        var s = SettingsStore.Current;
        return (s.Engine, s.AppleSpeechLocale, s.WhisperModel, s.ModelSource, s.CustomModelBaseUrl);
    }

    private async Task OpenSettingsAsync()
    {
        var before = SourceSettingsSnapshot();

        var window = new SettingsWindow();
        await window.ShowDialog(this);

        EngineComboBox.SelectedItem = SettingsStore.Current.Translation.EngineName;

        if (!window.Saved || SourceSettingsSnapshot() == before)
            return;

        // 识别引擎与语言是建识别器时定死的，因此正在识别时也得重建；
        // 否则改了语言会继续用旧语言识别。
        bool wasRunning = _running;
        if (wasRunning)
            StopCapture();

        _source?.Dispose();
        _source = null;
        // 设备句柄归属于旧引擎，重建后必须重新枚举，否则启动时会报无此设备
        LoadDevices();

        // 之前在识别就继续识别，用户不必再手动点一次开始
        if (wasRunning)
            await ToggleAsync();
    }

    private void OpenHistory()
    {
        if (_history is not null)
        {
            _history.Activate();
            return;
        }

        _history = new HistoryWindow();
        _history.Closed += (_, _) => _history = null;
        _history.Show(this);
    }

    private void ToggleOverlay()
    {
        if (_overlay is not null)
        {
            _overlay.Close();
            return;
        }

        _overlay = new OverlayWindow();
        _overlay.Closed += (_, _) =>
        {
            _overlay = null;
            OverlayButton.Content = "悬浮窗";
        };
        // 把当前已有文本先同步一份
        _overlay.UpdateOriginal(CaptionText.Text ?? string.Empty);
        _overlay.UpdateTranslation(TranslationText.Text ?? string.Empty);
        _overlay.Show();
        OverlayButton.Content = "关闭悬浮窗";
    }

    private void LoadDevices()
    {
        try
        {
            _source ??= CreateSource();
            _devices = _source.GetCaptureDevices();

            DeviceComboBox.ItemsSource = _devices
                .Select(d => d.Name + (d.IsDefault ? " (默认)" : string.Empty))
                .ToList();

            if (_devices.Length > 0 && DeviceComboBox.SelectedIndex < 0)
            {
                string? preferred = SettingsStore.Current.PreferredAudioDevice;
                int index = -1;
                if (!string.IsNullOrWhiteSpace(preferred))
                    index = Array.FindIndex(_devices,
                        d => string.Equals(d.Name, preferred, StringComparison.Ordinal));
                if (index < 0)
                    index = Array.FindIndex(_devices,
                        d => d.Name?.Contains("BlackHole", StringComparison.OrdinalIgnoreCase) == true);
                DeviceComboBox.SelectedIndex = index >= 0 ? index : 0;
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
            // 从任何输入设备（包括 BlackHole 这类虚拟声卡）取声都需麦克风权限。
            // 不先申请的话，底层只会报出“Unable to init device”之类的模糊错误。
            if (OperatingSystem.IsMacOS())
            {
                SetStatus("正在确认麦克风权限（若弹出对话框请允许）…");
                if (!await MacMicrophonePermission.EnsureAsync())
                {
                    SetStatus("未获得麦克风权限，无法读取音频。\n" +
                              "请到「系统设置 → 隐私与安全性 → 麦克风」中允许本应用。");
                    return;
                }
            }

            _source ??= CreateSource();

            if (DeviceComboBox.SelectedIndex >= 0 && DeviceComboBox.SelectedIndex < _devices.Length)
            {
                DeviceInfo selected = _devices[DeviceComboBox.SelectedIndex];
                _source.SelectedDevice = selected;
                // 记住本次选择，下次启动自动选中
                SettingsStore.Current.PreferredAudioDevice = selected.Name;
                SettingsStore.Save();
            }

            bool apple = SettingsStore.Current.Engine == RecognitionEngine.AppleSpeech;
            var progress = new Progress<double>(p =>
                Dispatcher.UIThread.Post(() => SetStatus($"首次使用，正在下载识别模型… {p:P0}")));

            SetStatus(apple
                ? "正在申请系统语音识别权限（若弹出对话框请允许）…"
                : "正在加载识别模型…");
            await _source.InitializeAsync(progress);

            _source.CaptionReceived += OnCaptionReceived;
            _source.Start();

            _running = true;
            StartStopButton.Content = "停止";
            SetStatus("识别中…（说话或播放音频试试）"
                + (_engineNote is null ? string.Empty : "\n" + _engineNote));
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
            _overlay?.UpdateOriginal(text);
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
                _overlay?.UpdateTranslation(translated);
            });

            // 仅记录成功的翻译；翻译引擎失败时会返回以 [ERROR] 开头的说明
            if (!translated.StartsWith("[ERROR]", StringComparison.Ordinal))
            {
                await HistoryStore.LogAsync(text, translated,
                    _translation.Settings.TargetLanguage, _translation.Settings.EngineName);
            }
        }
        catch (OperationCanceledException)
        {
            // 被新的翻译请求取代，忽略
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MainWindow] 记录历史失败: {ex.Message}");
        }
    }

    private void SetStatus(string message) => StatusText.Text = message;
}
