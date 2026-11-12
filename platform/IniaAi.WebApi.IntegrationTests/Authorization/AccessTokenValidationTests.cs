using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using IniaAi.Configuration.Security;
using IniaAi.Testing;
using IniaAi.WebApi.IntegrationTests.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IniaAi.WebApi.IntegrationTests.Authorization;

/// <summary>
/// The API accepts only access tokens its own token server signed, unaltered and unexpired: a token
/// changed after signing, unsigned, signed with a guessed key or a key it carries itself, past its
/// expiry, or a refresh token in its place, is refused like no token at all. Access tokens live 10
/// minutes here, and the token server's clock is a test clock.
/// </summary>
public class AccessTokenValidationTests
{
    private const string PROBE_PATH = "api/v1/users/me/tenants";
    private const int ACCESS_TOKEN_MINUTES = 10;

    private readonly SettableClock _clock = new(DateTimeOffset.UtcNow);
    private ClientBuilder? _api;

    public static TheoryData<string> Forgeries =>
    [
        nameof(PayloadChangedSignatureKept),
        nameof(UnsignedWithAlgNone),
        nameof(UnsignedWithAlgNoneCapitalised),
        nameof(SignedWithAnEmptyKey),
        nameof(SignedWithTheWordSecret),
        nameof(SignedByAKeyItCarriesInItsHeader),
    ];

