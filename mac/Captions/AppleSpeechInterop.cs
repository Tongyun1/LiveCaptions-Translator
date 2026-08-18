using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

using LiveCaptionsTranslator.Mac.Utils;

namespace LiveCaptionsTranslator.Mac.Captions;

/// <summary>
/// macOS 系统语音识别（SFSpeechRecognizer）互操作，纯 C# P/Invoke 调 Objective-C 运行时，
/// 无需 Swift/Xcode。
///
/// 两个前提均已实测确认，改动时勿破坏：
/// 1) 进程必须作为独立 .app 启动，且 Info.plist 含 NSSpeechRecognitionUsageDescription。
///    否则 TCC 会把权限归属到父进程（终端/IDE），调用 requestAuthorization 会直接终止进程。
/// 2) 回调依赖运行中的 run loop。用信号量等阻塞主线程会导致回调永不投递
///    （Avalonia 应用本身跑 run loop，因此正常运行时满足）。
///
/// 回调用 delegate 版 API（recognitionTaskWithRequest:delegate:），
/// 从而避免从 C# 构造 Objective-C block。
///
/// 内存所有权：这里没有 ARC，必须手工遵守 Cocoa 命名约定。
/// alloc/init 得到的对象归调用方持有，用完要 <see cref="Release"/>；
/// 其它方法（如 recognitionTaskWithRequest:delegate:、stringWithUTF8String:）
/// 返回的是 autorelease 对象，**绝不能** release，否则过释放崩溃；
/// 但它们需要所在线程有 autorelease 池，非主线程（如音频回调）需自己开池，
/// 见 <see cref="BeginAutoreleasePool"/>。
/// </summary>
[SupportedOSPlatform("macos")]
internal static class AppleSpeechInterop
{
    private const string Objc = "/usr/lib/libobjc.dylib";

