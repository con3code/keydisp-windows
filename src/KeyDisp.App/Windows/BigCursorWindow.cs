using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using KeyDisp.Core.Settings;
using static KeyDisp.App.Interop.NativeMethods;

namespace KeyDisp.App.Windows;

/// <summary>
/// マウスカーソルに追従する大きなポインタ (Mac 版 BigCursorController の重ね描き方式)。
/// Windows では SetSystemCursor で本物を差し替える手もあるが、システム全体の
/// カーソルスキームを書き換えるため復元リスクがあり、まずは Mac 版と同じ方式にする。
/// システムカーソル自体は先端に重なったまま残る。
/// </summary>
public sealed class BigCursorWindow : Window
{
    /// <summary>ポインタ画像の周囲に確保する余白の割合 (輪郭線のぶん)。</summary>
    private const double PadRatio = 0.18;
    /// <summary>ポインタの縦横比 (高さ 1 に対する幅)。</summary>
    private const double Aspect = 0.62;

    /// <summary>矢印ポインタの形。先端が (0,0) の単位座標 (Y 下向き)。</summary>
    private static readonly Point[] Outline =
    {
        new(0.00, 0.00), // 先端
        new(0.00, 0.78),
        new(0.19, 0.60),
        new(0.31, 0.92),
        new(0.45, 0.86),
        new(0.33, 0.55),
        new(0.58, 0.53),
    };

    private readonly AppSettings _settings;
    private readonly Path _shape = new();
    private IntPtr _hwnd;
    private (int X, int Y) _lastPos = (int.MinValue, int.MinValue);

    public BigCursorWindow(AppSettings settings)
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
        Content = _shape;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            var ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
            ex |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(ex));
        };
        _settings.PropertyChanged += OnSettingsChanged;
        RebuildShape();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.BigCursor):
                if (!_settings.BigCursor && IsVisible) Hide();
                break;
            case nameof(AppSettings.BigCursorSize):
            case nameof(AppSettings.BigCursorColorHex):
                RebuildShape();
                break;
        }
    }

    private void RebuildShape()
    {
        var h = _settings.BigCursorSize;
        var pad = h * PadRatio;
        Width = h * Aspect + pad * 2;
        Height = h + pad * 2;

        Color fill;
        try
        {
            fill = (Color)ColorConverter.ConvertFromString(_settings.BigCursorColorHex);
        }
        catch
        {
            fill = Colors.White;
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(Map(Outline[0], h, pad), isFilled: true, isClosed: true);
            for (var i = 1; i < Outline.Length; i++)
            {
                ctx.LineTo(Map(Outline[i], h, pad), isStroked: true, isSmoothJoin: false);
            }
        }
        geometry.Freeze();
        _shape.Data = geometry;
        _shape.Fill = new SolidColorBrush(fill);
        _shape.Stroke = new SolidColorBrush(Color.FromArgb(217, 0, 0, 0)); // 黒 85%
        _shape.StrokeThickness = Math.Max(1.5, h * 0.045);
        _shape.StrokeLineJoin = PenLineJoin.Round;
    }

    private static Point Map(Point unit, double h, double pad) =>
        new(pad + unit.X * h, pad + unit.Y * h);

    /// <summary>マウスが動いたときに呼ぶ (フックの移動イベントから)。先端をカーソル位置に合わせる。</summary>
    public void OnMove(int x, int y)
    {
        if (!_settings.BigCursor) return;
        if (Math.Abs(x - _lastPos.X) < 1 && Math.Abs(y - _lastPos.Y) < 1) return;
        _lastPos = (x, y);
        if (!IsVisible) Show();
        if (_hwnd == IntPtr.Zero) return;
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var padPx = (int)Math.Round(_settings.BigCursorSize * PadRatio * dpi);
        SetWindowPos(_hwnd, HWND_TOPMOST,
            x - padPx, y - padPx,
            (int)Math.Round(Width * dpi), (int)Math.Round(Height * dpi),
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }
}