    [Fact]
    public async Task TheTokenAsIssued_IsAccepted_AndItsClaimsAreRecorded()
    {
        (TestAccount account, string accessToken, string refreshToken) = await SignInAsync();
        await using (account)
        {
            using HttpResponseMessage response = await account.GetAsync(PROBE_PATH, accessToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string[] parts = accessToken.Split('.');
            JsonObject header = JsonNode.Parse(Base64UrlDecode(parts[0]))!.AsObject();
            JsonObject payload = JsonNode.Parse(Base64UrlDecode(parts[1]))!.AsObject();
            await RecordAsync(
                "claims.txt",
                $"header alg={header["alg"]}, typ={header["typ"]}, members: {string.Join(", ", header.Select(member => member.Key))}\npayload claim names: {string.Join(", ", payload.Select(claim => claim.Key))}\nrefresh token: {refreshToken.Split('.').Length} dot-separated parts, header {Base64UrlDecode(refreshToken.Split('.')[0])}");
        }
    }

    [Theory]
    [MemberData(nameof(Forgeries))]
    public async Task AForgedToken_IsRefused(string forgery)
    {
        (TestAccount account, string accessToken, _) = await SignInAsync();
        await using (account)
        {
            Assert.Equal(HttpStatusCode.OK, (await account.GetAsync(PROBE_PATH, accessToken)).StatusCode);
            string forged = forgery switch
            {
                nameof(PayloadChangedSignatureKept) => PayloadChangedSignatureKept(accessToken),
                nameof(UnsignedWithAlgNone) => UnsignedWithAlgNone(accessToken),
                nameof(UnsignedWithAlgNoneCapitalised) => UnsignedWithAlgNoneCapitalised(accessToken),
                nameof(SignedWithAnEmptyKey) => SignedWithAnEmptyKey(accessToken),
                nameof(SignedWithTheWordSecret) => SignedWithTheWordSecret(accessToken),
                nameof(SignedByAKeyItCarriesInItsHeader) => SignedByAKeyItCarriesInItsHeader(accessToken),
                _ => throw new ArgumentOutOfRangeException(nameof(forgery)),
            };

            using HttpResponseMessage response = await account.GetAsync(PROBE_PATH, forged);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task ATokenPastItsExpiry_IsRefused()
    {
        (TestAccount account, string accessToken, _) = await SignInAsync();
        await using (account)
        {
            Assert.Equal(HttpStatusCode.OK, (await account.GetAsync(PROBE_PATH, accessToken)).StatusCode);

            _clock.Now += TimeSpan.FromMinutes(ACCESS_TOKEN_MINUTES) + TimeSpan.FromMinutes(6);
            using HttpResponseMessage response = await account.GetAsync(PROBE_PATH, accessToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task ARefreshTokenSentAsTheBearer_IsRefused()
    {
        (TestAccount account, string accessToken, string refreshToken) = await SignInAsync();
        await using (account)
        {
            Assert.Equal(HttpStatusCode.OK, (await account.GetAsync(PROBE_PATH, accessToken)).StatusCode);

            using HttpResponseMessage response = await account.GetAsync(PROBE_PATH, refreshToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task ATokenSignedWithTheDeploymentsKey_ButNamingAnotherIssuer_IsRefused()
    {
        (TestAccount account, string accessToken, _) = await SignInAsync();
        await using (account)
        {
            byte[] key = Encoding.UTF8.GetBytes(_api!.CurrentScope.ServiceProvider.GetRequiredService<IOptions<JwtSettings>>().Value.SecretKey);
            JsonObject payload = JsonNode.Parse(Base64UrlDecode(accessToken.Split('.')[1]))!.AsObject();
            string resigned = SignedWithHmac(accessToken.Split('.')[1], key);
            payload["iss"] = "https://another-deployment.example/";
            string otherIssuer = SignedWithHmac(Base64UrlEncode(Encoding.UTF8.GetBytes(payload.ToJsonString())), key);

            using HttpResponseMessage control = await account.GetAsync(PROBE_PATH, resigned);
            using HttpResponseMessage response = await account.GetAsync(PROBE_PATH, otherIssuer);

            Assert.Equal(HttpStatusCode.OK, control.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    private async Task<(TestAccount Account, string AccessToken, string RefreshToken)> SignInAsync()
    {
        _api = new ClientBuilder(services =>
        {
            _ = services.AddSingleton<TimeProvider>(_clock);
            _ = services.Configure<JwtSettings>(jwt => jwt.TokenValidityInMinutes = ACCESS_TOKEN_MINUTES);
        });
        TestAccount account = await TestAccount.CreateAsync(_api);
        (string accessToken, string refreshToken) = await account.SignInAsync();
        return (account, accessToken, refreshToken);
    }

    private static string PayloadChangedSignatureKept(string token)
    {
        string[] parts = token.Split('.');
        char[] payload = parts[1].ToCharArray();
        int middle = payload.Length / 2;
        payload[middle] = payload[middle] == 'A' ? 'B' : 'A';
        return $"{parts[0]}.{new string(payload)}.{parts[2]}";
    }

    private static string UnsignedWithAlgNone(string token)
    {
        return Unsigned(token, "none");
    }

    private static string UnsignedWithAlgNoneCapitalised(string token)
    {
        return Unsigned(token, "None");
    }

    private static string Unsigned(string token, string algorithm)
    {
        string header = Base64UrlEncode(Encoding.UTF8.GetBytes($"{{\"alg\":\"{algorithm}\",\"typ\":\"at+jwt\"}}"));
        return $"{header}.{token.Split('.')[1]}.";
    }

    private static string SignedWithAnEmptyKey(string token)
    {
        return SignedWithHmac(token.Split('.')[1], []);
    }

    private static string SignedWithTheWordSecret(string token)
    {
        return SignedWithHmac(token.Split('.')[1], Encoding.UTF8.GetBytes("secret"));
    }

    private static string SignedWithHmac(string payload, byte[] key)
    {
        string header = Base64UrlEncode(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"at+jwt\"}"));
        string signed = $"{header}.{payload}";
        return $"{signed}.{Base64UrlEncode(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(signed)))}";
    }

    private static string SignedByAKeyItCarriesInItsHeader(string token)
    {
        using var key = RSA.Create(2048);
        RSAParameters publicPart = key.ExportParameters(includePrivateParameters: false);
        var header = new JsonObject
        {
            ["alg"] = "RS256",
            ["typ"] = "at+jwt",
            ["jwk"] = new JsonObject
            {
                ["kty"] = "RSA",
                ["n"] = Base64UrlEncode(publicPart.Modulus!),
                ["e"] = Base64UrlEncode(publicPart.Exponent!),
            },
        };
        string signed = $"{Base64UrlEncode(Encoding.UTF8.GetBytes(header.ToJsonString()))}.{token.Split('.')[1]}";
        byte[] signature = key.SignData(Encoding.ASCII.GetBytes(signed), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signed}.{Base64UrlEncode(signature)}";
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string Base64UrlDecode(string text)
    {
        string padded = text.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - (padded.Length % 4)) % 4);
        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }

    private static async Task RecordAsync(string file, string text)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(text);
        await TestEvidence.WriteAsync($"access-token-{file}", text + "\n");
    }
}
