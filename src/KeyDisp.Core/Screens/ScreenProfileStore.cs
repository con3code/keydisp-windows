using System.ComponentModel;
using KeyDisp.Core.Scheduling;
using KeyDisp.Core.Settings;

namespace KeyDisp.Core.Screens;

/// <summary>
/// 画面 (安定 ID) ごとの表示プロファイルと定位置の記憶
/// (Mac 版 OverlayWindowController の rememberProfile / applyProfile / framesByScreen の移植)。
/// 「会場は大きく濃く・手元は小さく薄く」のような画面ごとの使い分けを支える。
///
/// - 編集 HUD にある 10 項目の変更を 150ms デバウンスで現在画面のプロファイルとして記憶
/// - 適用中は記憶しない (再入ガード)。値が実際に変わる項目だけ書き込む
/// - フレームは別途 RememberFrame で記憶 (ドラッグ中の保留は呼び出し側が制御)
/// </summary>
public sealed class ScreenProfileStore : IDisposable
{
    /// <summary>プロファイルとして記憶する設定キー (編集 HUD にある項目と一致)。</summary>
    public static readonly IReadOnlySet<string> ProfileProperties = new HashSet<string>
    {
        nameof(AppSettings.KeyStyle),
        nameof(AppSettings.DisplayScale),
        nameof(AppSettings.MaxRows),
        nameof(AppSettings.StackFromTop),
        nameof(AppSettings.RowAlignment),
        nameof(AppSettings.TextColorHex),
        nameof(AppSettings.KeyColorHex),
        nameof(AppSettings.BackgroundEnabled),
        nameof(AppSettings.BackgroundOpacity),
        nameof(AppSettings.HiddenOnCurrentScreen),
    };

    private readonly AppSettings _settings;
    private readonly IDelayScheduler _scheduler;
    private readonly Func<string?> _currentScreenId;
    private readonly Dictionary<string, ScreenProfileDocument> _profiles;
    private IDisposable? _debounce;
    private bool _isApplying;

    /// <summary>記憶内容が変わった (リポジトリが保存する契機)。</summary>
    public event Action? Changed;

    public ScreenProfileStore(
        AppSettings settings, IDelayScheduler scheduler,
        Func<string?> currentScreenId,
        Dictionary<string, ScreenProfileDocument>? initialProfiles = null)
    {
        _settings = settings;
        _scheduler = scheduler;
        _currentScreenId = currentScreenId;
        _profiles = initialProfiles ?? new Dictionary<string, ScreenProfileDocument>();
        _settings.PropertyChanged += OnSettingsChanged;
    }

    public IReadOnlyDictionary<string, ScreenProfileDocument> Profiles => _profiles;

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isApplying) return;
        if (e.PropertyName is not string name || !ProfileProperties.Contains(name)) return;
        _debounce?.Dispose();
        _debounce = _scheduler.Schedule(TimeSpan.FromMilliseconds(150), () =>
        {
            _debounce = null;
            if (_currentScreenId() is string id) RememberProfile(id);
        });
    }

    /// <summary>いまの表示設定一式を、指定した画面のプロファイルとして記憶する。</summary>
    public void RememberProfile(string screenId)
    {
        var doc = GetOrAdd(screenId);
        doc.Style = (int)_settings.KeyStyle;
        doc.DisplayScale = _settings.DisplayScale;
        doc.MaxRows = _settings.MaxRows;
        doc.StackFromTop = _settings.StackFromTop;
        doc.RowAlignment = (int)_settings.RowAlignment;
        doc.TextColorHex = _settings.TextColorHex;
        doc.KeyColorHex = _settings.KeyColorHex;
        doc.BackgroundEnabled = _settings.BackgroundEnabled;
        doc.BackgroundOpacity = _settings.BackgroundOpacity;
        doc.Hidden = _settings.HiddenOnCurrentScreen;
        Changed?.Invoke();
    }

    /// <summary>指定した画面の定位置を記憶する。</summary>
    public void RememberFrame(string screenId, RectD frame)
    {
        GetOrAdd(screenId).Frame = frame.ToArray();
        Changed?.Invoke();
    }

    /// <summary>その画面で記憶している定位置 (画面と交差しているもののみ有効)。</summary>
    public RectD? StoredFrame(string screenId, RectD screenBounds)
    {
        if (!_profiles.TryGetValue(screenId, out var doc)) return null;
        var rect = RectD.FromArray(doc.Frame);
        if (rect is not RectD r || !screenBounds.IntersectsWith(r)) return null;
        return r;
    }

    /// <summary>
    /// 指定した画面のプロファイルへ切り替える (無ければ「表示する」を既定とする)。
    /// 値が実際に変わるものだけ書き込む。適用中の変更は記憶しない。
    /// </summary>
    public void Adopt(string screenId)
    {
        _isApplying = true;
        try
        {
            if (_profiles.TryGetValue(screenId, out var doc))
            {
                Apply(doc);
            }
            else if (_settings.HiddenOnCurrentScreen)
            {
                _settings.HiddenOnCurrentScreen = false;
            }
        }
        finally
        {
            _isApplying = false;
        }
    }

    private void Apply(ScreenProfileDocument doc)
    {
        if (doc.Style is int style && (KeyStyle)style != _settings.KeyStyle)
        {
            _settings.KeyStyle = (KeyStyle)style;
        }
        if (doc.DisplayScale is double scale && Math.Abs(scale - _settings.DisplayScale) > 0.001)
        {
            _settings.DisplayScale = scale;
        }
        if (doc.MaxRows is double rows && Math.Abs(rows - _settings.MaxRows) > 0.001)
        {
            _settings.MaxRows = rows;
        }
        if (doc.StackFromTop is bool stack && stack != _settings.StackFromTop)
        {
            _settings.StackFromTop = stack;
        }
        if (doc.RowAlignment is int align && (RowAlignment)align != _settings.RowAlignment)
        {
            _settings.RowAlignment = (RowAlignment)align;
        }
        if (doc.TextColorHex is string text && text != _settings.TextColorHex)
        {
            _settings.TextColorHex = text;
        }
        if (doc.KeyColorHex is string key && key != _settings.KeyColorHex)
        {
            _settings.KeyColorHex = key;
        }
        if (doc.BackgroundEnabled is bool bg && bg != _settings.BackgroundEnabled)
        {
            _settings.BackgroundEnabled = bg;
        }
        if (doc.BackgroundOpacity is double opacity && Math.Abs(opacity - _settings.BackgroundOpacity) > 0.001)
        {
            _settings.BackgroundOpacity = opacity;
        }
        var hidden = doc.Hidden ?? false;
        if (hidden != _settings.HiddenOnCurrentScreen)
        {
            _settings.HiddenOnCurrentScreen = hidden;
        }
    }

    /// <summary>起動時: いまの画面の「この画面では表示しない」だけ復元する。</summary>
    public void RestoreHiddenFlag(string screenId)
    {
        if (_profiles.TryGetValue(screenId, out var doc))
        {
            _settings.HiddenOnCurrentScreen = doc.Hidden ?? false;
        }
    }

    /// <summary>画面ごとの記憶をすべて消す (表示位置リセット用)。</summary>
    public void Reset()
    {
        _profiles.Clear();
        Changed?.Invoke();
    }

    private ScreenProfileDocument GetOrAdd(string screenId)
    {
        if (!_profiles.TryGetValue(screenId, out var doc))
        {
            doc = new ScreenProfileDocument();
            _profiles[screenId] = doc;
        }
        return doc;
    }

    public void Dispose() => _settings.PropertyChanged -= OnSettingsChanged;
}
