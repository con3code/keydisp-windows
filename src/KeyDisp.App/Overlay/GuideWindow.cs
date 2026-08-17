using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using KeyDisp.Core.Screens;
using static KeyDisp.App.Interop.NativeMethods;

namespace KeyDisp.App.Overlay;

/// <summary>
/// 画面中心の点線ガイド (Mac 版 CenterGuideView)。
/// 編集モードのドラッグで中心に吸着したときだけ、対象画面の全面に表示する。
/// </summary>
public sealed class GuideWindow : Window
{
    private static readonly Brush GuideBrush =
        new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00)); // アプリのアクセント (アンバー)

    private readonly Line _vertical;
    private readonly Line _horizontal;
    private readonly Canvas _canvas;
    private IntPtr _hwnd;

    public GuideWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;

        _vertical = MakeLine();
        _horizontal = MakeLine();
        _canvas = new Canvas();
        _canvas.Children.Add(_vertical);
        _canvas.Children.Add(_horizontal);
        Content = _canvas;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            var ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
            ex |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(ex));
        };
    }

    private static Line MakeLine() => new()
    {
        Stroke = GuideBrush,
        StrokeThickness = 1.5,
        StrokeDashArray = new DoubleCollection { 6, 5 },
        Visibility = Visibility.Collapsed,
    };

    /// <summary>対象画面 (物理 px) に合わせて表示する。</summary>
    public void ShowGuides(RectD screenBounds, bool vertical, bool horizontal)
    {
        if (!vertical && !horizontal)
        {
            HideGuides();
            return;
        }
        if (!IsVisible) Show();
        // 物理 px で画面全面へ (DPI 換算は SetWindowPos なら不要)
        SetWindowPos(_hwnd, HWND_TOPMOST,
            (int)screenBounds.X, (int)screenBounds.Y,
            (int)screenBounds.Width, (int)screenBounds.Height,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);

        var w = _canvas.ActualWidth > 0 ? _canvas.ActualWidth : ActualWidth;
        var h = _canvas.ActualHeight > 0 ? _canvas.ActualHeight : ActualHeight;
        _vertical.Visibility = vertical ? Visibility.Visible : Visibility.Collapsed;
        _horizontal.Visibility = horizontal ? Visibility.Visible : Visibility.Collapsed;
        _vertical.X1 = w / 2; _vertical.X2 = w / 2; _vertical.Y1 = 0; _vertical.Y2 = h;
        _horizontal.Y1 = h / 2; _horizontal.Y2 = h / 2; _horizontal.X1 = 0; _horizontal.X2 = w;
    }

    public void HideGuides()
    {
        if (IsVisible) Hide();
    }
}
