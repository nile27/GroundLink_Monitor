using System.Windows;
using Microsoft.Data.SqlClient;

namespace GroundLink
{
    public partial class MainWindow : Window
    {
        private const string ConnectionString =
            @"Server=localhost\SQLEXPRESS;Database=MonitorDB;Trusted_Connection=True;TrustServerCertificate=True;";

        // 배터리 경고 기준 (요구사항 정의서 제안값, 아직 확정 아님)
        private const double BatteryWarning = 30;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadLatest();
            LoadHistoryChart(24, 5, "1일");   // 처음엔 1일로 시작
        }

        private void TestButton_Click(object sender, RoutedEventArgs e)
        {
            LoadLatest();
        }

        // ③ 기간 버튼 3개: 같은 함수를 값만 바꿔서 부름
        private void HourButton_Click(object sender, RoutedEventArgs e)
        {
            LoadHistoryChart(1, 1, "1시간");
        }

        private void DayButton_Click(object sender, RoutedEventArgs e)
        {
            LoadHistoryChart(24, 5, "1일");
        }

        private void WeekButton_Click(object sender, RoutedEventArgs e)
        {
            LoadHistoryChart(168, 30, "1주일");
        }

        // ③ 과거 기록 차트
        // hours = 최근 몇 시간, bucketMinutes = 몇 분 단위로 묶을지, label = 제목에 쓸 이름
        private void LoadHistoryChart(int hours, int bucketMinutes, string label)
        {
            // 안쪽 SELECT에서 구간을 나누고, 바깥 SELECT에서 구간별로 묶음
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

            var times = new List<DateTime>();
            var avgs = new List<double>();
            var mins = new List<double>();
            var maxs = new List<double>();

            try
            {
                using var conn = new SqlConnection(ConnectionString);
                conn.Open();

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@bucket", bucketMinutes);   // @bucket 자리에 값
                cmd.Parameters.AddWithValue("@hours", hours);            // @hours 자리에 값

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    times.Add(reader.GetDateTime(0));
                    avgs.Add(reader.GetDouble(1));
                    mins.Add(reader.GetDouble(2));
                    maxs.Add(reader.GetDouble(3));
                }
            }
            catch (Exception ex)
            {
                ResultText.Text = $"차트 조회 실패: {ex.Message}";
                return;
            }

            MyPlot.Plot.Clear();
            MyPlot.Plot.Font.Set("Malgun Gothic");

            // 데이터가 하나도 없으면 그리지 않음 (빈 배열로 그리면 에러가 날 수 있음)
            if (times.Count == 0)
            {
                MyPlot.Plot.Title($"배터리 (최근 {label}): 데이터 없음");
                MyPlot.Refresh();
                return;
            }

            double[] xs = times.Select(t => t.ToOADate()).ToArray();

            var band = MyPlot.Plot.Add.FillY(xs, mins.ToArray(), maxs.ToArray());
            band.LegendText = "최소~최대";

            var avgLine = MyPlot.Plot.Add.Scatter(xs, avgs.ToArray());
            avgLine.LegendText = "평균";
            avgLine.MarkerSize = 0;

            var warnLine = MyPlot.Plot.Add.HorizontalLine(BatteryWarning);
            warnLine.LegendText = $"경고 {BatteryWarning} %";
            warnLine.LinePattern = ScottPlot.LinePattern.Dashed;
            warnLine.Color = ScottPlot.Colors.Orange;

            MyPlot.Plot.Title($"배터리 (최근 {label}, {bucketMinutes}분 요약)");
            MyPlot.Plot.YLabel("%");
            MyPlot.Plot.ShowLegend();
            MyPlot.Plot.Axes.DateTimeTicksBottom();
            MyPlot.Plot.Axes.AutoScale();
            MyPlot.Refresh();

            int belowCount = mins.Count(m => m < BatteryWarning);
            ResultText.Text =
                $"[{label}] {times.Count}개 구간 · 경고선 아래 {belowCount}개 · 최저 {mins.Min():F1} %";
        }

        // ① 현재 값 (2단계 그대로)
        private void LoadLatest()
        {
            try
            {
                using var conn = new SqlConnection(ConnectionString);
                conn.Open();

                const string sql = @"SELECT TOP 1 RecordedAt, Battery, Temperature FROM dbo.Telemetry WHERE SatelliteId = 'SAT-001' ORDER BY RecordedAt DESC;";

                using var cmd = new SqlCommand(sql, conn);
                using var reader = cmd.ExecuteReader();

                if (reader.Read())
                {
                    DateTime recordedAt = reader.GetDateTime(0);
                    double battery = reader.GetDouble(1);
                    double temperature = reader.GetDouble(2);

                    ResultText.Text =
                        $"시각: {recordedAt:yyyy-MM-dd HH:mm:ss}\n" +
                        $"배터리: {battery:F1} %\n" +
                        $"온도: {temperature:F1} °C";
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
}