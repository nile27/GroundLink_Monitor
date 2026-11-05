using System.Data;
using Microsoft.Data.SqlClient;
using GroundLink.Models;

namespace GroundLink.Data;

/// <summary>
/// SQL을 직접 작성해서 실행하는 저장소 (ADO.NET).
/// 지금은 "한 건씩 INSERT" 방식. W8-4에서 SqlBulkCopy와 비교 예정.
/// </summary>
public class SqlTelemetryRepository : ITelemetryRepository
{
    // @이 붙은 건 "파라미터" 자리. 값은 아래에서 따로 넣는다
    private const string InsertTelemetrySql = @"
        INSERT INTO dbo.Telemetry (SatelliteId, RecordedAt, Battery, Temperature, Mode, InContact)
        VALUES (@SatelliteId, @RecordedAt, @Battery, @Temperature, @Mode, @InContact);";

    private const string InsertAlarmSql = @"
        INSERT INTO dbo.Alarm (SatelliteId, RaisedAt, Message)
        VALUES (@SatelliteId, @RaisedAt, @Message);";

    private const string SelectHistorySql = @"
        SELECT RecordedAt, Battery, Temperature
        FROM dbo.Telemetry
        WHERE SatelliteId = @SatelliteId
          AND RecordedAt >= @From AND RecordedAt < @To
        ORDER BY RecordedAt;";

    public async Task SaveTelemetryAsync(IReadOnlyList<TelemetryRecord> records)
    {
        // 연결 열기 (using: 다 쓰면 자동으로 닫힘)
        await using var conn = new SqlConnection(DbSettings.ConnectionString);
        await conn.OpenAsync();

        // 한 건씩 INSERT (일부러 단순하게. W8-4의 "개선 전" 기준)
        foreach (var r in records)
        {
            await using var cmd = new SqlCommand(InsertTelemetrySql, conn);

            // 칸 타입을 DB와 똑같이 지정해서 값 넣기
            cmd.Parameters.Add("@SatelliteId", SqlDbType.VarChar, 16).Value = r.SatelliteId;
            cmd.Parameters.Add("@RecordedAt", SqlDbType.DateTime2).Value = r.RecordedAt;
            cmd.Parameters.Add("@Battery", SqlDbType.Float).Value = r.Battery;
            cmd.Parameters.Add("@Temperature", SqlDbType.Float).Value = r.Temperature;
            cmd.Parameters.Add("@Mode", SqlDbType.VarChar, 8).Value = r.Mode;
            cmd.Parameters.Add("@InContact", SqlDbType.Bit).Value = r.InContact;

            await cmd.ExecuteNonQueryAsync(); // 결과 행이 없는 명령 실행 (INSERT, UPDATE, DELETE)
        }
    }

    public async Task SaveAlarmAsync(AlarmEvent alarm)
    {
        await using var conn = new SqlConnection(DbSettings.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(InsertAlarmSql, conn);
        cmd.Parameters.Add("@SatelliteId", SqlDbType.VarChar, 16).Value = alarm.SatelliteId;
        cmd.Parameters.Add("@RaisedAt", SqlDbType.DateTime2).Value = alarm.Time;
        cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 200).Value = alarm.Message; // 한글 → NVarChar
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<TelemetryPoint>> GetHistoryAsync(string satelliteId, DateTime from, DateTime to)
    {
        var result = new List<TelemetryPoint>();

        await using var conn = new SqlConnection(DbSettings.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(SelectHistorySql, conn);
        cmd.Parameters.Add("@SatelliteId", SqlDbType.VarChar, 16).Value = satelliteId;
        cmd.Parameters.Add("@From", SqlDbType.DateTime2).Value = from;
        cmd.Parameters.Add("@To", SqlDbType.DateTime2).Value = to;

        // 결과를 한 줄씩 읽기
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new TelemetryPoint(
                reader.GetDateTime(0),   // RecordedAt (SELECT 순서대로 0번)
                reader.GetDouble(1),     // Battery
                reader.GetDouble(2)));   // Temperature
        }

        return result;
    }
}