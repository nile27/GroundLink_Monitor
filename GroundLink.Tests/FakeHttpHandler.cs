using System.Net;
using System.Net.Http;
using System.Text;

namespace GroundLink.Tests;

/// <summary>
/// 테스트용 가짜 HTTP 응답. 실제 네트워크 없이 "이 주소엔 이 JSON"을 돌려준다.
/// HttpClient가 요청을 보내면 진짜 서버 대신 이 클래스가 대답한다.
/// </summary>
public class FakeHttpHandler : HttpMessageHandler
{
    // "경로" 또는 "메서드 경로"(예: "POST /satellites/SAT-001/commands") → (상태 코드, 본문)
    private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _routes = new();

    /// <summary>받은 요청 (메서드, 경로) 기록</summary>
    public List<(string Method, string Path)> Requests { get; } = new();

    /// <summary>응답 등록. method를 주면 그 메서드에만, 안 주면 모든 메서드에 적용</summary>
    public void Json(string path, string body, HttpStatusCode status = HttpStatusCode.OK, string? method = null)
        => _routes[method is null ? path : $"{method} {path}"] = (status, body);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var method = request.Method.Method;
        var path = request.RequestUri!.AbsolutePath;
        Requests.Add((method, path));

        var (status, body) =
            _routes.TryGetValue($"{method} {path}", out var exact) ? exact :
            _routes.TryGetValue(path, out var any) ? any :
            (HttpStatusCode.NotFound, "{}");

        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
    }

    /// <summary>이 처리기를 쓰는 HttpClient (서버 주소는 아무거나)</summary>
    public HttpClient CreateClient() => new(this) { BaseAddress = new Uri("http://test.local/") };
}
