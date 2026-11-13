// Added for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using System.Globalization;
using System.Windows.Data;

namespace ASPAssistant.App.Converters;

public sealed class LineupTrackedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values[0] is string id && values[1] is IReadOnlySet<string> ids && ids.Contains(id);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
