using Microsoft.Win32;

namespace KeyDisp.App.Services;

/// <summary>
/// ログイン時起動 (Mac 版 SMAppService 相当)。HKCU の Run キーで登録する。
/// MSIX 配布に切り替える場合は StartupTask 実装へ差し替える。
/// </summary>
public sealed class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "KeyDisp";

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (exe is null) return;
                key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception)
        {
            // レジストリ書き込み失敗でアプリを落とさない
        }
    }
}
