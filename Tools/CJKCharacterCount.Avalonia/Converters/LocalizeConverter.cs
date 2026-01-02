using Avalonia.Data.Converters;
using CJKCharacterCount.Core;
using System;
using System.Globalization;

namespace CJKCharacterCount.Avalonia.Converters;

public class LocalizeConverter : IValueConverter
{
    public static readonly LocalizeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string key)
        {
            return Localization.Get(key);
        }
        if (parameter is string paramKey)
        {
            return Localization.Get(paramKey);
        }
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
