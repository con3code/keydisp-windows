using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using KeyDisp.App.Interop;
using KeyDisp.App.Services;
using KeyDisp.Core.Display;
using KeyDisp.Core.Formatting;
using KeyDisp.Core.Layout;
using KeyDisp.Core.Screens;
using KeyDisp.Core.Settings;
using static KeyDisp.App.Interop.NativeMethods;

namespace KeyDisp.App.Overlay;

/// <summary>
/// キー表示を載せる透明・クリック透過・最前面のウィンドウ (Mac 版 OverlayWindowController 相当)。
///
/// - フレームは物理 px + Win32 API で管理し、WPF の DIP には境界でのみ変換する
///   (DPI 混在環境で保存値が壊れないように)
/// - 編集モード: クリック透過を解除し、WM_NCHITTEST で内側ドラッグ移動・端ドラッグリサイズ、
///   WM_MOVING で画面中心スナップ + ガイド表示
/// - ディスプレイ安定 ID ごとにフレームとプロファイルを記憶 (ScreenProfileStore)
/// - followCursorScreen: 新しい行が入った瞬間にカーソルのある画面へ移る
/// </summary>
public partial class OverlayWindow : Window
{
    /// <summary>再描画が必要な設定キー。</summary>
    private static readonly HashSet<string> DisplayProperties = new()
    {
        nameof(AppSettings.OverlayVisible),
        nameof(AppSettings.DisplayScale),
        nameof(AppSettings.MaxRows),
        nameof(AppSettings.StackFromTop),
        nameof(AppSettings.RowAlignment),
        nameof(AppSettings.KeyStyle),
        nameof(AppSettings.TextColorHex),
        nameof(AppSettings.TextOutline),
        nameof(AppSettings.TextOutlineColorHex),
        nameof(AppSettings.KeyColorHex),
        nameof(AppSettings.BackgroundEnabled),
        nameof(AppSettings.BackgroundOpacity),
        nameof(AppSettings.CustomImagePath),
        nameof(AppSettings.PlusSeparator),
        nameof(AppSettings.GlobeOnImeKeys),
        nameof(AppSettings.HiddenOnCurrentScreen),
    };

    /// <summary>表示領域の自動拡張が要る設定キー。</summary>
    private static readonly HashSet<string> GrowProperties = new()
    {
        nameof(AppSettings.DisplayScale),
        nameof(AppSettings.MaxRows),
        nameof(AppSettings.KeyStyle),
    };

    private readonly KeyDisplayModel _model;
    private readonly AppSettings _settings;
    private readonly OverlayMetrics _metrics;
    private readonly KeyFormatter _formatter;
    private readonly ScreenService _screens;
    private readonly ScreenProfileStore _store;
    private readonly SettingsRepository _repository;
    private readonly Dictionary<Guid, OverlayRowControl> _rows = new();
    private GuideWindow? _guide;
    private IntPtr _hwnd;
    private int _lastEntryCount;
    private bool _dragging;
    private string? _dragStartScreenId;
    /// <summary>dragToMove 用: カーソルが見えている行の上にあるか。</summary>
    private bool _hoverOverRow;
    /// <summary>編集モードのサンプル行 (表記設定が変わったら作り直す)。</summary>
    private List<KeyEntry>? _samples;
    private string _samplesKey = "";

