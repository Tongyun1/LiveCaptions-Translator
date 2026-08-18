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

目前只能从源码运行，没有提供打包好的可执行文件，所以**必须先装 .NET SDK**。

| 需要装什么 | 说明 | 安装命令 |
|-----------|------|---------|
| macOS 12+ | Apple Silicon 或 Intel | — |
| [.NET SDK 10](https://dotnet.microsoft.com/download) | 本项目目标框架是 `net10.0`，**SDK 8 编译不了**，必须 10 或更高 | `brew install --cask dotnet-sdk` |
| [BlackHole](https://github.com/ExistentialAudio/BlackHole) | 虚拟声卡，用于捕获系统音频 | `brew install --cask blackhole-2ch` |

装完 SDK 后确认一下版本，输出应为 `10.x` 或更高：

```bash
dotnet --version
```

如果提示 `command not found: dotnet`，新开一个终端窗口再试（安装程序会写入 `/etc/paths.d/dotnet`，需要重新加载 PATH）。

## 一次性配置：捕获系统声音

macOS 不允许应用直接抓系统输出，需用虚拟声卡中转：

1. 安装 BlackHole：
   ```bash
   brew install --cask blackhole-2ch
   ```
   安装后**需重启**生效。
2. 打开「音频 MIDI 设置」(Audio MIDI Setup) → 左下角 `+` → 新建「多输出设备」，勾选你的**扬声器/耳机 + BlackHole 2ch**。
3. 把系统输出切到这个「多输出设备」（这样你能听到声音，程序也能抓到）。

> **调节音量**：切换到「多输出设备」后，菜单栏音量键会失效——这是 macOS 的限制（多输出设备本身不支持系统音量调节），不是本程序的问题。改在「音频 MIDI 设置」里调：选中你的「多输出设备」，在右侧子设备列表中点**扬声器/耳机那一行**，拖动它的音量滑块即可（BlackHole 那行保持满音量不用动）。

## 构建与运行

```bash
git clone https://github.com/SakiRinn/LiveCaptions-Translator.git
cd LiveCaptions-Translator/mac
dotnet run
```

首次点「开始」会自动下载 Whisper 模型（约 140MB，缓存于下方数据目录），需要联网，之后离线可用。

### 打包成不依赖 .NET 的版本（可选）

如果想把程序拷给没装 .NET 的人用，可以打包成自包含版本。运行时会被打进包里，对方机器上什么都不用装：

```bash
cd mac

# Apple Silicon
dotnet publish -c Release -r osx-arm64 --self-contained true -o ./out

# Intel 芯片则改成
dotnet publish -c Release -r osx-x64 --self-contained true -o ./out
```

产物在 `mac/out/`（已在 .gitignore 中，不会被误提交），约 128MB，直接跑 `./out/LiveCaptionsTranslator.Mac` 即可启动。

注意几点：

- 打包的人自己仍然需要 .NET SDK，只是**使用者**不再需要。
- 以上只在 Apple Silicon 上实测过。Intel 的依赖库（miniaudio 与 whisper.cpp 均含 `osx-x64` / `macos-x64`）是齐的，但未经真机验证。
- 产物未做代码签名，对方首次打开会被 macOS Gatekeeper 拦下。让对方在产物目录执行一次即可解除：
  ```bash
  xattr -dr com.apple.quarantine ./LiveCaptionsTranslator.Mac
  ```

## 使用

1. 「音频设备」下拉框选 **BlackHole 2ch**（选择会被记住）。
2. 需要时点「设置」配置翻译引擎、目标语言、OpenAI 密钥、Whisper 模型。
3. 点「开始」，播放音视频，主窗口显示原文与译文。
4. 点「悬浮窗」可浮出一条字幕条，可拖动、可缩放。
5. 点「历史」查看以往翻译，可搜索、分页、导出 CSV。

---

## 功能

- ✅ 系统音频采集 + 本地离线语音识别（Whisper，Metal 加速）
- ✅ 实时翻译：Google（免配置）/ OpenAI 兼容接口
- ✅ 悬浮字幕窗：无边框、置顶、半透明、可拖动、可缩放；**可浮在其它 App 的真全屏之上**
- ✅ 设置持久化：记住设备、引擎、目标语言、模型、OpenAI 配置
- ✅ 翻译历史：SQLite 本地存储，支持搜索、分页、导出 CSV、清空
- ⏳ 计划中：更多翻译引擎

## 已知限制

- **免费 Google 接口**偶尔被限流（返回 `[ERROR]`）；需要稳定翻译建议在「设置」里改用 OpenAI 兼容接口并填入自己的密钥。

## 数据与设置位置

```
~/Library/Application Support/LiveCaptionsTranslator/
├── settings.json      应用设置
├── history.db         翻译历史（SQLite）
└── models/            Whisper 模型缓存
```

---

## 技术栈

| 用途 | 库 |
|------|-----|
| UI 框架 | Avalonia（跨平台，net10.0） |
| 语音识别 | Whisper.net + Whisper.net.Runtime |
| 音频采集 | SoundFlow（miniaudio） |
| 历史存储 | Microsoft.Data.Sqlite |
| 系统窗口互操作 | 纯 C# P/Invoke 调 Objective-C 运行时（无需 Swift/Xcode） |

## 目录结构

```
mac/
├── Program.cs / App.axaml        程序入口
├── Views/                        界面（主窗口、设置窗、悬浮窗、历史窗）
├── Audio/SystemAudioCapture.cs   音频采集
├── Captions/                     字幕来源接口 + Whisper 实现 + 模型下载
├── Services/                     翻译引擎、设置持久化、历史存储
├── Models/                       数据模型（设置、翻译配置、历史记录）
└── Utils/                        路径、macOS 原生互操作
```
