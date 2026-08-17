using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using KeyDisp.App.Interop;
using KeyDisp.Core.Display;
using KeyDisp.Core.Settings;
using static KeyDisp.App.Interop.NativeMethods;

namespace KeyDisp.App.Overlay;

/// <summary>
/// キー表示を載せる透明・クリック透過・最前面のウィンドウ (Mac 版 OverlayWindowController の MVP 版)。
/// MVP では固定位置 (プライマリ画面の左下)。ドラッグ移動・画面別プロファイルは Phase 4。
/// </summary>
public partial class OverlayWindow : Window
{
    private readonly KeyDisplayModel _model;
    private readonly AppSettings _settings;
    private IntPtr _hwnd;

    public OverlayWindow(KeyDisplayModel model, AppSettings settings)
    {
        InitializeComponent();
        _model = model;
        _settings = settings;

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
        switch (e.PropertyName)
        {
            case nameof(AppSettings.OverlayVisible):
            case nameof(AppSettings.DisplayScale):
            case nameof(AppSettings.TextColorHex):
            case nameof(AppSettings.KeyColorHex):
            case nameof(AppSettings.BackgroundEnabled):
            case nameof(AppSettings.BackgroundOpacity):
            case nameof(AppSettings.RowAlignment):
            case nameof(AppSettings.StackFromTop):
                Rebuild();
                break;
        }
    }

    /// <summary>行一覧を作り直して表示状態を反映する (MVP: 全再構築。差分更新は Phase 3)。</summary>
    private void Rebuild()
    {
        Rows.VerticalAlignment = _settings.StackFromTop
            ? VerticalAlignment.Top
            : VerticalAlignment.Bottom;
        Rows.ItemsSource = _model.Entries
            .Select(e => new OverlayRowVm(e, _settings, Width))
            .ToList();
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