    [DllImport(Objc)] private static extern IntPtr objc_getClass(string name);
    [DllImport(Objc)] private static extern IntPtr sel_registerName(string name);
    [DllImport(Objc)] private static extern IntPtr objc_allocateClassPair(IntPtr super, string name, int extra);
    [DllImport(Objc)] private static extern void objc_registerClassPair(IntPtr cls);
    [DllImport(Objc)] [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool class_addMethod(IntPtr cls, IntPtr sel, IntPtr imp, string types);

    [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr r, IntPtr s);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern IntPtr SendP(IntPtr r, IntPtr s, IntPtr a);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPP(IntPtr r, IntPtr s, IntPtr a, IntPtr b);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern long SendLong(IntPtr r, IntPtr s);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendBool(IntPtr r, IntPtr s);
    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern void SendVoidBool(IntPtr r, IntPtr s, [MarshalAs(UnmanagedType.I1)] bool a);
    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern void SendVoidUInt(IntPtr r, IntPtr s, uint a);
    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendInitFormat(IntPtr r, IntPtr s,
        ulong commonFormat, double sampleRate, uint channels,
        [MarshalAs(UnmanagedType.I1)] bool interleaved);
    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendInitBuffer(IntPtr r, IntPtr s, IntPtr format, uint frameCapacity);

    [DllImport("/usr/lib/libSystem.dylib")] private static extern IntPtr dlopen(string path, int mode);
    [DllImport("/usr/lib/libSystem.dylib")] private static extern IntPtr dlsym(IntPtr handle, string symbol);

    private static IntPtr Sel(string s) => sel_registerName(s);

    // ---------- 内存管理 ----------

    [DllImport(Objc)] private static extern IntPtr objc_autoreleasePoolPush();
    [DllImport(Objc)] private static extern void objc_autoreleasePoolPop(IntPtr pool);

    /// <summary>
    /// 释放一个由 alloc/init 得到的对象。不要用在 autorelease 对象上。
    /// </summary>
    public static void Release(IntPtr obj)
    {
        if (obj != IntPtr.Zero) Send(obj, Sel("release"));
    }

    /// <summary>
    /// 开一个 autorelease 池。音频回调等非主线程没有现成的池，
    /// 不开的话系统内部产生的 autorelease 对象会一直累积。
    /// 必须配对 <see cref="DrainAutoreleasePool"/>（用 try/finally）。
    /// </summary>
    public static IntPtr BeginAutoreleasePool() => objc_autoreleasePoolPush();

    /// <summary>排空并关闭 <see cref="BeginAutoreleasePool"/> 开的池。</summary>
    public static void DrainAutoreleasePool(IntPtr pool)
    {
        if (pool != IntPtr.Zero) objc_autoreleasePoolPop(pool);
    }

    private static string? FromNsString(IntPtr ns) => ns == IntPtr.Zero
        ? null : Marshal.PtrToStringUTF8(Send(ns, Sel("UTF8String")));

    private static IntPtr ToNsString(string s)
    {
        IntPtr utf8 = Marshal.StringToCoTaskMemUTF8(s);
        try { return SendP(objc_getClass("NSString"), Sel("stringWithUTF8String:"), utf8); }
        finally { Marshal.FreeCoTaskMem(utf8); }
    }

    private static bool _loaded;

    /// <summary>加载所需系统框架。多次调用无副作用。</summary>
    public static void EnsureFrameworksLoaded()
    {
        if (_loaded) return;
        dlopen("/System/Library/Frameworks/Speech.framework/Speech", 2);
        dlopen("/System/Library/Frameworks/AVFoundation.framework/AVFoundation", 2);
        _loaded = true;
    }

    // ---------- 授权 ----------

    /// <summary>授权状态：0 未决定、1 已拒绝、2 受限、3 已授权。</summary>
    public static long AuthorizationStatus() =>
        SendLong(objc_getClass("SFSpeechRecognizer"), Sel("authorizationStatus"));

    // ---------- 授权回调 ----------
    //
    // requestAuthorization: 必须传一个真实的 block，传 nil 不会弹出授权对话框（已实测）。

    /// <summary>授权回调写入的结果（-1 表示尚未回调）。</summary>
    private static long _authCallbackStatus = -1;

    private static IntPtr _authBlock;

    [UnmanagedCallersOnly]
    private static void OnAuthorizationResult(IntPtr block, long status)
    {
        // 异常绝不能跨回原生栈
        try { Volatile.Write(ref _authCallbackStatus, status); }
        catch { }
    }

    /// <summary>
    /// 发起授权请求。必须传真实 block，否则系统不会弹框。
    /// 结果既会写入回调，也可通过 <see cref="AuthorizationStatus"/> 轮询。
    /// </summary>
    public static unsafe void RequestAuthorization()
    {
        Volatile.Write(ref _authCallbackStatus, -1);
        if (_authBlock == IntPtr.Zero)
        {
            _authBlock = ObjCBlock.CreateGlobal(
                (IntPtr)(delegate* unmanaged<IntPtr, long, void>)&OnAuthorizationResult);
        }
        SendP(objc_getClass("SFSpeechRecognizer"), Sel("requestAuthorization:"), _authBlock);
    }

    /// <summary>读取授权回调结果；-1 表示尚未回调。</summary>
    public static long AuthorizationCallbackResult() => Volatile.Read(ref _authCallbackStatus);

    // ---------- 识别器 ----------

    /// <summary>按语言创建识别器；该语言不受支持时返回 IntPtr.Zero。</summary>
    public static IntPtr CreateRecognizer(string localeId)
    {
        IntPtr locale = SendP(objc_getClass("NSLocale"),
            Sel("localeWithLocaleIdentifier:"), ToNsString(localeId));
        return SendP(Send(objc_getClass("SFSpeechRecognizer"), Sel("alloc")),
            Sel("initWithLocale:"), locale);
    }

    public static bool IsAvailable(IntPtr recognizer) =>
        recognizer != IntPtr.Zero && SendBool(recognizer, Sel("isAvailable"));

    /// <summary>该语言是否支持设备上（离线）识别。</summary>
    public static bool SupportsOnDevice(IntPtr recognizer) =>
        recognizer != IntPtr.Zero && SendBool(recognizer, Sel("supportsOnDeviceRecognition"));

    /// <summary>系统支持的语言代码列表。</summary>
    public static string[] SupportedLocales()
    {
        EnsureFrameworksLoaded();
        IntPtr set = Send(objc_getClass("SFSpeechRecognizer"), Sel("supportedLocales"));
        IntPtr arr = Send(set, Sel("allObjects"));
        long count = SendLong(arr, Sel("count"));
        var result = new string[Math.Max(0, count)];
        for (long i = 0; i < count; i++)
        {
            IntPtr locale = SendIndex(arr, Sel("objectAtIndex:"), (nuint)i);
            result[i] = FromNsString(Send(locale, Sel("localeIdentifier"))) ?? string.Empty;
        }
        return result;
    }

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIndex(IntPtr r, IntPtr s, nuint i);

    // ---------- 识别请求 ----------

    /// <summary>创建可持续追加音频的识别请求，用于实时字幕。</summary>
    public static IntPtr CreateBufferRequest(bool requireOnDevice)
    {
        IntPtr req = Send(Send(objc_getClass("SFSpeechAudioBufferRecognitionRequest"), Sel("alloc")),
            Sel("init"));
        SendVoidBool(req, Sel("setShouldReportPartialResults:"), true);
        if (requireOnDevice)
            SendVoidBool(req, Sel("setRequiresOnDeviceRecognition:"), true);
        return req;
    }

    /// <summary>告知请求音频已结束，识别器随后给出最终结果。</summary>
    public static void EndAudio(IntPtr request)
    {
        if (request != IntPtr.Zero) Send(request, Sel("endAudio"));
    }

    /// <summary>取消识别任务。</summary>
    public static void CancelTask(IntPtr task)
    {
        if (task != IntPtr.Zero) Send(task, Sel("cancel"));
    }

    /// <summary>用 delegate 版 API 启动识别任务。</summary>
    public static IntPtr StartTask(IntPtr recognizer, IntPtr request) =>
        SendPP(recognizer, Sel("recognitionTaskWithRequest:delegate:"), request, GetDelegate());

    // ---------- 音频喂入 ----------

    private static IntPtr _format16kMono;

    /// <summary>16kHz 单声道 float32 的 AVAudioFormat（缓存复用）。</summary>
    private static IntPtr Format16kMono()
    {
        if (_format16kMono != IntPtr.Zero) return _format16kMono;
        // AVAudioPCMFormatFloat32 = 1
        _format16kMono = SendInitFormat(
            Send(objc_getClass("AVAudioFormat"), Sel("alloc")),
            Sel("initWithCommonFormat:sampleRate:channels:interleaved:"),
            1UL, 16000.0, 1U, false);
        return _format16kMono;
    }

    /// <summary>
    /// 把 16kHz 单声道 float 采样包成 AVAudioPCMBuffer 并追加到识别请求。
    /// </summary>
    public static unsafe bool AppendSamples(IntPtr request, float[] samples, int count)
    {
        if (request == IntPtr.Zero || count <= 0) return false;

        IntPtr format = Format16kMono();
        if (format == IntPtr.Zero) return false;

        IntPtr buffer = SendInitBuffer(
            Send(objc_getClass("AVAudioPCMBuffer"), Sel("alloc")),
            Sel("initWithPCMFormat:frameCapacity:"), format, (uint)count);
        if (buffer == IntPtr.Zero) return false;

        SendVoidUInt(buffer, Sel("setFrameLength:"), (uint)count);

        try
        {
            // floatChannelData 是 float* const*，取第 0 声道
            IntPtr channels = Send(buffer, Sel("floatChannelData"));
            if (channels == IntPtr.Zero) return false;
            IntPtr channel0 = Marshal.ReadIntPtr(channels);
            if (channel0 == IntPtr.Zero) return false;

            fixed (float* src = samples)
                Buffer.MemoryCopy(src, (void*)channel0, (long)count * sizeof(float),
                    (long)count * sizeof(float));

            SendP(request, Sel("appendAudioPCMBuffer:"), buffer);
            return true;
        }
        finally
        {
            // 必须释放：音频回调约每秒 100 次，不释放的话内存约每分钟涨 6MB（已实测）。
            // append 后该对象的 retainCount 会从 1 变 2（已实测），说明请求自己持有了一份，
            // 因此释放我们那一份是安全的。
            Release(buffer);
        }
    }

    // ---------- delegate 动态类 ----------

    private static IntPtr _delegateInstance;

    /// <summary>识别到文本时触发；第二个参数表示是否为该段的最终结果。</summary>
    public static event Action<string, bool>? TranscriptionReceived;

    [UnmanagedCallersOnly]
    private static void OnHypothesize(IntPtr self, IntPtr sel, IntPtr task, IntPtr transcription)
    {
        // 异常绝不能跨回原生栈，否则进程直接终止
        try
        {
            string? text = FromNsString(Send(transcription, Sel("formattedString")));
            if (!string.IsNullOrWhiteSpace(text))
                TranscriptionReceived?.Invoke(text!, false);
        }
        catch { }
    }

    [UnmanagedCallersOnly]
    private static void OnFinishRecognition(IntPtr self, IntPtr sel, IntPtr task, IntPtr result)
    {
        try
        {
            string? text = FromNsString(
                Send(Send(result, Sel("bestTranscription")), Sel("formattedString")));
            if (!string.IsNullOrWhiteSpace(text))
                TranscriptionReceived?.Invoke(text!, true);
        }
        catch { }
    }

    /// <summary>创建并缓存实现 SFSpeechRecognitionTaskDelegate 的对象。</summary>
    private static unsafe IntPtr GetDelegate()
    {
        if (_delegateInstance != IntPtr.Zero)
            return _delegateInstance;

        IntPtr cls = objc_allocateClassPair(objc_getClass("NSObject"), "_LctSpeechDelegate", 0);
        if (cls == IntPtr.Zero)
        {
            cls = objc_getClass("_LctSpeechDelegate");   // 已注册过
        }
        else
        {
            class_addMethod(cls, Sel("speechRecognitionTask:didHypothesizeTranscription:"),
                (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, void>)&OnHypothesize, "v@:@@");
            class_addMethod(cls, Sel("speechRecognitionTask:didFinishRecognition:"),
                (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, void>)&OnFinishRecognition, "v@:@@");
            objc_registerClassPair(cls);
        }

        _delegateInstance = Send(Send(cls, Sel("alloc")), Sel("init"));
        return _delegateInstance;
    }
}
