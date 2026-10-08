using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SMCalendar.Core;

/// <summary>
/// 데스크톱 앱용 OAuth 2.0 (루프백 리디렉션 + PKCE).
/// refresh token 은 DPAPI 로 암호화해서 현재 Windows 사용자만 읽을 수 있게 저장한다.
/// </summary>
internal sealed class GoogleAuth
{
    const string Scope = "https://www.googleapis.com/auth/calendar.events https://www.googleapis.com/auth/calendar.readonly";
    const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    static readonly byte[] Entropy = "SMCalendar.v1"u8.ToArray();

    ClientInfo? _client;
    string? _refreshToken;
    string? _accessToken;
    DateTime _expiryUtc;
    readonly SemaphoreSlim _lock = new(1, 1);

    public bool HasClient => _client != null;
    public bool IsSignedIn => _refreshToken != null;

    static string TokenPath => AppPaths.File("token.dat");

    public void Load()
    {
        _client = FindClient();
        try
        {
            if (File.Exists(TokenPath))
            {
                var bytes = ProtectedData.Unprotect(File.ReadAllBytes(TokenPath), Entropy, DataProtectionScope.CurrentUser);
                _refreshToken = Encoding.UTF8.GetString(bytes);
            }
        }
        catch { _refreshToken = null; }
    }

