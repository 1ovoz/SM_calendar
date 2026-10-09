using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SMCalendar.Core;

internal static class Net
{
    /// <summary>처음 사용할 때 한 번만 만든다 (시작 속도에 영향 없음).</summary>
    public static readonly HttpClient Http = Create();

    /// <summary>
    /// Google 이 사용하는 루트 인증서의 공개키(SPKI) SHA-256 목록 (https://pki.goog/roots.pem, 2026-10 기준).
    /// Windows 의 일반 인증서 검증을 통과하고 + 체인에 이 중 하나가 있어야만 연결한다.
    /// PC 에 몰래 설치된 가짜 루트 인증서로 HTTPS 를 가로채는 중간자 공격을 막는다.
    /// </summary>
    static readonly HashSet<string> GoogleRootPins =
    [
        "hxqRlPTu1bMS/0DITB1SSu0vd4u/8l8TjPgfaAp63Gc=", // GTS Root R1
        "Vfd95BwDeSQo+NUYxVEEIlvkOlWY2SalKK1lPhzOx78=", // GTS Root R2
        "QXnt2YHvdHR3tJYmQIr0Paosp6t/nggsEGD4QJZ3Q0g=", // GTS Root R3
        "mEflZT5enoR1FuXLgYYGqnVEoZvmf9c2bVBpiOjYQ0c=", // GTS Root R4
        "CLOmM1/OXvSPjw5UOYbAf9GKOxImEp9hhku9W90fHMk=", // GlobalSign ECC Root CA - R4
        "cGuxAXyFXFkWm61cF4HPWX8S0srS9j0aSqN0k4AP+4A=", // GlobalSign Root CA - R3
        "fg6tdrtoGdwvVFEahDVPboswe53YIFjqbABPAdndpd8=", // GlobalSign ECC Root CA - R5
        "aCdH+LpiG4fN07wpXtXKvOciocDANj0daLOJKNJ4fx4=", // GlobalSign Root CA - R6
        "8ca6Zwz8iOTfUpc8rkIPCgid1HQUT+WAbEIAZOFZEik=", // DigiCert Assured ID Root G2
        "Fe7TOVlLME+M+Ee0dzcdjW/sYfTbKwGvWJ58U7Ncrkw=", // DigiCert Assured ID Root G3
        "i7WTqTvh0OioIruIfFR4kMPnBqrS2rdiVPl/s2uC/CY=", // DigiCert Global Root G2
        "uUwZgwDOxcBXrQcntwu+kYFpkiVkOaezL0WYEZ3anJc=", // DigiCert Global Root G3
        "Wd8xe/qfTwq3ylFNd3IpaqLHZbh2ZNCLluVzmeNkcpw=", // DigiCert Trusted Root G4
        "Ko8tivDrEjiY90yGasP6ZpBU4jwXvHqVvQI0GS3GNdA=", // Go Daddy Root CA - G2
        "gI1os/q0iEpflxrOfRBVDXqVoWN3Tz7Dav/7IT++THQ=", // Starfield Root CA - G2
        "58qRu/uxh4gFezqAcERupSkRYBlBAvfcw7mEjGPLnNU=", // COMODO ECC CA
        "grX4Ta9HpZx6tSHkmCrvpApTQGo67CYDnvprLg5yRME=", // COMODO RSA CA
        "ICGRfpgmOUXIWcQ/HXPLQTkFPEFPoDyjvH7ohhQpjzs=", // USERTrust ECC CA
        "x4QzPSC810K5/cMjb05Qm4k3Bw5zBn4lTdO/nEW/Td4=", // USERTrust RSA CA
        "Og7FTBgyooHcMGImf9k3QpAI4chBs3tKeasb3Uy6V/8=", // SSL.com Client RSA Root CA 2022
        "TDQyyYTGauKnVHQ5g8qJaeLxD0CGOE3Di4BgUyywvqc=", // SSL.com Client ECC Root CA 2022
    ];

    static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            // Google API 는 리디렉션하지 않는다. 다른 곳으로 끌려가지 않도록 차단
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = true,
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                RemoteCertificateValidationCallback = ValidateCertificate,
            },
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        // Google API 는 UA 에 "gzip" 이 있어야 압축 응답을 준다.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SMCalendar/1.0 (gzip)");
        return client;
    }

    internal static bool ValidateCertificate(object sender, X509Certificate? cert, X509Chain? chain, SslPolicyErrors errors)
    {
        // 1) 이름 불일치·만료·신뢰할 수 없는 체인 등 일반 검증 실패는 무조건 거부
        if (errors != SslPolicyErrors.None || chain == null) return false;
        // 2) 체인 안에 Google 루트(또는 그 교차 서명)가 있어야 통과
        foreach (var element in chain.ChainElements)
        {
            var spki = element.Certificate.PublicKey.ExportSubjectPublicKeyInfo();
            if (GoogleRootPins.Contains(Convert.ToBase64String(SHA256.HashData(spki)))) return true;
        }
        return false;
    }
}

internal sealed class AuthException(string message) : Exception(message);

internal sealed class ApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
