using System.IO;
using System.Text.Json;
using GroundLink.Models;
using GroundLink.Network;

namespace GroundLink.Services;

/*
 * 텔레메트리 수신 서비스 (수신 전용 스레드)
 * 접속 후 별도 스레드에서 한 줄씩 받고, 받을 때마다 / 끊길 때 이벤트로 알림.
 * 이벤트는 수신 스레드에서 발생 → 화면 값으로 바꾸는 쪽(ViewModel)이 UI 스레드로 넘김 (T-09)
 *
 * 1. Start: 접속 + 수신 스레드 시작 (접속 실패 시 SocketException)
 * 2. ReceiveLoop: ReadLine 반복 (여기서 멈춰도 UI는 안 멈춤)
 * 3. Received / Disconnected 이벤트
 */
public class TelemetryReceiver
{
    private readonly TelemetryClient client = new TelemetryClient();
    private Thread? receiveThread;

    // 1건 받음 (수신 스레드에서 발생)
    public event Action<Telemetry>? Received;

    // 연결 끊김, 이유 글자 (수신 스레드에서 발생)
    public event Action<string>? Disconnected;

    public bool IsConnected => client.IsConnected;

    // 접속 + 수신 스레드 시작 함수
    public void Start(string host, int port)
    {
        client.Connect(host, port);   // 실패하면 SocketException (부른 쪽에서 처리)

        receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "TelemetryReceive" };
        receiveThread.Start();
    }

    // 수신 반복 함수 (수신 스레드에서 실행)
    private void ReceiveLoop()
    {
        string reason;

        try
        {
            while (true)
            {
                Telemetry? data = client.ReceiveOne();   // 다음 줄 올 때까지 수신 스레드만 멈춤
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

        client.Disconnect();
        Disconnected?.Invoke(reason);
    }
}
