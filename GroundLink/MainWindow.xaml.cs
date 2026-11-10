/*
 * GroundLink 관제 메인 화면
 *
 * 1. ① 현재 값: DB 최신 1줄 조회 / 실시간 수신 값 + 경고 표시
 * 2. ③ 과거 기록: 기간별 요약 조회 → 평균선 + 최소~최대 띠 + 경고선
 * 3. 실시간 수신: 별도 스레드에서 TCP 수신 → JSON 변환 → Dispatcher로 화면 갱신
 */

using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Data.SqlClient;

namespace GroundLink
{
    public partial class MainWindow : Window
    {
        private const string ConnectionString =
            @"Server=localhost\SQLEXPRESS;Database=MonitorDB;Trusted_Connection=True;TrustServerCertificate=True;";

        // 경고 기준 (DOCS 데이터 요구사항)
        private const double BatteryWarning = 60;        // % 미만 (LEO DoD 상한 40% 초과)
        private const double TemperatureLow = 0;         // °C 미만 (리튬이온 충전 하한)
        private const double TemperatureHigh = 40;       // °C 초과 (LEO 배터리 온도 범위 상한)

        // 시뮬레이터와 같은 필드 이름 규칙 (camelCase)
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public MainWindow()
        {
            InitializeComponent();
            ScottPlot.Fonts.AddFontFile("KoreanFont", @"C:\Windows\Fonts\malgun.ttf");
        }

        /// <summary>창 표시 직후 1회: 현재 값 + 1일 차트 로드.</summary>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadLatest();
            LoadHistoryChart(24, 5, "1일");
        }

        /// <summary>"최신 값 조회" 버튼: 현재 값 다시 조회.</summary>
        private void TestButton_Click(object sender, RoutedEventArgs e)
        {
            LoadLatest();
        }

        /// <summary>
        /// "시뮬레이터 연결" 버튼: 수신 스레드를 시작하고 바로 반환.
        /// UI 스레드는 기다리지 않으므로 화면이 멈추지 않음.
        /// </summary>
        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            ConnectButton.IsEnabled = false;   // 중복 연결 방지
            LiveText.Text = $"실시간: 접속 중... (UI 스레드 {Environment.CurrentManagedThreadId})";

