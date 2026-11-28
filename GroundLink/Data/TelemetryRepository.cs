using System.Data;
using GroundLink.Models;
using Microsoft.Data.SqlClient;





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

            DateTime recordedAt = reader.GetDateTime(0);  
            double battery = reader.GetDouble(1);         
            double temperature = reader.GetDouble(2);     

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


        // 기간 요약 조회 함수 (배터리·온도, 비동기)
        public async Task<(List<SummaryPoint> Battery, List<SummaryPoint> Temperature)> GetSummariesAsync(
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

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@satelliteId", SqlDbType.VarChar, 16).Value = satelliteId;
            command.Parameters.Add("@from", SqlDbType.DateTime2).Value = from;
            command.Parameters.Add("@to", SqlDbType.DateTime2).Value = to;
            command.Parameters.Add("@bucket", SqlDbType.Int).Value = bucketSeconds;

            List<SummaryPoint> battery = new List<SummaryPoint>();
            List<SummaryPoint> temperature = new List<SummaryPoint>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                DateTime bucketStart = reader.GetDateTime(0);
                battery.Add(new SummaryPoint(bucketStart, reader.GetDouble(1), reader.GetDouble(2), reader.GetDouble(3)));
                temperature.Add(new SummaryPoint(bucketStart, reader.GetDouble(4), reader.GetDouble(5), reader.GetDouble(6)));
            }
            return (battery, temperature);
        }

        // 하루 요약 채우기 함수
        public async Task FillDailyAsync(string satelliteId, DateTime fromDay, DateTime toDay)
        {
            const string createSql = """
            IF OBJECT_ID('dbo.TelemetryDaily') IS NULL
            CREATE TABLE dbo.TelemetryDaily (
                SatelliteId     VARCHAR(16) NOT NULL,
                Day             DATE        NOT NULL,
                SampleCount     INT         NOT NULL,
                BatterySum      FLOAT       NOT NULL,
                BatteryMin      FLOAT       NOT NULL,
                BatteryMax      FLOAT       NOT NULL,
                TemperatureSum  FLOAT       NOT NULL,
                TemperatureMin  FLOAT       NOT NULL,
                TemperatureMax  FLOAT       NOT NULL,
                CONSTRAINT PK_TelemetryDaily PRIMARY KEY (SatelliteId, Day)
            );
            """;

            const string daysSql = """
            SELECT Day FROM dbo.TelemetryDaily
            WHERE SatelliteId = @satelliteId AND Day >= @fromDay AND Day < @toDay;
            """;

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            await using (var createCommand = new SqlCommand(createSql, connection))
                await createCommand.ExecuteNonQueryAsync();   

            HashSet<DateTime> savedDays = new HashSet<DateTime>();  
            await using (var daysCommand = new SqlCommand(daysSql, connection))
            {
                daysCommand.Parameters.Add("@satelliteId", SqlDbType.VarChar, 16).Value = satelliteId;
                daysCommand.Parameters.Add("@fromDay", SqlDbType.Date).Value = fromDay;
                daysCommand.Parameters.Add("@toDay", SqlDbType.Date).Value = toDay;
                await using var reader = await daysCommand.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    savedDays.Add(reader.GetDateTime(0));
            }

            DateTime yesterday = toDay.AddDays(-1);
            for (DateTime day = fromDay; day < toDay; day = day.AddDays(1))
            {
                if (!savedDays.Contains(day) || day == yesterday)
                    await FillDayAsync(connection, satelliteId, day);
            }
        }

        // 하루 요약 1줄 저장 함수
        private static async Task FillDayAsync(SqlConnection connection, string satelliteId, DateTime day)
        {
            const string sql = """
            DELETE FROM dbo.TelemetryDaily WHERE SatelliteId = @satelliteId AND Day = @day;

            INSERT INTO dbo.TelemetryDaily
                (SatelliteId, Day, SampleCount, BatterySum, BatteryMin, BatteryMax, TemperatureSum, TemperatureMin, TemperatureMax)
            SELECT @satelliteId, @day, COUNT(*), SUM(Battery), MIN(Battery), MAX(Battery),
                   SUM(Temperature), MIN(Temperature), MAX(Temperature)
            FROM dbo.Telemetry
            WHERE SatelliteId = @satelliteId AND RecordedAt >= @day AND RecordedAt < DATEADD(DAY, 1, @day)
            HAVING COUNT(*) > 0;
            """;

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@satelliteId", SqlDbType.VarChar, 16).Value = satelliteId;
            command.Parameters.Add("@day", SqlDbType.DateTime2).Value = day;
            await command.ExecuteNonQueryAsync();
        }

        // 하루 요약 조회 함수 (배터리·온도)
        public async Task<(List<SummaryPoint> Battery, List<SummaryPoint> Temperature)> GetDailyAsync(
            string satelliteId, DateTime fromDay, DateTime toDay)
        {
            const string sql = """
            SELECT Day,
                   BatterySum / SampleCount, BatteryMin, BatteryMax,
                   TemperatureSum / SampleCount, TemperatureMin, TemperatureMax
            FROM dbo.TelemetryDaily
            WHERE SatelliteId = @satelliteId AND Day >= @fromDay AND Day < @toDay
            ORDER BY Day;
            """;

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@satelliteId", SqlDbType.VarChar, 16).Value = satelliteId;
            command.Parameters.Add("@fromDay", SqlDbType.Date).Value = fromDay;
            command.Parameters.Add("@toDay", SqlDbType.Date).Value = toDay;

            List<SummaryPoint> battery = new List<SummaryPoint>();
            List<SummaryPoint> temperature = new List<SummaryPoint>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                DateTime day = reader.GetDateTime(0);
                battery.Add(new SummaryPoint(day, reader.GetDouble(1), reader.GetDouble(2), reader.GetDouble(3)));
                temperature.Add(new SummaryPoint(day, reader.GetDouble(4), reader.GetDouble(5), reader.GetDouble(6)));
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
