using System.Net;

namespace SMCalendar.Core;

internal static class Net
{
    /// <summary>처음 사용할 때 한 번만 만든다 (시작 속도에 영향 없음).</summary>
    public static readonly HttpClient Http = Create();

    static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(60),
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        // Google API 는 UA 에 "gzip" 이 있어야 압축 응답을 준다.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SMCalendar/1.0 (gzip)");
        return client;
    }
}

internal sealed class AuthException(string message) : Exception(message);

internal sealed class ApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
