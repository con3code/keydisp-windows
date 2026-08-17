using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using KeyDisp.App.Interop;
using KeyDisp.App.Overlay;
using KeyDisp.App.Services;
using KeyDisp.App.Windows;
using KeyDisp.Core.Display;
using KeyDisp.Core.Formatting;
using KeyDisp.Core.Layout;
using KeyDisp.Core.Screens;
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
    private ScreenService _screens = null!;
    private ScreenProfileStore? _profileStore;
    private EditHudWindow? _editHud;
    private SettingsWindow? _settingsWindow;
    private MouseHighlightWindow? _mouseHighlight;
    private BigCursorWindow? _bigCursor;
    private HotEdgeWatcher? _hotEdge;
    private readonly StartupManager _startup = new();
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
        // 自動起動の実状態 (レジストリ) と同期してから監視を始める
        _settings.LaunchAtLogin = _startup.IsEnabled();
        _repository.Attach();

        var scheduler = new DispatcherDelayScheduler();
        var layout = new KeyboardLayoutProvider();
        var formatter = new KeyFormatter(_settings, layout);
        var metrics = new OverlayMetrics(_settings, new WpfTextMeasurer());
        _model = new KeyDisplayModel(_settings, scheduler);
        _machine = new KeyStateMachine(_model, _settings, formatter, scheduler, _probe, metrics);

        _screens = new ScreenService();
        _profileStore = new ScreenProfileStore(
            _settings, scheduler, () => _overlay?.CurrentScreenId(), _repository.DisplayProfiles);
        _profileStore.Changed += _repository.RequestSave;

        _overlay = new OverlayWindow(
            _model, _settings, metrics, formatter, _screens, _profileStore, _repository);
        // 表示前にフレーム復元と WndProc フックを済ませるため、ハンドルを先に作る
        new WindowInteropHelper(_overlay).EnsureHandle();

        _mouseHighlight = new MouseHighlightWindow(_settings);
        _bigCursor = new BigCursorWindow(_settings);
        _hotEdge = new HotEdgeWatcher(_settings, _model, _screens);

        _settings.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(AppSettings.EditMode):
                    OnEditModeChanged();
                    break;
                case nameof(AppSettings.LaunchAtLogin):
                    _startup.SetEnabled(_settings.LaunchAtLogin);
                    break;
                case nameof(AppSettings.HotKeyVk):
                case nameof(AppSettings.HotKeyModifiers):
                    _hotKey?.Register(_settings.HotKeyModifiers, _settings.HotKeyVk);
                    break;
                case nameof(AppSettings.BigCursor):
                case nameof(AppSettings.MouseHighlight):
                case nameof(AppSettings.DragToMove):
                    UpdateMouseMoveForwarding();
                    if (e.PropertyName == nameof(AppSettings.BigCursor) && _settings.BigCursor)
                    {
                        var (cx, cy) = _screens.CursorPosition();
                        _bigCursor?.OnMove((int)cx, (int)cy);
                    }
                    break;
            }
        };

        _messageWindow = new MessageWindow();
        _tray = new TrayIcon(_messageWindow, "KeyDisp", BuildTrayMenu);
        _hotKey = new HotKeyManager(_messageWindow);
        _hotKey.Pressed += () => _settings.OverlayVisible = !_settings.OverlayVisible;
        _hotKey.Register(_settings.HotKeyModifiers, _settings.HotKeyVk);

        _hook = new LowLevelHookHost();
        UpdateMouseMoveForwarding();
        _hook.Start();
        _ = ConsumeInputAsync();

        // 初回起動時のプライバシー説明 (Windows にはセキュア入力保護が無いため)
        if (!_settings.PrivacyNoticeShown)
        {
            MessageBox.Show(
                L("KeyDisp は押したキーとマウス操作を画面に表示します。入力の記録や送信は一切行いません。\n\n" +
                  "ご注意: Windows にはパスワード入力欄を保護する仕組みがないため、「すべてのキー入力を表示」をオンにしている間はパスワードも画面に表示され得ます。人に画面を見せる前に、ショートカット (既定 Alt+Win+K) で表示を切り替えてください。",
                  "KeyDisp shows the keys and mouse actions you press on screen. Nothing is ever logged or transmitted.\n\n" +
                  "Note: Windows offers no secure-input protection, so while \"Show all keystrokes\" is enabled, passwords may also appear on screen. Use the toggle shortcut (default Alt+Win+K) before typing sensitive text."),
                "KeyDisp", MessageBoxButton.OK, MessageBoxImage.Information);
            _settings.PrivacyNoticeShown = true;
        }

        // 定期 reconcile (取り残し回収)。イベント処理側と同じ 1 秒周期
        _reconcileTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _reconcileTimer.Tick += (_, _) =>
        {
            _machine.ReconcileHeldState();
            _consumerPressed.RemoveWhere(vk => !_probe.IsKeyDown(vk));
        };
        _reconcileTimer.Start();
    }

    /// <summary>マウス移動の転送は、必要とする機能がオンのときだけ有効にする (負荷対策)。</summary>
    private void UpdateMouseMoveForwarding()
    {
        _hook.ForwardMouseMoves =
            _settings.BigCursor || _settings.MouseHighlight || _settings.DragToMove;
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
                    // マウスハイライトへの転送はキー表示の表示/非表示と独立 (Mac 版と同じ)
                    _mouseHighlight?.OnButton(ev.Button, ev.IsDown, ev.X, ev.Y);
                    _machine.HandleMouseButton(ev.Button, ev.IsDown);
                    break;
                case RawInputKind.MouseMove:
                    _mouseHighlight?.OnMove(ev.X, ev.Y);
                    _bigCursor?.OnMove(ev.X, ev.Y);
                    _overlay?.UpdateRowHover(ev.X, ev.Y);
                    break;
            }
        }
    }

    /// <summary>編集モードの出入り: HUD の表示と、カーソル画面への移動 (Mac 版と同じ順序)。</summary>
    private void OnEditModeChanged()
    {
        if (_settings.EditMode)
        {
            // メニューを操作した画面のプロファイルが編集対象になるよう、先に移す
            if (_settings.FollowCursorScreen) _overlay?.MoveToCursorScreen();
            if (_editHud is null)
            {
                _editHud = new EditHudWindow(_settings, _screens)
                {
                    // オーバーレイの所有ウィンドウにすると、破線枠をクリック/ドラッグして
                    // オーバーレイが最前面へ上がっても HUD は常にその上に保たれる
                    Owner = _overlay,
                };
            }
            _editHud.ShowOnCursorScreen();
        }
        else
        {
            _editHud?.Hide();
        }
    }

    private IReadOnlyList<TrayMenuItem> BuildTrayMenu() => new[]
    {
        new TrayMenuItem(L("キー表示", "Show Keystrokes"), _settings.OverlayVisible,
            () => _settings.OverlayVisible = !_settings.OverlayVisible),
        new TrayMenuItem(L("表示編集モード", "Edit Display Mode"), _settings.EditMode,
            () => _settings.EditMode = !_settings.EditMode),
        new TrayMenuItem(L("表示位置をリセット", "Reset Position"), false,
            () => _overlay?.ResetPosition()),
        TrayMenuItem.Separator,
        new TrayMenuItem(L("すべてのキー入力を表示", "Show All Keystrokes"), _settings.ShowAllKeys,
            () => _settings.ShowAllKeys = !_settings.ShowAllKeys),
        TrayMenuItem.Separator,
        new TrayMenuItem(L("設定…", "Settings…"), false, OpenSettings),
        new TrayMenuItem(L("設定フォルダを開く", "Open Settings Folder"), false, OpenSettingsFolder),
        TrayMenuItem.Separator,
        new TrayMenuItem(L("KeyDisp を終了", "Quit KeyDisp"), false, Shutdown),
    };

    private void OpenSettings()
    {
        _settingsWindow ??= new SettingsWindow(_settings);
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

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
        _profileStore?.Dispose();
        _hotEdge?.Dispose();
        _repository?.Dispose(); // 保留中の設定変更を確定保存
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
