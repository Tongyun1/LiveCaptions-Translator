# LiveCaptions Translator · macOS 版

把**电脑正在播放的声音**实时转成文字并翻译出来。看没有字幕的外语视频、听英文会议、刷油管的时候，屏幕上会浮出一条中文字幕。

识别有两种引擎可选：

- **macOS 系统语音识别（默认）**：不用下模型、开箱即用、延迟极低。但只有英文和中文能离线，其余语言会把音频传给苹果服务器；且仅 Apple Silicon（M 系芯片）可离线。
- **Whisper**：约 99 种语言全部在本机识别，音频不上传；代价是首次使用要下载模型（最小 31MB），且字幕会慢 1～3 秒。

听日语、韩语、德语这类内容时，建议到设置里改用 Whisper。

两种引擎识别出的**文字**都会发给翻译服务（默认 Google，可在设置里改成你自己的接口）。

---

## 开始之前，先了解两件事

**1. 需要装一个虚拟声卡 BlackHole**。macOS 从系统层面就不允许任何 App 直接偷听「电脑正在播放的声音」。BlackHole 的作用是给声音开一条岔路——声音一边照常进你的耳机，一边送给本程序去识别。这一步绕不过去，所有同类工具都得这么做。

**2. 本应用没有经过 Apple 证书签名**，所以首次打开需要多一步手动确认（下面第 1 步有说明）。

整套配置大约 10 分钟，**只需要做一次**，中间要重启一次电脑。

---

## 第 1 步：下载并打开应用

