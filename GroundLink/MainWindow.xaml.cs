/*
 * GroundLink 관제 메인 화면
 *
 * 화면과 기능 클래스를 연결만 한다. 실제 일은 각 클래스가 담당.
 * 1. 연결 상태: 색 점 + 상태 글자 (연결 안 됨 / 접속 중 / 수신 중 / 끊김), 끊기면 3초마다 재접속
 * 2. 현재 값: DB 최신 값 / 실시간 수신 값 + 경고까지 남은 여유
 * 3. 실시간 차트: 배터리·배터리 온도 최근 5분
 * 4. 과거 기록: 3시간·1일 = 분 단위 평균 파동 / 1주일 = 궤도 1바퀴 단위 최고·최저
 * 5. 수신 데이터 저장: 10건씩 모아 DB에 저장
 *
 * 기능 클래스
 *   Data/TelemetryRepository   DB 조회·저장
 *   Network/TelemetryReceiver  TCP 수신 (별도 스레드)
 *   Rules/WarningRules         경고 판정
 *   Charts/ChartDrawer         차트 그리기
 *   Theme                      화면 색
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

        // 상태 색 (Theme 값을 WPF 붓으로)
        private static readonly Brush TextBrush = BrushOf(Theme.Text);
        private static readonly Brush MutedBrush = BrushOf(Theme.Muted);
        private static readonly Brush AccentBrush = BrushOf(Theme.Accent);
        private static readonly Brush BackgroundBrush = BrushOf(Theme.Background);
        private static readonly Brush OkBrush = BrushOf(Theme.Ok);
        private static readonly Brush CautionBrush = BrushOf(Theme.Caution);
        private static readonly Brush DangerBrush = BrushOf(Theme.Danger);

        private readonly TelemetryRepository repository = new(ConnectionString);

        // 실시간 차트 데이터 (UI 스레드에서만 접근)
        private DataLogger? batteryLogger;
        private DataLogger? temperatureLogger;
        private DateTime? liveStart;   // 첫 수신 시각 = 실시간 차트 왼쪽 끝

        // 저장 대기 리스트: 10건 모이면 DB에 저장 (수신 스레드에서만 사용)
        private readonly List<Telemetry> buffer = new();

        public MainWindow()
        {
            InitializeComponent();
            ChartDrawer.RegisterKoreanFont();
        }

        // ───────── 시작 ─────────

        /// <summary>창 표시 직후 1회: 실시간 차트 준비 + DB 최신 값 + 1일 기록.</summary>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            batteryLogger = ChartDrawer.SetupLive(
                BatteryLivePlot, "배터리 · 실시간", "%", 80, 100,             // 평소 범위 85~100
                WarningRules.BatteryWarning);

            temperatureLogger = ChartDrawer.SetupLive(
                TemperatureLivePlot, "배터리 온도 · 실시간", "°C", 10, 20,     // 평소 범위 12~18
                WarningRules.TemperatureLow, WarningRules.TemperatureHigh);

            ShowLatestFromDb();
            ShowWaveHistory(24, 5, "1일", DayButton);
        }

        // ───────── 버튼 ─────────

        /// <summary>"최신 값 조회": DB 최신 값 다시 표시.</summary>
        private void TestButton_Click(object sender, RoutedEventArgs e) => ShowLatestFromDb();

        /// <summary>기간 버튼: 최근 3시간 (궤도 약 2바퀴), 1분 평균.</summary>
        private void RecentButton_Click(object sender, RoutedEventArgs e) => ShowWaveHistory(3, 1, "3시간", RecentButton);

        /// <summary>기간 버튼: 최근 1일 (궤도 약 15바퀴), 5분 평균.</summary>
        private void DayButton_Click(object sender, RoutedEventArgs e) => ShowWaveHistory(24, 5, "1일", DayButton);

        /// <summary>기간 버튼: 최근 1주일, 궤도 1바퀴 단위.</summary>
        private void WeekButton_Click(object sender, RoutedEventArgs e) => ShowOrbitHistory(168, "1주일", WeekButton);

        /// <summary>
        /// "시뮬레이터 연결": 수신 시작. 수신은 별도 스레드라 UI는 바로 자유.
        /// 끊기면 수신기가 3초마다 알아서 다시 접속하므로 버튼은 다시 켜지 않음.
        /// </summary>
        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            ConnectButton.IsEnabled = false;   // 중복 연결 방지

            var receiver = new TelemetryReceiver(SimulatorHost, SimulatorPort);
            receiver.Connecting += OnConnecting;
            receiver.Received += OnReceived;
            receiver.Disconnected += OnDisconnected;
            receiver.Start();
        }

        // ───────── 수신 이벤트 (수신 스레드에서 호출됨) ─────────

        /// <summary>접속 시도: 첫 시도는 "접속 중", 2번째부터는 몇 번째 재접속인지 표시.</summary>
        private void OnConnecting(int attempt)
        {
            Dispatcher.InvokeAsync(() =>
            {
                ShowConnectionState("접속 중", CautionBrush);
                LiveText.Text = attempt == 1 ? "" : $"재접속 시도 {attempt}번째";
            });
        }

        /// <summary>1건 수신: 저장 대기 리스트에 넣고, UI 스레드로 넘겨 현재 값·실시간 차트 갱신.</summary>
        private void OnReceived(Telemetry data)
        {
            int threadId = Environment.CurrentManagedThreadId;   // 수신 스레드 번호 (확인용)

            buffer.Add(data);
            if (buffer.Count >= 10)
            {
                // DB 오류로 수신까지 끊기지 않게 여기서 처리. 실패하면 비우지 않고 다음 건 때 다시 저장 시도
                try
                {
                    repository.InsertBatch(buffer);
                    buffer.Clear();   // 저장한 뒤 비우기
                }
                catch (Exception ex)
                {
                    string message = $"저장 실패 (대기 {buffer.Count}건): {ex.Message}";
                    Dispatcher.InvokeAsync(() => ResultText.Text = message);
                }
            }

            // 화면 변경은 UI 스레드에서만 (직접 하면 InvalidOperationException)
            Dispatcher.InvokeAsync(() =>
            {
                ShowConnectionState("수신 중", OkBrush);
                LiveText.Text = $"마지막 수신 {data.RecordedAt:HH:mm:ss} · 1초 주기 (수신 스레드 {threadId})";
                ShowCurrentValues(data);

                liveStart ??= data.RecordedAt;   // 첫 수신이면 차트 왼쪽 끝으로

                // 마지막 인자 1.0 = y축 최소 폭. 이보다 좁으면 ±0.1~0.2 잡음이 큰 출렁임처럼 보임
                if (batteryLogger != null)
                    ChartDrawer.AppendLive(BatteryLivePlot, batteryLogger, liveStart.Value, data.RecordedAt, data.Battery, 1.0);
                if (temperatureLogger != null)
                    ChartDrawer.AppendLive(TemperatureLivePlot, temperatureLogger, liveStart.Value, data.RecordedAt, data.Temperature, 1.0);
            });
        }

        /// <summary>끊김·접속 실패: 남은 데이터 저장, 사유와 재접속 예정 표시.</summary>
        private void OnDisconnected(string reason)
        {
            try
            {
                if (buffer.Count > 0)
                {
                    repository.InsertBatch(buffer);
                    buffer.Clear();
                }
            }
            catch (Exception ex)
            {
                reason += $" / 남은 데이터 저장 실패: {ex.Message}";
            }

            Dispatcher.InvokeAsync(() =>
            {
                ShowConnectionState("끊김", DangerBrush);
                LiveText.Text = $"{reason} · {TelemetryReceiver.RetryDelaySeconds}초 후 재접속";   // 저장 실패 시 그 내용 포함
            });
        }

        // ───────── 화면 표시: 상단·현재 값 ─────────

        /// <summary>연결 상태 표시: 점과 글자를 같은 색으로.</summary>
        private void ShowConnectionState(string text, Brush color)
        {
            StatusDot.Fill = color;          // 1. 점 색
            StatusText.Text = text;          // 2. 상태 글자
            StatusText.Foreground = color;   // 3. 글자 색
        }

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
                LiveText.Text = $"DB 최신 값 {data.RecordedAt:yyyy-MM-dd HH:mm:ss}";
            }
            catch (Exception ex)
            {
                ResultText.Text = $"조회 실패: {ex.Message}";
            }
        }

        /// <summary>현재 값 카드: 큰 숫자 + 경고까지 남은 여유. 경고면 빨간색.</summary>
        private void ShowCurrentValues(Telemetry data)
        {
            bool batteryWarn = WarningRules.IsBatteryWarning(data.Battery);
            BatteryText.Text = $"{data.Battery:F1} %";
            BatteryText.Foreground = batteryWarn ? DangerBrush : TextBrush;
            BatteryMarginText.Text = batteryWarn
                ? $"경고: {WarningRules.BatteryWarning} % 미만"
                : $"경고까지 {WarningRules.BatteryMargin(data.Battery):F1} %p";
            BatteryMarginText.Foreground = batteryWarn ? DangerBrush : MutedBrush;

            bool tempWarn = WarningRules.IsTemperatureWarning(data.Temperature);
            TemperatureText.Text = $"{data.Temperature:F1} °C";
            TemperatureText.Foreground = tempWarn ? DangerBrush : TextBrush;
            TemperatureMarginText.Text = tempWarn
                ? $"경고: {WarningRules.TemperatureLow} ~ {WarningRules.TemperatureHigh} °C 벗어남"
                : $"경고까지 {WarningRules.TemperatureMargin(data.Temperature):F1} °C";
            TemperatureMarginText.Foreground = tempWarn ? DangerBrush : MutedBrush;
        }

        // ───────── 화면 표시: 과거 기록 ─────────

        /// <summary>
        /// 3시간·1일: 분 단위 평균선. 충전↗·방전↘ 파동이 그대로 보임.
        /// 1주일은 파동이 약 107개라 화면 폭에서 띠로 뭉개지므로 이 방식을 쓰지 않음.
        /// </summary>
        private void ShowWaveHistory(int hours, int bucketMinutes, string label, Button button)
        {
            SelectPeriodButton(button);
            HistoryDescriptionText.Text =
                $"{bucketMinutes}분 평균 · 궤도 1바퀴(94.5분)마다 파동 1개. " +
                "햇빛에서 충전되어 오르고(↗), 그늘에서 방전되어 내려감(↘). 온도도 같은 방향";

            try
            {
                HistorySummary summary = repository.GetSummary(SatelliteId, hours, bucketMinutes);

                ChartDrawer.DrawHistory(BatteryHistoryPlot, summary.Battery,
                    $"배터리 · 최근 {label}", "%", WarningRules.BatteryWarning);
                ChartDrawer.DrawHistory(TemperatureHistoryPlot, summary.Temperature,
                    $"배터리 온도 · 최근 {label}", "°C", WarningRules.TemperatureLow, WarningRules.TemperatureHigh);

                ShowBatteryRecentSummary(summary.Battery);
                ShowTemperatureRecentSummary(summary.Temperature);
            }
            catch (Exception ex)
            {
                ResultText.Text = $"차트 조회 실패: {ex.Message}";
            }
        }

        /// <summary>1주일: 궤도 1바퀴 = 점 1개. 파동 대신 바퀴별 최고·최저가 유지되는지 봄.</summary>
        private void ShowOrbitHistory(int hours, string label, Button button)
        {
            SelectPeriodButton(button);
            HistoryDescriptionText.Text =
                "궤도 1바퀴(94.5분) = 점 1개 · 위 선 = 바퀴 중 최고(햇빛 끝), 아래 선 = 바퀴 중 최저(그늘 끝). " +
                "선이 평평하면 매 바퀴 같은 폭으로 충전·방전 중, 아래 선이 내려가면 방전이 깊어진 것. 다 끝난 바퀴만 표시";

            try
            {
                HistorySummary summary = repository.GetOrbitSummary(SatelliteId, hours);

                ChartDrawer.DrawOrbitHistory(BatteryHistoryPlot, summary.Battery,
                    $"배터리 · 최근 {label} · 바퀴별", "%", WarningRules.BatteryWarning);
                ChartDrawer.DrawOrbitHistory(TemperatureHistoryPlot, summary.Temperature,
                    $"배터리 온도 · 최근 {label} · 바퀴별", "°C", WarningRules.TemperatureLow, WarningRules.TemperatureHigh);

                ShowBatteryOrbitSummary(summary.Battery);
                ShowTemperatureOrbitSummary(summary.Temperature);
            }
            catch (Exception ex)
            {
                ResultText.Text = $"차트 조회 실패: {ex.Message}";
            }
        }

        /// <summary>선택한 기간 버튼만 강조색. 나머지는 스타일 기본값으로 되돌림.</summary>
        private void SelectPeriodButton(Button selected)
        {
            foreach (Button button in new[] { RecentButton, DayButton, WeekButton })
            {
                if (button == selected)
                {
                    button.Background = AccentBrush;
                    button.Foreground = BackgroundBrush;
                }
                else
                {
                    button.ClearValue(BackgroundProperty);
                    button.ClearValue(ForegroundProperty);
                }
            }
        }

        /// <summary>파동 차트 배터리 결론: 경고 구간 수, 최저값, 경고선까지 여유.</summary>
        private void ShowBatteryRecentSummary(List<SummaryPoint> points)
        {
            if (points.Count == 0)
            {
                ShowSummary(BatteryHistorySummaryText, "배터리: 데이터 없음", MutedBrush);
                return;
            }

            int warnCount = points.Count(p => WarningRules.IsBatteryWarning(p.Min));   // 경고선 아래로 내려간 구간 수
            double lowest = points.Min(p => p.Min);

            if (warnCount == 0)
                ShowSummary(BatteryHistorySummaryText,
                    $"배터리: 경고 없음 · 최저 {lowest:F1} % (경고까지 {WarningRules.BatteryMargin(lowest):F1} %p)",
                    OkBrush);
            else
                ShowSummary(BatteryHistorySummaryText,
                    $"배터리: 경고 구간 {warnCount}개 · 최저 {lowest:F1} %", DangerBrush);
        }

        /// <summary>파동 차트 온도 결론: 경고 구간 수, 최저~최고, 가까운 경고선까지 여유.</summary>
        private void ShowTemperatureRecentSummary(List<SummaryPoint> points)
        {
            if (points.Count == 0)
            {
                ShowSummary(TemperatureHistorySummaryText, "배터리 온도: 데이터 없음", MutedBrush);
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
                    OkBrush);
            else
                ShowSummary(TemperatureHistorySummaryText,
                    $"배터리 온도: 경고 구간 {warnCount}개 · {lowest:F1} ~ {highest:F1} °C", DangerBrush);
        }

        /// <summary>바퀴별 배터리 결론: 바퀴 수, 가장 낮은 최저값, 첫 바퀴→마지막 바퀴 최저값 변화.</summary>
        private void ShowBatteryOrbitSummary(List<SummaryPoint> points)
        {
            if (points.Count == 0)
            {
                ShowSummary(BatteryHistorySummaryText, "배터리: 다 끝난 바퀴 없음 (데이터 부족)", MutedBrush);
                return;
            }

            int warnCount = points.Count(p => WarningRules.IsBatteryWarning(p.Min));   // 경고선 아래로 내려간 바퀴 수
            double lowest = points.Min(p => p.Min);
            double change = points[^1].Min - points[0].Min;   // 음수가 커지면 방전이 깊어지는 중

            if (warnCount == 0)
                ShowSummary(BatteryHistorySummaryText,
                    $"배터리: {points.Count}바퀴 경고 없음 · 최저 {lowest:F1} % (경고까지 {WarningRules.BatteryMargin(lowest):F1} %p) · " +
                    $"최저 변화 {change:+0.0;-0.0;0.0} %p",
                    OkBrush);
            else
                ShowSummary(BatteryHistorySummaryText,
                    $"배터리: {points.Count}바퀴 중 경고 {warnCount}바퀴 · 최저 {lowest:F1} %", DangerBrush);
        }

        /// <summary>바퀴별 온도 결론: 바퀴 수, 최저~최고, 가까운 경고선까지 여유.</summary>
        private void ShowTemperatureOrbitSummary(List<SummaryPoint> points)
        {
            if (points.Count == 0)
            {
                ShowSummary(TemperatureHistorySummaryText, "배터리 온도: 다 끝난 바퀴 없음 (데이터 부족)", MutedBrush);
                return;
            }

            int warnCount = points.Count(p =>
                WarningRules.IsTemperatureWarning(p.Min) || WarningRules.IsTemperatureWarning(p.Max));
            double lowest = points.Min(p => p.Min);
            double highest = points.Max(p => p.Max);
            double margin = Math.Min(WarningRules.TemperatureMargin(lowest), WarningRules.TemperatureMargin(highest));

            if (warnCount == 0)
                ShowSummary(TemperatureHistorySummaryText,
                    $"배터리 온도: {points.Count}바퀴 경고 없음 · {lowest:F1} ~ {highest:F1} °C (경고까지 {margin:F1} °C)",
                    OkBrush);
            else
                ShowSummary(TemperatureHistorySummaryText,
                    $"배터리 온도: {points.Count}바퀴 중 경고 {warnCount}바퀴 · {lowest:F1} ~ {highest:F1} °C", DangerBrush);
        }

        /// <summary>결론 글자와 색 표시.</summary>
        private static void ShowSummary(TextBlock target, string text, Brush color)
        {
            target.Text = text;
            target.Foreground = color;
        }

        /// <summary>"#RRGGBB" → WPF 붓. Freeze: 바뀌지 않는 붓이라 여러 곳에서 같이 써도 안전.</summary>
        private static Brush BrushOf(string hex)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
            brush.Freeze();
            return brush;
        }
    }
}
