using System.ComponentModel;
using System.Media;
using System.Windows;
using System.Windows.Input;
using KeyDisp.Core.Settings;
using static KeyDisp.App.Services.Localization;

namespace KeyDisp.App.Windows;

/// <summary>
/// 設定画面 (Mac 版 SettingsView 相当。5 ペイン)。
/// 閉じるとウィンドウは隠すだけで使い回す。文言は L() で日英切替。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private bool _recordingHotKey;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        DataContext = settings;
        ApplyLabels();
        UpdateHotKeyText();
        Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };
        _settings.PropertyChanged += OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.Language))
        {
            ApplyLabels();
            UpdateHotKeyText();
        }
    }

    private void ApplyLabels()
    {
        Title = L("KeyDisp 設定", "KeyDisp Settings");

        DisplayTab.Header = L("表示", "Display");
        HoldLabel.Text = L("表示を保つ時間", "Hold Time");
        FadeLabel.Text = L("フェード時間", "Fade Time");
        TypingAnimCheck.Content = L("文字追記のアニメーション", "Animate typing");
        StepReleaseCheck.Content = L("修飾キーを離すたびに履歴を残す", "Keep history per modifier release");
        HoldJudgeLabel.Text = L("押しっぱなし判定", "Hold Threshold");
        DragToMoveCheck.Content = L("表示中のキーを掴んで移動できるようにする", "Drag visible keys to move the display");
        FollowCursorCheck.Content = L("カーソルのある画面へ表示を移す", "Follow the cursor's screen");
        TopEdgeCheck.Content = L("画面上端にカーソルを置いている間、表示を消さない", "Freeze fading while the cursor is at the top edge");
        HotCornerCheck.Content = L("画面下端にカーソルを置いている間、表示を隠す", "Hide the display while the cursor is at the bottom edge");

        LabelsTab.Header = L("キー表記", "Key Labels");
        ShowAllCheck.Content = L("すべてのキー入力を表示", "Show all keystrokes");
        CountRepeatsCheck.Content = L("同じキーの連続入力を ×n でまとめる", "Merge repeats as ×n");
        DistinguishCaseCheck.Content = L("英字の大文字と小文字を区別して表示", "Distinguish letter case");
        KanaCheck.Content = L("かな入力 (JIS かな配列) で表示", "Show as JIS kana layout");
        LabelStyleLabel.Text = L("表記スタイル", "Label Style");
        LabelStyleCombo.ItemsSource = new[]
        {
            L("Windows 表記", "Windows"),
            L("Mac 記号", "Mac symbols"),
            L("併記 (Ctrl/⌘)", "Both (Ctrl/⌘)"),
        };
        JisABCCheck.Content = L("英数/かな を ABC/あいう と表示", "Show 英数/かな as ABC/あいう");
        GlobeCheck.Content = L("入力切替キーに 🌐 を付ける", "Add 🌐 to IME switch keys");
        PlusSeparatorCheck.Content = L("キーの間に + を表示", "Separate keys with +");
        PressOrderCheck.Content = L("修飾キーを押した順に並べる", "Order modifiers by press sequence");
        ArrowGroupLabel.Text = L("矢印キー", "Arrow Keys");
        ArrowGroupCombo.ItemsSource = new[]
        {
            L("同時押しのみまとめる", "Group simultaneous only"),
            L("連続入力もまとめる", "Group consecutive too"),
            L("まとめない", "Don't group"),
        };
        KeyClickComboCheck.Content = L("押しっぱなしの文字キー + クリックも表示", "Show held key + click combos");

        DesignTab.Header = L("デザイン", "Design");
        StyleLabel.Text = L("スタイル", "Style");
        StyleCombo.ItemsSource = new[]
        {
            L("シンプル", "Simple"),
            L("キーキャップ", "Keycap"),
            L("カスタム画像", "Custom Image"),
        };
        ScaleLabel.Text = L("サイズ", "Size");
        MaxRowsLabel.Text = L("表示の行数", "Rows");
        StackTopCheck.Content = L("新しい入力を上に表示（ぶら下がり式）", "Newest at top (hang-down)");
        AlignLabel.Text = L("行の揃え", "Align");
        AlignCombo.ItemsSource = new[]
        {
            L("左揃え", "Left"),
            L("中央揃え", "Center"),
            L("右揃え", "Right"),
        };
        TextColorLabel.Text = L("文字色", "Text Color");
        OutlineCheck.Content = L("文字の縁取り", "Text outline");
        OutlineColorLabel.Text = L("縁取りの色", "Outline Color");
        KeyColorLabel.Text = L("背景色", "Key Color");
        BgCheck.Content = L("背景を表示", "Show background");
        OpacityLabel.Text = L("背景の濃さ", "Opacity");
        CustomImageLabel.Text = L("背景画像", "Background Image");
        BrowseButton.Content = L("選択…", "Browse…");

        MouseTab.Header = L("マウス", "Mouse");
        ClickInDisplayCheck.Content = L("修飾キー + クリックをキー表示に出す", "Show modifier + click in the key display");
        HighlightCheck.Content = L("クリック中のカーソルをハイライト", "Highlight the cursor while clicking");
        HighlightColorLabel.Text = L("ハイライト色", "Highlight Color");
        HighlightSizeLabel.Text = L("ハイライトの大きさ", "Highlight Size");
        BigCursorCheck.Content = L("大きいポインタを表示", "Show a big pointer");
        BigCursorSizeLabel.Text = L("ポインタの大きさ", "Pointer Size");
        BigCursorColorLabel.Text = L("ポインタの色", "Pointer Color");
        BigCursorNote.Text = L(
            "システムのカーソル自体は変更しないため、標準のカーソルは先端に重なったまま残ります。",
            "The system cursor itself is not changed, so it stays visible at the pointer's tip.");

        GeneralTab.Header = L("一般", "General");
        LanguageLabel.Text = L("言語", "Language");
        LanguageCombo.ItemsSource = new[]
        {
            L("システム標準", "System Default"),
            "日本語",
            "English",
        };
        LaunchAtLoginCheck.Content = L("ログイン時に起動", "Launch at login");
        HotKeyLabel.Text = L("表示切替のショートカット", "Toggle Shortcut");
        HotKeyHint.Text = L(
            "枠をクリックしてからキーを押すと変更できます。Shift 以外の修飾キーを含めてください。Esc でキャンセル。",
            "Click the box, then press keys to change. Include a modifier other than Shift. Esc cancels.");
        PrivacyNote.Text = L(
            "KeyDisp は入力を画面に表示するだけで、記録も送信もしません。Windows にはパスワード入力を保護する仕組みがないため、「すべてのキー入力を表示」をオンにしている間はパスワードも画面に表示され得ます。人に画面を見せる前にショートカットで表示を切ってください。",
            "KeyDisp only displays your input on screen — nothing is logged or transmitted. Windows offers no secure-input protection, so while \"Show all keystrokes\" is on, passwords may also appear on screen. Use the toggle shortcut before typing sensitive text.");
    }

    // ── ホットキーレコーダ ────────────────────────────────

    private void OnHotKeyBoxClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _recordingHotKey = true;
        HotKeyBox.Text = L("キーを押してください…", "Press keys…");
        HotKeyBox.Focus();
        e.Handled = true;
    }

    private void OnHotKeyBoxKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_recordingHotKey) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        // 修飾キー単体は無視して本命のキーを待つ
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
        {
            return;
        }
        if (key == Key.Escape)
        {
            _recordingHotKey = false;
            UpdateHotKeyText();
            return;
        }
        var mods = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= 0x1;      // MOD_ALT
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= 0x2;  // MOD_CONTROL
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= 0x4;    // MOD_SHIFT
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) mods |= 0x8; // MOD_WIN
        // Shift 単独 (または修飾なし) は通常入力と衝突するので拒否 (Mac 版と同じルール)
        if ((mods & ~0x4) == 0)
        {
            SystemSounds.Beep.Play();
            return;
        }
        _settings.HotKeyModifiers = mods;
        _settings.HotKeyVk = KeyInterop.VirtualKeyFromKey(key);
        _recordingHotKey = false;
        UpdateHotKeyText();
    }

    private void OnHotKeyBoxLostFocus(object sender, RoutedEventArgs e)
    {
        _recordingHotKey = false;
        UpdateHotKeyText();
    }

    private void UpdateHotKeyText()
    {
        var parts = new List<string>();
        var mods = _settings.HotKeyModifiers;
        if ((mods & 0x2) != 0) parts.Add("Ctrl");
        if ((mods & 0x1) != 0) parts.Add("Alt");
        if ((mods & 0x4) != 0) parts.Add("Shift");
        if ((mods & 0x8) != 0) parts.Add("Win");
        parts.Add(KeyName(_settings.HotKeyVk));
        HotKeyBox.Text = string.Join("+", parts);
    }

    private static string KeyName(int vk)
    {
        var name = KeyInterop.KeyFromVirtualKey(vk).ToString();
        // 数字キーは "D1" のように付く接頭辞を外す
        if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1])) return name[1..].ToString();
        return name;
    }

    // ── カラーピッカー ────────────────────────────────────

    private void OnSwatchClicked(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        ColorPickerHelper.HandleSwatchClick(sender, e, _settings);

    // ── 背景画像の選択 ────────────────────────────────────

    private void OnBrowseImage(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = L("画像ファイル", "Image Files") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif|" +
                     L("すべてのファイル", "All Files") + "|*.*",
        };
        if (dialog.ShowDialog(this) == true)
        {
            _settings.CustomImagePath = dialog.FileName;
        }
    }
}
