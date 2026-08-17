using KeyDisp.Core.Input;
using KeyDisp.Core.Settings;

namespace KeyDisp.Core.Formatting;

/// <summary>
/// キーコード → 表示トークン列への変換 (Mac 版 KeyFormatter.swift の VK ベース移植)。
///
/// 内部の正準トークンは Mac 版と同じ Mac 記号 ("↩" "␣" "⌘" など) を使い、
/// 表示時に osLabelStyle で Windows 表記へ写像する。Mac 版と逆向きに見えるが、
/// 対応表 (windowsLabels) と localized() のロジックをそのまま共有できる。
/// 既定スタイルが Windows なので、ユーザーには「Enter」「Ctrl」が見える。
///
/// 修飾キーの対応は物理キーではなく「ショートカット互換」: 物理 Ctrl ⇔ ⌘
/// (Mac の ⌘C を Windows の Ctrl+C に対応させる教室用途の判断。Mac 版 DEVLOG 参照)。
/// Win キーには Mac 対応が無いためどのスタイルでも "Win"。
/// </summary>
public sealed class KeyFormatter
{
    private readonly AppSettings _settings;
    private readonly IKeyboardLayout _layout;

    public KeyFormatter(AppSettings settings, IKeyboardLayout layout)
    {
        _settings = settings;
        _layout = layout;
    }

    // ── 特殊キー (vk → Mac 正準トークン) ──────────────────

    private static readonly Dictionary<int, string> SpecialKeys = new()
    {
        [Vk.Enter] = "↩",
        [Vk.Tab] = "⇥",
        [Vk.Space] = "␣",
        [Vk.Back] = "⌫",
        [Vk.Delete] = "⌦",
        [Vk.Escape] = "⎋",
        [Vk.CapsLock] = "⇪",
        [Vk.Home] = "↖",
        [Vk.End] = "↘",
        [Vk.PageUp] = "⇞",
        [Vk.PageDown] = "⇟",
        [Vk.Left] = "←",
        [Vk.Up] = "↑",
        [Vk.Right] = "→",
        [Vk.Down] = "↓",
        [Vk.Insert] = "Ins",
        [Vk.PrintScreen] = "PrtSc",
        [Vk.ScrollLock] = "ScrLk",
        [Vk.Pause] = "Pause",
        [Vk.Apps] = "Menu",
        [Vk.NumLock] = "NumLock",
        // IME まわり。正準は Mac 版と同じ 英数/かな で、Windows 表記が 無変換/変換
        [Vk.NonConvert] = "英数",
        [Vk.Convert] = "かな",
        [Vk.Kana] = "カナ",
        [Vk.Kanji] = "半/全",
        [Vk.OemAuto] = "半/全",
        [Vk.OemEnlw] = "半/全",
        // メディアキー (Windows では通常の VK として届く。Mac 版の NX 写像は不要)
        [Vk.VolumeMute] = "Mute",
        [Vk.VolumeDown] = "Vol−",
        [Vk.VolumeUp] = "Vol+",
        [Vk.MediaPrev] = "⏮",
        [Vk.MediaPlayPause] = "⏯",
        [Vk.MediaNext] = "⏭",
        [Vk.MediaStop] = "⏹",
    };

    /// <summary>Mac 記号 → Windows 表記の対応表 (Mac 版と同一。ショートカット互換の対応)。</summary>
    private static readonly Dictionary<string, string> WindowsLabels = new()
    {
        ["⌘"] = "Ctrl", ["⌥"] = "Alt", ["⇧"] = "Shift",
        ["↩"] = "Enter", ["⇥"] = "Tab", ["␣"] = "Space",
        ["⌫"] = "BackSpace", ["⌦"] = "Delete", ["⎋"] = "Esc", ["⇪"] = "CapsLock",
        ["↖"] = "Home", ["↘"] = "End", ["⇞"] = "PgUp", ["⇟"] = "PgDn",
        ["英数"] = "無変換", ["かな"] = "変換",
    };

