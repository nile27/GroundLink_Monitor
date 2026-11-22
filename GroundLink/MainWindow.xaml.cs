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

using System.Diagnostics;
using GroundLink.Data;
using GroundLink.Models;
using Microsoft.Data.SqlClient;


namespace GroundLink;

// 메인 화면
public partial class MainWindow : Window
{

    // DB 접속 정보 (나중에 설정 파일로 옮김) 추후 수정 및 이동
    private const string ConnectionString = @"Server=localhost\SQLEXPRESS;Database=MonitorDB;Trusted_Connection=True;TrustServerCertificate=True;";
    private const string SatelliteId = "SAT-001";

    private readonly TelemetryRepository repository = new TelemetryRepository(ConnectionString);
    public MainWindow()
    {
        InitializeComponent();

        SetStatus("연결 안 됨", Theme.Muted);
        SelectPeriod(DayButton);   // 처음 조회 기간 1일
        DrawSampleChart();

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

    // 고정 데이터 선 차트 함수 (1시간, 1분 간격 60점 + 경고선)
    private void DrawSampleChart()
    {
        int count = 60;
        double[] xs = new double[count];
        double[] ys = new double[count];

        DateTime start = DateTime.Now.AddHours(-1);
        for (int i = 0; i < count; i++)
        {
            xs[i] = start.AddMinutes(i).ToOADate();        // 시각 → 숫자 (차트 x축은 숫자)
            ys[i] = 90 + 8 * Math.Sin(i / 10.0);           // 82~98 % 사이 파도 (가짜 값)
        }

        BatteryLivePlot.Plot.Add.Scatter(xs, ys);
        BatteryLivePlot.Plot.Axes.DateTimeTicksBottom();   // x축 눈금을 날짜·시각으로

        var warningLine = BatteryLivePlot.Plot.Add.HorizontalLine(60);   // 경고 기준 60 %
        warningLine.LinePattern = ScottPlot.LinePattern.Dashed;

        BatteryLivePlot.Plot.Axes.SetLimitsY(50, 100);     // y축 범위 고정 (경고선이 보이게)
        BatteryLivePlot.Refresh();
    }


    // 배터리 최신 값 조회 이벤트
    private void LoadLatestButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";

        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            Telemetry? latest = repository.GetLatest(SatelliteId);
            stopwatch.Stop();
            Debug.WriteLine($"GetLatest {stopwatch.Elapsed.TotalMilliseconds:F1} ms");   // 측정 기록 (출력 창)

            if (latest == null)
            {
                ErrorText.Text = "데이터 없음";
                return;
            }

            ShowCurrentValues(latest.Battery, latest.Temperature);
            BatteryMarginText.Text = $"{latest.RecordedAt:HH:mm:ss} 기준";
        }
        catch (SqlException ex)
        {
            ErrorText.Text = $"DB 조회 실패: {ex.Message}";
        }
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