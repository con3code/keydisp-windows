using System.Globalization;
using KeyDisp.Core.Settings;

namespace KeyDisp.App.Services;

/// <summary>
/// 簡易ローカライズ (Mac 版の L(ja, en) ヘルパー方式。.resx は使わない)。
/// 起動時に Configure でアプリ設定を渡す。
/// </summary>
public static class Localization
{
    private static AppSettings? _settings;

    public static void Configure(AppSettings settings) => _settings = settings;

    public static bool IsJapanese => _settings?.Language switch
    {
        AppLanguage.Japanese => true,
        AppLanguage.English => false,
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja",
    };

    /// <summary>現在の言語設定に応じて日本語 / 英語の文言を返す。</summary>
    public static string L(string ja, string en) => IsJapanese ? ja : en;
}
