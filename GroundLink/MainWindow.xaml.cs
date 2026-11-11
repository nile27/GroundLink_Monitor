/*
 * GroundLink 관제 메인 화면
 *
 * 화면과 기능 클래스를 연결만 한다. 실제 일은 각 클래스가 담당.
 * 1. 현재 값: DB 최신 값 / 실시간 수신 값 + 경고 표시
 * 2. 실시간 차트: 배터리·배터리 온도 최근 5분
 * 3. 과거 기록: 기간별 요약 차트 (1시간 / 1일 / 1주일)
 *
 * 기능 클래스
 *   Data/TelemetryRepository   DB 조회
 *   Network/TelemetryReceiver  TCP 수신 (별도 스레드)
 *   Rules/WarningRules         경고 판정
 *   Charts/ChartDrawer         차트 그리기
 */

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GroundLink.Charts;
using GroundLink.Data;
using GroundLink.Models;
using GroundLink.Network;
using GroundLink.Rules;
using ScottPlot.Plottables;

namespace GroundLink
{
    public partial class MainWindow : Window
    {
        private const string ConnectionString =
            @"Server=localhost\SQLEXPRESS;Database=MonitorDB;Trusted_Connection=True;TrustServerCertificate=True;";

        private const string SatelliteId = "SAT-001";
        private const string SimulatorHost = "127.0.0.1";
        private const int SimulatorPort = 5000;

        private readonly TelemetryRepository repository = new(ConnectionString);

        // 실시간 차트 데이터 (UI 스레드에서만 접근)
        private DataLogger? batteryLogger;
        private DataLogger? temperatureLogger;
        private DateTime? liveStart;   // 첫 수신 시각 = 실시간 차트 x축 0초

        // 저장 대기 리스트: 10건 모이면 DB에 저장 (수신 스레드에서만 사용)
        private readonly List<Telemetry> buffer = new();

        public MainWindow()
        {
            InitializeComponent();
            ChartDrawer.RegisterKoreanFont();
        }

        // ───────── 시작 ─────────

        /// <summary>창 표시 직후 1회: 실시간 차트 준비 + DB 최신 값 + 1일 차트.</summary>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            batteryLogger = ChartDrawer.SetupLive(
                BatteryLivePlot, "배터리 (최근 5분)", "%", 80, 100,          // 평소 범위 85~100
                WarningRules.BatteryWarning);

            temperatureLogger = ChartDrawer.SetupLive(
                TemperatureLivePlot, "배터리 온도 (최근 5분)", "°C", 10, 20,  // 평소 범위 12~18
                WarningRules.TemperatureLow, WarningRules.TemperatureHigh);

