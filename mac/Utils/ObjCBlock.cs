using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LiveCaptionsTranslator.Mac.Utils;

/// <summary>
/// 从 C# 构造 Objective-C block。部分系统 API（如权限申请）必须传入真实 block，
/// 传 nil 不会触发系统弹框（已实测）。
///
/// block 是固定布局的结构体：isa 指向 _NSConcreteGlobalBlock、标志位、实现函数指针、
/// 描述符。标记为全局 block 可避开引用计数与拷贝/释放辅助函数，最为简单稳妥。
/// 布局错误会导致原生崩溃，改动需谨慎。
/// </summary>
[SupportedOSPlatform("macos")]
internal static class ObjCBlock
{
    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern IntPtr dlsym(IntPtr handle, string symbol);

    /// <summary>RTLD_DEFAULT，在已加载的镜像中按默认顺序查找符号。</summary>
    private static readonly IntPtr RtldDefault = new(-2);

    private const int BlockIsGlobal = 1 << 28;

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockDescriptor
    {
        public nuint Reserved;
        public nuint Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockLiteral
    {
        public IntPtr Isa;
        public int Flags;
        public int Reserved;
        public IntPtr Invoke;
        public IntPtr Descriptor;
    }

    /// <summary>
    /// 创建一个全局 block，其实现为 <paramref name="invoke"/>（须是
    /// [UnmanagedCallersOnly] 方法的函数指针，第一个参数为 block 自身）。
    /// 返回的内存故意不释放：全局 block 需在进程存续期内一直有效。
    /// </summary>
    public static IntPtr CreateGlobal(IntPtr invoke)
    {
        IntPtr globalBlockClass = dlsym(RtldDefault, "_NSConcreteGlobalBlock");
        if (globalBlockClass == IntPtr.Zero || invoke == IntPtr.Zero)
            return IntPtr.Zero;

        IntPtr descriptor = Marshal.AllocHGlobal(Marshal.SizeOf<BlockDescriptor>());
        Marshal.StructureToPtr(new BlockDescriptor
        {
            Reserved = 0,
            Size = (nuint)Marshal.SizeOf<BlockLiteral>()
        }, descriptor, false);

        IntPtr block = Marshal.AllocHGlobal(Marshal.SizeOf<BlockLiteral>());
        Marshal.StructureToPtr(new BlockLiteral
        {
            Isa = globalBlockClass,
            Flags = BlockIsGlobal,
            Reserved = 0,
            Invoke = invoke,
            Descriptor = descriptor
        }, block, false);

        return block;
    }
}
