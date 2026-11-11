using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using GroundLink.Models;

namespace GroundLink.Network;

/*
 * 시뮬레이터 TCP 수신.
 * 별도 스레드에서 한 줄씩 받아 Telemetry로 변환하고 이벤트로 알림.
 * 이벤트는 수신 스레드에서 발생 → 화면을 바꾸려면 받는 쪽에서 Dispatcher로 넘겨야 함.
 *
 * 1. 접속 시도 → Connecting
 * 2. 한 줄 받을 때마다 → Received
 * 3. 끊기거나 접속 실패 → Disconnected, 3초 쉬고 1번부터 다시 (창을 닫을 때까지)
 */
public class TelemetryReceiver
{
    public const int RetryDelaySeconds = 3;   // 바로 다시 시도하면 시뮬레이터가 꺼진 동안 CPU·로그만 낭비

    // 시뮬레이터와 같은 필드 이름 규칙 (camelCase)
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string host;
    private readonly int port;

    /// <summary>접속 시도 시작. 값 = 몇 번째 시도인지 (끊긴 뒤 1부터 다시 셈). 수신 스레드에서 발생.</summary>
    public event Action<int>? Connecting;

    /// <summary>1건 수신 (수신 스레드에서 발생).</summary>
    public event Action<Telemetry>? Received;

    /// <summary>끊김 또는 접속 실패 사유. 이후 3초 뒤 다시 시도. 수신 스레드에서 발생.</summary>
    public event Action<string>? Disconnected;

    public TelemetryReceiver(string host, int port)
    {
        this.host = host;
        this.port = port;
    }

    /// <summary>수신 스레드 시작. 기다리지 않고 바로 반환.</summary>
    public void Start()
    {
        var thread = new Thread(ReceiveLoop)
        {
            IsBackground = true   // 창 닫으면 같이 종료
        };
        thread.Start();
    }

    /// <summary>JSON 한 줄 → Telemetry. 깨진 줄은 null.</summary>
    public static Telemetry? Parse(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<Telemetry>(line, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>수신 스레드 본체: (접속 → 수신 → 끊김 → 3초 대기)를 반복.</summary>
    private void ReceiveLoop()
    {
        int attempt = 0;

        while (true)
        {
            attempt++;
            Connecting?.Invoke(attempt);

            bool received = ReceiveOnce(out string reason);
            if (received) attempt = 0;   // 한 번이라도 받았으면 다음 끊김부터 1번째로 다시 셈

            Disconnected?.Invoke(reason);
            Thread.Sleep(TimeSpan.FromSeconds(RetryDelaySeconds));   // 수신 스레드만 쉼, UI는 정상
        }
    }

    /// <summary>접속 1번: 끊길 때까지 한 줄씩 수신. 1건이라도 받았으면 true, 끊긴 사유는 reason.</summary>
    private bool ReceiveOnce(out string reason)
    {
        bool received = false;
        reason = "연결 끊김";

        try
        {
            using var client = new TcpClient(host, port);
            using var reader = new StreamReader(client.GetStream());

            while (true)
            {
                string? line = reader.ReadLine();   // 블로킹: 수신 스레드만 멈춤, UI는 정상
                if (line == null) break;            // null = 상대가 연결을 닫음

                Telemetry? data = Parse(line);
                if (data == null) continue;         // 깨진 줄은 건너뛰고 연결 유지

                received = true;
                Received?.Invoke(data);
            }
        }
        catch (Exception ex)
        {
            reason = $"오류 - {ex.Message}";   // 접속 거부, 시뮬레이터 종료 등
        }

        return received;
    }
}
