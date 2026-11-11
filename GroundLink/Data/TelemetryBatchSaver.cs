using GroundLink.Models;

namespace GroundLink.Data;

/*
 * 수신한 텔레메트리를 모아서 한 번에 DB에 저장.
 * 1건씩 저장하면 DB 왕복이 매초 생기고, 너무 오래 모으면 앱 종료 시 잃는 데이터가 많아짐 → 10건 단위.
 * 수신 스레드 하나에서만 사용 (여러 스레드가 동시에 쓰지 않으므로 lock 없음).
 */
public class TelemetryBatchSaver
{
    public const int BatchSize = 10;   // 10건 모이면 저장 (1초 1건 → 약 10초마다)

    private readonly TelemetryRepository repository;
    private readonly List<Telemetry> buffer = new();

    /// <summary>지금까지 저장한 건수.</summary>
    public int SavedCount { get; private set; }

    public TelemetryBatchSaver(TelemetryRepository repository)
    {
        this.repository = repository;
    }

    /// <summary>1건 추가. 10건이 모이면 저장.</summary>
    public void Add(Telemetry item)
    {
        buffer.Add(item);
        if (buffer.Count >= BatchSize) Flush();
    }

    /// <summary>
    /// 모인 것 전부 저장. 연결이 끊길 때도 호출해서 남은 것을 저장.
    /// 실패하면 예외를 던지고 버퍼는 그대로 둠 → 다음 저장 때 다시 시도.
    /// </summary>
    public void Flush()
    {
        if (buffer.Count == 0) return;

        repository.InsertBatch(buffer);
        SavedCount += buffer.Count;
        buffer.Clear();   // 저장 성공한 뒤에만 비움
    }
}
