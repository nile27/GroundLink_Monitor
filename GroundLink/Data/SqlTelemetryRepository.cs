using System.Data;
using Microsoft.Data.SqlClient;
using GroundLink.Models;

namespace GroundLink.Data;

/// <summary>
/// SQL을 직접 실행하는 저장소 (ADO.NET).
/// 모든 await에 ConfigureAwait(false): 이 클래스는 화면을 안 건드리므로
/// 작업이 끝난 뒤 UI 스레드로 돌아올 필요가 없다 → 화면 부담 감소
/// </summary>
public class SqlTelemetryRepository : ITelemetryRepository
{
    public enum SaveMode
    {
        RowByRow,              // A. 한 건씩
        RowByRowInTransaction, // B. 한 건씩 + 트랜잭션
        BulkCopy               // C. SqlBulkCopy
    }

    private readonly SaveMode _mode;

    public SqlTelemetryRepository(SaveMode mode = SaveMode.BulkCopy)
    {
        _mode = mode;
    }

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

    // ───── 텔레메트리 저장 ─────
    public async Task SaveTelemetryAsync(IReadOnlyList<TelemetryRecord> records)
    {
        await using var conn = new SqlConnection(DbSettings.ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);

        switch (_mode)
        {
            case SaveMode.RowByRow:
                await InsertRowsAsync(conn, records, null).ConfigureAwait(false);
                break;

            case SaveMode.RowByRowInTransaction:
                await using (var tx = (SqlTransaction)await conn.BeginTransactionAsync().ConfigureAwait(false))
                {
                    await InsertRowsAsync(conn, records, tx).ConfigureAwait(false);
                    await tx.CommitAsync().ConfigureAwait(false);
                }
                break;

            case SaveMode.BulkCopy:
                await BulkInsertAsync(conn, records).ConfigureAwait(false);
                break;
        }
    }

    private static async Task InsertRowsAsync(SqlConnection conn, IReadOnlyList<TelemetryRecord> records, SqlTransaction? tx)
    {
        foreach (var r in records)
        {
            await using var cmd = new SqlCommand(InsertTelemetrySql, conn, tx);
            cmd.Parameters.Add("@SatelliteId", SqlDbType.VarChar, 16).Value = r.SatelliteId;
            cmd.Parameters.Add("@RecordedAt", SqlDbType.DateTime2).Value = r.RecordedAt;
            cmd.Parameters.Add("@Battery", SqlDbType.Float).Value = r.Battery;
            cmd.Parameters.Add("@Temperature", SqlDbType.Float).Value = r.Temperature;
            cmd.Parameters.Add("@Mode", SqlDbType.VarChar, 8).Value = r.Mode;
            cmd.Parameters.Add("@InContact", SqlDbType.Bit).Value = r.InContact;
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    private static async Task BulkInsertAsync(SqlConnection conn, IReadOnlyList<TelemetryRecord> records)
    {
        var table = new DataTable();
        table.Columns.Add("SatelliteId", typeof(string));
        table.Columns.Add("RecordedAt", typeof(DateTime));
        table.Columns.Add("Battery", typeof(double));
        table.Columns.Add("Temperature", typeof(double));
        table.Columns.Add("Mode", typeof(string));
        table.Columns.Add("InContact", typeof(bool));

        foreach (var r in records)
            table.Rows.Add(r.SatelliteId, r.RecordedAt, r.Battery, r.Temperature, r.Mode, r.InContact);

        using var bulk = new SqlBulkCopy(conn) { DestinationTableName = "dbo.Telemetry" };
        foreach (DataColumn c in table.Columns)
            bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);

        await bulk.WriteToServerAsync(table).ConfigureAwait(false);
    }

    // ───── 알람 저장 ─────
    public async Task SaveAlarmAsync(AlarmEvent alarm)
    {
        await using var conn = new SqlConnection(DbSettings.ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);

        await using var cmd = new SqlCommand(InsertAlarmSql, conn);
        cmd.Parameters.Add("@SatelliteId", SqlDbType.VarChar, 16).Value = alarm.SatelliteId;
        cmd.Parameters.Add("@RaisedAt", SqlDbType.DateTime2).Value = alarm.Time;
        cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 200).Value = alarm.Message;
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    // ───── 기간 조회 ─────
    public async Task<IReadOnlyList<TelemetryPoint>> GetHistoryAsync(string satelliteId, DateTime from, DateTime to)
    {
        var result = new List<TelemetryPoint>();

        await using var conn = new SqlConnection(DbSettings.ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);

        await using var cmd = new SqlCommand(SelectHistorySql, conn);
        cmd.Parameters.Add("@SatelliteId", SqlDbType.VarChar, 16).Value = satelliteId;
        cmd.Parameters.Add("@From", SqlDbType.DateTime2).Value = from;
        cmd.Parameters.Add("@To", SqlDbType.DateTime2).Value = to;

        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            result.Add(new TelemetryPoint(reader.GetDateTime(0), reader.GetDouble(1), reader.GetDouble(2)));
        }

        return result;
    }
}