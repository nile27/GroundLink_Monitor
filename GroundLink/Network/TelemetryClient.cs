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
        public void Connect(string host, int port)
        {
            client = new TcpClient();
            client.Connect(host, port);                     // 실패하면 SocketException
            reader = new StreamReader(client.GetStream());
        }

        
        public Telemetry? ReceiveOne()
        {
            if (reader == null)
                return null;

            string? line = reader.ReadLine();               // 블로킹: 최대 약 1초
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