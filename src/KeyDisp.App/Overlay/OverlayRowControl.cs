using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KeyDisp.Core.Display;
using KeyDisp.Core.Formatting;
using KeyDisp.Core.Settings;

namespace KeyDisp.App.Overlay;

/// <summary>
/// オーバーレイの 1 行。中身の組み立ては RowContentFactory に任せ、
/// このコントロールは出入り・フェード・×n パルスのアニメーションを担当する。
/// </summary>
public sealed class OverlayRowControl : ContentControl
{
    private readonly ScaleTransform _pulse = new(1, 1);
    private readonly TranslateTransform _slide = new();
    private int _lastCount = 1;
    private KeyEntryPhase _lastPhase = KeyEntryPhase.Active;
    private long _lastPulseMs;

    public Guid EntryId { get; }

    public OverlayRowControl(Guid entryId)
    {
        EntryId = entryId;
        // パルスの拡大は左下基準 (Mac 版 anchor: .bottomLeading)
        RenderTransformOrigin = new Point(0, 1);
        var group = new TransformGroup();
        group.Children.Add(_pulse);
        group.Children.Add(_slide);
        RenderTransform = group;
    }

    public void Update(
        KeyEntry entry, IReadOnlyList<string> tokens,
        AppSettings settings, KeyFormatter formatter, double maxWidth)
    {
        var scale = settings.DisplayScale;
        Content = RowContentFactory.Build(
            tokens, entry.IsTyping, entry.Count, settings, formatter, maxWidth);
        MaxWidth = maxWidth;
        Margin = new Thickness(0, OverlayConstants.RowSpacing * scale / 2,
            0, OverlayConstants.RowSpacing * scale / 2);
        HorizontalAlignment = settings.RowAlignment switch
        {
            RowAlignment.Center => HorizontalAlignment.Center,
            RowAlignment.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Left,
        };

        // ×n パルス (autorepeat の高頻度更新は間引く)
        if (entry.Count > _lastCount)
        {
            var now = Environment.TickCount64;
            if (now - _lastPulseMs > OverlayConstants.PulseThrottleMs)
            {
                _lastPulseMs = now;
                Pulse();
            }
        }
        _lastCount = entry.Count;

        // フェード: fading に入ったら opacity → 0。凍結で holding に戻ったら即復帰
        if (entry.Phase == KeyEntryPhase.Fading && _lastPhase != KeyEntryPhase.Fading)
        {
            var fade = new DoubleAnimation(0, TimeSpan.FromSeconds(Math.Max(0.05, settings.FadeDuration)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            BeginAnimation(OpacityProperty, fade);
        }
        else if (entry.Phase != KeyEntryPhase.Fading && _lastPhase == KeyEntryPhase.Fading)
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
        }
        _lastPhase = entry.Phase;
    }

    private void Pulse()
    {
        var spring = new SpringEase
        {
            Response = OverlayConstants.PulseSpringResponse,
            DampingFraction = OverlayConstants.PulseSpringDamping,
        };
        var anim = new DoubleAnimation(
            OverlayConstants.PulseScale, 1.0,
            SpringEase.DurationFor(OverlayConstants.PulseSpringResponse))
        {
            EasingFunction = spring,
        };
        _pulse.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        _pulse.BeginAnimation(ScaleTransform.ScaleYProperty, anim.Clone());
    }

    /// <summary>新しい行の出現アニメーション (下端積み上げなら下から、ぶら下がりなら上から)。</summary>
    public void AnimateInsertion(bool fromTop, bool animationEnabled)
    {
        if (!animationEnabled) return;
        var spring = new SpringEase
        {
            Response = OverlayConstants.InsertSpringResponse,
            DampingFraction = OverlayConstants.InsertSpringDamping,
        };
        var duration = SpringEase.DurationFor(OverlayConstants.InsertSpringResponse);
        _slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(fromTop ? -20 : 20, 0, duration) { EasingFunction = spring });
        BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.18)));
    }
}
