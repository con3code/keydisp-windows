using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeyDisp.App.Overlay;

/// <summary>
/// カスタム背景画像を 9 分割 (ナインパッチ) で引き伸ばして子要素の背面に描く
/// (Mac 版 Image.resizable(capInsets:) の自前実装。WPF に相当機能は無い)。
/// 四隅は元の比率のまま、上下の中央は横に、左右の中央は縦に、中央だけ縦横に伸びる。
/// 画像が読めない場合は角丸の単色矩形にフォールバックする。
/// </summary>
public sealed class NinePatchDecorator : Decorator
{
    private static string _cachedPath = "";
    private static BitmapImage? _cachedImage;

    public string ImagePath { get; set; } = "";
    /// <summary>画像を等倍で置いたときの表示高さ (キー表示 1 行ぶん)。</summary>
    public double BaseHeight { get; set; } = 60;
    public double BackgroundOpacity { get; set; } = 1.0;
    public Brush FallbackBrush { get; set; } = Brushes.Transparent;
    public double FallbackCornerRadius { get; set; } = 12;

    private static BitmapImage? LoadImage(string path)
    {
        if (path == _cachedPath) return _cachedImage;
        _cachedPath = path;
        _cachedImage = null;
        try
        {
            if (path.Length > 0 && File.Exists(path))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path);
                image.EndInit();
                image.Freeze();
                _cachedImage = image;
            }
        }
        catch (Exception)
        {
            _cachedImage = null;
        }
        return _cachedImage;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = new Size(ActualWidth, ActualHeight);
        if (size.Width <= 0 || size.Height <= 0) return;

        var image = LoadImage(ImagePath);
        if (image is null || image.PixelWidth <= 0 || image.PixelHeight <= 0)
        {
            dc.PushOpacity(BackgroundOpacity);
            dc.DrawRoundedRectangle(FallbackBrush, null,
                new Rect(size), FallbackCornerRadius, FallbackCornerRadius);
            dc.Pop();
            return;
        }

        // 解像度はそのままに、表示上の寸法だけ 1 行ぶんの高さへ合わせる (Mac 版と同じ)
        var dispH = BaseHeight;
        var dispW = image.PixelWidth * (BaseHeight / image.PixelHeight);
        // 画像を縦横 3 等分した位置で切る。表示領域が小さいときは四隅どうしが
        // 重ならないところまで切り幅を詰める (潰れ防止)
        var capV = Math.Min(dispH / 3, Math.Max(0, size.Height / 2 - 0.5));
        var capH = Math.Min(dispW / 3, Math.Max(0, size.Width / 2 - 0.5));
        // 表示単位 → 元画像ピクセルへの換算
        var srcV = capV * (image.PixelHeight / dispH);
        var srcH = capH * (image.PixelWidth / dispW);

        double[] srcX = { 0, srcH, image.PixelWidth - srcH, image.PixelWidth };
        double[] srcY = { 0, srcV, image.PixelHeight - srcV, image.PixelHeight };
        double[] dstX = { 0, capH, size.Width - capH, size.Width };
        double[] dstY = { 0, capV, size.Height - capV, size.Height };

        dc.PushOpacity(BackgroundOpacity);
        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 3; col++)
            {
                var srcRect = new Int32Rect(
                    (int)Math.Round(srcX[col]),
                    (int)Math.Round(srcY[row]),
                    Math.Max(1, (int)Math.Round(srcX[col + 1] - srcX[col])),
                    Math.Max(1, (int)Math.Round(srcY[row + 1] - srcY[row])));
                var dstRect = new Rect(
                    dstX[col], dstY[row],
                    Math.Max(0, dstX[col + 1] - dstX[col]),
                    Math.Max(0, dstY[row + 1] - dstY[row]));
                if (dstRect.Width <= 0 || dstRect.Height <= 0) continue;
                if (srcRect.X < 0 || srcRect.Y < 0 ||
                    srcRect.X + srcRect.Width > image.PixelWidth ||
                    srcRect.Y + srcRect.Height > image.PixelHeight) continue;
                dc.DrawImage(new CroppedBitmap(image, srcRect), dstRect);
            }
        }
        dc.Pop();
    }
}
