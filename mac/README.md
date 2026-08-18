# LiveCaptions Translator · macOS 版

把**电脑正在播放的声音**实时转成文字并翻译出来。看没字幕的外语视频、听英文会议时，屏幕上会浮出一条中文字幕。

两种识别引擎，在设置里随时切换：

| 引擎 | 优点 | 代价 |
|------|------|------|
| **macOS 系统识别**（默认） | 不用下模型、开箱即用、几乎字字跟随 | 只有英文和中文能离线，其余语言音频会传给苹果服务器；仅 Apple Silicon 可离线 |
| **Whisper** | 约 99 种语言全部本机识别，音频不上传 | 首次要下模型（最小 31MB），字幕慢 1～3 秒 |

听日语、韩语、德语这类内容时，建议改用 Whisper。

两种引擎识别出的**文字**都会发给翻译服务（默认 Google，可换成你自己的接口）。

---

# 安装

需要装一个虚拟声卡 **BlackHole**。macOS 从系统层面不允许任何 App 直接偷听「正在播放的声音」，BlackHole 的作用是给声音开一条岔路：一边照常进你的耳机，一边送给本程序识别。这一步绕不过去，所有同类工具都一样。

整套配置约 10 分钟，**只需做一次**，中间要重启电脑一次。

## 第 1 步：下载

