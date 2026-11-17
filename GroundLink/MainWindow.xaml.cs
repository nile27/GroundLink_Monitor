/*
 * GroundLink 관제 메인 화면 (View)
 *
 * 화면 값·버튼·수신·저장은 MainViewModel이 담당하고, XAML이 바인딩으로 보여줌.
 * 여기에는 ScottPlot 차트와 화면 타이머만 남음: 차트는 바인딩이 안 돼서 ViewModel이 알려주면 직접 그림.
 * 1. 시작: 설정 읽기, ViewModel 만들기·연결, 실시간 차트 준비, 첫 화면 데이터 요청
 * 2. 실시간 차트: 수신 묶음 알림 → 점 추가 후 1번 그림
 * 3. 과거 기록 차트: 조회 완료 알림 → 다시 그리기
 * 4. 타이머: 0.1초마다 수신 묶음 반영, 1초마다 성능 표시
 *
 * 기능 클래스
 *   ViewModels/MainViewModel   화면에 보일 값 (바인딩), 버튼 명령, 수신·저장 처리
 *   Data/TelemetryRepository   DB 조회·저장
 *   Network/TelemetryReceiver  TCP 수신 (별도 스레드, ViewModel이 사용)
 *   Rules/WarningRules         경고 판정
 *   Charts/ChartDrawer         차트 그리기
 *   Performance/PerfMonitor    성능 측정값 모으기
 *   AppSettings                실행 설정 (appsettings.json)
 *   Theme                      화면 색
 */

using System.Windows;
using System.Windows.Threading;
using GroundLink.Charts;
using GroundLink.Data;
using GroundLink.Models;
using GroundLink.Rules;
using GroundLink.ViewModels;
using ScottPlot.Plottables;

namespace GroundLink
{
    public partial class MainWindow : Window
    {
        // 화면에 보일 값. XAML의 {Binding ...}이 이 객체를 봄
        private readonly MainViewModel viewModel;

        // 실시간 차트 데이터 (UI 스레드에서만 접근)
        private DataLogger? batteryLogger;
        private DataLogger? temperatureLogger;
        private DateTime? liveStart;   // 첫 수신 시각 = 실시간 차트 왼쪽 끝

        // DispatcherTimer: UI 스레드에서 실행되는 타이머 → 안에서 화면·ViewModel을 바로 다뤄도 됨
        private readonly DispatcherTimer flushTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
        private readonly DispatcherTimer perfTimer = new() { Interval = TimeSpan.FromSeconds(1) };

        public MainWindow()
        {
            InitializeComponent();

            AppSettings settings = LoadSettingsOrExit();

            // 마지막 인자: 수신 스레드에서 온 값 변경을 UI 스레드로 넘기는 방법
            viewModel = new MainViewModel(
                new TelemetryRepository(settings.ConnectionString), settings.SatelliteId,
                settings.SimulatorHost, settings.SimulatorPort,
                action => Dispatcher.InvokeAsync(action));
            viewModel.TelemetryReceived += OnTelemetryReceived;
            viewModel.HistoryLoaded += OnHistoryLoaded;
            DataContext = viewModel;   // 바인딩 대상 연결 (창 안 모든 요소가 이 객체를 기준으로 {Binding} 해석)

            ChartDrawer.RegisterKoreanFont();

            flushTimer.Tick += (_, _) => viewModel.FlushPending();
            perfTimer.Tick += (_, _) => viewModel.PublishPerf();
        }

        // ───────── 1. 시작 ─────────

        /// <summary>창 표시 직후 1회: 실시간 차트 준비 + DB 최신 값 + 1일 기록 + 타이머 시작.</summary>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            batteryLogger = ChartDrawer.SetupLive(
                BatteryLivePlot, "배터리 · 실시간", "%", 80, 100,             // 평소 범위 85~100
                WarningRules.BatteryWarning);

            temperatureLogger = ChartDrawer.SetupLive(
                TemperatureLivePlot, "배터리 온도 · 실시간", "°C", 10, 20,     // 평소 범위 12~18
                WarningRules.TemperatureLow, WarningRules.TemperatureHigh);

            // 실제로 그린 시간 측정: Refresh()는 "다시 그려 달라" 요청만 해서 Stopwatch로는 못 잼
            BatteryLivePlot.Plot.RenderManager.RenderFinished += (_, render) => viewModel.ReportRender(render.Elapsed);
            TemperatureLivePlot.Plot.RenderManager.RenderFinished += (_, render) => viewModel.ReportRender(render.Elapsed);

            // 버튼과 같은 명령을 코드에서 실행
            viewModel.LoadLatestCommand.Execute(null);
            viewModel.ShowHistoryCommand.Execute(MainViewModel.PeriodDay);

            flushTimer.Start();
            perfTimer.Start();
        }

        /// <summary>설정 파일을 읽음. 실패하면 이유를 보여주고 종료 (설정 없이는 DB·시뮬레이터에 연결할 수 없음).</summary>
        private static AppSettings LoadSettingsOrExit()
        {
            try
            {
                return AppSettings.Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "설정 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
                throw;   // 여기까지 오지 않음 (컴파일러용)
            }
        }

        // ───────── 2. 실시간 차트 ─────────

        /// <summary>수신 묶음 (UI 스레드): 실시간 차트에 점을 모두 추가하고 1번 그림.</summary>
        private void OnTelemetryReceived(IReadOnlyList<Telemetry> items)
        {
            liveStart ??= items[0].RecordedAt;   // 첫 수신이면 차트 왼쪽 끝으로

            // 마지막 인자 1.0 = y축 최소 폭. 이보다 좁으면 ±0.1~0.2 잡음이 큰 출렁임처럼 보임
            if (batteryLogger != null)
                ChartDrawer.AppendLive(BatteryLivePlot, batteryLogger, liveStart.Value, items, t => t.Battery, 1.0);
            if (temperatureLogger != null)
                ChartDrawer.AppendLive(TemperatureLivePlot, temperatureLogger, liveStart.Value, items, t => t.Temperature, 1.0);
        }

        // ───────── 3. 과거 기록 차트 ─────────

        /// <summary>과거 기록 조회 완료: 기간 종류에 맞는 차트로 다시 그림.</summary>
        private void OnHistoryLoaded(HistoryChartData chart)
        {
            if (chart.ByOrbit)
            {
                ChartDrawer.DrawOrbitHistory(BatteryHistoryPlot, chart.Summary.Battery,
                    $"배터리 · 최근 {chart.Label} · 바퀴별", "%", WarningRules.BatteryWarning);
                ChartDrawer.DrawOrbitHistory(TemperatureHistoryPlot, chart.Summary.Temperature,
                    $"배터리 온도 · 최근 {chart.Label} · 바퀴별", "°C", WarningRules.TemperatureLow, WarningRules.TemperatureHigh);
            }
            else
            {
                ChartDrawer.DrawHistory(BatteryHistoryPlot, chart.Summary.Battery,
                    $"배터리 · 최근 {chart.Label}", "%", WarningRules.BatteryWarning);
                ChartDrawer.DrawHistory(TemperatureHistoryPlot, chart.Summary.Temperature,
                    $"배터리 온도 · 최근 {chart.Label}", "°C", WarningRules.TemperatureLow, WarningRules.TemperatureHigh);
            }
        }
    }
}
