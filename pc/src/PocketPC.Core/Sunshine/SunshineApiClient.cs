using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PocketPC.Core.Security;

namespace PocketPC.Core.Sunshine;

/// <summary>
/// Talks to Sunshine's admin API on this PC (loopback only) using HTTP basic auth.
/// Requests carry no Origin/Referer header, which Sunshine treats as a non-browser
/// client that needs no CSRF token.
/// </summary>
public sealed class SunshineApiClient : ISunshineApi, IDisposable
{
    private readonly HttpClient _http;
    private readonly Func<SunshineLogin?> _login;

    /// <param name="webUiPort">Sunshine's web UI port (base port + 1, normally 47990).</param>
    /// <param name="login">Returns the stored admin login (decrypted on demand).</param>
    /// <param name="pinnedCertificateFingerprint">
    /// Sunshine's certificate fingerprint if we could read it. When null we still only ever
    /// connect to 127.0.0.1, so a remote attacker can't impersonate Sunshine.
    /// </param>
    public SunshineApiClient(int webUiPort, Func<SunshineLogin?> login, string? pinnedCertificateFingerprint)
        : this(new Uri($"https://127.0.0.1:{webUiPort}/"), login, pinnedCertificateFingerprint)
    {
    }

    internal SunshineApiClient(Uri baseAddress, Func<SunshineLogin?> login, string? pinnedCertificateFingerprint)
    {
        _login = login;
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                    certificate is not null &&
                    (pinnedCertificateFingerprint is null ||
                     Fingerprint.Matches(Fingerprint.Of(certificate.GetRawCertData()), pinnedCertificateFingerprint)),
            },
        };

        // Pairing requests are held open by Sunshine until the Moonlight handshake finishes.
        _http = new HttpClient(handler) { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task<SunshineAccess> CheckAccessAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Get, "api/clients/list", null, ct);
            return response.StatusCode switch
            {
                HttpStatusCode.OK => SunshineAccess.Ok,
                >= HttpStatusCode.MultipleChoices and < HttpStatusCode.BadRequest => SunshineAccess.NeedsInitialSetup,
                _ => SunshineAccess.Unauthorized,
            };
        }
        catch (Exception e) when (IsConnectionFailure(e))
        {
            return SunshineAccess.NotRunning;
        }
    }

    public async Task<IReadOnlyList<SunshinePendingPairing>?> GetPendingPairingsAsync(CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/pin", null, ct);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
        {
            return null;
        }

        await EnsureSuccessAsync(response, ct);
        var body = await response.Content.ReadFromJsonAsync<PendingPairingsResponse>(ct);
        return body?.Pairings?.Select(p => new SunshinePendingPairing(p.Id ?? "", p.Name ?? "", p.Address ?? "")).ToList();
    }

    public async Task<bool> SubmitPinAsync(string? pairingId, string pin, string clientName, CancellationToken ct = default)
    {
        object payload = pairingId is null
            ? new { pin, name = clientName }
            : new { pairing_id = pairingId, pin, name = clientName };
        using var response = await SendAsync(HttpMethod.Post, "api/pin", payload, ct);
        await EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<StatusResponse>(ct))?.Status == true;
    }

    public async Task<IReadOnlyList<SunshineClient>> ListClientsAsync(CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/clients/list", null, ct);
        await EnsureSuccessAsync(response, ct);
        var body = await response.Content.ReadFromJsonAsync<ClientListResponse>(ct);
        return body?.NamedCerts?
            .Where(c => c.Uuid is not null)
            .Select(c => new SunshineClient(c.Name ?? "", c.Uuid!, c.Enabled ?? true))
            .ToList() ?? [];
    }

    public async Task<bool?> SetClientEnabledAsync(string uuid, bool enabled, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/clients/update", new { uuid, enabled }, ct);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
        {
            return null;
        }

        await EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<StatusResponse>(ct))?.Status == true;
    }

    public async Task<bool> UnpairAsync(string uuid, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/clients/unpair", new { uuid }, ct);
        await EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<StatusResponse>(ct))?.Status == true;
    }

    public async Task CloseRunningAppAsync(CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/apps/close", new { }, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<bool> CreateInitialLoginAsync(SunshineLogin login, CancellationToken ct = default)
    {
        var payload = new
        {
            currentUsername = "",
            currentPassword = "",
            newUsername = login.Username,
            newPassword = login.Password,
            confirmNewPassword = login.Password,
        };
        using var response = await SendAsync(HttpMethod.Post, "api/password", payload, ct, authenticate: false);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        return (await response.Content.ReadFromJsonAsync<StatusResponse>(ct))?.Status == true;
    }

    public void Dispose() => _http.Dispose();

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, object? jsonBody, CancellationToken ct, bool authenticate = true)
    {
        using var request = new HttpRequestMessage(method, path);
        if (authenticate && _login() is { } login)
        {
            var raw = Encoding.UTF8.GetBytes($"{login.Username}:{login.Password}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
        }

        if (jsonBody is not null)
        {
            request.Content = JsonContent.Create(jsonBody, mediaType: new MediaTypeHeaderValue("application/json"));
        }

        return await _http.SendAsync(request, ct);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await response.Content.ReadAsStringAsync(ct);
        throw new SunshineApiException(response.StatusCode, detail);
    }

    private static bool IsConnectionFailure(Exception e) =>
        e is HttpRequestException { InnerException: SocketException } or HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError }
            or TaskCanceledException { InnerException: TimeoutException };

    private sealed class StatusResponse
    {
        [JsonPropertyName("status")]
        [JsonConverter(typeof(LenientBoolConverter))]
        public bool Status { get; set; }
    }

    private sealed class PendingPairingsResponse
    {
        [JsonPropertyName("pairings")]
        public List<PendingPairingDto>? Pairings { get; set; }
    }

    private sealed class PendingPairingDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("address")]
        public string? Address { get; set; }
    }

    private sealed class ClientListResponse
    {
        [JsonPropertyName("named_certs")]
        public List<ClientDto>? NamedCerts { get; set; }
    }

    private sealed class ClientDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("uuid")]
        public string? Uuid { get; set; }

        [JsonPropertyName("enabled")]
        [JsonConverter(typeof(LenientNullableBoolConverter))]
        public bool? Enabled { get; set; }
    }

    /// <summary>Older Sunshine builds send "true"/"false" strings instead of JSON booleans.</summary>
    private sealed class LenientBoolConverter : JsonConverter<bool>
    {
        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType switch
            {
                JsonTokenType.True => true,
                JsonTokenType.False => false,
                JsonTokenType.String => bool.TryParse(reader.GetString(), out var b) && b,
                JsonTokenType.Number => reader.GetInt32() != 0,
                _ => false,
            };

        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) =>
            writer.WriteBooleanValue(value);
    }

    private sealed class LenientNullableBoolConverter : JsonConverter<bool?>
    {
        public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Null
                ? null
                : new LenientBoolConverter().Read(ref reader, typeof(bool), options);

        public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteBooleanValue(value.Value);
            }
        }
    }
}

public sealed class SunshineApiException(HttpStatusCode status, string detail)
    : Exception($"Sunshine returned {(int)status} {status}: {Truncate(detail)}")
{
    public HttpStatusCode Status { get; } = status;

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300] + "…";
}
