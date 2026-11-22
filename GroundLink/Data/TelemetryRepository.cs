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
    }
}
