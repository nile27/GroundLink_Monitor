using System.IO;
using System.Text.Json;

namespace GroundLink;

/*
 * 실행 설정 (appsettings.json).
 * DB 주소·시뮬레이터 주소를 코드에 박아 두면 PC가 바뀔 때마다 다시 빌드해야 함 → 파일로 분리.
 * 파일은 빌드할 때 실행 파일 옆으로 복사됨 (GroundLink.csproj의 CopyToOutputDirectory).
 *
 * ConnectionString : SQL Server 연결 문자열
 * SatelliteId      : 조회·표시할 위성 ID
 * SimulatorHost    : 시뮬레이터 주소
 * SimulatorPort    : 시뮬레이터 포트
 */
public record AppSettings(string ConnectionString, string SatelliteId, string SimulatorHost, int SimulatorPort)
{
    private const string FileName = "appsettings.json";

    /// <summary>실행 파일 옆 appsettings.json을 읽음. 없거나 값이 빠지면 무엇이 문제인지 알려주는 예외.</summary>
    public static AppSettings Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, FileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"{FileName}이 실행 파일 옆에 없습니다: {path}");

        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path))
            ?? throw new InvalidDataException($"{FileName}을 읽지 못했습니다.");

        if (string.IsNullOrWhiteSpace(settings.ConnectionString) || string.IsNullOrWhiteSpace(settings.SatelliteId) ||
            string.IsNullOrWhiteSpace(settings.SimulatorHost) || settings.SimulatorPort <= 0)
            throw new InvalidDataException($"{FileName}에 빠진 값이 있습니다.");

        return settings;
    }
}
