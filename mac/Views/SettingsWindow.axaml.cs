using System;
using System.Linq;

using Avalonia.Controls;

using LiveCaptionsTranslator.Mac.Captions;
using LiveCaptionsTranslator.Mac.Models;
using LiveCaptionsTranslator.Mac.Services;

namespace LiveCaptionsTranslator.Mac.Views;

/// <summary>设置界面：编辑并持久化 Whisper 模型、翻译引擎、目标语言与 OpenAI 配置。</summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    /// <summary>下拉框项对应的模型枚举（按索引对应，避免反解析展示文本）。</summary>
    private static readonly WhisperModel[] Models = Enum.GetValues<WhisperModel>();

    private static readonly RecognitionEngine[] Engines = Enum.GetValues<RecognitionEngine>();

    private static readonly (string Id, string Display)[] AppleLocales =
    {
        ("en-US", "英语（美国）— 可离线"),
        ("zh-CN", "中文（简体）— 可离线"),
        ("ja-JP", "日语 — 需联网"),
        ("ko-KR", "韩语 — 需联网"),
        ("de-DE", "德语 — 需联网"),
        ("fr-FR", "法语 — 需联网"),
        ("es-ES", "西班牙语 — 需联网"),
        ("ru-RU", "俄语 — 需联网"),
        ("zh-TW", "中文（繁体）— 需联网"),
        ("yue-CN", "粤语 — 需联网")
    };

    private static string DescribeEngine(RecognitionEngine e) => e switch
    {
        RecognitionEngine.AppleSpeech => "macOS 系统语音识别（默认）",
        RecognitionEngine.Whisper => "Whisper（本地）",
        _ => e.ToString()
    };

    private static string EngineHint(RecognitionEngine e) => e switch
    {
        RecognitionEngine.AppleSpeech =>
            "无需下载模型、延迟更低。仅英文与中文可离线，其余语言由苹果服务器识别（音频会上传）。\n" +
            "需以 .app 方式启动并授予语音识别权限；仅 Apple Silicon 上可离线。从源码运行时会自动退回 Whisper。",
        RecognitionEngine.Whisper =>
            "约 99 种语言全部本地识别，音频不上传；首次使用需下载模型（最小 31MB）。",
        _ => string.Empty
    };

    /// <summary>是否点击了“保存”。</summary>
    public bool Saved { get; private set; }

    public SettingsWindow()
    {
        InitializeComponent();
        _settings = SettingsStore.Current;

        EngineKindComboBox.ItemsSource = Engines.Select(DescribeEngine).ToList();
        AppleLocaleComboBox.ItemsSource = AppleLocales.Select(l => l.Display).ToList();
        ModelComboBox.ItemsSource = Models.Select(WhisperModelProvider.DescribeModel).ToList();
        ModelSourceComboBox.ItemsSource =
            Enum.GetValues<ModelDownloadSource>().Select(s => s.ToString()).ToList();
        EngineComboBox.ItemsSource = TranslationSettings.EngineNames;

        LoadFromSettings();
        EngineKindComboBox.SelectionChanged += (_, _) => UpdateEngineVisibility();
        UpdateEngineVisibility();

        SaveButton.Click += (_, _) => ApplyAndClose();
        CancelButton.Click += (_, _) => Close();
    }

    /// <summary>根据所选引擎，只展示与它相关的选项。</summary>
    private void UpdateEngineVisibility()
    {
        int index = EngineKindComboBox.SelectedIndex;
        RecognitionEngine engine = index >= 0 && index < Engines.Length
            ? Engines[index] : RecognitionEngine.Whisper;

        bool whisper = engine == RecognitionEngine.Whisper;
        WhisperModelPanel.IsVisible = whisper;
        ModelSourcePanel.IsVisible = whisper;
        CustomUrlPanel.IsVisible = whisper;
        AppleLocalePanel.IsVisible = !whisper;
        EngineHintText.Text = EngineHint(engine);
    }

    private void LoadFromSettings()
    {
        EngineKindComboBox.SelectedIndex = Math.Max(0, Array.IndexOf(Engines, _settings.Engine));
        int localeIndex = Array.FindIndex(AppleLocales,
            l => string.Equals(l.Id, _settings.AppleSpeechLocale, StringComparison.OrdinalIgnoreCase));
        AppleLocaleComboBox.SelectedIndex = localeIndex >= 0 ? localeIndex : 0;
        ModelComboBox.SelectedIndex = Math.Max(0, Array.IndexOf(Models, _settings.WhisperModel));
        ModelSourceComboBox.SelectedItem = _settings.ModelSource.ToString();
        CustomModelUrlBox.Text = _settings.CustomModelBaseUrl;
        EngineComboBox.SelectedItem = _settings.Translation.EngineName;
        TargetLanguageBox.Text = _settings.Translation.TargetLanguage;
        ApiUrlBox.Text = _settings.Translation.OpenAI.ApiUrl;
        ApiKeyBox.Text = _settings.Translation.OpenAI.ApiKey;
        ModelNameBox.Text = _settings.Translation.OpenAI.ModelName;
    }

    private void ApplyAndClose()
    {
        if (EngineKindComboBox.SelectedIndex is int ei && ei >= 0 && ei < Engines.Length)
            _settings.Engine = Engines[ei];

        if (AppleLocaleComboBox.SelectedIndex is int li && li >= 0 && li < AppleLocales.Length)
            _settings.AppleSpeechLocale = AppleLocales[li].Id;

        if (ModelComboBox.SelectedIndex >= 0 && ModelComboBox.SelectedIndex < Models.Length)
            _settings.WhisperModel = Models[ModelComboBox.SelectedIndex];

        if (ModelSourceComboBox.SelectedItem is string sourceName &&
            Enum.TryParse<ModelDownloadSource>(sourceName, out var source))
            _settings.ModelSource = source;

        string custom = CustomModelUrlBox.Text?.Trim() ?? string.Empty;
        _settings.CustomModelBaseUrl = custom.Length == 0 ? null : custom;

        if (EngineComboBox.SelectedItem is string engine)
            _settings.Translation.EngineName = engine;

        _settings.Translation.TargetLanguage = string.IsNullOrWhiteSpace(TargetLanguageBox.Text)
            ? "zh-CN" : TargetLanguageBox.Text!.Trim();
        _settings.Translation.OpenAI.ApiUrl = ApiUrlBox.Text?.Trim() ?? string.Empty;
        _settings.Translation.OpenAI.ApiKey = ApiKeyBox.Text?.Trim() ?? string.Empty;
        _settings.Translation.OpenAI.ModelName = ModelNameBox.Text?.Trim() ?? string.Empty;

        SettingsStore.Save();
        Saved = true;
        Close();
    }
}
