using System.Windows;
using Microsoft.Data.SqlClient;

namespace GroundLink
{
    public partial class MainWindow : Window
    {
        // DB 주소 + 로그인 방법
        // @를 붙이면 \ 를 그대로 쓸 수 있음 (안 붙이면 \\ 로 써야 함)
        private const string ConnectionString =
            @"Server=localhost\SQLEXPRESS;Database=MonitorDB;Trusted_Connection=True;TrustServerCertificate=True;";

        public MainWindow()
        {
            InitializeComponent();
            
        }

        // 버튼 누르면 → LoadLatest 부르기만 함
        private void TestButton_Click(object sender, RoutedEventArgs e)
        {
            LoadLatest();
            
        }

        // 버튼 안에 있던 코드를 그대로 옮겨온 함수
        private void LoadLatest()
        {
            
            try
            {
                // DB 연동
                using var conn = new SqlConnection(ConnectionString);
                conn.Open();   // 여기서 실제로 접속

                const string sql = @"SELECT TOP 1 RecordedAt, Battery, Temperature FROM dbo.Telemetry WHERE SatelliteId = 'SAT-001' ORDER BY RecordedAt DESC;";

                // 쿼리 + 보낼 연결을 묶어두기만
                using var cmd = new SqlCommand(sql, conn);
                
                // 보내고 결과 받기
                using var reader = cmd.ExecuteReader();

                if(reader.Read())
                {
                    // 데이터 꺼내기
                    DateTime recordedAt = reader.GetDateTime(0);
                    double battery = reader.GetDouble(1);
                    double temperature = reader.GetDouble(2);

                    // 7. 화면에 표시 
                    ResultText.Text =
                        $"시각: {recordedAt:yyyy-MM-dd HH:mm:ss}\n" +
                        $"배터리: {battery:F1} %\n" +
                        $"온도: {temperature:F1} °C";
                }

                //ResultText.Text = $"연결 성공! 서버 버전: {conn.ServerVersion}";
            }
            catch (Exception ex)
            {
                ResultText.Text = $"연결 실패: {ex.Message}";
            }
        }
    }
}