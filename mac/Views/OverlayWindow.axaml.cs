using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

using LiveCaptionsTranslator.Mac.Utils;

namespace LiveCaptionsTranslator.Mac.Views;

/// <summary>
/// 悬浮字幕窗：无边框、置顶、半透明、可拖动、可缩放。显示译文（大）与原文（小）。
/// 对应 Windows 版 OverlayWindow 的核心行为（简化实现，独立于 Windows 代码）。
/// </summary>
public partial class OverlayWindow : Window
{
    private double _translationFontSize = 22;
    private double _originalFontSize = 15;

    private bool _resizing;

    /// <summary>NSPanel 转换前的原始窗口类，关闭前需还原。</summary>
    private IntPtr _originalWindowClass = IntPtr.Zero;
    private bool _closing;

    public OverlayWindow()
    {
        InitializeComponent();

        RootBorder.PointerPressed += OnDragStart;
        PointerEntered += (_, _) => Toolbar.IsVisible = true;
        PointerExited += (_, _) => Toolbar.IsVisible = false;

        ResizeGrip.Cursor = new Cursor(StandardCursorType.BottomRightCorner);
        ResizeGrip.PointerPressed += OnResizePressed;
        ResizeGrip.PointerMoved += OnResizeMoved;
        ResizeGrip.PointerReleased += OnResizeReleased;

        FontIncreaseButton.Click += (_, _) => AdjustFontSize(+2);
        FontDecreaseButton.Click += (_, _) => AdjustFontSize(-2);
        CloseButton.Click += (_, _) => Close();

        Opened += (_, _) =>
        {
            MoveToBottomCenter();
            ConvertToPanel();
            // 延迟再设一次,对抗 Avalonia 在窗口完全就绪后可能的层级重置
            DispatcherTimer.RunOnce(ConvertToPanel, TimeSpan.FromMilliseconds(500));
        };
        Closing += (_, _) =>
        {
            _closing = true;
            RestoreWindowClass();
        };
    }

    /// <summary>
    /// 将窗口转换为 NSPanel（一次性）。转换后悬浮窗可浮在其它 App 的全屏之上。
    /// </summary>
    private void ConvertToPanel()
    {
        if (_closing || _originalWindowClass != IntPtr.Zero || !OperatingSystem.IsMacOS())
            return;
        try
        {
            var platformHandle = TryGetPlatformHandle();
            _originalWindowClass = MacWindowInterop.ConvertToFloatingPanel(
                platformHandle?.Handle ?? IntPtr.Zero, platformHandle?.HandleDescriptor);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[OverlayWindow] NSPanel 转换失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 关闭前还原窗口类。不还原的话，NSWindow dealloc 时 AppKit 注销 KVO
    /// 观察者会因为 isa 已被替换而抛 NSRangeException，导致退出时崩溃。
    /// </summary>
    private void RestoreWindowClass()
    {
        if (_originalWindowClass == IntPtr.Zero || !OperatingSystem.IsMacOS())
            return;
        try
        {
            var platformHandle = TryGetPlatformHandle();
            MacWindowInterop.RestoreClass(
                platformHandle?.Handle ?? IntPtr.Zero,
                platformHandle?.HandleDescriptor,
                _originalWindowClass);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[OverlayWindow] 还原窗口类失败: {ex.Message}");
        }
        finally
        {
            _originalWindowClass = IntPtr.Zero;
        }
    }

    /// <summary>更新原文。</summary>
    public void UpdateOriginal(string text) => OriginalText.Text = text;

    /// <summary>更新译文。</summary>
    public void UpdateTranslation(string text) => TranslationText.Text = text;

    private void OnDragStart(object? sender, PointerPressedEventArgs e)
    {
        if (_resizing)
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        try
        {
            BeginMoveDrag(e);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[OverlayWindow] 拖动失败: {ex.Message}");
        }
    }

    // 手动缩放：SE 方向拖动时窗口左上角固定，指针相对窗口的坐标即为期望的宽高。
    private void OnResizePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        _resizing = true;
        e.Pointer.Capture(ResizeGrip);
        e.Handled = true;
    }

    private void OnResizeMoved(object? sender, PointerEventArgs e)
    {
        if (!_resizing)
            return;
        Point p = e.GetPosition(this);
        Width = Math.Max(MinWidth, p.X + 8);
        Height = Math.Max(MinHeight, p.Y + 8);
        e.Handled = true;
    }

    private void OnResizeReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_resizing)
            return;
        _resizing = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void AdjustFontSize(double delta)
    {
        _translationFontSize = Math.Clamp(_translationFontSize + delta, 12, 48);
        _originalFontSize = Math.Clamp(_originalFontSize + delta, 10, 40);
        TranslationText.FontSize = _translationFontSize;
        OriginalText.FontSize = _originalFontSize;
    }

    private void MoveToBottomCenter()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
            return;

        var area = screen.WorkingArea;
        int x = area.X + (area.Width - (int)(Width * screen.Scaling)) / 2;
        int y = area.Y + area.Height - (int)(Height * screen.Scaling) - 60;
        Position = new PixelPoint(x, y);
    }
}