到 [Releases](https://github.com/SakiRinn/LiveCaptions-Translator/releases) 页面下载：

| 下载哪个 | 体积 | 适合谁 |
|---------|------|--------|
| `...-arm64-withruntime.zip` | 46 MB | **绝大多数人选这个**，下完就能用 |
| `...-arm64.zip` | 15 MB | 已装 .NET 10 运行时的人 |

芯片是 Intel 的，选带 `x64` 的那个。

解压后把 App 拖进「**应用程序**」文件夹。

> **如果你选了 15 MB 那个**，需要先装 .NET 10 运行时，否则应用双击没反应：
>
> ```bash
> brew install --cask dotnet-runtime
> ```
>
> 没有 `brew` 就到 [微软下载页](https://dotnet.microsoft.com/download/dotnet/10.0)，在 **.NET Runtime** 一栏选 macOS 对应芯片的安装包（Arm64 或 x64）。注意别选成 SDK，那是给开发者的，体积大很多。
>
> 实在弄不清就直接下 46 MB 那个，什么都不用装。

## 第 2 步：首次打开（需要绕过系统拦截）

本应用没有 Apple 证书签名，所以第一次打开会被系统拦下。任选一种做法，**只需做一次**：

**办法 A — 一条命令，一步到位**

```bash
xattr -dr com.apple.quarantine "/Applications/LiveCaptions Translator.app"
```

**办法 B — 纯鼠标操作**

1. 先**双击一次** App，会被拒绝。这一步是必需的 —— 不先试一次，后面那个按钮不会出现。
2. 打开**系统设置 → 隐私与安全性**，往下滑到「安全性」那一段。
3. 找到「已阻止打开 LiveCaptions Translator」这行提示，点旁边的**「仍要打开」**。
4. 再确认一次，可能要输开机密码。

## 第 3 步：装 BlackHole，然后重启电脑

```bash
brew install --cask blackhole-2ch
```

没有 `brew`？先去 [brew.sh](https://brew.sh) 装 Homebrew，或从 [BlackHole 官网](https://existential.audio/blackhole/) 下载安装包双击安装。

装完**必须重启电脑**，否则系统认不到这个声卡。

## 第 4 步：把声音分出一路（重启后做）

1. 打开「音频 MIDI 设置」——按 `Command + 空格`，输入 `音频 MIDI` 回车。
2. 点左下角 **`+`** → 选「**创建多输出设备**」。
3. 在右侧列表把**你平时用的扬声器或耳机**和 **BlackHole 2ch** 都勾上。
4. 点屏幕右上角音量图标（或系统设置 → 声音 → 输出），把输出切换到刚建好的「**多输出设备**」。

做完后你听声音应该和平常一样。如果听不到了，回第 3 小步检查扬声器有没有勾上。

> **切换后菜单栏调不了音量**
>
> 这是 macOS 的限制，不是程序的问题 —— 多输出设备本身不支持系统音量键。
>
> 改在「音频 MIDI 设置」里调：选中「多输出设备」，在右侧列表点**扬声器那一行**，拖它的音量滑块（BlackHole 那行保持满音量不用动）。

---

# 使用

双击「应用程序」里的 **LiveCaptions Translator**。

1. **「音频设备」下拉框选 `BlackHole 2ch`** —— 选一次就会被记住。
2. **点「开始」**。首次会弹出两个授权请求，**都请允许**：
   - **麦克风** —— 从 BlackHole 这类虚拟声卡取声，在 macOS 眼里也算麦克风。
   - **语音识别** —— 默认引擎需要它（换成 Whisper 则不需要，但首次要下模型）。
3. **播放视频或音频**，窗口上半显示识别的原文，下半显示译文。
4. **点「悬浮窗」** —— 屏幕底部浮出一条字幕，按住可拖动，拖右下角的半透明小方块能改大小，视频全屏了它也还在最上面。鼠标移上去会出现 `A-` `A+` `✕`，用来调字号和关闭。
5. **点「历史」** —— 查看以往翻译，可搜索、翻页、导出成 Excel 能打开的表格。
6. **点「设置」** —— 换识别引擎、改目标语言、换翻译引擎、填自己的 API 密钥。

## 换识别引擎

「设置」→「识别引擎」。两个引擎在下载的 App 里都能直接用，区别见开头那张表。

换 Whisper 模型时注意顺序：**先点「停止」，再去设置里改，改完重新点「开始」**。边识别边改的话，新模型要等重启程序才生效。

---

# 遇到问题？

**双击没任何反应（窗口不弹、Dock 也不跳）**

你下的可能是 15 MB 那个版本，但没装 .NET 10 运行时。装上就好：

```bash
brew install --cask dotnet-runtime
```

或者直接重新下载 46 MB 的那个版本，它自带运行时，不依赖任何额外安装。

**听得到声音，但一直不出字幕**

九成是系统输出没切对。点右上角音量图标确认，输出必须是你建的那个「**多输出设备**」，不能是「扬声器」或「BlackHole 2ch」单独一个。

**声音听不到了**

输出选成了「BlackHole 2ch」单独一项，声音全跑进虚拟声卡了。改选「多输出设备」。

**下拉框里没有 BlackHole 2ch**

装完 BlackHole 没重启。重启后再点程序里的「刷新」。

**启动失败：`Unable to init device … NoDevice`**

没拿到**麦克风权限**（从任何输入设备取声，macOS 都算麦克风）。

到「系统设置 → 隐私与安全性 → 麦克风」里允许本应用。如果列表里找不到它、或开关已经开着却仍然报错，在终端执行下面两行，然后重新打开应用（会重新弹授权框）：

```bash
tccutil reset Microphone io.github.sakirinn.livecaptionstranslator.mac
tccutil reset SpeechRecognition io.github.sakirinn.livecaptionstranslator.mac
```

> 这多发生在**升级到新版本之后**：临时签名每次打包都会变，系统发现旧授权记录对不上新签名，就既不放行也不弹框。

**译文显示 `[ERROR] ...`**

默认用 Google 的免费接口，用多了会被临时限流。要稳定就点「设置」把翻译引擎换成 `OpenAI`，填自己的接口地址和密钥。

**用 Whisper 时，点了「开始」很久没反应**

在下载识别模型。看一眼状态栏的进度 —— 默认优先走国内镜像（ModelScope），一般几十秒。

下载会自动重试多个源（ModelScope 失败则回退 Hugging Face），并且**支持断点续传**：中断了重新点「开始」会接着下，不会从头来。

如果实在下不动，三条路：

1. 「设置」里换「模型下载源」。
2. 「设置」里换个小模型。带 **Q5** 的是压缩版：

   | 选项 | 体积 |
   |------|------|
   | Tiny-Q5 | **31 MB** |
   | Base-Q5 | 57 MB |
   | Tiny | 74 MB |
   | Base（默认） | 141 MB |

3. **手动下载** —— 浏览器、下载工具、网盘、找人拷都行，把文件放进下面这个目录，程序就不再联网了：

   ```
   ~/Library/Application Support/LiveCaptionsTranslator/models/
   ```

   文件名必须一字不差：`ggml-tiny.bin` / `ggml-base.bin` / `ggml-small.bin` / `ggml-medium.bin`（Q5 版加后缀，如 `ggml-tiny-q5_1.bin`）。下载地址：

   ```
   https://modelscope.cn/models/cjc1887415157/whisper.cpp/resolve/master/ggml-base.bin
   ```

**识别得不太准**

用 Whisper 的话，把模型换大一档（`Base` → `Small`）。越大越准，但越慢、越占内存，且要重新下载。带 Q5 的是压缩版，体积小但精度略降。

用系统识别引擎的话，确认「系统识别的语言」和音频实际语言一致。

**字幕比声音慢一两秒**

Whisper 需要攒够一小段声音才能判断内容，1～3 秒延迟是这类技术的固有特性，不是卡了。想要更快就换系统识别引擎。

---

# 你的数据存在哪

```
~/Library/Application Support/LiveCaptionsTranslator/
├── settings.json      你的设置
├── history.db         翻译历史
└── models/            下载好的识别模型
```

想彻底重置，删掉这个文件夹即可，下次启动会重新生成。翻译历史也可以在程序里点「历史」→「清空」。

---
---

# 以下是给开发者的部分

普通使用者不需要往下看。

## 从源码运行

需要 [.NET SDK 10](https://dotnet.microsoft.com/download)（`brew install --cask dotnet-sdk`）。目标框架是 `net10.0`，**SDK 8 编译不了**。

```bash
git clone https://github.com/SakiRinn/LiveCaptions-Translator.git
cd LiveCaptions-Translator/mac
dotnet run
```

若提示 `command not found: dotnet`，关掉终端重开一个（安装程序写入 `/etc/paths.d/dotnet`，需重新加载 PATH）。

注意：这样跑**拿不到系统授权**，所以用不了系统识别引擎 —— 程序会自动退回 Whisper 并在状态栏说明原因。要测系统识别得先 `./package-app.sh` 打包。

本目录完全独立，不依赖也不改动 Windows 版（`../src`）的代码。根目录 `LiveCaptionsTranslator.csproj` 里有两行 `Remove="mac\**"`，用于把本目录从 Windows 项目的默认通配中排除 —— 少了它，Windows 构建会把这里的源码一起编译进去。

## 目录结构

```
mac/
├── Program.cs / App.axaml        程序入口
├── Info.plist                    .app 的身份与权限用途声明
├── package-app.sh                打包成 .app 与可分发 zip
├── Views/                        主窗口、设置窗、悬浮窗、历史窗
├── Audio/SystemAudioCapture.cs   音频采集
├── Captions/                     字幕来源接口 + 两种引擎实现 + 模型下载
├── Services/                     翻译引擎、设置持久化、历史存储
├── Models/                       数据模型
└── Utils/                        路径、macOS 原生互操作、block 构造、权限
```

## 技术栈

| 用途 | 库 |
|------|-----|
| UI 框架 | Avalonia（跨平台，net10.0） |
| 语音识别 | Whisper.net + Whisper.net.Runtime；系统 `SFSpeechRecognizer` |
| 音频采集 | SoundFlow（miniaudio） |
| 历史存储 | Microsoft.Data.Sqlite |
| 系统互操作 | 纯 C# P/Invoke 调 Objective-C 运行时，无需 Swift/Xcode |

## 实现要点

Windows 版借用系统「实时字幕」拿文字，macOS 没有该功能，因此自己完成两步：

1. **采集系统音频** → [SoundFlow](https://www.nuget.org/packages/SoundFlow)（底层 miniaudio / CoreAudio）从输入设备读 PCM，重采样为 16kHz 单声道。
2. **语音识别** → 两种实现，都实现 `ICaptionSource` 因而可互换：
   - `WhisperCaptionSource` —— [Whisper.net](https://www.nuget.org/packages/Whisper.net)（whisper.cpp）本地转文字。
   - `AppleSpeechCaptionSource` —— 系统 `SFSpeechRecognizer`。

### 悬浮窗浮于其它 App 的全屏之上

必须是 `NSPanel`，而 Avalonia 只创建 `NSWindow`。做法是运行时动态建一个 `NSPanel` 子类、把 Avalonia 的自定义窗口方法复制进去再换类。

**关键：窗口销毁前必须还原原类**，否则 KVO 注销会让进程 abort（换类会打乱 KVO 的 isa 记账）。见 `Utils/MacWindowInterop.cs`。

### 系统语音识别的三个坑

都是实测踩出来的，改动时勿破坏：

1. **必须作为独立 `.app` 启动**。TCC 按 bundle 身份记账；从终端以子进程运行时权限会被归属到父进程（终端/IDE），请求直接失败。
2. **`requestAuthorization:` 必须传真实的 block**。传 nil 不会弹授权框，状态永远停在 `notDetermined`。`Utils/ObjCBlock.cs` 按规范拼出全局 block —— 布局写错会导致原生崩溃。
3. **从输入设备取声需要麦克风权限**。不主动申请的话，底层只报「Unable to init device」这种看不懂的错，所以 `Utils/MacMicrophonePermission.cs` 会先申请并给出明确结论。

识别回调用 delegate 版 API（`recognitionTaskWithRequest:delegate:`），从而避开为回调再造一个 block；delegate 类用 `objc_allocateClassPair` 动态创建。

系统对单个识别任务有约 1 分钟上限，因此 `AppleSpeechCaptionSource` 每 45 秒主动换任务。

## 打包与分发

```bash
cd mac
./package-app.sh                        # 依赖框架（默认）
./package-app.sh --with-runtime         # 自包含
./package-app.sh --with-runtime osx-x64 # Intel 的自包含版
```

产物在 `mac/out/`（已被 .gitignore 忽略），同时生成 `.app` 和可直接上传的 zip。实测体积（arm64）：

| 方式 | .app | 下载（zip） | 使用者预先需要 |
|------|------|-----------|--------------|
| 依赖框架 | 36 MB | **15 MB** | 装 .NET 10 运行时 |
| 自包含 | 118 MB | 46 MB | 无 |

命名对齐 Windows 版约定：`LiveCaptionsTranslator-macOS-arm64.zip` 与 `...-arm64-withruntime.zip`。

几个实现细节：

- 必须用 `ditto` 而非普通 `zip`，否则会丢掉符号链接与扩展属性，解压出的 `.app` 可能无法运行。
- 脚本会删掉 `runtimes/` 里非目标平台的原生库（依赖包不区分平台全拷进来），约省 10MB。
- **模型默认不内置**，首次使用时联网下载。若想让用户开箱即用，把 `ggml-*.bin` 放进 `mac/bundled-models/` 再打包，应用会优先用它。

### 发 Release

执行上面的打包命令，把 `mac/out/` 里的 zip 上传到 GitHub Release 即可。

### Gatekeeper 与签名

脚本只做临时（ad-hoc）签名，`spctl` 对它的判定是 `rejected`。**已在另一台 Mac 上实测确认**：下载后双击打不开，且**右键选「打开」也不管用**，必须先双击一次被拒、再到「系统设置 → 隐私与安全性」点「仍要打开」，或执行 `xattr -dr com.apple.quarantine`。分发时必须把这个流程写清楚。

临时签名基于内容哈希，**每次重新打包都会变**。系统发现旧授权记录对不上新签名时，会既不放行也不弹框（表现为音频设备初始化失败），需 `tccutil reset`。要彻底解决只能用 Apple 开发者证书签名并公证。

## 已知限制

- 未做 Apple 证书签名与公证：使用者首次打开需手动绕过 Gatekeeper；升级后系统可能「忘记」已授予的权限。
- 系统识别引擎：需以 `.app` 运行并授权；离线仅支持 `en-*` 与 `zh-CN`（实测共支持 63 种语言，其中 58 种需联网、音频会上传到苹果服务器）；且仅 Apple Silicon 可离线。
- Whisper 非流式，字幕有约 1～3 秒固有延迟。
- 免费 Google 翻译接口偶尔被限流，返回 `[ERROR]`；建议改用 OpenAI 兼容接口。
