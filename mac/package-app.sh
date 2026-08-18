#!/bin/bash
#
# 把 macOS 版打包成可双击运行的 .app。
#
# 为什么需要打包：macOS 的权限系统（TCC）按 bundle 身份记账，只有作为独立的 .app
# 启动，应用才能申请「语音识别」等权限。从终端以子进程方式运行时，系统会把权限
# 归属到父进程（终端/IDE），请求会失败。
#
# 用法：
#   ./package-app.sh                      # 依赖框架（默认，体积小，用户需装 .NET 运行时）
#   ./package-app.sh --with-runtime       # 自包含（体积大，用户无需装任何东西）
#   ./package-app.sh --with-runtime osx-x64   # 指定架构（Intel）
#
set -euo pipefail

cd "$(dirname "$0")"

SELF_CONTAINED=false
RID=""
for arg in "$@"; do
    case "$arg" in
        --with-runtime) SELF_CONTAINED=true ;;
        osx-arm64|osx-x64) RID="$arg" ;;
        *) echo "未知参数：$arg" >&2; exit 1 ;;
    esac
done

if [ -z "$RID" ]; then
    case "$(uname -m)" in
        arm64) RID="osx-arm64" ;;
        x86_64) RID="osx-x64" ;;
        *) echo "无法识别的架构：$(uname -m)，请显式传入 osx-arm64 或 osx-x64" >&2; exit 1 ;;
    esac
fi

APP_NAME="LiveCaptions Translator"
EXECUTABLE="LiveCaptionsTranslator.Mac"
OUT_DIR="out"
APP="$OUT_DIR/$APP_NAME.app"
PUBLISH_DIR="obj/app-publish-$RID"

if [ "$SELF_CONTAINED" = true ]; then
    echo "==> 架构：${RID}（自包含，用户无需安装 .NET）"
else
    echo "==> 架构：${RID}（依赖框架，用户需先装 .NET 10 运行时）"
fi

echo "==> 发布"
rm -rf "$PUBLISH_DIR"
dotnet publish -c Release -r "$RID" --self-contained "$SELF_CONTAINED" -o "$PUBLISH_DIR" >/dev/null

echo "==> 组装 .app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH_DIR"/. "$APP/Contents/MacOS/"
cp Info.plist "$APP/Contents/Info.plist"
printf 'APPL????' > "$APP/Contents/PkgInfo"

if [ ! -x "$APP/Contents/MacOS/$EXECUTABLE" ]; then
    echo "错误：未找到可执行文件 ${EXECUTABLE}，Info.plist 的 CFBundleExecutable 需与其一致" >&2
    exit 1
fi

# 依赖包（whisper.cpp / miniaudio 等）会把所有平台的原生库都拷进 runtimes/，
# 其中 Linux/Windows 以及另一个 macOS 架构在本包里用不到，删掉可约省 10MB。
RUNTIMES="$APP/Contents/MacOS/runtimes"
if [ -d "$RUNTIMES" ]; then
    KEEP="macos-${RID#osx-}"       # osx-arm64 -> macos-arm64
    BEFORE=$(du -sk "$RUNTIMES" | awk '{print $1}')
    for dir in "$RUNTIMES"/*/; do
        name=$(basename "$dir")
        case "$name" in
            "$KEEP"|osx*) ;;                    # 保留目标平台
            *) rm -rf "$dir" ;;
        esac
    done
    AFTER=$(du -sk "$RUNTIMES" | awk '{print $1}')
    echo "==> 精简无关平台原生库：省下 $(( (BEFORE - AFTER) / 1024 )) MB（保留 ${KEEP}）"
fi

# 内置识别模型（可选，默认不内置）：模型默认在首次使用时联网下载，
# 不计入安装包体积。如果确实想让用户开箱即用、不用联网，
# 把 ggml-*.bin 放到 mac/bundled-models/ 后再执行本脚本即可。
if [ -d bundled-models ] && [ -n "$(ls -A bundled-models 2>/dev/null)" ]; then
    echo "==> 内置模型（会显著增大体积）：$(ls bundled-models | tr '\n' ' ')"
    mkdir -p "$APP/Contents/Resources/models"
    cp bundled-models/*.bin "$APP/Contents/Resources/models/"
fi

# 临时签名（ad-hoc）。没有签名的应用无法通过 TCC 稳定地记住授权，
# 因此这一步是必要的；正式分发仍需 Apple 开发者证书签名并公证。
echo "==> 临时签名"
codesign --force --deep --sign - "$APP" 2>&1 | sed 's/^/    /'

# 打成可上传的压缩包。必须用 ditto：普通 zip 会丢掉符号链接与扩展属性，
# 解压出来的 .app 可能无法运行或签名失效。
# 命名对齐 Windows 版约定：带 -withruntime 的是自包含版。
ARCH="${RID#osx-}"
if [ "$SELF_CONTAINED" = true ]; then
    ZIP="$OUT_DIR/LiveCaptionsTranslator-macOS-${ARCH}-withruntime.zip"
else
    ZIP="$OUT_DIR/LiveCaptionsTranslator-macOS-${ARCH}.zip"
fi
rm -f "$ZIP"
ditto -c -k --sequesterRsrc --keepParent "$APP" "$ZIP"

BUNDLE_ID=$(plutil -extract CFBundleIdentifier raw Info.plist)

echo
echo "完成："
echo "  应用：$APP  （$(du -sh "$APP" | awk '{print $1}')）"
echo "  可分发压缩包：$ZIP  （$(du -sh "$ZIP" | awk '{print $1}')）"
echo
if [ "$SELF_CONTAINED" != true ]; then
    echo "  ⚠ 本包不含 .NET 运行时，使用者需先安装 .NET 10 运行时："
    echo "      brew install --cask dotnet-sdk"
    echo "      或从 https://dotnet.microsoft.com/download 下载 Runtime 安装包"
    echo
fi
echo "  运行：open \"$APP\""
echo "  安装：拖进「应用程序」文件夹"
echo
echo "提示：未经 Apple 证书签名，别人首次打开会被 Gatekeeper 拦下，"
echo "      需在该应用上右键选择「打开」，或执行："
echo "      xattr -dr com.apple.quarantine \"$APP\""
echo
echo "      另：临时签名每次打包都会变，系统可能因此“忘记”已授予的权限。"
echo "      若启动后报无权限或无法初始化设备，执行："
echo "      tccutil reset Microphone ${BUNDLE_ID}"
echo "      tccutil reset SpeechRecognition ${BUNDLE_ID}"
