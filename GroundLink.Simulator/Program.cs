/*
 * 위성 시뮬레이터
 *
 * 1. TCP 서버 (127.0.0.1:5000)
 * 2. 텔레메트리 전송 (JSON, 줄 단위). 기본 1초에 1건, --hz N 이면 1초에 N건 (성능 측정용)
 * 3. 값 생성: 궤도 모델 (햇빛 충전 / 그늘 방전), 02_seed_week.sql과 동일
 *    --warn 이면 배터리를 40 %p 낮춰 보냄 (경고 표시 확인용)
 * 4. 연결 끊김 시 재대기
 *
 * 실행 예
 *   dotnet run --project GroundLink.Simulator                  1초 1건
 *   dotnet run --project GroundLink.Simulator -- --hz 100      1초 100건
 *   dotnet run --project GroundLink.Simulator -- --warn        경고 값
 */

using System.Diagnostics;
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
const double WarnOffset = 40.0;     // --warn: 85~100 → 45~60 (경고 기준 60 % 미만에 걸림)

var epoch = new DateTime(2026, 1, 1, 0, 0, 0);   // 위상 기준 (SQL과 동일)

// 실행 옵션
int hz = 1;
bool warn = false;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--hz" && i + 1 < args.Length && int.TryParse(args[i + 1], out int n) && n is >= 1 and <= 1000)
        hz = n;
    if (args[i] == "--warn")
        warn = true;
}
TimeSpan interval = TimeSpan.FromSeconds(1.0 / hz);

var listener = new TcpListener(IPAddress.Loopback, 5000);
listener.Start();

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};
var random = new Random();

// 궤도 안 위치(0 ~ 5668초) → 배터리 잔량. 0 = 햇빛 시작
double BatteryAt(double phase) =>
    phase < SunDuration
        ? (SocMax - Dod) + Dod * phase / SunDuration                   // 충전 85 → 100
        : SocMax - Dod * (phase - SunDuration) / EclipseDuration;      // 방전 100 → 85

// 궤도 안 위치 → 배터리 온도
double TemperatureAt(double phase) =>
    phase < SunDuration
        ? TempMean - TempAmp * Math.Cos(Math.PI * phase / SunDuration)                       // 상승 12 → 18
        : TempMean + TempAmp * Math.Cos(Math.PI * (phase - SunDuration) / EclipseDuration);  // 하강 18 → 12

// -amplitude 이상 amplitude 미만의 잡음 (가정값)
double Noise(double amplitude) => (random.NextDouble() * 2 - 1) * amplitude;

Console.WriteLine($"설정: 1초에 {hz}건{(warn ? ", 경고 값 (배터리 -40 %p)" : "")}");

while (true)
{
    Console.WriteLine("접속 대기: 127.0.0.1:5000");
    using TcpClient client = listener.AcceptTcpClient();   // 접속 올 때까지 정지 (블로킹)
    Console.WriteLine($"접속됨: {client.Client.RemoteEndPoint}");

    using NetworkStream stream = client.GetStream();
    using var writer = new StreamWriter(stream) { AutoFlush = true };

    // 보낼 시각을 "시작 시각 + 순번 × 간격"으로 정함.
    // Thread.Sleep은 Windows에서 약 15.6 ms 단위라 Sleep(10)으로는 100건/초가 안 나옴
    // → 밀린 만큼 한 번에 보내서 초당 건수를 맞추고, 시각은 간격대로 붙임 (DB 고유 인덱스에서 겹치지 않게)
    DateTime start = DateTime.Now;
    var clock = Stopwatch.StartNew();
    long sent = 0;
    long sentAtLastLog = 0;
    var lastLog = TimeSpan.Zero;

    try
    {
        while (true)
        {
            long due = (long)(clock.Elapsed / interval) + 1;   // 지금까지 보냈어야 할 건수
            for (; sent < due; sent++)
            {
                DateTime at = start + interval * sent;
                double phase = (at - epoch).TotalSeconds % OrbitPeriod;

                double battery = Math.Min(100.0, BatteryAt(phase) + Noise(0.1)) - (warn ? WarnOffset : 0);
                double temperature = TemperatureAt(phase) + Noise(0.2);

                var message = new Telemetry("SAT-001", at, Math.Round(battery, 2), Math.Round(temperature, 2));
                string json = JsonSerializer.Serialize(message, jsonOptions);

                // 줄바꿈 = 메시지 구분자 (TCP는 메시지 경계 없음)
                writer.WriteLine(json);

                // 콘솔 출력은 느려서 1초에 1건일 때만 매번 출력
                if (hz == 1)
                {
                    string zone = phase < SunDuration ? "햇빛" : "그늘";
                    Console.WriteLine($"[{zone} {(int)phase,4}초] 보냄: {json}");
                }
            }

            // 1초에 여러 건일 때는 1초마다 실제 보낸 건수만 출력
            if (hz > 1 && clock.Elapsed - lastLog >= TimeSpan.FromSeconds(1))
            {
                Console.WriteLine($"최근 1초 {sent - sentAtLastLog}건 보냄 (누적 {sent}건)");
                sentAtLastLog = sent;
                lastLog = clock.Elapsed;
            }

            Thread.Sleep(1);   // 실제로는 약 1~16 ms 쉼. 밀린 건은 다음 바퀴에 몰아서 보냄
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
