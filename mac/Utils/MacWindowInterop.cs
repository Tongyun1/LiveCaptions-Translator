using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LiveCaptionsTranslator.Mac.Utils;

/// <summary>
/// macOS 原生窗口互操作（通过 Objective-C 运行时，纯 C# P/Invoke，无需 Swift/Xcode）。
/// 核心功能：将 Avalonia 创建的 NSWindow 运行时转换为 NSPanel，使悬浮窗能浮在
/// 其它 App 的全屏空间（Full Screen Space）之上。
///
/// 实现细节：直接 object_setClass 到纯 NSPanel 会导致 Avalonia 原生层调用自定义方法
/// （如 getExtendedTitleBarHeight）时崩溃。因此我们在运行时动态创建一个 NSPanel
/// 子类（_AvnFloatingPanel），并把 Avalonia 原始窗口类的自定义方法复制过来，
/// 这样既有 NSPanel 的全屏行为，又保留了 Avalonia 需要的自定义方法。
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacWindowInterop
{
    private const string Libobjc = "/usr/lib/libobjc.dylib";

    // 消息发送
    [DllImport(Libobjc, EntryPoint = "sel_registerName")]
    private static extern IntPtr SelRegisterName([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgSend(IntPtr receiver, IntPtr selector);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern void MsgSendVoidUInt(IntPtr receiver, IntPtr selector, ulong arg);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern void MsgSendVoidLong(IntPtr receiver, IntPtr selector, long arg);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern void MsgSendVoidBool(IntPtr receiver, IntPtr selector,
        [MarshalAs(UnmanagedType.I1)] bool arg);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern ulong MsgSendRetULong(IntPtr receiver, IntPtr selector);

    // 运行时类操作
    [DllImport(Libobjc, EntryPoint = "objc_getClass")]
    private static extern IntPtr ObjcGetClass([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(Libobjc, EntryPoint = "object_setClass")]
    private static extern IntPtr ObjectSetClass(IntPtr obj, IntPtr cls);

    [DllImport(Libobjc, EntryPoint = "object_getClass")]
    private static extern IntPtr ObjectGetClass(IntPtr obj);

    [DllImport(Libobjc, EntryPoint = "object_getClassName")]
    private static extern IntPtr ObjectGetClassName(IntPtr obj);

    [DllImport(Libobjc, EntryPoint = "class_getSuperclass")]
    private static extern IntPtr ClassGetSuperclass(IntPtr cls);

    // 动态类创建
    [DllImport(Libobjc, EntryPoint = "objc_allocateClassPair")]
    private static extern IntPtr ObjcAllocateClassPair(IntPtr superclass,
        [MarshalAs(UnmanagedType.LPStr)] string name, int extraBytes);

    [DllImport(Libobjc, EntryPoint = "objc_registerClassPair")]
    private static extern void ObjcRegisterClassPair(IntPtr cls);

    // 方法操作
    [DllImport(Libobjc, EntryPoint = "class_copyMethodList")]
    private static extern IntPtr ClassCopyMethodList(IntPtr cls, out uint count);

    [DllImport(Libobjc, EntryPoint = "class_addMethod")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool ClassAddMethod(IntPtr cls, IntPtr sel, IntPtr imp,
        [MarshalAs(UnmanagedType.LPStr)] string types);

    [DllImport(Libobjc, EntryPoint = "class_respondsToSelector")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool ClassRespondsToSelector(IntPtr cls, IntPtr sel);

    [DllImport(Libobjc, EntryPoint = "method_getName")]
    private static extern IntPtr MethodGetName(IntPtr method);

    [DllImport(Libobjc, EntryPoint = "method_getImplementation")]
    private static extern IntPtr MethodGetImplementation(IntPtr method);

    [DllImport(Libobjc, EntryPoint = "method_getTypeEncoding")]
    private static extern IntPtr MethodGetTypeEncoding(IntPtr method);

    [DllImport("libSystem.dylib", EntryPoint = "free")]
    private static extern void Free(IntPtr ptr);

    // NSWindowCollectionBehavior 标志位
    private const ulong CanJoinAllSpaces = 1UL << 0;
    private const ulong FullScreenAuxiliary = 1UL << 8;

    // NSWindowStyleMask
    private const ulong NonactivatingPanel = 1UL << 7;  // 128

    // Window levels
    private const long StatusWindowLevel = 25;

    // 缓存动态创建的类
    private static IntPtr _floatingPanelClass = IntPtr.Zero;

    /// <summary>
    /// 将 Avalonia 的 NSWindow 转换为自定义的 NSPanel 子类并设置全屏悬浮属性。
    /// 动态创建的子类保留了 Avalonia 的自定义方法，避免 unrecognized selector 崩溃。
    /// 调用时机：窗口打开后只需调用一次。
    /// </summary>
    public static bool ConvertToFloatingPanel(IntPtr handle, string? descriptor)
    {
        if (handle == IntPtr.Zero)
            return false;

        IntPtr window;
        if (string.Equals(descriptor, "NSWindow", StringComparison.OrdinalIgnoreCase))
            window = handle;
        else if (string.Equals(descriptor, "NSView", StringComparison.OrdinalIgnoreCase))
            window = MsgSend(handle, SelRegisterName("window"));
        else
        {
            Console.Error.WriteLine($"[MacWindowInterop] 未知句柄类型 '{descriptor}'");
            return false;
        }

        if (window == IntPtr.Zero)
        {
            Console.Error.WriteLine("[MacWindowInterop] 未获取到 NSWindow");
            return false;
        }

        // 获取或创建动态的 _AvnFloatingPanel 类
        IntPtr floatingPanelClass = GetOrCreateFloatingPanelClass(window);
        if (floatingPanelClass == IntPtr.Zero)
        {
            Console.Error.WriteLine("[MacWindowInterop] 无法创建动态 Panel 类");
            return false;
        }

        // 运行时替换窗口类
        IntPtr oldClass = ObjectSetClass(window, floatingPanelClass);
        if (oldClass == IntPtr.Zero)
        {
            Console.Error.WriteLine("[MacWindowInterop] object_setClass 失败");
            return false;
        }

        // 设置 styleMask：加入 nonactivatingPanel 位
        ulong currentMask = MsgSendRetULong(window, SelRegisterName("styleMask"));
        MsgSendVoidUInt(window, SelRegisterName("setStyleMask:"), currentMask | NonactivatingPanel);

        // 设置 NSPanel 特有属性
        MsgSendVoidBool(window, SelRegisterName("setFloatingPanel:"), true);
        MsgSendVoidBool(window, SelRegisterName("setBecomesKeyOnlyIfNeeded:"), true);
        MsgSendVoidBool(window, SelRegisterName("setHidesOnDeactivate:"), false);

        // 设置 collectionBehavior
        MsgSendVoidUInt(window, SelRegisterName("setCollectionBehavior:"),
            CanJoinAllSpaces | FullScreenAuxiliary);

        // 设置窗口层级
        MsgSendVoidLong(window, SelRegisterName("setLevel:"), StatusWindowLevel);

        IntPtr classNamePtr = ObjectGetClassName(window);
        string? className = classNamePtr != IntPtr.Zero
            ? Marshal.PtrToStringAnsi(classNamePtr) : "?";
        Console.Error.WriteLine(
            $"[MacWindowInterop] 成功转换为浮动面板 (class={className}, level={StatusWindowLevel})");
        return true;
    }

    /// <summary>
    /// 获取或创建 _AvnFloatingPanel 动态类。
    /// 该类继承自 NSPanel，同时复制了 Avalonia 原始窗口类上的所有自定义方法。
    /// </summary>
    private static IntPtr GetOrCreateFloatingPanelClass(IntPtr window)
    {
        if (_floatingPanelClass != IntPtr.Zero)
            return _floatingPanelClass;

        IntPtr panelClass = ObjcGetClass("NSPanel");
        IntPtr nsWindowClass = ObjcGetClass("NSWindow");
        IntPtr originalClass = ObjectGetClass(window);

        if (panelClass == IntPtr.Zero || nsWindowClass == IntPtr.Zero || originalClass == IntPtr.Zero)
            return IntPtr.Zero;

        // 创建 NSPanel 的动态子类
        IntPtr newClass = ObjcAllocateClassPair(panelClass, "_AvnFloatingPanel", 0);
        if (newClass == IntPtr.Zero)
        {
            // 可能已经创建过（重入），尝试获取
            _floatingPanelClass = ObjcGetClass("_AvnFloatingPanel");
            return _floatingPanelClass;
        }

        // 从 Avalonia 的原始窗口类链上复制自定义方法到新类
        // （遍历从 originalClass 到 NSWindow 之间的每一层，复制 NSPanel 没有的方法）
        IntPtr cls = originalClass;
        int methodsCopied = 0;
        while (cls != IntPtr.Zero && cls != nsWindowClass && cls != panelClass)
        {
            methodsCopied += CopyNewMethods(cls, newClass, panelClass);
            cls = ClassGetSuperclass(cls);
        }

        ObjcRegisterClassPair(newClass);
        _floatingPanelClass = newClass;

        Console.Error.WriteLine(
            $"[MacWindowInterop] 创建动态类 _AvnFloatingPanel，复制了 {methodsCopied} 个自定义方法");
        return newClass;
    }

    /// <summary>
    /// 将 sourceClass 上定义的、但 NSPanel 没有的方法复制到 targetClass。
    /// </summary>
    private static int CopyNewMethods(IntPtr sourceClass, IntPtr targetClass, IntPtr panelClass)
    {
        uint count;
        IntPtr methods = ClassCopyMethodList(sourceClass, out count);
        if (methods == IntPtr.Zero || count == 0)
            return 0;

        int copied = 0;
        try
        {
            for (uint i = 0; i < count; i++)
            {
                IntPtr method = Marshal.ReadIntPtr(methods, (int)(i * IntPtr.Size));
                IntPtr sel = MethodGetName(method);

                // 只复制 NSPanel 类链上不存在的方法（Avalonia 自定义的）
                if (!ClassRespondsToSelector(panelClass, sel))
                {
                    IntPtr imp = MethodGetImplementation(method);
                    IntPtr typeEncPtr = MethodGetTypeEncoding(method);
                    string? types = typeEncPtr != IntPtr.Zero
                        ? Marshal.PtrToStringAnsi(typeEncPtr) : null;

                    if (types != null && imp != IntPtr.Zero)
                    {
                        ClassAddMethod(targetClass, sel, imp, types);
                        copied++;
                    }
                }
            }
        }
        finally
        {
            Free(methods);
        }
        return copied;
    }
}
