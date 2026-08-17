using KeyDisp.Core.Screens;

namespace KeyDisp.Core.Tests.Screens;

public class ScreenGeometryTests
{
    [Fact]
    public void Remap_PreservesRelativeMarginRatio()
    {
        // 元画面 (0,0,1000,800) の左下寄り 25% の位置 → 別画面 (2000,0,2000,1600) でも 25%
        var frame = new RectD(200, 300, 200, 200);
        var source = new RectD(0, 0, 1000, 800);
        var target = new RectD(2000, 0, 2000, 1600);
        var result = ScreenGeometry.Remap(frame, source, target);
        // rx = 200/800 = 0.25 → x = 2000 + 0.25*1800 = 2450
        Assert.Equal(2450, result.X);
        // ry = 300/600 = 0.5 → y = 0.5*1400 = 700
        Assert.Equal(700, result.Y);
        Assert.Equal(200, result.Width); // サイズは変えない
    }

    [Fact]
    public void Remap_FrameLargerThanSource_LandsAtTargetOrigin()
    {
        var frame = new RectD(0, 0, 1200, 900);
        var source = new RectD(0, 0, 1000, 800);
        var target = new RectD(0, 0, 3000, 2000);
        var result = ScreenGeometry.Remap(frame, source, target);
        Assert.Equal(0, result.X);
        Assert.Equal(0, result.Y);
    }

    [Fact]
    public void Clamp_MovesFrameInsideBounds()
    {
        var bounds = new RectD(0, 0, 1000, 800);
        var result = ScreenGeometry.Clamp(new RectD(900, -50, 300, 200), bounds);
        Assert.Equal(700, result.X);  // 右にはみ出し → 右端に寄せる
        Assert.Equal(0, result.Y);    // 上にはみ出し → 上端に寄せる
    }

    [Fact]
    public void Clamp_ShrinksOversizedFrame()
    {
        var bounds = new RectD(0, 0, 1000, 800);
        var result = ScreenGeometry.Clamp(new RectD(0, 0, 1500, 1000), bounds);
        Assert.Equal(1000, result.Width);
        Assert.Equal(800, result.Height);
    }

    [Fact]
    public void SnapToCenter_SnapsEachAxisIndependently()
    {
        var screen = new RectD(0, 0, 1000, 800); // 中心 (500, 400)
        // 中心 (505, 700): X は閾値内、Y は外
        var (frame, snapV, snapH) = ScreenGeometry.SnapToCenter(
            new RectD(405, 600, 200, 200), screen, threshold: 10);
        Assert.True(snapV);
        Assert.False(snapH);
        Assert.Equal(400, frame.X); // 中心 500 に吸着
        Assert.Equal(600, frame.Y); // Y は動かない
    }

    [Fact]
    public void SnapToCenter_NoSnapOutsideThreshold()
    {
        var screen = new RectD(0, 0, 1000, 800);
        var original = new RectD(100, 100, 200, 200);
        var (frame, snapV, snapH) = ScreenGeometry.SnapToCenter(original, screen);
        Assert.False(snapV);
        Assert.False(snapH);
        Assert.Equal(original, frame);
    }
}
