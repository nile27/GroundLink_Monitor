using GroundLink.Data;
using GroundLink.Models;
using Microsoft.Data.SqlClient;

namespace GroundLink.Services;

// 수신 데이터 저장 서비스 (10건씩)
public class TelemetrySaver
{
    public const int BatchSize = 10;   // 한 번에 저장할 건수

    private readonly TelemetryRepository repository;
    private readonly List<Telemetry> buffer = new List<Telemetry>();   // 저장 대기
    private int savedCount;   // 지금까지 저장한 건수

    // 저장 완료 이벤트 (누적 건수)
    public event Action<int>? Saved;

    // 저장 실패 이벤트 (오류 글자)
    public event Action<string>? SaveFailed;

    public TelemetrySaver(TelemetryRepository repository)
    {
        this.repository = repository;
    }

    // 1건 추가 함수
    public void Add(Telemetry item)
    {
        buffer.Add(item);
        if (buffer.Count >= BatchSize)
            Flush();
    }

    // 남은 것 저장 함수
    public void Flush()
    {
        if (buffer.Count == 0)
            return;

        try
        {
            repository.SaveBatch(buffer);
            savedCount += buffer.Count;
            buffer.Clear();
            Saved?.Invoke(savedCount);
        }
        catch (SqlException ex)
        {
            SaveFailed?.Invoke($"DB 저장 실패 ({buffer.Count}건): {ex.Message}");
        }
    }
}