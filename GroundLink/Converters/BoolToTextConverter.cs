using System.Globalization;
using System.Windows.Data;

namespace GroundLink.Converters;

/// <summary>
/// bool 값을 글자로 바꾸는 변환기.
/// </summary>
public class BoolToTextConverter : IValueConverter
{
    public string TrueText { get; set; } = "예";
    public string FalseText { get; set; } = "아니요";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? TrueText : FalseText;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}