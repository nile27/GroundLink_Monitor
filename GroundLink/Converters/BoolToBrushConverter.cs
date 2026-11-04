using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace GroundLink.Converters;

/// <summary>
/// bool 값을 색(Brush)으로 바꾸는 변환기.
/// 어떤 색을 쓸지는 XAML에서 TrueBrush, FalseBrush로 지정한다.
/// </summary>
public class BoolToBrushConverter : IValueConverter
{
    public Brush TrueBrush { get; set; } = Brushes.Green;
    public Brush FalseBrush { get; set; } = Brushes.Gray;

    // 데이터 → 화면 (true면 TrueBrush, 아니면 FalseBrush)
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? TrueBrush : FalseBrush;

    // 화면 → 데이터 (표시만 하니까 안 씀)
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}