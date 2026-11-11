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

    public void Insert(Telemetry item)
    {
        const string sql = @"
            INSERT INTO dbo.Telemetry (SatelliteId, RecordedAt, Battery, Temperature, Source)
            VALUES (@satelliteId, @recordedAt, @battery, @temperature,2);
            ";

        // 연결 DB
        using var conn = new SqlConnection(connectionString);
        conn.Open();
        // 쿼리 명령 만들기
        using var cmd = new SqlCommand(sql, conn);

        // 파라미터 넣기: 쿼리의 @이름 자리에 실제 값 채우기
        cmd.Parameters.Add("@satelliteId", SqlDbType.VarChar, 16).Value = item.SatelliteId;   // 칸 타입(VARCHAR)과 맞춤
        cmd.Parameters.AddWithValue("@recordedAt", item.RecordedAt);
        cmd.Parameters.AddWithValue("@battery", item.Battery);
        cmd.Parameters.AddWithValue("@temperature", item.Temperature);

        // 5. 실행: INSERT는 결과 표가 없으므로 ExecuteReader 대신 ExecuteNonQuery
        cmd.ExecuteNonQuery();

    }

    /// <summary>여러 건을 연결 1번으로 저장 (Source = 2: 소켓 수신).</summary>
    public void InsertBatch(List<Telemetry> items)
    {
        // 1. 쿼리 문자열
        const string sql = @"
            INSERT INTO dbo.Telemetry (SatelliteId, RecordedAt, Battery, Temperature, Source)
            VALUES (@satelliteId, @recordedAt, @battery, @temperature, 2);";

        // DB 연결
        using var conn = new SqlConnection(connectionString);
        conn.Open();

        // 3. 트랜잭션
        using var tx = conn.BeginTransaction();

        // 4. 값 삽입
        using var cmd = new SqlCommand(sql, conn, tx);
        var pSatellite = cmd.Parameters.Add("@satelliteId", SqlDbType.VarChar, 16);
        var pTime = cmd.Parameters.Add("@recordedAt", SqlDbType.DateTime2);
        var pBattery = cmd.Parameters.Add("@battery", SqlDbType.Float);
        var pTemperature = cmd.Parameters.Add("@temperature", SqlDbType.Float);

        // 4. 한 건씩 값만 바꿔서 실행
        foreach (Telemetry item in items)
        {
            pSatellite.Value = item.SatelliteId;
            pTime.Value = item.RecordedAt;
            pBattery.Value = item.Battery;
            pTemperature.Value = item.Temperature;
            cmd.ExecuteNonQuery();
        }


        tx.Commit();
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
    /// 최근 hours 시간의 배터리·배터리 온도를 bucketMinutes 분 단위로 요약 (평균·최소·최대).
    /// 한 번의 조회로 두 값을 같은 구간으로 묶음.
    /// </summary>
    public HistorySummary GetSummary(string satelliteId, int hours, int bucketMinutes)
    {
        // 안쪽 SELECT: 구간 나누기 / 바깥 SELECT: 구간별 평균·최소·최대
        const string sql = @"
            SELECT Bucket,
                   AVG(Battery)     AS AvgBattery,
                   MIN(Battery)     AS MinBattery,
                   MAX(Battery)     AS MaxBattery,
                   AVG(Temperature) AS AvgTemperature,
                   MIN(Temperature) AS MinTemperature,
                   MAX(Temperature) AS MaxTemperature
            FROM (
                SELECT DATE_BUCKET(MINUTE, @bucket, RecordedAt) AS Bucket,
                       Battery,
                       Temperature
                FROM dbo.Telemetry
                WHERE SatelliteId = @satelliteId
                  AND RecordedAt >= DATEADD(HOUR, -@hours, SYSDATETIME())
            ) AS t
            GROUP BY Bucket
            ORDER BY Bucket;";

        // 결과 줄 수를 모르므로 List
        var battery = new List<SummaryPoint>();
        var temperature = new List<SummaryPoint>();

        using var conn = new SqlConnection(connectionString);
        conn.Open();

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@satelliteId", SqlDbType.VarChar, 16).Value = satelliteId;
        cmd.Parameters.AddWithValue("@bucket", bucketMinutes);   // 값은 문자열 연결 대신 파라미터로
        cmd.Parameters.AddWithValue("@hours", hours);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            DateTime bucket = reader.GetDateTime(0);   // 0 = Bucket

            battery.Add(new SummaryPoint(
                bucket,
                reader.GetDouble(1),     // 1 = AvgBattery
                reader.GetDouble(2),     // 2 = MinBattery
                reader.GetDouble(3)));   // 3 = MaxBattery

            temperature.Add(new SummaryPoint(
                bucket,
                reader.GetDouble(4),     // 4 = AvgTemperature
                reader.GetDouble(5),     // 5 = MinTemperature
                reader.GetDouble(6)));   // 6 = MaxTemperature
        }

        return new HistorySummary(battery, temperature);
    }
}
