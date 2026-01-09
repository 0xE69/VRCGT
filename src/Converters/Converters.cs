using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Collections;

namespace VRCGroupTools.Converters;

public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && !b;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && !b;
    }
}

public class InverseBoolToVisConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && b ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToLoginTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && b ? "Logging in..." : "Login";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToVerifyTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && b ? "Verifying..." : "Verify";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToScanTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && b ? "Stop" : "Start Scan";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToScanColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && b ? new SolidColorBrush(Color.FromRgb(244, 67, 54)) : new SolidColorBrush(Color.FromRgb(124, 77, 255));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ModuleToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var currentModule = value as string;
        var targetModule = parameter as string;
        return currentModule == targetModule ? new SolidColorBrush(Colors.White) : new SolidColorBrush(Color.FromRgb(136, 136, 136));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class StringMatchConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value?.ToString() == parameter?.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
        {
            return parameter?.ToString() ?? "";
        }
        return Binding.DoNothing;
    }
}

public class BoolToScanCommandConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // This is a simplified converter - actual implementation would need the ViewModel
        return null!;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToVerifiedColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && b 
            ? new SolidColorBrush(Color.FromRgb(76, 175, 80))   // Green for verified
            : new SolidColorBrush(Color.FromRgb(158, 158, 158)); // Gray for unverified
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToVerifiedTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && b ? "✓ 18+ Verified" : "Not Verified";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

// Audit Log specific converters
public class BoolToPausePlayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && b ? "⏸️" : "▶️";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToPollingColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && b 
            ? new SolidColorBrush(Color.FromRgb(76, 175, 80))   // Green
            : new SolidColorBrush(Color.FromRgb(158, 158, 158)); // Gray
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToPollingTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && b ? "Auto-refresh ON (every 60s)" : "Auto-refresh OFF";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class CountGreaterThanZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToSaveButtonConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool isEdit && isEdit ? "💾 Save Changes" : "📤 Create Post";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value != null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Returns Visible if value is not null/empty, Collapsed otherwise
        if (value == null) return Visibility.Collapsed;
        if (value is string s && string.IsNullOrWhiteSpace(s)) return Visibility.Collapsed;
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class StringMatchToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var valueStr = value?.ToString() ?? "(null)";
        var paramStr = parameter?.ToString() ?? "(null)";
        var isMatch = valueStr == paramStr;
        Console.WriteLine($"[VISIBILITY] Value='{valueStr}' Parameter='{paramStr}' Match={isMatch}");
        return isMatch ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class CollectionContainsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
        {
            return false;
        }

        var collection = values[0] as IEnumerable;
        var item = values[1];

        if (collection == null || item == null)
        {
            return false;
        }

        foreach (var entry in collection)
        {
            if (Equals(entry, item))
            {
                return true;
            }
        }
        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class DayToggleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is List<DayOfWeek> days && parameter is string dayStr && Enum.TryParse<DayOfWeek>(dayStr, out var day))
        {
            return days.Contains(day);
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Note: we return the full list; binding must be TwoWay and bound to the collection
        return Binding.DoNothing;
    }
}

public class DaysOfWeekStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is List<DayOfWeek> days && days.Count > 0)
        {
            return string.Join(",", days.Select(d => d.ToString()[..3]));
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var list = new List<DayOfWeek>();
        if (value is string s && !string.IsNullOrWhiteSpace(s))
        {
            var parts = s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                if (Enum.TryParse<DayOfWeek>(Normalize(part), true, out var day))
                {
                    if (!list.Contains(day)) list.Add(day);
                }
            }
        }
        return list;
    }

    private static string Normalize(string token)
    {
        return token switch
        {
            "Mon" => "Monday",
            "Tue" => "Tuesday",
            "Wed" => "Wednesday",
            "Thu" => "Thursday",
            "Fri" => "Friday",
            "Sat" => "Saturday",
            "Sun" => "Sunday",
            _ => token
        };
    }
}

public class BoolToTestButtonConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && b ? "Testing..." : "Test";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
