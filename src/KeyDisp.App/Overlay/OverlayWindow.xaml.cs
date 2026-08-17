using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using KeyDisp.Core.Display;
using KeyDisp.Core.Formatting;
using KeyDisp.Core.Layout;
using KeyDisp.Core.Settings;
using static KeyDisp.App.Interop.NativeMethods;

namespace KeyDisp.App.Overlay;

/// <summary>
/// キー表示を載せる透明・クリック透過・最前面のウィンドウ (Mac 版 OverlayWindowController 相当)。
/// 行の増減は差分更新し、出入り・フェード・パルスのアニメーションは OverlayRowControl が担う。
/// 現状は固定位置 (プライマリ画面の左下)。ドラッグ移動・画面別プロファイルは Phase 4。
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
    };

    private readonly KeyDisplayModel _model;
    private readonly AppSettings _settings;
    private readonly OverlayMetrics _metrics;
    private readonly KeyFormatter _formatter;
    private readonly Dictionary<Guid, OverlayRowControl> _rows = new();
    private IntPtr _hwnd;

    public OverlayWindow(
        KeyDisplayModel model, AppSettings settings,
        OverlayMetrics metrics, KeyFormatter formatter)
    {
        InitializeComponent();
        _model = model;
        _settings = settings;
        _metrics = metrics;
        _formatter = formatter;

        // プライマリ画面の作業領域の左下 +40 (Mac 版 resetPosition と同じ既定位置)
        var work = SystemParameters.WorkArea;
        Left = work.Left + 40;
        Top = work.Bottom - Height - 40;

        _model.Changed += Rebuild;
        _settings.PropertyChanged += OnSettingsChanged;
        SourceInitialized += OnSourceInitialized;

        // 折り返し判定用にオーバーレイの内側幅を publish する
        _settings.OverlayContentWidth = Width - 32;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        // クリック透過・非アクティブ化・Alt+Tab 非表示
        var ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
        ex |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(ex));
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is string name && DisplayProperties.Contains(name)) Rebuild();
    }

    /// <summary>
    /// 行一覧を差分更新する。表示領域に収まらない行は OverlayMetrics.VisibleRows が
    /// 古い方から落とす (新しい行が切れないように)。
    /// </summary>
    private void Rebuild()
    {
        var maxRowWidth = Math.Max(60, Width - 32);
        var visible = _metrics.VisibleRows(_model.Entries, Width, Height);
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
            if (isNew)
            {
                control.AnimateInsertion(
                    fromTop: _settings.StackFromTop, animationEnabled: true);
            }
        }
        RefreshVisibility();
    }

    /// <summary>
    /// 表示条件: overlayVisible かつ行がある (Mac 版 refreshVisibility)。
    /// 行が無いときは Hide して描画合成の対象から外す。
    /// </summary>
    private void RefreshVisibility()
    {
        var wantsVisible = _settings.OverlayVisible && _model.Entries.Count > 0;
        if (wantsVisible)
        {
            if (!IsVisible) Show();
            // 新しい行が入るたびに最前面を主張し直す (他の topmost に抜かれた場合の対策)
            if (_hwnd != IntPtr.Zero)
            {
                SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
        }
        else if (IsVisible)
        {
            Hide();
        }
    }
}
