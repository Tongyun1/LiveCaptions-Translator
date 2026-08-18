using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LiveCaptionsTranslator.Mac.Utils;

/// <summary>
/// macOS 原生窗口互操作（通过 Objective-C 运行时，纯 C# P/Invoke，无需 Swift/Xcode）。
/// 用于让悬浮窗能浮在其它 App 的全屏空间之上——这是 Avalonia 的 Topmost 无法做到的，
/// 必须设置 NSWindow 的 collectionBehavior 与更高的 window level。
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacWindowInterop
{
    private const string Libobjc = "/usr/lib/libobjc.dylib";

    [DllImport(Libobjc, EntryPoint = "sel_registerName")]
    private static extern IntPtr SelRegisterName([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgSend(IntPtr receiver, IntPtr selector);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern void MsgSendVoidUInt(IntPtr receiver, IntPtr selector, ulong arg);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern void MsgSendVoidLong(IntPtr receiver, IntPtr selector, long arg);

    // NSWindowCollectionBehavior 标志位
    private const ulong CanJoinAllSpaces = 1UL << 0;      // 出现在所有 Space
    private const ulong FullScreenAuxiliary = 1UL << 8;   // 可浮于全屏 App 之上

    // NSScreenSaverWindowLevel，足够高以覆盖全屏内容
    private const long ScreenSaverLevel = 1000;

    /// <summary>
    /// 让指定句柄对应的 NSWindow 能浮在全屏 App 之上。
    /// 为避免向未知类型对象发送选择子导致原生崩溃，
    /// 仅在句柄类型为 NSView 或 NSWindow 时处理，其余一律安全跳过。
    /// </summary>
    /// <param name="handle">Avalonia 顶层窗口的原生句柄。</param>
    /// <param name="descriptor">句柄类型描述（如 NSView / NSWindow）。</param>
    public static void EnableOverlayOverFullscreen(IntPtr handle, string? descriptor)
    {
        if (handle == IntPtr.Zero)
            return;

        IntPtr window;
        if (string.Equals(descriptor, "NSWindow", StringComparison.OrdinalIgnoreCase))
        {
            window = handle;
        }
        else if (string.Equals(descriptor, "NSView", StringComparison.OrdinalIgnoreCase))
        {
            window = MsgSend(handle, SelRegisterName("window"));
        }
        else
        {
            // 未知类型：不冲击选择子，避免崩溃
            Console.Error.WriteLine($"[MacWindowInterop] 未知句柄类型 '{descriptor}'，已安全跳过全屏悬浮设置");
            return;
        }

        if (window == IntPtr.Zero)
        {
            Console.Error.WriteLine($"[MacWindowInterop] view=0x{handle:X} 但未获取到 NSWindow");
            return;
        }

        MsgSendVoidUInt(window, SelRegisterName("setCollectionBehavior:"),
            CanJoinAllSpaces | FullScreenAuxiliary);
        MsgSendVoidLong(window, SelRegisterName("setLevel:"), ScreenSaverLevel);
    }
}
