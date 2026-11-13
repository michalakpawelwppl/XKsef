using System.Globalization;
using System.Windows.Data;

namespace xKsef.Wpf.Converters;

public sealed class LocalDateTimeOffsetConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is DateTimeOffset date ? date.ToLocalTime().ToString("yyyy-MM-dd HH:mm", culture) : string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