            var receiveThread = new Thread(ReceiveLoop)
            {
                IsBackground = true   // 창 닫으면 같이 종료
            };
            receiveThread.Start();    // 시작만 하고 바로 다음 줄로
        }

        /// <summary>
        /// 수신 스레드에서 실행: TCP 접속 → 한 줄씩 수신 → JSON 변환 → 화면에 전달.
        /// 화면 요소는 직접 건드리지 않고 Dispatcher를 거침.
        /// </summary>
        private void ReceiveLoop()
        {
            int threadId = Environment.CurrentManagedThreadId;

            try
            {
                using var client = new TcpClient("127.0.0.1", 5000);
                using var reader = new StreamReader(client.GetStream());

                while (true)
                {
                    string? line = reader.ReadLine();   // 블로킹: 수신 스레드만 멈춤, UI는 정상
                    if (line == null) break;            // null = 연결 끊김

                    // JSON 변환은 수신 스레드에서 (UI 스레드 일은 최소로)
                    Telemetry? data;
                    try
                    {
                        data = JsonSerializer.Deserialize<Telemetry>(line, JsonOptions);
                    }
                    catch (JsonException)
                    {
                        continue;   // 깨진 줄은 버리고 연결은 유지
                    }
                    if (data == null) continue;

                    // 화면 변경은 UI 스레드에 부탁 (직접 하면 InvalidOperationException)
                    Dispatcher.InvokeAsync(() =>
                    {
                        LiveText.Text = $"실시간: 수신 중 (수신 스레드 {threadId}) · 마지막 {data.RecordedAt:HH:mm:ss}";
                        ShowCurrentValues(data);
                    });
                }

                Dispatcher.InvokeAsync(() => LiveText.Text = "실시간: 연결 끊김");
            }
            catch (Exception ex)
            {
                // 시뮬레이터 꺼짐, 접속 거부 등
                Dispatcher.InvokeAsync(() => LiveText.Text = $"실시간: 오류 - {ex.Message}");
            }
            finally
            {
                // 끝나면 다시 연결할 수 있게
                Dispatcher.InvokeAsync(() => ConnectButton.IsEnabled = true);
            }
        }

        /// <summary>
        /// ① 현재 값 표시 + 경고 판정 (F-02). UI 스레드에서만 호출.
        /// </summary>
        private void ShowCurrentValues(Telemetry data)
        {
            bool batteryWarn = data.Battery < BatteryWarning;
            bool tempWarn = data.Temperature < TemperatureLow || data.Temperature > TemperatureHigh;

            BatteryText.Text = $"배터리: {data.Battery:F1} %" + (batteryWarn ? "  경고" : "");
            BatteryText.Foreground = batteryWarn ? Brushes.Red : Brushes.Black;

            TemperatureText.Text = $"배터리 온도: {data.Temperature:F1} °C" + (tempWarn ? "  경고" : "");
            TemperatureText.Foreground = tempWarn ? Brushes.Red : Brushes.Black;
        }

        /// <summary>기간 버튼: 최근 1시간, 1분 단위.</summary>
        private void HourButton_Click(object sender, RoutedEventArgs e)
        {
            LoadHistoryChart(1, 1, "1시간");
        }

        /// <summary>기간 버튼: 최근 1일, 5분 단위.</summary>
        private void DayButton_Click(object sender, RoutedEventArgs e)
        {
            LoadHistoryChart(24, 5, "1일");
        }

        /// <summary>기간 버튼: 최근 1주일, 30분 단위.</summary>
        private void WeekButton_Click(object sender, RoutedEventArgs e)
        {
            LoadHistoryChart(168, 30, "1주일");
        }

        /// <summary>
        /// ③ 과거 기록 차트: 최근 hours 시간을 bucketMinutes 분 단위로 요약해
        /// 평균선 + 최소~최대 띠 + 경고선으로 그림.
        /// </summary>
        /// <param name="hours">최근 몇 시간</param>
        /// <param name="bucketMinutes">요약 단위 (분)</param>
        /// <param name="label">제목에 표시할 기간 이름</param>
        private void LoadHistoryChart(int hours, int bucketMinutes, string label)
        {
            // 안쪽 SELECT: 구간 나누기 / 바깥 SELECT: 구간별 평균·최소·최대
            const string sql = @"
                SELECT Bucket,
                       AVG(Battery) AS AvgBattery,
                       MIN(Battery) AS MinBattery,
                       MAX(Battery) AS MaxBattery
                FROM (
                    SELECT DATE_BUCKET(MINUTE, @bucket, RecordedAt) AS Bucket,
                           Battery
                    FROM dbo.Telemetry
                    WHERE SatelliteId = 'SAT-001'
                      AND RecordedAt >= DATEADD(HOUR, -@hours, SYSDATETIME())
                ) AS t
                GROUP BY Bucket
                ORDER BY Bucket;";

            // 결과 줄 수를 모르므로 List에 담고 그릴 때 배열로 변환
            var times = new List<DateTime>();
            var avgs = new List<double>();
            var mins = new List<double>();
            var maxs = new List<double>();

            try
            {
                using var conn = new SqlConnection(ConnectionString);
                conn.Open();

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@bucket", bucketMinutes);   // 값은 문자열 연결 대신 파라미터로
                cmd.Parameters.AddWithValue("@hours", hours);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    times.Add(reader.GetDateTime(0));   // 0 = Bucket
                    avgs.Add(reader.GetDouble(1));      // 1 = AvgBattery
                    mins.Add(reader.GetDouble(2));      // 2 = MinBattery
                    maxs.Add(reader.GetDouble(3));      // 3 = MaxBattery
                }
            }
            catch (Exception ex)
            {
                ResultText.Text = $"차트 조회 실패: {ex.Message}";
                return;   // 실패 시 그리기 생략
            }

            MyPlot.Plot.Clear();                       // 이전 차트 지우기

            // 빈 배열로 그리면 에러 가능
            if (times.Count == 0)
            {
                MyPlot.Plot.Title($"배터리 (최근 {label}): 데이터 없음");
                MyPlot.Plot.Font.Set("KoreanFont");
                MyPlot.Refresh();
                return;
            }

            // DateTime → 숫자. 띠·평균선이 같은 x를 공유
            double[] xs = times.Select(t => t.ToOADate()).ToArray();

            // 1. 띠 (먼저 그려야 선이 위에 보임)
            var band = MyPlot.Plot.Add.FillY(xs, mins.ToArray(), maxs.ToArray());
            band.LegendText = "최소~최대";

            // 2. 평균선
            var avgLine = MyPlot.Plot.Add.Scatter(xs, avgs.ToArray());
            avgLine.LegendText = "평균";
            avgLine.MarkerSize = 0;   // 점 없이 선만

            // 3. 경고선
            var warnLine = MyPlot.Plot.Add.HorizontalLine(BatteryWarning);
            warnLine.LegendText = $"경고 {BatteryWarning} %";
            warnLine.LinePattern = ScottPlot.LinePattern.Dashed;
            warnLine.Color = ScottPlot.Colors.Orange;

            MyPlot.Plot.Title($"배터리 (최근 {label}, {bucketMinutes}분 요약)");
            MyPlot.Plot.YLabel("%");
            MyPlot.Plot.ShowLegend();
            MyPlot.Plot.Axes.DateTimeTicksBottom();   // x축 날짜 표시
            MyPlot.Plot.Axes.AutoScale();
            MyPlot.Plot.Font.Set("KoreanFont");
            MyPlot.Refresh();                          // 화면 반영 (없으면 안 보임)

            // U-09 요약 칸 재료
            int belowCount = mins.Count(m => m < BatteryWarning);
            ResultText.Text =
                $"[{label}] {times.Count}개 구간 · 경고선 아래 {belowCount}개 · 최저 {mins.Min():F1} %";
        }

        /// <summary>① 현재 값: DB에서 최신 1줄 조회해 표시.</summary>
        private void LoadLatest()
        {
            try
            {
                using var conn = new SqlConnection(ConnectionString);
                conn.Open();

                const string sql = @"SELECT TOP 1 RecordedAt, Battery, Temperature FROM dbo.Telemetry WHERE SatelliteId = 'SAT-001' ORDER BY RecordedAt DESC;";

                using var cmd = new SqlCommand(sql, conn);       // 위에서 연 conn으로 보냄 (재접속 아님)
                using var reader = cmd.ExecuteReader();          // 실행 + 결과 받기

                if (reader.Read())                               // 첫 줄로 이동, 없으면 false
                {
                    // DB 값도 실시간 값과 같은 방식으로 표시
                    var data = new Telemetry(
                        "SAT-001",
                        reader.GetDateTime(0),                   // 0 = RecordedAt
                        reader.GetDouble(1),                     // 1 = Battery
                        reader.GetDouble(2));                    // 2 = Temperature

                    ShowCurrentValues(data);
                    LiveText.Text = $"DB 최신 값: {data.RecordedAt:yyyy-MM-dd HH:mm:ss} (실시간 연결 전)";
                }
                else
                {
                    ResultText.Text = "데이터 없음";
                }
            }
            catch (Exception ex)
            {
                ResultText.Text = $"조회 실패: {ex.Message}";
            }
        }
    }

    /*
     * 텔레메트리 1건 (위성 → 지상). 시뮬레이터의 Telemetry와 같은 형식.
     *
     * SatelliteId  : 위성 ID (예: SAT-001)
     * RecordedAt   : 측정 시각 (로컬 시간)
     * Battery      : 배터리 잔량 (%, 0~100)
     * Temperature  : 배터리 온도 (°C)
     *
     * 두 프로젝트에 같은 정의가 따로 있음 → 7단계에서 공유 방식 정리
     */
    public record Telemetry(string SatelliteId, DateTime RecordedAt, double Battery, double Temperature);
}