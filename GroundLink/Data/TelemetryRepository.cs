using System.Data;
using GroundLink.Models;
using Microsoft.Data.SqlClient;
using System.Diagnostics;




namespace GroundLink.Data
{
   public  class TelemetryRepository
    {
        private readonly string connectionString;

        public TelemetryRepository(string connectionString)
        {
            this.connectionString = connectionString;
        }

        public Telemetry? GetLatest(string satelliteId) // ? 값이 있거나 null일수 있음
        {
            const string sql = """
            SELECT TOP 1 RecordedAt, Battery, Temperature
            FROM dbo.Telemetry
            WHERE SatelliteId = @satelliteId
            ORDER BY RecordedAt DESC;
            """;

            using var connection = new SqlConnection(connectionString);
            connection.Open();

            using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@satelliteId", SqlDbType.VarChar, 16).Value = satelliteId;


            using var reader = command.ExecuteReader();
            if (!reader.Read())
                return null;

            DateTime recordedAt = reader.GetDateTime(0);   // SELECT 첫 번째 칸
            double battery = reader.GetDouble(1);          // 두 번째 칸
            double temperature = reader.GetDouble(2);      // 세 번째 칸

            return new Telemetry(satelliteId, recordedAt, battery, temperature);


        }

        public List<Telemetry> GetRange(string satelliteId, DateTime from)
        {
            const string sql = """
        SELECT RecordedAt, Battery, Temperature
        FROM dbo.Telemetry
        WHERE SatelliteId = @satelliteId AND RecordedAt >= @from
        ORDER BY RecordedAt;
        """;

            using var connection = new SqlConnection(connectionString);
            connection.Open();

            using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@satelliteId", SqlDbType.VarChar, 16).Value = satelliteId;
            command.Parameters.Add("@from", SqlDbType.DateTime2).Value = from;

            List<Telemetry> rows = new List<Telemetry>();
            using var reader = command.ExecuteReader();
            while (reader.Read())   // 줄이 있는 동안 반복
            {
                rows.Add(new Telemetry(satelliteId, reader.GetDateTime(0), reader.GetDouble(1), reader.GetDouble(2)));
            }
            return rows;
        }

        // 기간 요약 조회 함수 (끝 시각 없음)
        public List<SummaryPoint> GetSummary(string satelliteId, DateTime from, int bucketSeconds)
        {
            return GetSummary(satelliteId, from, DateTime.MaxValue, bucketSeconds);
        }

        // 기간 요약 조회 함수 (배터리)
        public List<SummaryPoint> GetSummary(string satelliteId, DateTime from, DateTime to, int bucketSeconds)
        {
            return GetSummaries(satelliteId, from, to, bucketSeconds).Battery;
        }

        // 기간 요약 조회 함수 (배터리·온도)
        public (List<SummaryPoint> Battery, List<SummaryPoint> Temperature) GetSummaries(
            string satelliteId, DateTime from, DateTime to, int bucketSeconds)
        {
            const string sql = """
            SELECT DATE_BUCKET(SECOND, @bucket, RecordedAt) AS BucketStart,
                   AVG(Battery), MIN(Battery), MAX(Battery),
                   AVG(Temperature), MIN(Temperature), MAX(Temperature)
            FROM dbo.Telemetry
            WHERE SatelliteId = @satelliteId AND RecordedAt >= @from AND RecordedAt < @to
            GROUP BY DATE_BUCKET(SECOND, @bucket, RecordedAt)
            ORDER BY BucketStart;
            """;

            using var connection = new SqlConnection(connectionString);
            connection.Open();

            using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@satelliteId", SqlDbType.VarChar, 16).Value = satelliteId;
            command.Parameters.Add("@from", SqlDbType.DateTime2).Value = from;
            command.Parameters.Add("@to", SqlDbType.DateTime2).Value = to;
            command.Parameters.Add("@bucket", SqlDbType.Int).Value = bucketSeconds;

            List<SummaryPoint> battery = new List<SummaryPoint>();
            List<SummaryPoint> temperature = new List<SummaryPoint>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                DateTime bucketStart = reader.GetDateTime(0);
                battery.Add(new SummaryPoint(bucketStart, reader.GetDouble(1), reader.GetDouble(2), reader.GetDouble(3)));
                temperature.Add(new SummaryPoint(bucketStart, reader.GetDouble(4), reader.GetDouble(5), reader.GetDouble(6)));
            }
            return (battery, temperature);
        }

        // 여러 건 저장 함수 (트랜잭션)
        public void SaveBatch(IReadOnlyList<Telemetry> items)
        {
            const string sql = """
            INSERT INTO dbo.Telemetry (SatelliteId, RecordedAt, Battery, Temperature, Source)
            VALUES (@satelliteId, @recordedAt, @battery, @temperature, 2);
            """;

            using var connection = new SqlConnection(connectionString);
            connection.Open();

            using SqlTransaction transaction = connection.BeginTransaction();
            try
            {
                using var command = new SqlCommand(sql, connection, transaction);
                SqlParameter satelliteId = command.Parameters.Add("@satelliteId", SqlDbType.VarChar, 16);
                SqlParameter recordedAt = command.Parameters.Add("@recordedAt", SqlDbType.DateTime2);
                SqlParameter battery = command.Parameters.Add("@battery", SqlDbType.Float);
                SqlParameter temperature = command.Parameters.Add("@temperature", SqlDbType.Float);

                foreach (Telemetry item in items)
                {
                    satelliteId.Value = item.SatelliteId;
                    recordedAt.Value = item.RecordedAt;
                    battery.Value = item.Battery;
                    temperature.Value = item.Temperature;
                    command.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }




    }


}
