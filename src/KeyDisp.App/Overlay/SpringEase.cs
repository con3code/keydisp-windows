using System.Windows;
using System.Windows.Media.Animation;

namespace KeyDisp.App.Overlay;

/// <summary>
/// SwiftUI の spring(response:dampingFraction:) 相当のイージング。
/// 減衰バネの閉形式解をそのまま使う (WPF 標準の ElasticEase は減衰特性が別物)。
/// ストーリーボードの長さは DurationFor(response) を使うこと
/// (収束までの実時間 = response × 1.5 を正規化時間に写像している)。
/// </summary>
public sealed class SpringEase : EasingFunctionBase
{
    public double Response { get; set; } = 0.28;
    public double DampingFraction { get; set; } = 0.85;

    public SpringEase()
    {
        EasingMode = EasingMode.EaseIn; // EaseInCore の曲線をそのまま使う
    }

    public static Duration DurationFor(double response) =>
        new(TimeSpan.FromSeconds(response * 1.5));

    protected override double EaseInCore(double normalizedTime)
    {
        var settleSeconds = Response * 1.5;
        var t = normalizedTime * settleSeconds;
        var wn = 2 * Math.PI / Response;                  // 固有角振動数
        var z = Math.Clamp(DampingFraction, 0.01, 0.9999); // 減衰比
        var wd = wn * Math.Sqrt(1 - z * z);               // 減衰角振動数
        var decay = Math.Exp(-z * wn * t);
        return 1 - decay * (Math.Cos(wd * t) + z * wn / wd * Math.Sin(wd * t));
    }

    protected override Freezable CreateInstanceCore() =>
        new SpringEase { Response = Response, DampingFraction = DampingFraction };
}