    /// <summary>表記設定 (ABC/あいう、Windows/Mac/併存) を 1 トークンへ適用する。</summary>
    public string Localized(string token)
    {
        var mac = token;
        if (_settings.JisABCLabels)
        {
            if (token == "英数") mac = "ABC";
            if (token == "かな") mac = "あいう";
        }
        switch (_settings.OSLabelStyle)
        {
            case OSLabelStyle.Mac:
                return mac;
            case OSLabelStyle.Windows:
                return WindowsLabels.TryGetValue(token, out var win) ? win : mac;
            case OSLabelStyle.Both:
                if (WindowsLabels.TryGetValue(token, out var w) && w != mac)
                {
                    // Windows 版なのでネイティブ表記 (Windows) を先に置く
                    return $"{w}/{mac}";
                }
                return mac;
            default:
                return mac;
        }
    }

    // ── 修飾キー ─────────────────────────────────────────

    /// <summary>
    /// 表示順の基準となる修飾キーの並び。Windows の慣例 (Win, Ctrl, Alt, Shift)。
    /// 正準トークンは Mac 記号 (Ctrl→⌘ はショートカット互換の対応)。
    /// </summary>
    public static readonly IReadOnlyList<(ModifierKeys Flag, string Symbol)> ModifierDisplayOrder =
        new (ModifierKeys, string)[]
        {
            (ModifierKeys.Win, "Win"),
            (ModifierKeys.Control, "⌘"),
            (ModifierKeys.Alt, "⌥"),
            (ModifierKeys.Shift, "⇧"),
        };

    /// <summary>
    /// 修飾キーの記号列。pressOrder は押された順 (設定がオンのときだけ使われる)。
    /// pressOrder に無いものは標準の並びで後ろに付ける。
    /// </summary>
    public List<string> ModifierTokens(ModifierKeys flags, IReadOnlyList<ModifierKeys>? pressOrder = null)
    {
        var order = ModifierDisplayOrder;
        if (pressOrder is not null && _settings.ModifierPressOrder)
        {
            var sorted = new List<(ModifierKeys Flag, string Symbol)>();
            foreach (var f in pressOrder)
            {
                var item = ModifierDisplayOrder.FirstOrDefault(o => o.Flag == f);
                if (item.Symbol is not null && sorted.All(s => s.Flag != f)) sorted.Add(item);
            }
            sorted.AddRange(ModifierDisplayOrder.Where(o => sorted.All(s => s.Flag != o.Flag)));
            order = sorted;
        }

        var tokens = new List<string>();
        foreach (var (flag, symbol) in order)
        {
            if (flags.HasFlag(flag) && flag != ModifierKeys.None) tokens.Add(symbol);
        }
        // 表記変換後に同じ表示が並んだ場合は重複を除く (Mac 版の ⌃/⌘→Ctrl 対策の踏襲)
        var mapped = tokens.Select(Localized);
        var seen = new HashSet<string>();
        return mapped.Where(t => seen.Add(t)).ToList();
    }

    /// <summary>vk が修飾キーなら対応するフラグ、でなければ None。</summary>
    public static ModifierKeys ModifierOf(int vk) => vk switch
    {
        Vk.Shift or Vk.LShift or Vk.RShift => ModifierKeys.Shift,
        Vk.Control or Vk.LControl or Vk.RControl => ModifierKeys.Control,
        Vk.Menu or Vk.LMenu or Vk.RMenu => ModifierKeys.Alt,
        Vk.LWin or Vk.RWin => ModifierKeys.Win,
        _ => ModifierKeys.None,
    };

    public static bool IsModifierVk(int vk) => ModifierOf(vk) != ModifierKeys.None;

    /// <summary>修飾キーの L/R 個別 VK の一覧 (reconcile での実状態照会用)。</summary>
    public static readonly IReadOnlyList<int> ModifierVks = new[]
    {
        Vk.LShift, Vk.RShift, Vk.LControl, Vk.RControl, Vk.LMenu, Vk.RMenu, Vk.LWin, Vk.RWin,
    };