    public OverlayWindow(
        KeyDisplayModel model, AppSettings settings, OverlayMetrics metrics,
        KeyFormatter formatter, ScreenService screens, ScreenProfileStore store,
        SettingsRepository repository)
    {
        InitializeComponent();
        _model = model;
        _settings = settings;
        _metrics = metrics;
        _formatter = formatter;
        _screens = screens;
        _store = store;
        _repository = repository;

        _model.Changed += OnModelChanged;
        _settings.PropertyChanged += OnSettingsChanged;
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) =>
        {
            PublishContentWidth();
            Rebuild();
        };
    }

    // ── フレーム管理 (物理 px) ────────────────────────────

    private double DpiScale => _hwnd == IntPtr.Zero
        ? 1.0
        : VisualTreeHelper.GetDpi(this).DpiScaleX;

    private RectD CurrentFrame()
    {
        if (_hwnd == IntPtr.Zero) return new RectD(0, 0, 620, 440);
        GetWindowRect(_hwnd, out var r);
        return new RectD(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    private void SetFrame(RectD frame)
    {
        if (_hwnd == IntPtr.Zero) return;
        SetWindowPos(_hwnd, IntPtr.Zero,
            (int)Math.Round(frame.X), (int)Math.Round(frame.Y),
            (int)Math.Round(frame.Width), (int)Math.Round(frame.Height),
            SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>いまオーバーレイの中心がある画面の安定 ID。</summary>
    public string? CurrentScreenId() =>
        _hwnd == IntPtr.Zero ? null : _screens.FromRectCenter(CurrentFrame()).StableId;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        ApplyClickThrough();
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        RestoreFrame();
        PublishContentWidth();
        if (CurrentScreenId() is string id) _store.RestoreHiddenFlag(id);
    }

    private void ApplyClickThrough()
    {
        if (_hwnd == IntPtr.Zero) return;
        var ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
        ex |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        // 編集モード中は全域、dragToMove では行の上にいる間だけマウスを受け付ける
        if (_settings.EditMode || _hoverOverRow) ex &= ~WS_EX_TRANSPARENT;
        else ex |= WS_EX_TRANSPARENT;
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(ex));
    }

    /// <summary>
    /// dragToMove 用: カーソル位置 (物理 px) が見えている行の上にあるかでマウス受付を切り替える。
    /// レイヤードウィンドウでは「透明ピクセルはクリック透過」という OS の挙動が効かないため、
    /// 行の矩形を実測して判定する (Mac 版 refreshMouseAcceptance と同じ構造)。
    /// フックのマウス移動イベントから呼ばれる。
    /// </summary>
    public void UpdateRowHover(int x, int y)
    {
        if (!_settings.DragToMove || _settings.EditMode || !IsVisible || _dragging) return;
        var over = CursorIsOverRow(x, y);
        if (over != _hoverOverRow)
        {
            _hoverOverRow = over;
            ApplyClickThrough();
        }
    }

    private bool CursorIsOverRow(double x, double y)
    {
        var frame = CurrentFrame();
        if (!frame.Contains(x, y)) return false;
        var margin = 8 * DpiScale;
        foreach (var child in RowsPanel.Children)
        {
            if (child is not OverlayRowControl control || control.ActualWidth <= 0) continue;
            Point topLeft, bottomRight;
            try
            {
                topLeft = control.PointToScreen(new Point(0, 0)); // 物理 px
                bottomRight = control.PointToScreen(new Point(control.ActualWidth, control.ActualHeight));
            }
            catch (InvalidOperationException)
            {
                continue; // まだビジュアルツリーに載っていない
            }
            if (x >= topLeft.X - margin && x <= bottomRight.X + margin &&
                y >= topLeft.Y - margin && y <= bottomRight.Y + margin)
            {
                return true;
            }
        }
        return false;
    }

    private void RestoreFrame()
    {
        var stored = RectD.FromArray(_repository.OverlayFrame);
        if (stored is RectD r && _screens.All().Any(s => s.Bounds.IntersectsWith(r)))
        {
            SetFrame(r);
            return;
        }
        SetFrame(DefaultFrame());
    }

    private RectD DefaultFrame()
    {
        var wa = _screens.Primary().WorkArea;
        var frame = CurrentFrame();
        return new RectD(wa.X + 40, wa.MaxY - frame.Height - 40, frame.Width, frame.Height);
    }

    /// <summary>
    /// 位置とサイズをメインスクリーン左下のデフォルト状態へ戻し、
    /// 画面ごとの定位置・表示設定の記憶もすべてクリアする。
    /// </summary>
    public void ResetPosition()
    {
        _store.Reset();
        var wa = _screens.Primary().WorkArea;
        var dpi = DpiScale;
        var scale = Math.Max(1, _settings.DisplayScale);
        var width = Math.Min(620 * scale * dpi, wa.Width - 80);
        var height = Math.Min(440 * scale * dpi, wa.Height - 80);
        SetFrame(new RectD(wa.X + 40, wa.MaxY - height - 40, width, height));
        PublishContentWidth();
        SaveFrame();
        Rebuild();
    }

    private void SaveFrame()
    {
        var frame = CurrentFrame();
        _repository.OverlayFrame = frame.ToArray();
        // ドラッグ中は画面別の記憶を更新しない。画面をまたぐ途中の位置が
        // 元の画面の定位置を上書きしてしまうため、確定はドラッグ終了時に行う
        if (!_dragging && CurrentScreenId() is string id)
        {
            _store.RememberFrame(id, frame);
        }
        _repository.RequestSave();
    }

    /// <summary>1 行に入るキーの数の判断に使うため、内側の幅 (DIP) を設定へ伝える。</summary>
    private void PublishContentWidth()
    {
        var width = ActualWidth > 0 ? ActualWidth : Width;
        _settings.OverlayContentWidth = width - OverlayMetrics.Padding;
    }

    // ── WndProc (編集モードの移動・リサイズ・スナップ) ─────

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_NCHITTEST when _settings.EditMode:
            {
                var x = (short)(lParam.ToInt64() & 0xFFFF);
                var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                handled = true;
                return new IntPtr(HitTest(x, y));
            }
            case WM_NCHITTEST when _settings.DragToMove && _hoverOverRow:
            {
                // 行を掴んだらウィンドウごと移動、行の外は下のアプリへ素通し
                var x = (short)(lParam.ToInt64() & 0xFFFF);
                var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                handled = true;
                return new IntPtr(CursorIsOverRow(x, y) ? HTCAPTION : HTTRANSPARENT);
            }
            case WM_NCLBUTTONDOWN when _settings.EditMode:
            {
                // 枠なしウィンドウでは HT コードだけではリサイズが始まらないため、
                // システムのリサイズループ (SC_SIZE + 方向) を明示的に起動する
                var direction = (int)wParam switch
                {
                    HTLEFT => 1,
                    HTRIGHT => 2,
                    HTTOP => 3,
                    HTTOPLEFT => 4,
                    HTTOPRIGHT => 5,
                    HTBOTTOM => 6,
                    HTBOTTOMLEFT => 7,
                    HTBOTTOMRIGHT => 8,
                    _ => 0,
                };
                if (direction != 0)
                {
                    PostMessageW(hwnd, WM_SYSCOMMAND, new IntPtr(SC_SIZE + direction), lParam);
                    handled = true;
                }
                break;
            }
            case WM_ENTERSIZEMOVE:
                BeginDragFreeze();
                break;
            case WM_MOVING when _settings.EditMode && _dragging:
                SnapMovingRect(lParam);
                handled = true;
                return new IntPtr(1);
            case WM_EXITSIZEMOVE:
                EndDrag();
                break;
        }
        return IntPtr.Zero;
    }

    private int HitTest(double x, double y)
    {
        var f = CurrentFrame();
        var margin = 8 * DpiScale;
        var left = x < f.X + margin;
        var right = x >= f.MaxX - margin;
        var top = y < f.Y + margin;
        var bottom = y >= f.MaxY - margin;
        if (top && left) return HTTOPLEFT;
        if (top && right) return HTTOPRIGHT;
        if (bottom && left) return HTBOTTOMLEFT;
        if (bottom && right) return HTBOTTOMRIGHT;
        if (left) return HTLEFT;
        if (right) return HTRIGHT;
        if (top) return HTTOP;
        if (bottom) return HTBOTTOM;
        return HTCAPTION; // 内側は掴んで移動
    }

    private void SnapMovingRect(IntPtr rectPtr)
    {
        var rect = Marshal.PtrToStructure<RECT>(rectPtr);
        var frame = new RectD(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        var screen = _screens.FromRectCenter(frame);
        var threshold = 10 * DpiScale;
        var (snapped, snapV, snapH) = ScreenGeometry.SnapToCenter(frame, screen.Bounds, threshold);
        if (snapped != frame)
        {
            rect.Left = (int)Math.Round(snapped.X);
            rect.Top = (int)Math.Round(snapped.Y);
            rect.Right = rect.Left + (int)Math.Round(snapped.Width);
            rect.Bottom = rect.Top + (int)Math.Round(snapped.Height);
            Marshal.StructureToPtr(rect, rectPtr, fDeleteOld: false);
        }
        _guide ??= new GuideWindow();
        _guide.ShowGuides(screen.Bounds, snapV, snapH);
    }

    /// <summary>ドラッグ / リサイズ中は表示を凍結する (WM_ENTERSIZEMOVE から)。</summary>
    private void BeginDragFreeze()
    {
        if (_dragging) return;
        _dragging = true;
        _dragStartScreenId = CurrentScreenId();
        _model.SetFreeze(FreezeReason.Dragging, true);
    }

    private void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        _model.SetFreeze(FreezeReason.Dragging, false);
        _guide?.HideGuides();
        PublishContentWidth();
        SaveFrame();
        // 画面をまたいでドラッグした場合は、移動先の画面のプロファイルへ切り替える
        // (元画面の見た目を引き連れたまま保存して、移動先の設定を上書きしないため)
        if (_settings.FollowCursorScreen &&
            CurrentScreenId() is string id && id != _dragStartScreenId)
        {
            _store.Adopt(id);
        }
        _dragStartScreenId = null;
    }

    // ── カーソルのある画面への追従 ─────────────────────────

    private void OnModelChanged()
    {
        var count = _model.Entries.Count;
        var added = count > _lastEntryCount;
        _lastEntryCount = count;
        // カーソル追従はタイマーで追わず、行が増えるこのタイミングに便乗する
        if (added) MoveToCursorScreenIfNeeded();
        Rebuild();
    }

    private void MoveToCursorScreenIfNeeded()
    {
        if (!_settings.FollowCursorScreen || _settings.EditMode || _dragging) return;
        MoveToCursorScreen();
    }

    /// <summary>
    /// カーソルのある画面へ表示を移す。その画面で記憶している定位置があれば復元し、
    /// 無ければ相対位置を比例変換して置く。編集モードに入る前にも呼ばれる。
    /// </summary>
    public void MoveToCursorScreen()
    {
        if (_hwnd == IntPtr.Zero) return;
        var (cx, cy) = _screens.CursorPosition();
        var target = _screens.FromPoint(cx, cy);
        if (target is null) return;
        var frame = CurrentFrame();
        if (target.Bounds.Contains(frame.MidX, frame.MidY)) return; // すでにその画面にいる

        var source = _screens.FromRectCenter(frame);
        var newFrame = _store.StoredFrame(target.StableId, target.Bounds)
            ?? ScreenGeometry.Remap(frame, source.WorkArea, target.WorkArea);
        SetFrame(ScreenGeometry.Clamp(newFrame, target.WorkArea));
        PublishContentWidth();
        SaveFrame();
        // その画面で記憶している表示設定一式があれば適用する。
        // 必ずフレームを移した後に行う (先に変えると、記憶が移動前の画面に上書きされる)
        _store.Adopt(target.StableId);
    }

    // ── 表示内容に合わせた拡張 ────────────────────────────

    /// <summary>足りない分だけ広げる (利用者が手で広げた大きさは縮めない)。</summary>
    private void GrowToFitContent()
    {
        if (_hwnd == IntPtr.Zero) return;
        var dpi = DpiScale;
        var needW = Math.Max(240, 260 * _settings.DisplayScale) * dpi;
        var needH = _metrics.RequiredHeight((int)_settings.MaxRows) * dpi;
        var frame = CurrentFrame();
        if (frame.Width >= needW && frame.Height >= needH) return;

        var newW = Math.Max(frame.Width, needW);
        var newH = Math.Max(frame.Height, needH);
        // 下端基準 (積み上げ式) は下端を保って上へ、上端基準 (ぶら下がり式) は上端を保って下へ
        var newY = _settings.StackFromTop ? frame.Y : frame.MaxY - newH;
        var grown = ScreenGeometry.Clamp(
            new RectD(frame.X, newY, newW, newH),
            _screens.FromRectCenter(frame).WorkArea);
        SetFrame(grown);
        PublishContentWidth();
        SaveFrame();
    }

    // ── 編集モード ───────────────────────────────────────

    /// <summary>編集モードの出入りで呼ばれる (App が EditMode の変更を仲介)。</summary>
    public void RefreshEditMode()
    {
        ApplyClickThrough();
        EditFrame.Visibility = _settings.EditMode ? Visibility.Visible : Visibility.Collapsed;
        if (!_settings.EditMode) _guide?.HideGuides();
        Rebuild();
    }

    /// <summary>編集モードでプレビューするサンプル行 (表記設定が変わったら作り直す)。</summary>
    private List<KeyEntry> SampleEntries()
    {
        var key = $"{_settings.OSLabelStyle}/{_settings.JisABCLabels}";
        if (_samples is not null && _samplesKey == key) return _samples;
        _samplesKey = key;
        List<string> Loc(params string[] tokens) => tokens.Select(_formatter.Localized).ToList();
        _samples = new List<KeyEntry>
        {
            KeyEntry.Sample(Loc("⌘", "⌥", "⌫"), isTyping: false),
            KeyEntry.Sample(Loc("⇧", "⇥"), isTyping: false),
            KeyEntry.Sample(new[] { "F3" }, isTyping: false),
            KeyEntry.Sample(Loc("⌘", "␣"), isTyping: false),
            KeyEntry.Sample(Loc("⎋"), isTyping: false, count: 2),
            KeyEntry.Sample(Loc("⌘", "⇧").Append("S").ToList(), isTyping: false),
            KeyEntry.Sample(new[] { "H", "E", "L", "L", "O" }, isTyping: true),
            KeyEntry.Sample(Loc("⌘").Append(KeyFormatter.ClickToken(0)).ToList(), isTyping: false),
        };
        return _samples;
    }

    // ── 描画 ─────────────────────────────────────────────

    /// <summary>
    /// 行一覧を差分更新する。表示領域に収まらない行は OverlayMetrics.VisibleRows が
    /// 古い方から落とす (新しい行が切れないように)。
    /// </summary>
    private void Rebuild()
    {
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        var maxRowWidth = Math.Max(60, width - 32);

        IReadOnlyList<KeyEntry> entries = _model.Entries;
        if (_settings.EditMode && entries.Count == 0)
        {
            var samples = SampleEntries();
            var rows = Math.Max(1, Math.Min(samples.Count, (int)_settings.MaxRows));
            entries = samples.Skip(samples.Count - rows).ToList();
        }

        var visible = _metrics.VisibleRows(entries, width, height);
        var ordered = _settings.StackFromTop
            ? Enumerable.Reverse(visible).ToList()
            : visible;

        // 消えた行のコントロールを破棄
        var liveIds = ordered.Select(v => v.Entry.Id).ToHashSet();
        foreach (var id in _rows.Keys.Where(id => !liveIds.Contains(id)).ToList())
        {
            _rows.Remove(id);
        }

        RowsPanel.VerticalAlignment = _settings.StackFromTop
            ? VerticalAlignment.Top
            : VerticalAlignment.Bottom;
        RowsPanel.Children.Clear();
        foreach (var row in ordered)
        {
            var isNew = !_rows.TryGetValue(row.Entry.Id, out var control);
            if (isNew)
            {
                control = new OverlayRowControl(row.Entry.Id);
                _rows[row.Entry.Id] = control;
            }
            control!.Update(row.Entry, row.Tokens, _settings, _formatter, maxRowWidth);
            RowsPanel.Children.Add(control);
            if (isNew && !_settings.EditMode)
            {
                control.AnimateInsertion(fromTop: _settings.StackFromTop, animationEnabled: true);
            }
        }
        RefreshVisibility();
    }

    /// <summary>
    /// 表示条件 (Mac 版 refreshVisibility)。編集モード中は「この画面では表示しない」でも
    /// 必ず見せる (見えないまま設定を触ることになり、解除もできなくなるため)。
    /// </summary>
    private void RefreshVisibility()
    {
        var shouldShow = _settings.EditMode ||
            (_settings.OverlayVisible &&
             _model.Entries.Count > 0 &&
             !_settings.HiddenOnCurrentScreen);
        if (shouldShow)
        {
            if (!IsVisible) Show();
            // 新しい行が入るたびに最前面を主張し直す (他の topmost に抜かれた場合の対策)。
            // 編集モード中は主張しない — 後から出た編集 HUD (同じく topmost) を
            // 追い越して、HUD の操作を塞いでしまうため
            if (_hwnd != IntPtr.Zero && !_settings.EditMode)
            {
                SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
        }
        else if (IsVisible)
        {
            // レイヤードウィンドウは非表示中に再描画されず、Hide 時の最後の合成画像が
            // 残る (次の Show でそれが 1 フレーム見えてしまう)。空になった状態を
            // 描画し終えてから隠すため、Hide は 1 フレーム遅らせる
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () =>
            {
                var stillHidden = !_settings.EditMode &&
                    !(_settings.OverlayVisible &&
                      _model.Entries.Count > 0 &&
                      !_settings.HiddenOnCurrentScreen);
                if (stillHidden && IsVisible) Hide();
            });
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not string name) return;
        if (name == nameof(AppSettings.EditMode))
        {
            RefreshEditMode();
            return;
        }
        if (name == nameof(AppSettings.DragToMove))
        {
            _hoverOverRow = false;
            ApplyClickThrough();
            return;
        }
        if (GrowProperties.Contains(name)) GrowToFitContent();
        if (DisplayProperties.Contains(name)) Rebuild();
    }
}
