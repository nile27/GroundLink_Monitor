using System.Net;
using System.Net.Sockets;
using System.Text.Json;

var listener = new TcpListener(IPAddress.Loopback, 5000);
listener.Start();

// C# 속성 이름(PascalCase)을 JSON 필드 이름(camelCase)으로 변환해 직렬화한다.
var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};

// 난수 생성기. 반복마다 새로 만들지 않고 하나를 계속 쓴다.
var random = new Random();

// 현재 상태. 연결이 끊겼다 다시 붙어도 이어서 변하도록 바깥 반복보다 밖에 둔다.
double battery = 62.3;
double temperature = 11.8;

// 평균 회귀 기준값과 강도. 매초 (기준값 - 현재값)의 2%만큼 기준값 쪽으로 끌어당긴다.
const double BatteryCenter = 60.0;
const double TemperatureCenter = 10.0;
const double PullStrength = 0.02;

// 바깥 반복: 연결 1개를 처리하고, 끊기면 다음 연결을 기다린다.
// 한 번에 클라이언트 1개만 처리한다. 처리 중에 들어온 두 번째 접속은 앞 연결이 끝날 때까지 대기열에서 기다린다.
while (true)
{
    Console.WriteLine("시뮬레이터: 127.0.0.1:5000 에서 접속 대기 중...");

    // 블로킹 호출: 클라이언트가 접속할 때까지 현재 스레드가 멈춘다.
    // using 범위 = 바깥 반복 1회. 반복이 끝나면 이 연결의 client, stream, writer가 Dispose된다.
    using TcpClient client = listener.AcceptTcpClient();
    Console.WriteLine($"접속됨: {client.Client.RemoteEndPoint}");

    using NetworkStream stream = client.GetStream();
    // 기본 인코딩: BOM 없는 UTF-8. AutoFlush: Write 호출마다 버퍼를 즉시 전송.
    using var writer = new StreamWriter(stream) { AutoFlush = true };

    try
    {
        // 안쪽 반복: 연결이 살아 있는 동안 1초마다 1개 전송.
        while (true)
        {
            // 다음 값 = 이전 값 + 기준값 쪽으로 당기는 양 + 랜덤 변화(-0.2 이상 0.2 미만).
            battery += (BatteryCenter - battery) * PullStrength + (random.NextDouble() - 0.5) * 0.4;
            temperature += (TemperatureCenter - temperature) * PullStrength + (random.NextDouble() - 0.5) * 0.4;

            // 물리적으로 가능한 범위를 벗어나지 않게 자른다.
            battery = Math.Clamp(battery, 0, 100);
            temperature = Math.Clamp(temperature, -40, 80);

            // 상태는 소수점 아래 전체를 유지하고, 보내는 값만 소수 첫째 자리로 반올림한다.
            var message = new Telemetry(
                "SAT-001",
                DateTime.Now,
                Math.Round(battery, 1),
                Math.Round(temperature, 1));

            // 객체 → JSON 문자열 1개. 줄바꿈 문자를 포함하지 않는다.
            string json = JsonSerializer.Serialize(message, jsonOptions);

            // 줄바꿈을 메시지 구분자로 사용: 한 줄 = 메시지 1개.
            writer.WriteLine(json);
            Console.WriteLine($"보냄: {json}");

            // 1,000ms 대기. 직렬화·전송 시간이 더해지므로 실제 간격은 1초보다 약간 길다.
            Thread.Sleep(1000);
        }
    }
    catch (IOException ex)
    {
        // 상대가 연결을 닫은 뒤의 쓰기는 IOException으로 실패한다.
        // TCP 특성상 끊긴 직후 1~2번의 쓰기는 오류 없이 넘어갈 수 있어서, 감지가 1~2초 늦을 수 있다.
        Console.WriteLine($"연결 끊김: {ex.Message}");
    }
}

// 전송 메시지 형식. 최상위 문을 쓰는 파일에서는 타입 선언을 코드 뒤에 둬야 한다.
record Telemetry(string SatelliteId, DateTime RecordedAt, double Battery, double Temperature);