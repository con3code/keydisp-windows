using System.ComponentModel;
using System.IO;
using System.Windows.Threading;
using KeyDisp.Core.Settings;

namespace KeyDisp.App.Services;

/// <summary>
/// settings.json (%APPDATA%\KeyDisp) の読み書き。
/// 保存は 500ms のデバウンス + 一時ファイル→アトミック置換 (強制終了時の破損防止)。
/// 実行時のみのプロパティ (editMode 等) は保存対象にしない。
/// </summary>
public sealed class SettingsRepository : IDisposable
{
    private static readonly HashSet<string> RuntimeOnlyProperties = new()
    {
        nameof(AppSettings.EditMode),
        nameof(AppSettings.OverlayContentWidth),
        nameof(AppSettings.HotCornerSuppressed),
        nameof(AppSettings.HiddenOnCurrentScreen),
    };

    private readonly AppSettings _settings;
    private readonly string _path;
    private readonly DispatcherTimer _debounce;
    /// <summary>画面別プロファイル (Phase 4 で使用)。ロード時の内容を保存時に持ち越す。</summary>
    private Dictionary<string, ScreenProfileDocument>? _displayProfiles;

    public SettingsRepository(AppSettings settings, string? path = null)
    {
        _settings = settings;
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KeyDisp", "settings.json");
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Save();
        };
    }

    /// <summary>起動時に一度呼ぶ。ファイルが無い・壊れている場合は既定値のまま。</summary>
    public void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var doc = SettingsDocument.FromJson(File.ReadAllText(_path));
            doc.Apply(_settings);
            _displayProfiles = doc.DisplayProfiles;
        }
        catch (Exception)
        {
            // 壊れたファイルは読み捨てて既定値で起動する (次の保存で上書きされる)
        }
    }

    /// <summary>設定変更の監視を開始する (Load の後に呼ぶ)。</summary>
    public void Attach()
    {
        _settings.PropertyChanged += OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is string name && RuntimeOnlyProperties.Contains(name)) return;
        _debounce.Stop();
        _debounce.Start();
    }

    public void Save()
    {
        try
        {
            var doc = SettingsDocument.From(_settings);
            doc.DisplayProfiles = _displayProfiles;
            var dir = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, doc.ToJson());
            if (File.Exists(_path)) File.Replace(tmp, _path, null);
            else File.Move(tmp, _path);
        }
        catch (Exception)
        {
            // 保存失敗でアプリを落とさない (次の変更で再試行される)
        }
    }

    public void Dispose()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
        if (_debounce.IsEnabled)
        {
            _debounce.Stop();
            Save(); // 保留中の変更を確定
        }
    }
}
