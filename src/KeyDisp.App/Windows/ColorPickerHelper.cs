using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using KeyDisp.Core.Settings;

namespace KeyDisp.App.Windows;

/// <summary>
/// 色スウォッチのクリックで標準のカラーピッカー (Win32 ColorDialog) を開く共通処理。
/// スウォッチ要素の Tag に AppSettings の hex プロパティ名を入れておく。
/// </summary>
internal static class ColorPickerHelper
{
    public static void HandleSwatchClick(object sender, MouseButtonEventArgs e, AppSettings settings)
    {
        if (sender is not FrameworkElement element || element.Tag is not string propertyName) return;
        var property = typeof(AppSettings).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property is null) return;

        var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };
        try
        {
            if (property.GetValue(settings) is string hex &&
                ColorConverter.ConvertFromString(hex) is Color current)
            {
                dialog.Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B);
            }
        }
        catch (FormatException)
        {
        }
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            property.SetValue(settings,
                $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}");
        }
        e.Handled = true;
    }
}
