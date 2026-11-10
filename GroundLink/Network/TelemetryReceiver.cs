using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using GroundLink.Models;

namespace GroundLink.Network;

/*
 * 시뮬레이터 TCP 수신.
 * 별도 스레드에서 한 줄씩 받아 Telemetry로 변환하고 이벤트로 알림.
 * 이벤트는 수신 스레드에서 발생 → 화면을 바꾸려면 받는 쪽에서 Dispatcher로 넘겨야 함.
 */
public class TelemetryReceiver
{
    // 시뮬레이터와 같은 필드 이름 규칙 (camelCase)
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string host;
    private readonly int port;

    /// <summary>1건 수신 (수신 스레드에서 발생).</summary>
    public event Action<Telemetry>? Received;

    /// <summary>수신 종료: 끊김 또는 오류 사유 (수신 스레드에서 발생).</summary>
    public event Action<string>? Stopped;

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

    /// <summary>수신 스레드 본체: 접속 → 한 줄씩 수신 → 변환 → Received 발생.</summary>
    private void ReceiveLoop()
    {
        string reason = "연결 끊김";

        try
        {
            using var client = new TcpClient(host, port);
            using var reader = new StreamReader(client.GetStream());

            while (true)
            {
                string? line = reader.ReadLine();   // 블로킹: 수신 스레드만 멈춤, UI는 정상
                if (line == null) break;            // null = 연결 끊김

                Telemetry? data = Parse(line);
                if (data != null) Received?.Invoke(data);   // 깨진 줄은 건너뛰고 연결 유지
            }
        }
        catch (Exception ex)
        {
            reason = $"오류 - {ex.Message}";   // 접속 거부, 시뮬레이터 종료 등
        }

        Stopped?.Invoke(reason);
    }
}
