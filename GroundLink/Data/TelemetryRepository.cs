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
    }


}
