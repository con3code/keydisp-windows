using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using KeyDisp.App.Interop;
using KeyDisp.App.Overlay;
using KeyDisp.App.Services;
using KeyDisp.Core.Display;
using KeyDisp.Core.Formatting;
using KeyDisp.Core.Layout;
using KeyDisp.Core.Settings;
using KeyDisp.Core.StateMachine;
using static KeyDisp.App.Services.Localization;

namespace KeyDisp.App;

/// <summary>
/// composition root (Mac 版 AppDelegate 相当)。トレイ常駐でウィンドウはオーバーレイのみ。
/// すべての状態処理は UI スレッド 1 本に載せる (フックだけ専用スレッド)。
/// </summary>
public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private AppSettings _settings = null!;
    private SettingsRepository _repository = null!;
    private KeyDisplayModel _model = null!;
    private KeyStateMachine _machine = null!;
    private LowLevelHookHost _hook = null!;
    private MessageWindow _messageWindow = null!;
    private TrayIcon? _tray;
    private HotKeyManager? _hotKey;
    private OverlayWindow? _overlay;
    private DispatcherTimer? _reconcileTimer;
    /// <summary>リピート合成用: 消費側から見た押下中キー (フックに autorepeat フラグが無いため)。</summary>
    private readonly HashSet<int> _consumerPressed = new();
    private readonly InputStateProbe _probe = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 単一インスタンス (古いビルドと並走すると挙動が混乱する。Mac 版 DEVLOG の教訓)
        _singleInstanceMutex = new Mutex(true, @"Local\KeyDispSingleInstance", out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        _settings = new AppSettings();
        Services.Localization.Configure(_settings);
        _repository = new SettingsRepository(_settings);
        _repository.Load();
        _repository.Attach();

        var scheduler = new DispatcherDelayScheduler();
        var layout = new KeyboardLayoutProvider();
        var formatter = new KeyFormatter(_settings, layout);
        var metrics = new OverlayMetrics(_settings, new WpfTextMeasurer());
        _model = new KeyDisplayModel(_settings, scheduler);
        _machine = new KeyStateMachine(_model, _settings, formatter, scheduler, _probe, metrics);

        _overlay = new OverlayWindow(_model, _settings, metrics, formatter);

        _messageWindow = new MessageWindow();
        _tray = new TrayIcon(_messageWindow, "KeyDisp", BuildTrayMenu);
        _hotKey = new HotKeyManager(_messageWindow);
        _hotKey.Pressed += () => _settings.OverlayVisible = !_settings.OverlayVisible;
        _hotKey.Register(_settings.HotKeyModifiers, _settings.HotKeyVk);

        _hook = new LowLevelHookHost();
        _hook.Start();
        _ = ConsumeInputAsync();

        // 定期 reconcile (取り残し回収)。イベント処理側と同じ 1 秒周期
        _reconcileTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _reconcileTimer.Tick += (_, _) =>
        {
            _machine.ReconcileHeldState();
            _consumerPressed.RemoveWhere(vk => !_probe.IsKeyDown(vk));
        };
        _reconcileTimer.Start();
    }

    /// <summary>フックからのイベントを UI スレッドで消費し、状態機械へ流す。</summary>
    private async Task ConsumeInputAsync()
    {
        await foreach (var ev in _hook.Events.ReadAllAsync())
        {
            switch (ev.Kind)
            {
                case RawInputKind.Key:
                    // KBDLLHOOKSTRUCT に autorepeat フラグは無いので、
                    // 「既に押下中の vk の keydown」をリピートとして合成する
                    var isRepeat = ev.IsDown && !_consumerPressed.Add(ev.Vk);
                    if (!ev.IsDown) _consumerPressed.Remove(ev.Vk);
                    _machine.HandleKey(ev.Vk, ev.IsDown, isRepeat);
                    break;
                case RawInputKind.MouseButton:
                    _machine.HandleMouseButton(ev.Button, ev.IsDown);
                    break;
            }
        }
    }

    private IReadOnlyList<TrayMenuItem> BuildTrayMenu() => new[]
    {
        new TrayMenuItem(L("キー表示", "Show Keystrokes"), _settings.OverlayVisible,
            () => _settings.OverlayVisible = !_settings.OverlayVisible),
        new TrayMenuItem(L("すべてのキー入力を表示", "Show All Keystrokes"), _settings.ShowAllKeys,
            () => _settings.ShowAllKeys = !_settings.ShowAllKeys),
        TrayMenuItem.Separator,
        new TrayMenuItem(L("設定フォルダを開く", "Open Settings Folder"), false, OpenSettingsFolder),
        TrayMenuItem.Separator,
        new TrayMenuItem(L("KeyDisp を終了", "Quit KeyDisp"), false, Shutdown),
    };

    private void OpenSettingsFolder()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyDisp");
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _reconcileTimer?.Stop();
        _hook?.Dispose();
        _hotKey?.Dispose();
        _tray?.Dispose();
        _messageWindow?.Dispose();
        _repository?.Dispose(); // 保留中の設定変更を確定保存
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
