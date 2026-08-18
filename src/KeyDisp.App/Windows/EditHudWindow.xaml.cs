using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using KeyDisp.App.Interop;
using KeyDisp.Core.Settings;
using static KeyDisp.App.Interop.NativeMethods;
using static KeyDisp.App.Services.Localization;

namespace KeyDisp.App.Windows;

/// <summary>
/// 表示編集モードの設定 HUD (Mac 版 EditHUDPanel 相当)。
/// オーバーレイとは独立したウィンドウなので、オーバーレイが画面いっぱいに
/// 広がっても常に画面内で操作できる。閉じる = 編集モード終了。
/// </summary>
public partial class EditHudWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ScreenService _screens;

    public EditHudWindow(AppSettings settings, ScreenService screens)
    {
        InitializeComponent();
        _settings = settings;
        _screens = screens;
        DataContext = settings;
        Closing += OnClosing;
    }

    private void ApplyLabels()
    {
        Title = L("表示編集モード", "Edit Display Mode");
        HintText.Text = L(
            "破線の枠の内側をドラッグすると移動、枠の端をドラッグするとリサイズできます。",
            "Drag inside the dashed frame to move it, or drag its edges to resize.");
        HiddenCheck.Content = L("この画面ではキー表示を出さない", "Don't show on this screen");
        StyleCombo.ItemsSource = new[]
        {
            L("シンプル", "Simple"),
            L("キーキャップ", "Keycap"),
            L("カスタム画像", "Custom Image"),
        };
        SizeLabel.Text = L("サイズ", "Size");
        RowsLabel.Text = L("表示の行数", "Rows");
        StackCheck.Content = L("新しい入力を上に表示（ぶら下がり式）", "Newest at top (hang-down)");
        AlignLabel.Text = L("行の揃え", "Align");
        AlignCombo.ItemsSource = new[]
        {
            L("左揃え", "Left"),
            L("中央揃え", "Center"),
            L("右揃え", "Right"),
        };
        TextColorLabel.Text = L("文字色", "Text");
        KeyColorLabel.Text = L("背景色", "Key");
        BgCheck.Content = L("背景を表示", "Show Background");
        OpacityLabel.Text = L("背景の濃さ", "Opacity");
        DoneButton.Content = L("完了", "Done");
    }

    /// <summary>
    /// カーソルのある画面 (= メニューを操作した画面) に出す。
    /// その画面内で以前動かした位置なら尊重し、別の画面なら右上 -24 に出し直す。
    /// </summary>
    public void ShowOnCursorScreen()
    {
        ApplyLabels();
        Show();
        Activate();
        var hwnd = new WindowInteropHelper(this).Handle;
        GetWindowRect(hwnd, out var r);
        var (cx, cy) = _screens.CursorPosition();
        var target = _screens.FromPoint(cx, cy) ?? _screens.Primary();
        var centerX = (r.Left + r.Right) / 2.0;
        var centerY = (r.Top + r.Bottom) / 2.0;
        if (!target.Bounds.Contains(centerX, centerY))
        {
            var wa = target.WorkArea;
            var width = r.Right - r.Left;
            var height = r.Bottom - r.Top;
            SetWindowPos(hwnd, IntPtr.Zero,
                (int)(wa.MaxX - width - 24), (int)(wa.Y + 24), 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }

    private void OnDone(object sender, RoutedEventArgs e) => _settings.EditMode = false;

    private void OnSwatchClicked(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        ColorPickerHelper.HandleSwatchClick(sender, e, _settings);

    /// <summary>閉じるボタン = 編集モード終了 (ウィンドウ自体は使い回す)。</summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        _settings.EditMode = false;
    }
}
