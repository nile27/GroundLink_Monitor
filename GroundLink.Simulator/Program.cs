/*
 * 위성 시뮬레이터
 *
 * 1. TCP 서버 (127.0.0.1:5000)
 * 2. 1초 주기 텔레메트리 전송 (JSON, 줄 단위)
 * 3. 값 생성: 궤도 모델 (햇빛 충전 / 그늘 방전), 02_seed_week.sql과 동일
 * 4. 연결 끊김 시 재대기
 */

using System.Net;
using System.Net.Sockets;
using System.Text.Json;

// 궤도 모델 (02_seed_week.sql과 같은 값)
const int OrbitPeriod = 5669;       // 궤도 주기 (초, GOMX-4 실측)
const int SunDuration = 3912;       // 햇빛 구간 (초)
const int EclipseDuration = 1757;   // 그늘 구간 (초, 그늘 비율 0.31)
const double SocMax = 100.0;        // 햇빛 끝 잔량 (%)
const double Dod = 15.0;            // 1바퀴 방전 깊이 (%)
const double TempMean = 15.0;       // 배터리 온도 평균 (°C)
const double TempAmp = 3.0;         // 온도 진폭 (°C, GOMX-4 실측 2.5~3.5)

var epoch = new DateTime(2026, 1, 1, 0, 0, 0);   // 위상 기준 (SQL과 동일)

var listener = new TcpListener(IPAddress.Loopback, 5000);
listener.Start();

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};

var random = new Random();

// 궤도 안 위치(0 ~ 5668초) → 배터리 잔량. 0 = 햇빛 시작
double BatteryAt(int phase) =>
    phase < SunDuration
        ? (SocMax - Dod) + Dod * phase / SunDuration                   // 충전 85 → 100
        : SocMax - Dod * (phase - SunDuration) / EclipseDuration;      // 방전 100 → 85

// 궤도 안 위치 → 배터리 온도
double TemperatureAt(int phase) =>
    phase < SunDuration
        ? TempMean - TempAmp * Math.Cos(Math.PI * phase / SunDuration)                       // 상승 12 → 18
        : TempMean + TempAmp * Math.Cos(Math.PI * (phase - SunDuration) / EclipseDuration);  // 하강 18 → 12

// -amplitude 이상 amplitude 미만의 잡음 (가정값)
double Noise(double amplitude) => (random.NextDouble() * 2 - 1) * amplitude;

while (true)
{
    Console.WriteLine("접속 대기: 127.0.0.1:5000");

    using TcpClient client = listener.AcceptTcpClient();   // 접속 올 때까지 정지 (블로킹)
    Console.WriteLine($"접속됨: {client.Client.RemoteEndPoint}");

    using NetworkStream stream = client.GetStream();
    using var writer = new StreamWriter(stream) { AutoFlush = true };

    try
    {
        while (true)
        {
            DateTime now = DateTime.Now;
            int phase = (int)((long)(now - epoch).TotalSeconds % OrbitPeriod);

            double battery = Math.Min(100.0, BatteryAt(phase) + Noise(0.1));
            double temperature = TemperatureAt(phase) + Noise(0.2);

            var message = new Telemetry(
                "SAT-001",
                now,
                Math.Round(battery, 2),
                Math.Round(temperature, 2));

            string json = JsonSerializer.Serialize(message, jsonOptions);

            // 줄바꿈 = 메시지 구분자 (TCP는 메시지 경계 없음)
            writer.WriteLine(json);

            string zone = phase < SunDuration ? "햇빛" : "그늘";
            Console.WriteLine($"[{zone} {phase,4}초] 보냄: {json}");

            Thread.Sleep(1000);
        }
    }
    catch (IOException ex)
    {
        // 끊김 감지 1~2초 지연 가능
        Console.WriteLine($"연결 끊김: {ex.Message}");
    }
}

/*
 * 텔레메트리 1건 (위성 → 지상)
 *
 * SatelliteId  : 위성 ID (예: SAT-001)
 * RecordedAt   : 측정 시각 (로컬 시간)
 * Battery      : 배터리 잔량 (%, 0~100)
 * Temperature  : 배터리 온도 (°C)
 *
 * JSON 필드명은 camelCase: satelliteId, recordedAt, battery, temperature
 * 최상위 문 파일 규칙상 타입 선언은 실행 코드 뒤에 위치
 */
record Telemetry(string SatelliteId, DateTime RecordedAt, double Battery, double Temperature);