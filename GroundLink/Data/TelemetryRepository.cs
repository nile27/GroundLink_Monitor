using System.Data;
using GroundLink.Models;
using Microsoft.Data.SqlClient;

namespace GroundLink.Data;

/*
 * 텔레메트리 DB 조회 (MonitorDB.dbo.Telemetry).
 * 화면을 모름: 조회 결과만 돌려주고, 오류는 호출한 쪽에서 처리.
 */
public class TelemetryRepository
{
    private readonly string connectionString;

    public TelemetryRepository(string connectionString)
    {
        this.connectionString = connectionString;
    }

    /// <summary>위성의 최신 1건. 없으면 null.</summary>
    public Telemetry? GetLatest(string satelliteId)
    {
        const string sql = @"
            SELECT TOP 1 RecordedAt, Battery, Temperature
            FROM dbo.Telemetry
            WHERE SatelliteId = @satelliteId
            ORDER BY RecordedAt DESC;";

        using var conn = new SqlConnection(connectionString);
        conn.Open();

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@satelliteId", SqlDbType.VarChar, 16).Value = satelliteId;   // 칸 타입(VARCHAR)과 맞춤

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;   // 첫 줄로 이동, 없으면 null

        return new Telemetry(
            satelliteId,
            reader.GetDateTime(0),   // 0 = RecordedAt
            reader.GetDouble(1),     // 1 = Battery
            reader.GetDouble(2));    // 2 = Temperature
    }

    /// <summary>
    /// 최근 hours 시간의 배터리를 bucketMinutes 분 단위로 요약 (평균·최소·최대).
    /// </summary>
    public List<SummaryPoint> GetSummary(string satelliteId, int hours, int bucketMinutes)
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
                WHERE SatelliteId = @satelliteId
                  AND RecordedAt >= DATEADD(HOUR, -@hours, SYSDATETIME())
            ) AS t
            GROUP BY Bucket
            ORDER BY Bucket;";

        var points = new List<SummaryPoint>();   // 결과 줄 수를 모르므로 List

        using var conn = new SqlConnection(connectionString);
        conn.Open();

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@satelliteId", SqlDbType.VarChar, 16).Value = satelliteId;
        cmd.Parameters.AddWithValue("@bucket", bucketMinutes);   // 값은 문자열 연결 대신 파라미터로
        cmd.Parameters.AddWithValue("@hours", hours);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            points.Add(new SummaryPoint(
                reader.GetDateTime(0),   // 0 = Bucket
                reader.GetDouble(1),     // 1 = AvgBattery
                reader.GetDouble(2),     // 2 = MinBattery
                reader.GetDouble(3)));   // 3 = MaxBattery
        }

        return points;
    }
}
