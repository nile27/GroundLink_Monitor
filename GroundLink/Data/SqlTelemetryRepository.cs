using System.Data;
using Microsoft.Data.SqlClient;
using GroundLink.Models;

namespace GroundLink.Data;

/// <summary>
/// SQL을 직접 작성해서 실행하는 저장소 (ADO.NET, Microsoft.Data.SqlClient).
///
/// 저장 방식 비교 결과 (위성 1000대 = 1000건 저장 한 번):
///   한 건씩 INSERT 약 212ms / 트랜잭션으로 묶기 약 129ms / SqlBulkCopy 약 26ms
///   → 5000대에서는 한 건씩 방식이 1초를 넘겨서 SqlBulkCopy를 채택.
///   (비교에 쓴 코드는 Git 이력에 남아 있음)
///
/// 모든 await에 ConfigureAwait(false):
///   이 클래스는 화면을 건드리지 않으므로 작업 후 UI 스레드로 돌아올 필요가 없다.
///   덕분에 DataTable 만들기 같은 일이 UI 스레드를 막지 않는다.
/// </summary>
public class SqlTelemetryRepository : ITelemetryRepository
{
    // ───────── SQL 문 ─────────
    // @로 시작하는 건 파라미터 자리. 값은 코드에서 따로 넣는다 (SQL 인젝션 방지)

    private const string InsertAlarmSql = @"
        INSERT INTO dbo.Alarm (SatelliteId, RaisedAt, Message)
        VALUES (@SatelliteId, @RaisedAt, @Message);";

    // DATE_BUCKET으로 시간을 @Bucket초 칸으로 묶고, 칸마다 최소·최대·평균
    private const string SelectBatteryHistorySql = @"
        SELECT
            DATE_BUCKET(SECOND, @Bucket, RecordedAt) AS T,
            MIN(Battery), MAX(Battery), AVG(Battery)
        FROM dbo.Telemetry
        WHERE SatelliteId = @SatelliteId
          AND RecordedAt >= @From AND RecordedAt < @To
        GROUP BY DATE_BUCKET(SECOND, @Bucket, RecordedAt)
        ORDER BY T;";

    // ───────── 텔레메트리 저장 (SqlBulkCopy) ─────────
    public async Task SaveTelemetryAsync(IReadOnlyList<TelemetryRecord> records)
    {
        // 1) DB 테이블과 같은 모양의 메모리 표 만들기 (Id는 자동 증가라 제외)
        var table = new DataTable();
        table.Columns.Add("SatelliteId", typeof(string));
        table.Columns.Add("RecordedAt", typeof(DateTime));
        table.Columns.Add("Battery", typeof(double));
        table.Columns.Add("Temperature", typeof(double));
        table.Columns.Add("Mode", typeof(string));
        table.Columns.Add("InContact", typeof(bool));

        foreach (var r in records)
            table.Rows.Add(r.SatelliteId, r.RecordedAt, r.Battery, r.Temperature, r.Mode, r.InContact);

        // 2) 연결 열기 (using: 다 쓰면 자동으로 닫힘. 실제로는 연결 풀이 재사용)
        await using var conn = new SqlConnection(DbSettings.ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);

        // 3) 한 번에 전송
        using var bulk = new SqlBulkCopy(conn) { DestinationTableName = "dbo.Telemetry" };

        // Id 칸을 건너뛰려고 칸 이름끼리 연결
        foreach (DataColumn c in table.Columns)
            bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);

        await bulk.WriteToServerAsync(table).ConfigureAwait(false);
    }

    // ───────── 알람 저장 ─────────
    public async Task SaveAlarmAsync(AlarmEvent alarm)
    {
        await using var conn = new SqlConnection(DbSettings.ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);

        await using var cmd = new SqlCommand(InsertAlarmSql, conn);

        // 파라미터 타입을 DB 칸 타입과 맞춰서 지정
        cmd.Parameters.Add("@SatelliteId", SqlDbType.VarChar, 16).Value = alarm.SatelliteId;
        cmd.Parameters.Add("@RaisedAt", SqlDbType.DateTime2).Value = alarm.Time;
        cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 200).Value = alarm.Message; // 한글 → NVarChar

        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false); // 결과 행이 없는 명령 실행
    }

    // ───────── 차트용 기간 조회 ─────────
    public async Task<IReadOnlyList<HistoryPoint>> GetBatteryHistoryAsync(string satelliteId, DateTime from, DateTime to, int bucketSeconds)
    {
        var result = new List<HistoryPoint>();

        await using var conn = new SqlConnection(DbSettings.ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);

        await using var cmd = new SqlCommand(SelectBatteryHistorySql, conn);
        cmd.Parameters.Add("@Bucket", SqlDbType.Int).Value = bucketSeconds;
        cmd.Parameters.Add("@SatelliteId", SqlDbType.VarChar, 16).Value = satelliteId;
        cmd.Parameters.Add("@From", SqlDbType.DateTime2).Value = from;
        cmd.Parameters.Add("@To", SqlDbType.DateTime2).Value = to;

        // 결과를 한 줄씩 읽기 (숫자는 SELECT에 쓴 칸 순서)
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            result.Add(new HistoryPoint(
                reader.GetDateTime(0),  // T
                reader.GetDouble(1),    // MIN
                reader.GetDouble(2),    // MAX
                reader.GetDouble(3)));  // AVG
        }

        return result;
    }
}
