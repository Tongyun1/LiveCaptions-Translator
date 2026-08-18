# LiveCaptions Translator · macOS 版

把**电脑正在播放的声音**实时转成文字并翻译出来。看没有字幕的外语视频、听英文会议、刷油管的时候，屏幕上会浮出一条中文字幕。

语音识别在你自己电脑上跑，音频不会上传。（但识别出的**文字**会发给翻译服务——默认是 Google，也可以在设置里改成你自己的接口。）

---

## 开始之前，先了解两件事

**1. 需要装两个东西**：微软的 .NET（用来运行程序）和 BlackHole（一个免费的虚拟声卡）。下面有完整命令，照着敲就行。

**2. 为什么要装 BlackHole**：macOS 从系统层面就不允许任何 App 直接偷听「电脑正在播放的声音」。BlackHole 的作用是给声音开一条岔路——声音一边照常进你的耳机，一边送给本程序去识别。这一步绕不过去，所有同类工具都得这么做。

整套配置大约 10 分钟，**只需要做一次**，中间要重启一次电脑。

---

## 第 1 步：装 .NET

打开「终端」(Terminal)，粘贴这行回车：

```bash
brew install --cask dotnet-sdk
```

> 没有 `brew` 命令？先去 [brew.sh](https://brew.sh) 装 Homebrew，或者直接从 [微软官网](https://dotnet.microsoft.com/download) 下载 .NET 10 的安装包双击安装。

装完确认一下，输出的数字要是 **10** 开头：

```bash
dotnet --version
```

如果提示 `command not found: dotnet`，**关掉终端重新开一个**再试（刚装好的命令需要新窗口才能认到）。

## 第 2 步：装 BlackHole，然后重启电脑

```bash
brew install --cask blackhole-2ch
```

装完**必须重启电脑**，否则系统认不到这个声卡。终端里也会提示你 `You must reboot`。

## 第 3 步：把声音分出一路（重启后做）

1. 打开「音频 MIDI 设置」——按 `Command + 空格`，输入 `音频 MIDI` 或 `Audio MIDI`，回车。
2. 点左下角的 **`+`** 按钮，选「**创建多输出设备**」。
3. 右侧会出现一个设备列表，把你**平时用的扬声器或耳机**和 **BlackHole 2ch** 两个都勾上。
4. 点屏幕右上角的音量图标（或系统设置 → 声音 → 输出），把输出**切换到刚建好的「多输出设备」**。

做完这一步，你听声音应该和平常一样。如果听不到了，检查第 3 小步里扬声器有没有勾上。

> **注意：切换后菜单栏调不了音量了**
>
> 这是 macOS 的限制，不是程序的问题——多输出设备本身不支持系统音量键。
>
> 改在「音频 MIDI 设置」里调：选中你的「多输出设备」，在右侧列表中点**扬声器那一行**，拖它的音量滑块。BlackHole 那行保持满音量不用动。

## 第 4 步：下载并启动程序

```bash
git clone https://github.com/SakiRinn/LiveCaptions-Translator.git
cd LiveCaptions-Translator/mac
dotnet run
```

第一次启动要编译，等个一两分钟，之后再启动就快了。程序窗口弹出来就成功了。

以后每次启动，只要重复最后两行（`cd` 和 `dotnet run`）。

---

## 怎么用

1. **「音频设备」下拉框选 `BlackHole 2ch`**。选一次就会被记住。
   - 列表里找不到？说明第 2 步装完没重启，重启一次。
2. **点「开始」**。
   - 第一次点会自动下载语音识别模型（约 140MB，需要联网）。状态栏会显示下载进度，等它跑完。这个只下载一次。
3. **播放视频或音频**。稍等一两秒，窗口上半部分出现识别的原文，下半部分出现译文。
4. **点「悬浮窗」**，屏幕底部会浮出一条字幕。按住字幕条可以拖到任意位置；右下角有个**半透明的小方块**，拖它能改字幕条大小。视频全屏了字幕也还在最上面。
   - 鼠标移到字幕条上时，右上角会出现 `A-` `A+` `✕` 三个按钮，用来调字号和关闭字幕条。
5. **点「历史」**看以前翻译过的内容，能搜索、翻页、导出成 Excel 能打开的表格。
6. **点「设置」**可以改翻译成哪种语言、换翻译引擎、填自己的 API 密钥。

### 字幕慢一两秒是正常的

语音识别需要攒够一小段声音才能判断你在说什么，所以字幕会比声音慢大约 1～3 秒。这是这类技术的固有特性，不是卡了。

---

## 遇到问题？

**听得到声音，但一直不出字幕**

九成是系统输出没切对。点右上角音量图标确认一下，输出必须是你建的那个「**多输出设备**」，不能是「扬声器」或「BlackHole 2ch」单独一个。

**下拉框里没有 BlackHole 2ch**

装完 BlackHole 没重启。重启电脑后再点程序里的「刷新」。

**声音听不到了**

系统输出选成了「BlackHole 2ch」单独一项——声音全跑到虚拟声卡里去了。改选「多输出设备」。

**译文显示 `[ERROR] ...`**

默认用的是 Google 的免费接口，用得多了会被临时限流。要稳定的话，点「设置」把引擎换成 `OpenAI`，填上自己的接口地址和密钥。

**点了「开始」很久没反应**

在下载识别模型（140MB），看一眼状态栏的进度。默认优先用国内镜像（ModelScope），一般几十秒就好。

**模型下载失败或特别慢**

下载会自动重试多个源（先 ModelScope 国内镜像，失败回退 Hugging Face 官方），而且**支持断点续传** —— 中断了重新点「开始」会接着下，不会从头开始。

如果还是不行，三条路：

1. 点「设置」把「模型下载源」换成另一个试试。
2. 点「设置」换个体积小的模型。带 **Q5** 的是压缩版，体积小很多：

   | 选项 | 体积 |
   |------|------|
   | Tiny-Q5 | **31 MB** |
   | Base-Q5 | 57 MB |
   | Tiny | 74 MB |
   | Base（默认） | 141 MB |
3. **手动下载**——用浏览器、下载工具、网盘或者找人拷都行，把文件放进这个目录就行，程序下次就不再联网了：

   ```
   ~/Library/Application Support/LiveCaptionsTranslator/models/
   ```

   文件名必须一字不差：`ggml-tiny.bin` / `ggml-base.bin` / `ggml-small.bin` / `ggml-medium.bin`。下载地址：

   ```
   https://modelscope.cn/models/cjc1887415157/whisper.cpp/resolve/master/ggml-base.bin
   ```

**识别得不太准**

点「设置」把 Whisper 模型换大一档（`Base` → `Small`）。越大越准，但越慢、越占内存，而且要重新下载对应的模型文件。

带 **Q5** 的选项是压缩版，体积小但精度略降。如果磁盘和网络都宽裕，用不带 Q5 的版本更准。

注意换模型的顺序：**先点「停止」，再去设置里改，改完重新点「开始」**。如果边识别边改，新模型要等重启程序才生效。

---

## 你的数据存在哪

```
~/Library/Application Support/LiveCaptionsTranslator/
├── settings.json      你的设置
├── history.db         翻译历史
└── models/            下载好的识别模型
```

想彻底重置，把这个文件夹删掉即可，下次启动会重新生成。翻译历史也可以在程序里点「历史」→「清空」。

---
---

# 以下是给开发者的部分

普通使用者不需要往下看。

## 功能

- ✅ 系统音频采集 + 本地离线语音识别（Whisper，Apple 芯片走 Metal 加速）
- ✅ 实时翻译：Google（免配置）/ OpenAI 兼容接口
- ✅ 悬浮字幕窗：无边框、置顶、半透明、可拖动、可缩放；可浮在其它 App 的真全屏之上
- ✅ 设置持久化：记住设备、引擎、目标语言、模型、OpenAI 配置
- ✅ 翻译历史：SQLite 本地存储，支持搜索、分页、导出 CSV、清空
- ⏳ 计划中：更多翻译引擎、`.app` 打包与签名

本目录完全独立，不依赖也不改动 Windows 版（`../src`）的代码。根目录的 `LiveCaptionsTranslator.csproj` 里有三行 `Remove="mac\**"`，用于把本目录从 Windows 项目的默认通配中排除。

## 实现要点

Windows 版借用系统「实时字幕」拿文字，macOS 没有该功能，因此本版本自己完成两步：

1. **采集系统音频** → [SoundFlow](https://www.nuget.org/packages/SoundFlow)（底层 miniaudio / CoreAudio）从输入设备读 PCM，重采样为 16kHz 单声道。
2. **本地语音识别** → [Whisper.net](https://www.nuget.org/packages/Whisper.net)（whisper.cpp）离线转文字，再送翻译引擎。

悬浮窗要浮在别的 App 的全屏之上必须是 `NSPanel`，而 Avalonia 只创建 `NSWindow`。做法是运行时动态建一个 `NSPanel` 子类、把 Avalonia 的自定义窗口方法复制进去再换类，并在窗口销毁前还原原类（否则 KVO 注销会让进程 abort）。全部是纯 C# P/Invoke 调 Objective-C 运行时，无需 Swift/Xcode。见 `Utils/MacWindowInterop.cs`。

## 技术栈

| 用途 | 库 |
|------|-----|
| UI 框架 | Avalonia（跨平台，net10.0） |
| 语音识别 | Whisper.net + Whisper.net.Runtime |
| 音频采集 | SoundFlow（miniaudio） |
| 历史存储 | Microsoft.Data.Sqlite |
| 系统窗口互操作 | 纯 C# P/Invoke 调 Objective-C 运行时 |

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

## 已知限制

- 免费 Google 接口偶尔被限流，返回 `[ERROR]`；建议改用 OpenAI 兼容接口。
- Whisper 非流式，字幕有约 1～3 秒固有延迟。
