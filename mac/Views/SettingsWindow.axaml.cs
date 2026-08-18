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

    /// <summary>是否点击了“保存”。</summary>
    public bool Saved { get; private set; }

    public SettingsWindow()
    {
        InitializeComponent();
        _settings = SettingsStore.Current;

        ModelComboBox.ItemsSource = Enum.GetValues<WhisperModel>().Select(m => m.ToString()).ToList();
        EngineComboBox.ItemsSource = TranslationSettings.EngineNames;

        LoadFromSettings();

        SaveButton.Click += (_, _) => ApplyAndClose();
        CancelButton.Click += (_, _) => Close();
    }

    private void LoadFromSettings()
    {
        ModelComboBox.SelectedItem = _settings.WhisperModel.ToString();
        EngineComboBox.SelectedItem = _settings.Translation.EngineName;
        TargetLanguageBox.Text = _settings.Translation.TargetLanguage;
        ApiUrlBox.Text = _settings.Translation.OpenAI.ApiUrl;
        ApiKeyBox.Text = _settings.Translation.OpenAI.ApiKey;
        ModelNameBox.Text = _settings.Translation.OpenAI.ModelName;
    }

    private void ApplyAndClose()
    {
        if (ModelComboBox.SelectedItem is string modelName &&
            Enum.TryParse<WhisperModel>(modelName, out var model))
            _settings.WhisperModel = model;

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
