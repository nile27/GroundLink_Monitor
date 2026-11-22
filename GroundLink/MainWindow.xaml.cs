using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace GroundLink;

// 메인 화면
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        SetStatus("연결 안 됨", Theme.Muted);
        SelectPeriod(DayButton);   // 처음 조회 기간 1일
    }

    // 색 글자("#RRGGBB") → Brush 변환 함수
    private static Brush ToBrush(string hex)
    {
        BrushConverter converter = new BrushConverter();        // XAML이 색 글자를 바꿀 때 쓰는 변환기
        object? result = converter.ConvertFromString(hex);       // 여러 타입용 함수라 결과가 object?
        Brush brush = (Brush)result!;                            // Brush로 형 변환, ! = null 아님 표시
        return brush;
    }

    // 상태 배지 갱신 함수 (테두리·점·글자 같은 색)
    private void SetStatus(string text, string colorHex, string detail = "")
    {
        Brush brush = ToBrush(colorHex);
        StatusBorder.BorderBrush = brush;
        StatusDot.Fill = brush;
        StatusText.Foreground = brush;
        StatusText.Text = text;
        StatusDetailText.Text = detail;
    }

    // 현재 값 표시 함수
    private void ShowCurrentValues(double battery, double temperature)
    {
        BatteryText.Text = $"{battery:F1} %";
        TemperatureText.Text = $"{temperature:F1} °C";
    }

    // 기간 버튼 강조 함수 (누른 버튼만 Tag = true → 스타일이 하늘색)
    private void SelectPeriod(Button selected)
    {
        Button[] buttons = new Button[] { RecentButton, DayButton, WeekButton };

        foreach (Button button in buttons)
        {
            bool isSelected = (button == selected);   // 누른 버튼이면 true
            button.Tag = isSelected;
        }
    }

    // 배터리 최신 값 조회 이벤트
    private void LoadLatestButton_Click(object sender, RoutedEventArgs e)
    {

    }

    // 시뮬레이터 연결 이벤트
    private void ConnectButton_Click(object sender, RoutedEventArgs e)
    {

    }

    // 기간 버튼 이벤트 (버튼 3개 공용)
    private void PeriodButton_Click(object sender, RoutedEventArgs e)
    {
        SelectPeriod((Button)sender);
    }
}