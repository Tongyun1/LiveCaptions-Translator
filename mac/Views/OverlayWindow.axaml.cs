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
    private DispatcherTimer? _reapplyTimer;

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
            EnableFullscreenOverlay();
            // Avalonia 会在初始化/激活时重置窗口层级，导致全屏时被盖住；
            // 因此周期性地重新应用，确保悬浮窗始终位于全屏内容之上。
            _reapplyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _reapplyTimer.Tick += (_, _) => EnableFullscreenOverlay();
            _reapplyTimer.Start();
        };
        Closed += (_, _) => _reapplyTimer?.Stop();
    }

    /// <summary>让悬浮窗能浮在全屏 App 之上（macOS 原生行为）。</summary>
    private void EnableFullscreenOverlay()
    {
        if (!OperatingSystem.IsMacOS())
            return;
        try
        {
            var platformHandle = TryGetPlatformHandle();
            IntPtr handle = platformHandle?.Handle ?? IntPtr.Zero;
            MacWindowInterop.EnableOverlayOverFullscreen(handle, platformHandle?.HandleDescriptor);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[OverlayWindow] 设置全屏悬浮失败: {ex.Message}");
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
