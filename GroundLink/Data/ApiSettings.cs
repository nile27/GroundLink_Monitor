namespace GroundLink.Data;

/// <summary>
/// 백엔드 서버(GroundLink NestJS) 접속 정보.
/// </summary>
public static class ApiSettings
{
    // 로컬에서 실행 중인 백엔드 주소 (끝에 / 를 붙여야 상대 경로가 올바르게 합쳐짐)
    // API 문서: http://127.0.0.1:3000/api/docs
    public const string BaseUrl = "http://127.0.0.1:3000/";

    // 요청 하나를 기다리는 최대 시간. 서버가 멈춰도 화면 갱신이 오래 막히지 않게 짧게
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);
}