    // ── キーの分類 ────────────────────────────────────────

    public static bool IsArrowKey(int vk) => vk is >= Vk.Left and <= Vk.Down;

    /// <summary>リピートしてもモードが変わらない入力切替キー (×n に数えない)。</summary>
    public static readonly IReadOnlySet<int> NoRepeatVks = new HashSet<int>
    {
        Vk.NonConvert, Vk.Convert, Vk.Kana, Vk.Kanji, Vk.OemAuto, Vk.OemEnlw,
    };

    /// <summary>
    /// 文字キーか (specialKeys にも修飾キーにも無い、文字が入力されるキー)。
    /// 英数字・テンキー・OEM 記号キーを対象にする。
    /// </summary>
    public static bool IsCharacterKey(int vk)
    {
        if (IsModifierVk(vk) || SpecialKeys.ContainsKey(vk)) return false;
        return vk is >= Vk.D0 and <= Vk.D9        // 数字
            or >= Vk.A and <= Vk.Z                // 英字
            or >= Vk.Numpad0 and <= Vk.NumpadDivide // テンキー
            or >= 0xBA and <= 0xC0                // OEM_1〜OEM_3 (;: =+ ,< -_ .> /? `~)
            or >= 0xDB and <= 0xDF                // OEM_4〜OEM_8 ([{ \| ]} '" ...)
            or 0xE2;                              // OEM_102 (JIS の \_ など)
    }

    // ── ラベル ────────────────────────────────────────────

    /// <summary>
    /// キーの表示ラベル。
    /// applyLabelStyle=false は表記スタイルを適用しない (Mac 記号のまま。
    /// タイピング中のスペース「␣」やアプリ内 UI 用)。
    /// preserveCase=true はレイアウトの実入力どおりの大小 (distinguishCase 用)。
    /// </summary>
    public string KeyLabel(int vk, bool shifted, bool capsLock = false,
        bool applyLabelStyle = true, bool preserveCase = false)
    {
        if (vk is >= Vk.F1 and <= Vk.F24) return $"F{vk - Vk.F1 + 1}";
        if (SpecialKeys.TryGetValue(vk, out var symbol))
        {
            return applyLabelStyle ? Localized(symbol) : symbol;
        }
        var ch = _layout.CharacterFor(vk, shifted, capsLock);
        if (!string.IsNullOrEmpty(ch))
        {
            return preserveCase ? ch : ch.ToUpperInvariant();
        }
        return $"key{vk}";
    }

    /// <summary>JIS かな配列のひらがな・記号 (kanaDisplay 用)。対応表に無ければ null。</summary>
    public string? KanaLabel(int vk, bool shifted) => JisKanaTable.Label(vk, shifted);

    public bool IsJapaneseInputMode() => _layout.IsJapaneseInputMode();

    // ── クリック ─────────────────────────────────────────

    public const string ClickTokenLeft = "«click»";
    public const string ClickTokenRight = "«rclick»";
    public const string ClickTokenMiddle = "«mclick»";

    /// <summary>クリックの疑似トークン (button: 0=左, 1=右, 2=中)。描画時にアイコンへ置換する。</summary>
    public static string ClickToken(int button) => button switch
    {
        1 => ClickTokenRight,
        2 => ClickTokenMiddle,
        _ => ClickTokenLeft,
    };

    public static bool IsClickToken(string token) =>
        token is ClickTokenLeft or ClickTokenRight or ClickTokenMiddle;

    /// <summary>IME 切替キーのトークンか (globeOnImeKeys の装飾対象)。</summary>
    public bool IsImeSwitchToken(string token) => token
        is "英数" or "かな" or "ABC" or "あいう" or "カナ" or "半/全"
        or "無変換" or "変換"
        || token.StartsWith("無変換/") || token.StartsWith("変換/");
}
