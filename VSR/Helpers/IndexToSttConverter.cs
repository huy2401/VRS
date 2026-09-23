using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace VSR.Helpers;

public class IndexToSttConverter : IValueConverter
{
    public static readonly IndexToSttConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int index && index >= 0)
        {
            return (index + 1).ToString();
        }
        return "1";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
