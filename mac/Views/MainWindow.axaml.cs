using System;
using System.Collections.Concurrent;
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

    // ---- 翻译节流 ----
    //
    // 字幕更新比翻译完成快得多（实测：系统识别约每 285ms 一次结果，
    // 一次翻译却要约 881ms）。若每条字幕都发起翻译并取消上一条，
    // 结果是几乎每次翻译都在完成前被杀 —— 译文和历史都大量丢失。
    //
    // 改成用一个串行的“泵”：字幕只往里放，泵做完一条再取最新的一条。
    // 这样既不会互相取消，又自然把频率降到“翻译能跑多快就多快”。
    // 字幕原文的显示不受此影响，仍然实时刷新。

    /// <summary>已说完的句子，每一句都要翻（不能被后面的半句顶掉）。</summary>
    private readonly ConcurrentQueue<string> _completedSentences = new();

    /// <summary>还在说的那半句，只留最新的一份。</summary>
    private string? _pendingPartial;

    /// <summary>已入队的最后一句完整句，用于去重（识别器会反复重发同一句）。</summary>
    private string? _lastEnqueued;

    /// <summary>队列上限。翻译实在跟不上时宁可丢最旧的，也不能无限堆积。</summary>
    private const int MaxQueuedSentences = 16;

    private CancellationTokenSource? _pumpCts;
    private Task? _pumpTask;

    private string _lastTranslatedSource = string.Empty;

    /// <summary>
    /// 已写入历史的最后一句，用于判定下一条是新增还是覆写。
    /// 放在内存而不是每次去查数据库，因为字幕每秒多次更新。
    /// </summary>
    private string? _lastLoggedSentence;
    private bool _lastLoggedWasComplete;
    private DateTime _lastLoggedAtUtc;

    /// <summary>
    /// 未说完的句子在这段时间内算草稿，可被下一句顶替；过了就当它定稿。
    /// 识别器经常把句子边界改来改去，没这个约束就会留下一堆半句碎片；
    /// 但如果无限期当草稿，像 [Music] 这种本来就没标点的内容会被白白覆盖掉。
    /// 2 秒是拿真实录音测出来的均衡点。
    /// </summary>
    private static readonly TimeSpan DraftWindow = TimeSpan.FromSeconds(2);

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
            // Progress<T> 会自己把回调派回创建它的同步上下文（这里就是 UI 线程）
            var progress = new Progress<double>(p =>
                SetStatus($"首次使用，正在下载识别模型… {p:P0}"));

            SetStatus(apple
                ? "正在申请系统语音识别权限（若弹出对话框请允许）…"
                : "正在加载识别模型…");
            await _source.InitializeAsync(progress);

            // 先退订再订：若上一次 Start 失败过，订阅可能已经建立，
            // 直接再订会变成重复订阅，导致每句字幕被翻译两次。
            _source.CaptionReceived -= OnCaptionReceived;
            _source.CaptionReceived += OnCaptionReceived;
            _source.Start();

            _pumpCts = new CancellationTokenSource();
            _pumpTask = Task.Run(() => TranslatePumpAsync(_pumpCts.Token));

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

        _pumpCts?.Cancel();
        try { _pumpTask?.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { /* 取消引起的异常，忽略 */ }
        _pumpCts?.Dispose();
        _pumpCts = null;
        _pumpTask = null;

        // 清掉积压的待翻内容，否则下次开始会先吐出上一次的尾巴
        while (_completedSentences.TryDequeue(out _)) { }
        Volatile.Write(ref _pendingPartial, null);
        _lastEnqueued = null;

        // 不清的话，重新开始后若首句与停止前最后一句相同，会被当成重复而不翻译
        _lastTranslatedSource = string.Empty;
        // 同理，不清的话重新开始后的第一句可能去覆写上一次的最后一条历史
        _lastLoggedSentence = null;
        _running = false;
        StartStopButton.Content = "开始";
        SetStatus("已停止。");
    }

    private void OnCaptionReceived(object? sender, string text)
    {
        // 两个引擎给的都是累计文本，直接显示会堆成一大段。只取当前那一句。
        string sentence = CaptionSegmenter.LatestSentence(text);
        if (!CaptionSegmenter.IsMeaningful(sentence))
            return;

        // 显示限长，但翻译与历史用完整句子：剪掉的那部分仍是上下文。
        string display = CaptionSegmenter.ShortenForDisplay(sentence);

        Dispatcher.UIThread.Post(() =>
        {
            CaptionText.Text = display;
            CaptionScroll.ScrollToEnd();
            _overlay?.UpdateOriginal(display);
        });

        EnqueueForTranslation(sentence);
    }

    /// <summary>
    /// 把字幕交给翻译泵。已说完的句子进队列逐句翻，
    /// 还在说的半句只占一个位——反正下一瞬就会被更新的版本取代。
    /// </summary>
    private void EnqueueForTranslation(string sentence)
    {
        if (CaptionSegmenter.IsComplete(sentence))
        {
            // 识别器在处理下一句时会反复重发已完成的这句，去重
            if (string.Equals(sentence, _lastEnqueued, StringComparison.Ordinal))
                return;
            _lastEnqueued = sentence;

            while (_completedSentences.Count >= MaxQueuedSentences)
                _completedSentences.TryDequeue(out _);
            _completedSentences.Enqueue(sentence);
        }
        else
        {
            Volatile.Write(ref _pendingPartial, sentence);
        }
    }

    /// <summary>取下一个要翻的文本：完整句优先，其次是最新的半句。</summary>
    private string? TakeNextForTranslation()
    {
        if (_completedSentences.TryDequeue(out string? completed))
            return completed;
        return Interlocked.Exchange(ref _pendingPartial, null);
    }

    /// <summary>
    /// 翻译泵：串行处理，一条做完再取下一条。
    /// 空闲时短睡一下就行，这点延迟相比翻译本身的耗时可忽略。
    /// </summary>
    private async Task TranslatePumpAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            string? next = TakeNextForTranslation();
            if (next is null)
            {
                try { await Task.Delay(80, token); }
                catch (OperationCanceledException) { break; }
                continue;
            }

            try
            {
                await TranslateOnceAsync(next, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // 单条失败不能终止泵，否则之后永远不再出译文
                Console.Error.WriteLine($"[MainWindow] 翻译泵异常，已跳过本条: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 本条历史应该覆写上一条还是新增。
    /// </summary>
    private bool ShouldOverwriteLastLog(string sentence)
    {
        if (_lastLoggedSentence is null)
            return false;

        // 同一句话的修正（识别器会回头改前面的词）
        if (CaptionSegmenter.IsSameSentence(sentence, _lastLoggedSentence))
            return true;

        // 上一条还没说完且刚写不久，当作草稿顶掉
        return !_lastLoggedWasComplete && DateTime.UtcNow - _lastLoggedAtUtc <= DraftWindow;
    }

    /// <summary>翻译一条并更新界面与历史。由翻译泵串行调用。</summary>
    private async Task TranslateOnceAsync(string text, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(text) || text == _lastTranslatedSource)
            return;
        _lastTranslatedSource = text;

        string translated;
        try
        {
            translated = await _translation.TranslateAsync(text, token);
        }
        catch (OperationCanceledException)
        {
            throw;   // 停止识别了，交由泵退出
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => SetStatus("翻译失败: " + ex.Message));
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            TranslationText.Text = translated;
            TranslationScroll.ScrollToEnd();
            _overlay?.UpdateTranslation(translated);
        });

        // 仅记录成功的翻译；翻译引擎失败时会返回以 [ERROR] 开头的说明
        if (translated.StartsWith("[ERROR]", StringComparison.Ordinal))
            return;

        try
        {
            await HistoryStore.LogAsync(text, translated,
                _translation.Settings.TargetLanguage, _translation.Settings.EngineName,
                ShouldOverwriteLastLog(text), token);

            _lastLoggedSentence = text;
            _lastLoggedWasComplete = CaptionSegmenter.IsComplete(text);
            _lastLoggedAtUtc = DateTime.UtcNow;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 历史记不上不应该影响字幕显示，因此只记日志
            Console.Error.WriteLine($"[MainWindow] 记录历史失败: {ex.Message}");
        }
    }

    private void SetStatus(string message) => StatusText.Text = message;
}
