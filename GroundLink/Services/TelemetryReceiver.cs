using System.IO;
using System.Text.Json;
using GroundLink.Models;
using GroundLink.Network;

namespace GroundLink.Services;

// 텔레메트리 수신 서비스 (수신 전용 스레드)
public class TelemetryReceiver
{
    private readonly TelemetryClient client = new TelemetryClient();
    private Thread? receiveThread;

    // 1건 받음 이벤트
    public event Action<Telemetry>? Received;

    // 연결 끊김 이벤트 (이유 글자)
    public event Action<string>? Disconnected;

    public bool IsConnected => client.IsConnected;

    // 접속 + 수신 스레드 시작 함수
    public void Start(string host, int port)
    {
        client.Connect(host, port);

        receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "TelemetryReceive" };
        receiveThread.Start();
    }

    // 수신 중지 함수
    public void Stop(TimeSpan wait)
    {
        client.Disconnect();
        receiveThread?.Join(wait);
    }

    // 수신 반복 함수
    private void ReceiveLoop()
    {
        string reason;

        try
        {
            while (true)
            {
                Telemetry? data = client.ReceiveOne();
                if (data == null)
                {
                    reason = "시뮬레이터가 연결을 끊음";
                    break;
                }

                Received?.Invoke(data);
            }
        }
        catch (IOException)
        {
            reason = "수신 중 연결 끊김";
        }
        catch (JsonException)
        {
            reason = "받은 데이터 형식 오류";
        }
        catch (ObjectDisposedException)
        {
            reason = "앱 종료";
        }

        client.Disconnect();
        Disconnected?.Invoke(reason);
    }
}
