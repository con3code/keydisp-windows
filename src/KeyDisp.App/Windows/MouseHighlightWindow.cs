using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using KeyDisp.Core.Settings;
using static KeyDisp.App.Interop.NativeMethods;

namespace KeyDisp.App.Windows;

/// <summary>
/// クリック / プレス (ドラッグ) 中にマウスカーソルの位置へ円形ハイライトを表示する
/// (Mac 版 MouseHighlightController)。左クリック = 塗りつぶし + リング、右クリック = 二重リング。
/// </summary>
public sealed class MouseHighlightWindow : Window
{
    private readonly AppSettings _settings;
    private readonly HashSet<int> _pressed = new();
    private readonly Grid _root = new();
    private DispatcherTimer? _hideTimer;
    private IntPtr _hwnd;

    public MouseHighlightWindow(AppSettings settings)
    {
        _settings = settings;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        Content = _root;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            var ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
            ex |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(ex));
        };
    }

    /// <summary>フックから転送されるボタンイベント (UI スレッド)。</summary>
    public void OnButton(int button, bool isDown, int x, int y)
    {
        if (!_settings.MouseHighlight)
        {
            if (_pressed.Count > 0)
            {
                _pressed.Clear();
                HideNow();
            }
            return;
        }

        if (isDown)
        {
            _pressed.Add(button);
            _hideTimer?.Stop();
            _hideTimer = null;
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            BuildVisual(button);
            MoveToCursor(x, y);
            if (!IsVisible) Show();
        }
        else
        {
            _pressed.Remove(button);
            if (_pressed.Count == 0) FadeOut();
        }
    }

    /// <summary>ドラッグ中の追従 (ボタン押下中のみ)。</summary>
    public void OnMove(int x, int y)
    {
        if (_pressed.Count > 0 && IsVisible) MoveToCursor(x, y);
    }

    private double WindowSizeDip => _settings.MouseHighlightSize + 24;

    private void MoveToCursor(int x, int y)
    {
        if (_hwnd == IntPtr.Zero) return;
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var sizePx = (int)Math.Round(WindowSizeDip * dpi);
        SetWindowPos(_hwnd, HWND_TOPMOST,
            x - sizePx / 2, y - sizePx / 2, sizePx, sizePx,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    private void BuildVisual(int button)
    {
        var d = _settings.MouseHighlightSize;
        Width = WindowSizeDip;
        Height = WindowSizeDip;
        Color color;
        try
        {
            color = (Color)ColorConverter.ConvertFromString(_settings.MouseColorHex);
        }
        catch
        {
            color = Color.FromRgb(0xFF, 0xB3, 0x00);
        }
        var brush = new SolidColorBrush(color);

        _root.Children.Clear();
        if (button == 1)
        {
            // 右クリック: 二重リング
            _root.Children.Add(Ring(d, brush, 4, 1.0));
            _root.Children.Add(Ring(d * 0.62, brush, 2.5, 0.6));
        }
        else
        {
            // 左クリック・その他: 塗りつぶし + リング
            var fill = new Ellipse
            {
                Width = d,
                Height = d,
                Fill = new SolidColorBrush(color) { Opacity = 0.35 },
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _root.Children.Add(fill);
            _root.Children.Add(Ring(d, brush, 3, 1.0));
        }
    }

    private static Ellipse Ring(double diameter, Brush brush, double thickness, double opacity) => new()
    {
        Width = diameter,
        Height = diameter,
        Stroke = brush,
        StrokeThickness = thickness,
        Opacity = opacity,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private void FadeOut()
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(0.25)));
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.27) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer?.Stop();
            _hideTimer = null;
            HideNow();
        };
        _hideTimer.Start();
    }

    private void HideNow()
    {
        if (IsVisible) Hide();
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
    }
}