    static ClientInfo? FindClient()
    {
        foreach (var dir in new[] { AppPaths.Dir, AppContext.BaseDirectory })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir, "client_secret*.json"))
                if (TryParseClient(f) is { } c) return c;
        }
        return null;
    }

    static ClientInfo? TryParseClient(string path)
    {
        try
        {
            var file = JsonSerializer.Deserialize(File.ReadAllBytes(path), JsonCtx.Default.ClientSecretFile);
            var c = file?.Installed ?? file?.Web;
            return string.IsNullOrEmpty(c?.ClientId) ? null : c;
        }
        catch { return null; }
    }

    public bool ImportClientFile(string path)
    {
        if (TryParseClient(path) is not { } c) return false;
        Directory.CreateDirectory(AppPaths.Dir);
        File.Copy(path, AppPaths.File("client_secret.json"), overwrite: true);
        _client = c;
        return true;
    }

    public async Task SignInAsync(CancellationToken ct)
    {
        var client = _client ?? throw new AuthException("OAuth 클라이언트 파일(client_secret.json)이 없습니다.");
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));

        // HttpListener 는 URL ACL 문제가 있어 TcpListener 로 직접 받는다.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var redirect = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
            var url = AuthEndpoint +
                      "?client_id=" + Uri.EscapeDataString(client.ClientId!) +
                      "&redirect_uri=" + Uri.EscapeDataString(redirect) +
                      "&response_type=code" +
                      "&scope=" + Uri.EscapeDataString(Scope) +
                      "&code_challenge=" + challenge +
                      "&code_challenge_method=S256" +
                      "&state=" + state +
                      "&access_type=offline&prompt=consent";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));

            string? code = null;
            while (code == null)
            {
                using var tcp = await listener.AcceptTcpClientAsync(timeout.Token);
                var stream = tcp.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 2048, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync(timeout.Token) ?? "";
                // 헤더를 끝까지 읽어야 브라우저가 연결 리셋 오류를 띄우지 않는다.
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token))) { }

                var parts = requestLine.Split(' ');
                var query = ParseQuery(parts.Length > 1 ? parts[1] : "/");

                if (query.TryGetValue("error", out var error))
                {
                    await RespondAsync(stream, 200, Page("로그인이 취소되었습니다", "창을 닫고 다시 시도하세요."));
                    throw new AuthException("로그인이 취소되었습니다: " + error);
                }
                if (query.TryGetValue("code", out var c) && query.GetValueOrDefault("state") == state)
                {
                    code = c;
                    await RespondAsync(stream, 200, Page("연결 완료", "SM Calendar 가 Google 캘린더에 연결되었습니다. 이 창을 닫아도 됩니다."));
                }
                else
                {
                    await RespondAsync(stream, 404, "");
                }
            }

            var tok = await TokenRequestAsync(new()
            {
                ["code"] = code,
                ["client_id"] = client.ClientId!,
                ["client_secret"] = client.ClientSecret ?? "",
                ["redirect_uri"] = redirect,
                ["grant_type"] = "authorization_code",
                ["code_verifier"] = verifier,
            }, ct);
            if (string.IsNullOrEmpty(tok.RefreshToken))
                throw new AuthException("refresh token 을 받지 못했습니다. 다시 로그인해 주세요.");

            _refreshToken = tok.RefreshToken;
            SetAccess(tok);
            var enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(_refreshToken), Entropy, DataProtectionScope.CurrentUser);
            AppPaths.WriteAtomic(TokenPath, enc);
        }
        finally
        {
            listener.Stop();
        }
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_accessToken != null && DateTime.UtcNow < _expiryUtc) return _accessToken;
            if (_refreshToken == null || _client == null) throw new AuthException("구글 계정 연결 필요");
            var tok = await TokenRequestAsync(new()
            {
                ["refresh_token"] = _refreshToken,
                ["client_id"] = _client.ClientId!,
                ["client_secret"] = _client.ClientSecret ?? "",
                ["grant_type"] = "refresh_token",
            }, ct).ConfigureAwait(false);
            SetAccess(tok);
            return _accessToken!;
        }
        finally { _lock.Release(); }
    }

    public void InvalidateAccessToken() => _accessToken = null;

    public void SignOut()
    {
        var token = _refreshToken;
        _refreshToken = null;
        _accessToken = null;
        try { File.Delete(TokenPath); } catch { }
        if (token != null)
        {
            // 토큰 폐기는 실패해도 상관 없다.
            _ = Net.Http.PostAsync("https://oauth2.googleapis.com/revoke",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }));
        }
    }

    void SetAccess(TokenResponse tok)
    {
        _accessToken = tok.AccessToken;
        _expiryUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, tok.ExpiresIn) - 60);
    }

    async Task<TokenResponse> TokenRequestAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var resp = await Net.Http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var text = Encoding.UTF8.GetString(body);
            if (text.Contains("invalid_grant") || text.Contains("invalid_client") || text.Contains("unauthorized_client"))
            {
                // 토큰 만료/폐기 → 다시 로그인해야 한다.
                _refreshToken = null;
                _accessToken = null;
                try { File.Delete(TokenPath); } catch { }
                throw new AuthException("다시 로그인 필요");
            }
            throw new ApiException((int)resp.StatusCode, "토큰 요청 실패");
        }
        return JsonSerializer.Deserialize(body, JsonCtx.Default.TokenResponse)
               ?? throw new ApiException(0, "토큰 응답을 읽을 수 없음");
    }

    static async Task RespondAsync(Stream stream, int status, string html)
    {
        var body = Encoding.UTF8.GetBytes(html);
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Not Found")}\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head);
        await stream.WriteAsync(body);
        await stream.FlushAsync();
    }

    static string Page(string title, string message) =>
        "<!doctype html><html lang=ko><meta charset=utf-8><title>SM Calendar</title>" +
        "<body style=\"font-family:'Malgun Gothic',sans-serif;background:#16161a;color:#eee;display:grid;place-items:center;height:100vh;margin:0\">" +
        $"<div style=text-align:center><h2>{WebUtility.HtmlEncode(title)}</h2><p>{WebUtility.HtmlEncode(message)}</p></div></body></html>";

    static Dictionary<string, string> ParseQuery(string target)
    {
        var result = new Dictionary<string, string>();
        int q = target.IndexOf('?');
        if (q < 0) return result;
        foreach (var pair in target[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            var key = Uri.UnescapeDataString(eq < 0 ? pair : pair[..eq]);
            var value = eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
            result[key] = value;
        }
        return result;
    }

    static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