            ShowLatestFromDb();
            ShowHistory(24, 5, "1일");
        }

        // ───────── 버튼 ─────────

        /// <summary>"최신 값 조회": DB 최신 값 다시 표시.</summary>
        private void TestButton_Click(object sender, RoutedEventArgs e) => ShowLatestFromDb();

        /// <summary>기간 버튼: 최근 1시간, 1분 단위.</summary>
        private void HourButton_Click(object sender, RoutedEventArgs e) => ShowHistory(1, 1, "1시간");

        /// <summary>기간 버튼: 최근 1일, 5분 단위.</summary>
        private void DayButton_Click(object sender, RoutedEventArgs e) => ShowHistory(24, 5, "1일");

        /// <summary>기간 버튼: 최근 1주일, 30분 단위.</summary>
        private void WeekButton_Click(object sender, RoutedEventArgs e) => ShowHistory(168, 30, "1주일");

        /// <summary>"시뮬레이터 연결": 수신 시작. 수신은 별도 스레드라 UI는 바로 자유.</summary>
        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            ConnectButton.IsEnabled = false;   // 중복 연결 방지
            LiveText.Text = "실시간: 접속 중...";

            var receiver = new TelemetryReceiver(SimulatorHost, SimulatorPort);
            receiver.Received += OnReceived;
            receiver.Stopped += OnStopped;
            receiver.Start();
        }

        // ───────── 수신 이벤트 (수신 스레드에서 호출됨) ─────────

        /// <summary>1건 수신: UI 스레드로 넘겨 현재 값·실시간 차트 갱신.</summary>
        private void OnReceived(Telemetry data)
        {
            int threadId = Environment.CurrentManagedThreadId;   // 수신 스레드 번호 (확인용)

            buffer.Add(data);
            if (buffer.Count >= 10)
            {
                repository.InsertBatch(buffer);
                buffer.Clear();   // 저장한 뒤 비우기
            }

            // 화면 변경은 UI 스레드에서만 (직접 하면 InvalidOperationException)
            Dispatcher.InvokeAsync(() =>
            {
                LiveText.Text = $"실시간: 수신 중 (수신 스레드 {threadId}) · 마지막 {data.RecordedAt:HH:mm:ss}";
                ShowCurrentValues(data);

                liveStart ??= data.RecordedAt;   // 첫 수신이면 기준 시각으로
                double seconds = (data.RecordedAt - liveStart.Value).TotalSeconds;

                if (batteryLogger != null)
                    ChartDrawer.AppendLive(BatteryLivePlot, batteryLogger, seconds, data.Battery);
                if (temperatureLogger != null)
                    ChartDrawer.AppendLive(TemperatureLivePlot, temperatureLogger, seconds, data.Temperature);
            });
        }

        /// <summary>수신 종료: 사유 표시, 다시 연결 가능하게.</summary>
        private void OnStopped(string reason)
        {

            try 
            { 
                if(buffer.Count > 0)
                {
                    repository.InsertBatch(buffer);
                    buffer.Clear();
                }
            }
            catch (Exception ex)
            {
                reason += $"남은 데이터 저장 실패: {ex.Message}";
            }

            Dispatcher.InvokeAsync(() =>
            {
                LiveText.Text = $"실시간: {reason}";
                ConnectButton.IsEnabled = true;
            });
        }

        // ───────── 화면 표시 ─────────

        /// <summary>DB 최신 값을 현재 값 칸에 표시.</summary>
        private void ShowLatestFromDb()
        {
            try
            {
                Telemetry? data = repository.GetLatest(SatelliteId);
                if (data == null)
                {
                    ResultText.Text = "데이터 없음";
                    return;
                }

                ShowCurrentValues(data);
                LiveText.Text = $"DB 최신 값: {data.RecordedAt:yyyy-MM-dd HH:mm:ss} (실시간 연결 전)";
            }
            catch (Exception ex)
            {
                ResultText.Text = $"조회 실패: {ex.Message}";
            }
        }

        /// <summary>과거 기록: 배터리·온도 각각 결론 한 줄 + 근거 차트.</summary>
        private void ShowHistory(int hours, int bucketMinutes, string label)
        {
            try
            {
                HistorySummary summary = repository.GetSummary(SatelliteId, hours, bucketMinutes);
                string period = $"최근 {label}, {bucketMinutes}분 요약";

                ChartDrawer.DrawHistory(BatteryHistoryPlot, summary.Battery,
                    $"배터리 ({period})", "%", WarningRules.BatteryWarning);
                ChartDrawer.DrawHistory(TemperatureHistoryPlot, summary.Temperature,
                    $"배터리 온도 ({period})", "°C", WarningRules.TemperatureLow, WarningRules.TemperatureHigh);

                ShowBatteryHistorySummary(summary.Battery);
                ShowTemperatureHistorySummary(summary.Temperature);
            }
            catch (Exception ex)
            {
                ResultText.Text = $"차트 조회 실패: {ex.Message}";
            }
        }

        /// <summary>배터리 과거 기록 결론: 경고 구간 수, 최저값, 경고선까지 여유.</summary>
        private void ShowBatteryHistorySummary(List<SummaryPoint> points)
        {
            if (points.Count == 0)
            {
                ShowSummary(BatteryHistorySummaryText, "배터리: 데이터 없음", Brushes.Gray);
                return;
            }

            int warnCount = points.Count(p => WarningRules.IsBatteryWarning(p.Min));   // 경고선 아래로 내려간 구간 수
            double lowest = points.Min(p => p.Min);

            if (warnCount == 0)
                ShowSummary(BatteryHistorySummaryText,
                    $"배터리: 경고 없음 · 최저 {lowest:F1} % (경고까지 {WarningRules.BatteryMargin(lowest):F1} %p)",
                    Brushes.DarkGreen);
            else
                ShowSummary(BatteryHistorySummaryText,
                    $"배터리: 경고 구간 {warnCount}개 · 최저 {lowest:F1} %", Brushes.Red);
        }

        /// <summary>온도 과거 기록 결론: 경고 구간 수, 최저~최고, 가까운 경고선까지 여유.</summary>
        private void ShowTemperatureHistorySummary(List<SummaryPoint> points)
        {
            if (points.Count == 0)
            {
                ShowSummary(TemperatureHistorySummaryText, "배터리 온도: 데이터 없음", Brushes.Gray);
                return;
            }

            // 구간 안에서 한 번이라도 범위를 벗어났으면 경고 구간
            int warnCount = points.Count(p =>
                WarningRules.IsTemperatureWarning(p.Min) || WarningRules.IsTemperatureWarning(p.Max));
            double lowest = points.Min(p => p.Min);
            double highest = points.Max(p => p.Max);
            double margin = Math.Min(WarningRules.TemperatureMargin(lowest), WarningRules.TemperatureMargin(highest));

            if (warnCount == 0)
                ShowSummary(TemperatureHistorySummaryText,
                    $"배터리 온도: 경고 없음 · {lowest:F1} ~ {highest:F1} °C (경고까지 {margin:F1} °C)",
                    Brushes.DarkGreen);
            else
                ShowSummary(TemperatureHistorySummaryText,
                    $"배터리 온도: 경고 구간 {warnCount}개 · {lowest:F1} ~ {highest:F1} °C", Brushes.Red);
        }

        /// <summary>결론 글자와 색 표시.</summary>
        private static void ShowSummary(TextBlock target, string text, Brush color)
        {
            target.Text = text;
            target.Foreground = color;
        }

        /// <summary>현재 값 칸: 숫자 + 경고 여부 또는 경고까지 남은 여유. 경고면 빨간색.</summary>
        private void ShowCurrentValues(Telemetry data)
        {
            bool batteryWarn = WarningRules.IsBatteryWarning(data.Battery);
            bool tempWarn = WarningRules.IsTemperatureWarning(data.Temperature);

            BatteryText.Text = $"배터리: {data.Battery:F1} %" + (batteryWarn
                ? "  경고"
                : $"  (경고까지 {WarningRules.BatteryMargin(data.Battery):F1} %p)");
            BatteryText.Foreground = batteryWarn ? Brushes.Red : Brushes.Black;

            TemperatureText.Text = $"배터리 온도: {data.Temperature:F1} °C" + (tempWarn
                ? "  경고"
                : $"  (경고까지 {WarningRules.TemperatureMargin(data.Temperature):F1} °C)");
            TemperatureText.Foreground = tempWarn ? Brushes.Red : Brushes.Black;
        }
    }
}
