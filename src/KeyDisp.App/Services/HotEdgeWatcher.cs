using System.ComponentModel;
using System.Windows.Threading;
using KeyDisp.App.Interop;
using KeyDisp.Core.Display;
using KeyDisp.Core.Settings;

namespace KeyDisp.App.Services;

/// <summary>
/// ホットエッジの監視 (Mac 版 AppDelegate の hotCornerTimer 相当)。
/// - 上端 10px: フェード凍結 (履歴を消さず保持)
/// - 下端 10px: キー表示を一時非表示 + 入力処理停止 (hotCornerSuppressed)
/// タイマーはどちらかの機能がオンのときだけ動かす (0.15 秒間隔)。
/// 注: 下端はタスクバーと競合し得る (実機で要確認、DEVICE-TEST-CHECKLIST E 項)。
/// </summary>
public sealed class HotEdgeWatcher : IDisposable
{
    private const double EdgeThreshold = 10;

    private readonly AppSettings _settings;
    private readonly KeyDisplayModel _model;
    private readonly ScreenService _screens;
    private readonly DispatcherTimer _timer;

    public HotEdgeWatcher(AppSettings settings, KeyDisplayModel model, ScreenService screens)
    {
        _settings = settings;
        _model = model;
        _screens = screens;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _timer.Tick += (_, _) => Tick();
        _settings.PropertyChanged += OnSettingsChanged;
        Refresh();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.TopEdgeFreeze) or nameof(AppSettings.HotCornerHide))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        if (_settings.TopEdgeFreeze || _settings.HotCornerHide)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
            Tick(); // 最後に 1 回だけ走らせて状態を戻す (Mac 版と同じ)
        }
    }

    private void Tick()
    {
        var (x, y) = _screens.CursorPosition();
        var screen = _screens.FromPoint(x, y);

        // 上端: フェード凍結
        var atTop = screen is not null && y <= screen.Bounds.Y + EdgeThreshold;
        _model.SetFrozen(_settings.TopEdgeFreeze && atTop);

        // 下端: 一時非表示 (編集モード中と表示オフ時は判定しない)
        var suppress = false;
        if (_settings.HotCornerHide && _settings.OverlayVisible && !_settings.EditMode)
        {
            suppress = screen is not null && y >= screen.Bounds.MaxY - EdgeThreshold;
        }
        if (suppress != _settings.HotCornerSuppressed)
        {
            _settings.HotCornerSuppressed = suppress;
            // 下端に入ったら表示中の行も消す (入力処理は状態機械側が止める)
            if (suppress) _model.ClearAll();
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _settings.PropertyChanged -= OnSettingsChanged;
    }
}