到 [Releases](https://github.com/SakiRinn/LiveCaptions-Translator/releases) 页面下载，按你的情况选一个：

| 下载哪个 | 体积 | 适合谁 |
|---------|------|--------|
| `...-arm64-withruntime.zip` | 46 MB | **绝大多数人选这个**，下完就能用 |
| `...-arm64.zip` | 15 MB | 已经装了 .NET 10 运行时的人 |

（芯片是 Intel 的选带 `x64` 的那个。）

解压后，**把 App 拖进「应用程序」文件夹**。

### 首次打开会被系统拦下，这样绕过

因为没有 Apple 证书签名，直接双击会提示无法打开。任选一种做法：

**办法 A（鼠标操作）**

1. 先**双击一次** App——会被拒绝，这一步是必需的，不先试一次后面就没有那个按钮。
2. 打开**系统设置 → 隐私与安全性**，往下滑到「安全性」那一段。
3. 会看到一行提示说已阻止打开“LiveCaptions Translator”，点它旁边的**「仍要打开」**。
4. 再确认一次，可能需要输入你的开机密码。

注意：新版 macOS 上在 App 上右键选「打开」**已经不管用了**，必须走上面这个设置里的流程。

**办法 B（一条命令，一步到位）**

把 App 拖进「应用程序」后，打开终端粘贴执行：

```bash
xattr -dr com.apple.quarantine "/Applications/LiveCaptions Translator.app"
```

两种做法都只需做一次，之后就能正常双击启动了。

> 为什么会这样：macOS 对从网上下载的未签名应用一律拦下。要彻底免掉这一步，需要开发者花钱买 Apple 证书并做公证。

## 第 2 步：装 BlackHole，然后重启电脑

```bash
brew install --cask blackhole-2ch
```

没有 `brew` 命令？先去 [brew.sh](https://brew.sh) 装 Homebrew，或者从 [BlackHole 官方页面](https://existential.audio/blackhole/) 下载安装包双击安装。

装完**必须重启电脑**，否则系统认不到这个声卡。

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

## 第 4 步：启动并授权

双击「应用程序」里的 **LiveCaptions Translator**。

第一次点「开始」时，系统会依次弹出两个授权请求，**两个都请允许**：

1. **麦克风** —— 从 BlackHole 这类虚拟声卡取声，在 macOS 眼里也算麦克风。
2. **语音识别** —— 默认使用系统识别引擎，需要这个权限。（若改用 Whisper 则不需要。）

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

换成系统语音识别引擎会快很多（几乎字字跟随）。

### 换识别引擎

点「设置」→「识别引擎」可以切换。两个引擎在下载的 App 里都能直接用。

**macOS 系统语音识别（默认）**：不用模型、快很多，但：

- 首次使用会弹**语音识别**授权，请允许
- 只有英文和中文能离线，其余语言会把音频传给苹果服务器
- 只在 Apple Silicon（M 系芯片）上能离线

**Whisper**：语言多、全本地，但首次需下载模型，且字幕慢 1～3 秒。听非英中内容时选它。

---

## 遇到问题？

**听得到声音，但一直不出字幕**

九成是系统输出没切对。点右上角音量图标确认一下，输出必须是你建的那个「**多输出设备**」，不能是「扬声器」或「BlackHole 2ch」单独一个。

**启动失败：Unable to init device … NoDevice**

没拿到**麦克风权限**。macOS 把从任何输入设备取声（包括 BlackHole 这类虚拟声卡）都归为麦克风权限。

到「系统设置 → 隐私与安全性 → 麦克风」里允许本应用。如果列表里看不到它、或者开关已经开着却仍然报错，在终端执行下面两行后重新打开应用（会重新弹授权框）：

```bash
tccutil reset Microphone io.github.sakirinn.livecaptionstranslator.mac
tccutil reset SpeechRecognition io.github.sakirinn.livecaptionstranslator.mac
```

这种情况多发生在**重新打包之后**：临时签名每次都会变，系统发现旧的授权记录对不上新签名，就既不放行也不弹框。

**选了系统语音识别，报“需要以 .app 方式启动”**

你在用源码跑（`dotnet run`）。这个引擎要申请系统授权，而 macOS 按 App 身份发权限，命令行跑法拿不到。用 Releases 里下载的 App，或自己执行 `mac/package-app.sh` 打包后再用。

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

## 从源码构建与运行

需要 [.NET SDK 10](https://dotnet.microsoft.com/download)（`brew install --cask dotnet-sdk`）。本项目目标框架是 `net10.0`，**SDK 8 编译不了**。

```bash
git clone https://github.com/SakiRinn/LiveCaptions-Translator.git
cd LiveCaptions-Translator/mac
dotnet run
```

若提示 `command not found: dotnet`，关掉终端重开一个（安装程序写入 `/etc/paths.d/dotnet`，需重新加载 PATH）。

注意：`dotnet run` 这种方式**拿不到系统授权**，因此无法使用「系统语音识别」引擎；要测它得先 `./package-app.sh` 打包。

## 功能

- ✅ 系统音频采集（BlackHole + SoundFlow）
- ✅ **两种识别引擎可切换**：Whisper（本地、约 99 种语言、Metal 加速）/ macOS 系统语音识别（免模型、低延迟）
- ✅ 实时翻译：Google（免配置）/ OpenAI 兼容接口
- ✅ 悬浮字幕窗：无边框、置顶、半透明、可拖动、可缩放；可浮在其它 App 的真全屏之上
- ✅ 设置持久化：记住设备、引擎、目标语言、模型、OpenAI 配置
- ✅ 翻译历史：SQLite 本地存储，支持搜索、分页、导出 CSV、清空
- ✅ `.app` 打包脚本（依赖框架 / 自包含两种）
- ⏳ 计划中：更多翻译引擎、Apple 证书签名与公证

本目录完全独立，不依赖也不改动 Windows 版（`../src`）的代码。根目录的 `LiveCaptionsTranslator.csproj` 里有三行 `Remove="mac\**"`，用于把本目录从 Windows 项目的默认通配中排除。

## 实现要点

Windows 版借用系统「实时字幕」拿文字，macOS 没有该功能，因此本版本自己完成两步：

1. **采集系统音频** → [SoundFlow](https://www.nuget.org/packages/SoundFlow)（底层 miniaudio / CoreAudio）从输入设备读 PCM，重采样为 16kHz 单声道。
2. **语音识别** → 两种实现，均实现 `ICaptionSource` 因而可互换：
   - `WhisperCaptionSource`：[Whisper.net](https://www.nuget.org/packages/Whisper.net)（whisper.cpp）本地转文字。
   - `AppleSpeechCaptionSource`：系统 `SFSpeechRecognizer`。

### 悬浮窗浮于其它 App 的全屏之上

必须是 `NSPanel`，而 Avalonia 只创建 `NSWindow`。做法是运行时动态建一个 `NSPanel` 子类、把 Avalonia 的自定义窗口方法复制进去再换类，并在窗口销毁前还原原类（否则 KVO 注销会让进程 abort）。见 `Utils/MacWindowInterop.cs`。

### 系统语音识别的两个坑

都已实测确认，改动时勿破坏：

1. **必须作为独立 `.app` 启动**。TCC 按 bundle 身份记账；从终端以子进程运行时，权限会被归属到父进程（终端/IDE），请求直接失败。
2. **`requestAuthorization:` 必须传真实的 block**。传 nil 不会弹授权框，状态永远停在 notDetermined。`Utils/ObjCBlock.cs` 按规范拼出全局 block（布局写错会导致原生崩溃）。

识别回调用 delegate 版 API（`recognitionTaskWithRequest:delegate:`），从而避开为回调再造一个 block；delegate 类用 `objc_allocateClassPair` 动态创建。

系统对单个识别任务有约 1 分钟限制，因此 `AppleSpeechCaptionSource` 每 45 秒主动换任务。

## 技术栈

| 用途 | 库 |
|------|-----|
| UI 框架 | Avalonia（跨平台，net10.0） |
| 语音识别 | Whisper.net + Whisper.net.Runtime |
| 音频采集 | SoundFlow（miniaudio） |
| 历史存储 | Microsoft.Data.Sqlite |
| 系统互操作 | 纯 C# P/Invoke 调 Objective-C 运行时（窗口、语音识别、权限），无需 Swift/Xcode |

## 目录结构

```
mac/
├── Program.cs / App.axaml        程序入口
├── Info.plist                    .app 的身份与权限用途声明
├── package-app.sh                打包成 .app 与可分发 zip
├── Views/                        界面（主窗口、设置窗、悬浮窗、历史窗）
├── Audio/SystemAudioCapture.cs   音频采集
├── Captions/                     字幕来源接口 + 两种引擎实现 + 模型下载
├── Services/                     翻译引擎、设置持久化、历史存储
├── Models/                       数据模型（设置、翻译配置、历史记录）
└── Utils/                        路径、macOS 原生互操作、block 构造、权限
```

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

- 必须用 `ditto` 而非普通 `zip` 打包，否则会丢掉符号链接与扩展属性，解压出的 `.app` 可能无法运行。
- 脚本会删掉 `runtimes/` 里非目标平台的原生库（依赖包不区分平台全拷），约省 10MB。
- **模型默认不内置**，首次使用时联网下载。确实需要开箱即用时，把 `ggml-*.bin` 放入 `mac/bundled-models/` 再打包，应用会优先用它。

### 发一个 Release

手动发：执行上面两条打包命令，把 `mac/out/` 里的 zip 上传到 GitHub Release 即可。

自动发：`mac/ci/macos-release.yml.example` 是一份可用的工作流草稿（打 `v*` tag 后自动构建 arm64/x64 共四个 zip 并附到 Release）。它没有直接放进 `.github/workflows/`，因为那属于本目录之外的改动，应由仓库维护者决定是否引入。

### Gatekeeper 与签名

脚本只做临时（ad-hoc）签名，`spctl` 对它的判定是 `rejected`。**已在另一台 Mac 上实测确认**：从网上下载后双击打不开，而且**右键选「打开」也不管用**，必须先双击一次被拒，再到「系统设置 → 隐私与安全性」里点「仍要打开」；或者执行 `xattr -dr com.apple.quarantine 应用路径`。分发时必须把这个流程写清楚。

临时签名基于内容哈希，**每次重新打包都会变**。系统发现旧授权记录对不上新签名时，会既不放行也不弹框（表现为音频设备初始化失败），需执行 `tccutil reset`。要彻底解决只能用 Apple 开发者证书签名并公证。

## 已知限制

- 免费 Google 接口偶尔被限流，返回 `[ERROR]`；建议改用 OpenAI 兼容接口。
- Whisper 非流式，字幕有约 1～3 秒固有延迟（系统语音识别引擎快得多）。
- 系统语音识别引擎：需以 `.app` 运行并授权；离线仅支持 `en-*` 与 `zh-CN`（实测共 63 种语言，其中 58 种需联网，音频会上传到苹果服务器）；且仅 Apple Silicon 可离线。
- 仅在 Apple Silicon 上实测过。Intel 的依赖库（`osx-x64` / `macos-x64`）齐全，但未经真机验证。
- 未做 Apple 证书签名与公证，分发时使用者需手动绕过 Gatekeeper；重新打包后系统可能“忘记”已授予的权限。
