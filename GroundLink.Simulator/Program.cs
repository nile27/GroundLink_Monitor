/*
 * 위성 시뮬레이터 (콘솔)
 *
 * 1. TCP 서버 : (127.0.0.1:5000)
 * 2. 1초마다 텔레메트리 JSON형태 데이터 1건 전송 
 * 3. 궤도 모델 (햇빛 = 충전 / 그늘 = 방전)
 * 4. 통신이 중간에 끊어지면 대기 상태
 *
 * 실행: dotnet run --project GroundLink.Simulator
 */

using System.Net;
using System.Net.Sockets;
using System.Text.Json;

// 궤도 모델 
const int OrbitPeriod = 5669;       // 궤도 1바퀴 (초, 약 94.5분)
const int SunDuration = 3912;       // 햇빛 구간 (초)
const int EclipseDuration = 1757;   // 그늘 구간 (초)
const double SocMax = 100.0;        // 햇빛 끝 배터리 (%)
const double Dod = 15.0;            // 1바퀴에 방전되는 양 (%) → 85 ~ 100
const double TempMean = 15.0;       // 온도 평균 (°C)
const double TempAmp = 3.0;         // 온도 변화 폭 (°C) → 12 ~ 18

DateTime epoch = new DateTime(2026, 1, 1, 0, 0, 0);   
Random random = new Random();

JsonSerializerOptions jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase  
};

TcpListener listener = new TcpListener(IPAddress.Loopback, 5000);  
listener.Start();

while (true)   
{
    Console.WriteLine("접속 대기: 127.0.0.1:5000");
    using TcpClient client = listener.AcceptTcpClient();   
    Console.WriteLine($"접속됨: {client.Client.RemoteEndPoint}");

    using NetworkStream stream = client.GetStream();
    using StreamWriter writer = new StreamWriter(stream) { AutoFlush = true };   

    try
    {
        using PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromSeconds(1));   // 1초 주기 타이머

        while (true)
        {
            await timer.WaitForNextTickAsync();   // 다음 1초까지 대기

            DateTime now = DateTime.Now;
            double phase = (now - epoch).TotalSeconds % OrbitPeriod;   

            double battery = Math.Min(100.0, BatteryAt(phase) + Noise(0.1));
            double temperature = TemperatureAt(phase) + Noise(0.2);

            Telemetry message = new Telemetry("SAT-001", now, Math.Round(battery, 2), Math.Round(temperature, 2));
            string json = JsonSerializer.Serialize(message, jsonOptions);

            writer.WriteLine(json);   

            string zone = phase < SunDuration ? "햇빛" : "그늘";
            Console.WriteLine($"[{zone} {(int)phase,4}초] 보냄: {json}");
        }
    }
    catch (IOException)
    {
      
        Console.WriteLine("연결 끊김");
    }
}

// 궤도 안 위치 → 배터리 (%). 0초 = 햇빛 시작
double BatteryAt(double phase) =>
    phase < SunDuration
        ? (SocMax - Dod) + Dod * phase / SunDuration                   // 햇빛: 충전 85 → 100
        : SocMax - Dod * (phase - SunDuration) / EclipseDuration;      // 그늘: 방전 100 → 85

// 궤도 안 위치 → 배터리 온도 (°C)
double TemperatureAt(double phase) =>
    phase < SunDuration
        ? TempMean - TempAmp * Math.Cos(Math.PI * phase / SunDuration)                       // 햇빛: 12 → 18
        : TempMean + TempAmp * Math.Cos(Math.PI * (phase - SunDuration) / EclipseDuration);  // 그늘: 18 → 12

// -amplitude ~ +amplitude 사이 잡음 (센서 흔들림 흉내)
double Noise(double amplitude) => (random.NextDouble() * 2 - 1) * amplitude;

// 보낼 데이터 1건 (앱의 Models/Telemetry와 필드 이름 같음)
record Telemetry(string SatelliteId, DateTime RecordedAt, double Battery, double Temperature);