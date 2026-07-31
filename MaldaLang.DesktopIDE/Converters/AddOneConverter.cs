// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Globalization;
using System.Windows.Data;

namespace MaldaLang.DesktopIDE.Converters;

public class AddOneConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intValue)
        {
            return (intValue + 1).ToString();
        }
        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string strValue && int.TryParse(strValue, out int intValue))
        {
            return intValue - 1;
        }
        return 0;
    }
}
