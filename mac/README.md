# LiveCaptions Translator · macOS 版

Windows 版 [LiveCaptions Translator](../README.md) 的 macOS 移植，实时把「电脑正在播放的声音」识别成文字并翻译显示。

本目录完全独立、不依赖也不改动 Windows 版（`../src`）代码。

---

## 工作原理

Windows 版借用系统「实时字幕」拿文字；macOS 没有该功能，因此本版本自己完成两步：

1. **采集系统音频** → 用 [SoundFlow](https://www.nuget.org/packages/SoundFlow)（底层 miniaudio / CoreAudio）从输入设备读取音频。
2. **本地语音识别** → 用 [Whisper.net](https://www.nuget.org/packages/Whisper.net)（whisper.cpp，Apple 芯片 Metal 加速）离线转文字，再送翻译引擎。

---

## 环境要求

- macOS 12+（Apple Silicon 或 Intel）
- [.NET SDK 8 或更高](https://dotnet.microsoft.com/download)（开发者用 `brew install --cask dotnet-sdk`）
- [BlackHole](https://github.com/ExistentialAudio/BlackHole) 虚拟声卡（用于捕获系统音频）

## 一次性配置：捕获系统声音

macOS 不允许应用直接抓系统输出，需用虚拟声卡中转：

1. 安装 BlackHole：
   ```bash
   brew install --cask blackhole-2ch
   ```
   安装后**需重启**生效。
2. 打开「音频 MIDI 设置」(Audio MIDI Setup) → 左下角 `+` → 新建「多输出设备」，勾选你的**扬声器/耳机 + BlackHole 2ch**。
3. 把系统输出切到这个「多输出设备」（这样你能听到声音，程序也能抓到）。

## 构建与运行

```bash
cd mac
dotnet run
```

首次点「开始」会自动下载 Whisper 模型（约 140MB，缓存于下方数据目录）。

## 使用

1. 「音频设备」下拉框选 **BlackHole 2ch**（选择会被记住）。
2. 需要时点「设置」配置翻译引擎、目标语言、OpenAI 密钥、Whisper 模型。
3. 点「开始」，播放音视频，主窗口显示原文与译文。
4. 点「悬浮窗」可浮出一条字幕条，可拖动、可缩放。

---

## 功能

- ✅ 系统音频采集 + 本地离线语音识别（Whisper，Metal 加速）
- ✅ 实时翻译：Google（免配置）/ OpenAI 兼容接口
- ✅ 悬浮字幕窗：无边框、置顶、半透明、可拖动、可缩放
- ✅ 设置持久化：记住设备、引擎、目标语言、模型、OpenAI 配置
- ⏳ 计划中：历史记录、更多翻译引擎、真全屏浮层

## 已知限制

- **真全屏浮层**：普通窗口无法浮在「别的 App 的真全屏」之上（macOS 限制，需 NSPanel，Avalonia 未提供）。
  变通：B 站等用「网页全屏」而非真全屏，悬浮窗即可正常显示。
- **免费 Google 接口**偶尔被限流（返回 `[ERROR]`）；需要稳定翻译建议在「设置」里改用 OpenAI 兼容接口并填入自己的密钥。

## 数据与设置位置

```
~/Library/Application Support/LiveCaptionsTranslator/
├── settings.json      应用设置
└── models/            Whisper 模型缓存
```

---

## 技术栈

| 用途 | 库 |
|------|-----|
| UI 框架 | Avalonia（跨平台，net10.0） |
| 语音识别 | Whisper.net + Whisper.net.Runtime |
| 音频采集 | SoundFlow（miniaudio） |
| 系统窗口互操作 | 纯 C# P/Invoke 调 Objective-C 运行时（无需 Swift/Xcode） |

## 目录结构

```
mac/
├── Program.cs / App.axaml        程序入口
├── Views/                        界面（主窗口、设置窗、悬浮窗）
├── Audio/SystemAudioCapture.cs   音频采集
├── Captions/                     字幕来源接口 + Whisper 实现 + 模型下载
├── Services/                     翻译引擎与设置持久化
├── Models/                       数据模型（设置、翻译配置）
└── Utils/                        路径、macOS 原生互操作
```
