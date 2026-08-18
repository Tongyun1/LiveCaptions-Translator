using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace LiveCaptionsTranslator.Mac.Utils;

/// <summary>
/// 麦克风（音频输入）权限。macOS 把从任何输入设备取声都归为麦克风权限，
/// 包括从 BlackHole 这类虚拟声卡读取系统声音。
///
/// 未授权时底层音频库只会报出「无法初始化设备」之类的模糊错误，
/// 因此这里主动申请并给出明确结论。
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacMicrophonePermission
{
    private const string Objc = "/usr/lib/libobjc.dylib";

    [DllImport(Objc)] private static extern IntPtr objc_getClass(string name);
    [DllImport(Objc)] private static extern IntPtr sel_registerName(string name);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern IntPtr SendP(IntPtr r, IntPtr s, IntPtr a);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern long SendLongP(IntPtr r, IntPtr s, IntPtr a);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern void SendVoidPP(IntPtr r, IntPtr s, IntPtr a, IntPtr b);
    [DllImport("/usr/lib/libSystem.dylib")] private static extern IntPtr dlopen(string path, int mode);

    private static IntPtr Sel(string s) => sel_registerName(s);

    /// <summary>AVMediaTypeAudio 的值就是字符串 "soun"。</summary>
    private const string MediaTypeAudio = "soun";

    /// <summary>授权状态：0 未决定、1 受限、2 已拒绝、3 已授权。</summary>
    public enum Status
    {
        NotDetermined = 0,
        Restricted = 1,
        Denied = 2,
        Authorized = 3
    }

    private static long _callbackResult = -1;

    [UnmanagedCallersOnly]
    private static void OnAccessResult(IntPtr block, byte granted)
    {
        // 异常绝不能跨回原生栈
        try { Volatile.Write(ref _callbackResult, granted != 0 ? 1 : 0); }
        catch { }
    }

    private static IntPtr _requestBlock;

    private static IntPtr MakeNsString(string s)
    {
        IntPtr utf8 = Marshal.StringToCoTaskMemUTF8(s);
        try { return SendP(objc_getClass("NSString"), Sel("stringWithUTF8String:"), utf8); }
        finally { Marshal.FreeCoTaskMem(utf8); }
    }

    private static void EnsureLoaded() =>
        dlopen("/System/Library/Frameworks/AVFoundation.framework/AVFoundation", 2);

    /// <summary>查询当前麦克风授权状态。</summary>
    public static Status CurrentStatus()
    {
        EnsureLoaded();
        IntPtr cls = objc_getClass("AVCaptureDevice");
        if (cls == IntPtr.Zero)
            return Status.Authorized;   // 拿不到类时不阻断流程，交由底层报错
        return (Status)SendLongP(cls, Sel("authorizationStatusForMediaType:"),
            MakeNsString(MediaTypeAudio));
    }

    /// <summary>
    /// 确保已获得麦克风权限；未决定时弹出系统对话框并等待用户选择。
    /// </summary>
    /// <returns>最终是否已授权。</returns>
    public static async Task<bool> EnsureAsync(CancellationToken cancellationToken = default)
    {
        Status status = CurrentStatus();
        if (status == Status.Authorized)
            return true;
        if (status is Status.Denied or Status.Restricted)
            return false;

        EnsureLoaded();
        unsafe
        {
            _requestBlock = _requestBlock != IntPtr.Zero
                ? _requestBlock
                : ObjCBlock.CreateGlobal(
                    (IntPtr)(delegate* unmanaged<IntPtr, byte, void>)&OnAccessResult);
        }
        if (_requestBlock == IntPtr.Zero)
            return false;

        Volatile.Write(ref _callbackResult, -1);
        SendVoidPP(objc_getClass("AVCaptureDevice"),
            Sel("requestAccessForMediaType:completionHandler:"),
            MakeNsString(MediaTypeAudio), _requestBlock);

        // 等用户在系统对话框上做选择
        for (int i = 0; i < 120; i++)
        {
            await Task.Delay(500, cancellationToken);
            if (Volatile.Read(ref _callbackResult) >= 0)
                break;
            if (CurrentStatus() != Status.NotDetermined)
                break;
        }

        return CurrentStatus() == Status.Authorized;
    }
}
