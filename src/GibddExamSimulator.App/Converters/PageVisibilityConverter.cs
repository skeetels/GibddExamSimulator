using System.Globalization;
using System.Windows;
using System.Windows.Data;
using GibddExamSimulator.ViewModels;

namespace GibddExamSimulator.Converters;

public sealed class PageVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not PageKind current || parameter is not string name ||
            !Enum.TryParse<PageKind>(name, out var expected))
            return Visibility.Collapsed;
        return current == expected ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
