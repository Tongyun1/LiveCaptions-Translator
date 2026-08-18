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

    /// <summary>是否点击了“保存”。</summary>
    public bool Saved { get; private set; }

    public SettingsWindow()
    {
        InitializeComponent();
        _settings = SettingsStore.Current;

        ModelComboBox.ItemsSource = Models.Select(WhisperModelProvider.DescribeModel).ToList();
        ModelSourceComboBox.ItemsSource =
            Enum.GetValues<ModelDownloadSource>().Select(s => s.ToString()).ToList();
        EngineComboBox.ItemsSource = TranslationSettings.EngineNames;

        LoadFromSettings();

        SaveButton.Click += (_, _) => ApplyAndClose();
        CancelButton.Click += (_, _) => Close();
    }

    private void LoadFromSettings()
    {
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
