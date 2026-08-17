namespace KeyDisp.Core.Screens;

/// <summary>矩形 (左上原点・Y 下向きの Windows 座標系。物理 px)。</summary>
public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double MaxX => X + Width;
    public double MaxY => Y + Height;
    public double MidX => X + Width / 2;
    public double MidY => Y + Height / 2;

    public bool Contains(double x, double y) =>
        x >= X && x < MaxX && y >= Y && y < MaxY;

    public bool IntersectsWith(RectD other) =>
        X < other.MaxX && other.X < MaxX && Y < other.MaxY && other.Y < MaxY;

    public double[] ToArray() => new[] { X, Y, Width, Height };

    public static RectD? FromArray(double[]? a) =>
        a is { Length: 4 } && a[2] > 0 && a[3] > 0 ? new RectD(a[0], a[1], a[2], a[3]) : null;
}

/// <summary>
/// オーバーレイ配置の純関数 (Mac 版 OverlayWindowController の remap/clamp/スナップ判定の移植)。
/// 座標系は Windows (Y 下向き) だが、計算は min/width ベースなので Mac 版と同じ式。
/// </summary>
public static class ScreenGeometry
{
    /// <summary>元の画面内での相対位置 (余白に対する比率) を保ったまま、別の画面へ写像する。</summary>
    public static RectD Remap(RectD frame, RectD source, RectD target)
    {
        var rx = source.Width > frame.Width
            ? (frame.X - source.X) / (source.Width - frame.Width) : 0;
        var ry = source.Height > frame.Height
            ? (frame.Y - source.Y) / (source.Height - frame.Height) : 0;
        return frame with
        {
            X = target.X + Math.Clamp(rx, 0, 1) * Math.Max(0, target.Width - frame.Width),
            Y = target.Y + Math.Clamp(ry, 0, 1) * Math.Max(0, target.Height - frame.Height),
        };
    }

    /// <summary>フレームを作業領域内に収める (大きすぎる場合は縮める)。</summary>
    public static RectD Clamp(RectD frame, RectD bounds)
    {
        var w = Math.Min(frame.Width, bounds.Width);
        var h = Math.Min(frame.Height, bounds.Height);
        return new RectD(
            Math.Max(bounds.X, Math.Min(frame.X, bounds.MaxX - w)),
            Math.Max(bounds.Y, Math.Min(frame.Y, bounds.MaxY - h)),
            w, h);
    }

    /// <summary>
    /// フレーム中心が画面中心に近ければ吸着させる (編集モードのドラッグ用)。
    /// SnapV = 縦の中心線に吸着 (X を動かした)、SnapH = 横の中心線に吸着。
    /// </summary>
    public static (RectD Frame, bool SnapV, bool SnapH) SnapToCenter(
        RectD frame, RectD screen, double threshold = 10)
    {
        var snapV = Math.Abs(frame.MidX - screen.MidX) <= threshold;
        var snapH = Math.Abs(frame.MidY - screen.MidY) <= threshold;
        var result = frame;
        if (snapV) result = result with { X = screen.MidX - frame.Width / 2 };
        if (snapH) result = result with { Y = screen.MidY - frame.Height / 2 };
        return (result, snapV, snapH);
    }
}
