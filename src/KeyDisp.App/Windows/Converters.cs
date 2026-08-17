using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace KeyDisp.App.Windows;

/// <summary>enum ⇄ ComboBox.SelectedIndex の変換。</summary>
public sealed class EnumToIntConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        System.Convert.ToInt32(value);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Enum.ToObject(targetType, System.Convert.ToInt32(value));
}

/// <summary>"#RRGGBB" → プレビュー用ブラシ。</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            if (value is string hex && ColorConverter.ConvertFromString(hex) is Color color)
            {
                return new SolidColorBrush(color);
            }
        }
        catch (FormatException)
        {
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
