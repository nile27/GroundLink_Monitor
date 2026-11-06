using System.Net;
using GroundLink.Models;
using GroundLink.Services;

namespace GroundLink.Tests;

/// <summary>
/// ApiSatelliteService 단위 테스트.
/// 진짜 서버 대신 FakeHttpHandler가 정해진 JSON을 돌려준다 (네트워크 없이 실행).
/// </summary>
public class ApiSatelliteServiceTests
{
    // 서버 GET /satellites 응답과 같은 모양 (필요 없는 칸도 실제처럼 넣음)
    private const string SatellitesJson = """
        [
          { "id": "SAT-001", "name": null, "createdAt": "2026-11-06T09:33:19.767Z",
            "latestTelemetry": { "id": "1", "satId": "SAT-001", "ts": "2026-11-06T09:33:42.871Z",
              "battery": 55.5, "temp": 21.3, "rssi": -92.4, "mode": "SAFE", "sunlit": true } },
          { "id": "SAT-002", "name": null, "createdAt": "2026-11-06T09:33:19.765Z",
            "latestTelemetry": { "id": "2", "satId": "SAT-002", "ts": "2026-11-06T09:33:42.871Z",
              "battery": 80.25, "temp": -5.5, "rssi": null, "mode": "NORMAL", "sunlit": false } },
          { "id": "SAT-003", "name": null, "createdAt": "2026-11-06T09:33:19.795Z", "latestTelemetry": null }
        ]
        """;

    private static (FakeHttpHandler Http, ApiSatelliteService Service) Create()
    {
        var http = new FakeHttpHandler();
        http.Json("/satellites", SatellitesJson);
        http.Json("/alarms", "[]");
        return (http, new ApiSatelliteService(http.CreateClient()));
    }

    [Fact]
    public async Task 서버_응답을_SatelliteStatus로_변환한다()
    {
        var (_, service) = Create();

        await service.RefreshAsync();

        Assert.Equal(3, service.Satellites.Count);

        var contact = service.Satellites[0];
        Assert.Equal("SAT-001", contact.Id);
        Assert.Equal(55.5, contact.Battery);
        Assert.Equal(21.3, contact.Temperature);
        Assert.Equal(SatelliteMode.Safe, contact.Mode);
        Assert.True(contact.InContact);   // rssi 값이 있으면 교신 중

        var noContact = service.Satellites[1];
        Assert.Equal(80.25, noContact.Battery);
        Assert.Equal(-5.5, noContact.Temperature);
        Assert.Equal(SatelliteMode.Normal, noContact.Mode);
        Assert.False(noContact.InContact); // rssi가 null이면 교신 없음

        Assert.Equal("SAT-003", service.Satellites[2].Id); // 텔레메트리가 없어도 목록엔 들어옴
    }

    [Fact]
    public async Task 다시_조회하면_같은_객체의_값만_바뀐다()
    {
        var (http, service) = Create();
        await service.RefreshAsync();
        var first = service.Satellites[0];

        http.Json("/satellites", SatellitesJson.Replace("55.5", "40.0"));
        await service.RefreshAsync();

        Assert.Equal(3, service.Satellites.Count);       // 위성이 늘어나지 않고
        Assert.Same(first, service.Satellites[0]);       // 같은 객체 (화면 바인딩 유지)
        Assert.Equal(40.0, service.Satellites[0].Battery);
    }

    [Fact]
    public async Task 서버가_실패해도_예외없이_마지막_값을_유지한다()
    {
        var (http, service) = Create();
        await service.RefreshAsync();

        http.Json("/satellites", "서버 오류", HttpStatusCode.InternalServerError);
        await service.RefreshAsync(); // 예외가 나면 테스트 실패

        Assert.Equal(55.5, service.Satellites[0].Battery);
    }

    [Fact]
    public async Task 위성이_명령을_거부하면_예외를_던진다()
    {
        var (http, service) = Create();
        const string path = "/satellites/SAT-002/commands";
        // 1) 명령 접수: 202 + PENDING
        http.Json(path, """{ "id": "c1", "status": "PENDING" }""", HttpStatusCode.Accepted, method: "POST");
        // 2) 명령 이력: 위성이 거부함 (배터리 20% 미만에서 NORMAL 요청)
        http.Json(path, """[ { "id": "c1", "status": "FAILED", "resultMessage": "배터리 부족" } ]""", method: "GET");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetModeAsync("SAT-002", SatelliteMode.Normal));

        Assert.Contains("FAILED", ex.Message);
        Assert.Contains(("POST", path), http.Requests);
    }

    [Fact]
    public async Task 명령이_성공하면_바로_다시_조회한다()
    {
        var (http, service) = Create();
        const string path = "/satellites/SAT-001/commands";
        http.Json(path, """{ "id": "c2", "status": "PENDING" }""", HttpStatusCode.Accepted, method: "POST");
        http.Json(path, """[ { "id": "c2", "status": "SUCCESS", "resultMessage": "모드 변경: NORMAL" } ]""", method: "GET");

        await service.SetModeAsync("SAT-001", SatelliteMode.Normal);

        // 1초 타이머를 기다리지 않고 명령 직후 위성 목록을 한 번 더 읽었는지
        Assert.Contains(("GET", "/satellites"), http.Requests);
        Assert.True(http.Requests.FindIndex(r => r == ("GET", "/satellites"))
                    > http.Requests.FindIndex(r => r == ("POST", path)));
    }
}
