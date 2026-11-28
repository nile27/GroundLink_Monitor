using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using GroundLink.Models;

namespace GroundLink.Network
{
    
    public class TelemetryClient
    {
        // JSON 파싱 작업
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase   
        };

        private TcpClient? client;
        private StreamReader? reader;

        public bool IsConnected => client != null && client.Connected;

        // 시뮬레이터 접속 함수
        public async Task ConnectAsync(string host, int port, TimeSpan timeout)
        {
            client = new TcpClient();
            using CancellationTokenSource cts = new CancellationTokenSource(timeout);

            try
            {
                await client.ConnectAsync(host, port, cts.Token);   // 시간 넘으면 OperationCanceledException
            }
            catch
            {
                Disconnect();   // 실패하면 정리
                throw;
            }

            reader = new StreamReader(client.GetStream());
        }

        
        public Telemetry? ReceiveOne()
        {
            if (reader == null)
                return null;

            string? line = reader.ReadLine();               // 한 줄 받기 (블로킹)
            if (line == null)
                return null;                                // 상대가 끊음

            return JsonSerializer.Deserialize<Telemetry>(line, JsonOptions);
        }

        // 연결 끊기 함수
        public void Disconnect()
        {
            reader?.Dispose();
            client?.Dispose();
            reader = null;
            client = null;
        }
    }
}