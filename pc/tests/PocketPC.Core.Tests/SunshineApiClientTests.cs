using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PocketPC.Core.Security;
using PocketPC.Core.Sunshine;

namespace PocketPC.Core.Tests;

/// <summary>
/// Checks our requests against a fake that mimics Sunshine's confighttp.cpp behaviour
/// (basic auth, /welcome redirect before first login, JSON content type, response shapes).
/// </summary>
public sealed class SunshineApiClientTests : IAsyncLifetime
{
    private readonly List<string> _requests = [];
    private WebApplication _app = null!;
    private Uri _base = null!;
    private string _fingerprint = null!;
    private SunshineLogin? _serverLogin = new("admin", "secret");
    private SunshineLogin? _clientLogin = new("admin", "secret");

    public async Task InitializeAsync()
    {
        var cert = ServerCertificate.Create("sunshine");
        cert = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(
            cert.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pkcs12), null);
        _fingerprint = Fingerprint.Of(cert);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, l => l.UseHttps(cert)));
        _app = builder.Build();

        _app.Use(async (ctx, next) =>
        {
            _requests.Add($"{ctx.Request.Method} {ctx.Request.Path}");
            Assert.False(ctx.Request.Headers.ContainsKey("Origin"));
            if (ctx.Request.Method == "POST" && ctx.Request.ContentType?.StartsWith("application/json") != true)
            {
                ctx.Response.StatusCode = 400;
                return;
            }

            if (ctx.Request.Path != "/api/password")
            {
                if (_serverLogin is null)
                {
                    ctx.Response.Redirect("/welcome");
                    return;
                }

                var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_serverLogin.Username}:{_serverLogin.Password}"));
                if (ctx.Request.Headers.Authorization != expected)
                {
                    ctx.Response.StatusCode = 401;
                    return;
                }
            }

            await next(ctx);
        });

        _app.MapGet("/api/clients/list", () => Results.Text(
            """{"named_certs":[{"name":"PocketPC A (abc123)","uuid":"u-1","enabled":true},{"name":"Old","uuid":"u-2"}],"status":true}""",
            "application/json"));
        _app.MapGet("/api/pin", () => Results.Text(
            """{"pairings":[{"id":"0123456789abcdef0123456789abcdef","name":"roth","address":"192.168.1.42"}]}""",
            "application/json"));
        _app.MapPost("/api/pin", async (HttpContext ctx) =>
        {
            var body = await JsonDocument.ParseAsync(ctx.Request.Body);
            var ok = body.RootElement.GetProperty("pin").GetString() == "1234" &&
                     body.RootElement.GetProperty("pairing_id").GetString() == "0123456789abcdef0123456789abcdef" &&
                     body.RootElement.GetProperty("name").GetString() == "PocketPC A (abc123)";
            return Results.Text(ok ? """{"status":true}""" : """{"status":false}""", "application/json");
        });
        _app.MapPost("/api/clients/update", () => Results.Text("""{"status":true}""", "application/json"));
        _app.MapPost("/api/clients/unpair", () => Results.Text("""{"status":"true"}""", "application/json"));
        _app.MapPost("/api/apps/close", () => Results.Text("""{"status":true}""", "application/json"));
        _app.MapPost("/api/password", async (HttpContext ctx) =>
        {
            var body = await JsonDocument.ParseAsync(ctx.Request.Body);
            _serverLogin = new SunshineLogin(
                body.RootElement.GetProperty("newUsername").GetString()!,
                body.RootElement.GetProperty("newPassword").GetString()!);
            return Results.Text("""{"status":true}""", "application/json");
        });

        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _base = new Uri(address.Replace("http://", "https://") + "/");
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    private SunshineApiClient Client(string? pin = null) => new(_base, () => _clientLogin, pin ?? _fingerprint);

    [Fact]
    public async Task Access_states()
    {
        using var client = Client();
        Assert.Equal(SunshineAccess.Ok, await client.CheckAccessAsync());

        _clientLogin = new("admin", "wrong");
        Assert.Equal(SunshineAccess.Unauthorized, await client.CheckAccessAsync());

        _serverLogin = null;
        Assert.Equal(SunshineAccess.NeedsInitialSetup, await client.CheckAccessAsync());
    }

    [Fact]
    public async Task Not_running_when_nothing_listens()
    {
        using var client = new SunshineApiClient(new Uri("https://127.0.0.1:1/"), () => null, null);
        Assert.Equal(SunshineAccess.NotRunning, await client.CheckAccessAsync());
    }

    [Fact]
    public async Task Refuses_unpinned_certificate()
    {
        using var client = Client(new string('0', 64));
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => client.ListClientsAsync());
    }

    [Fact]
    public async Task Reads_clients_and_pending_pairings()
    {
        using var client = Client();
        var clients = await client.ListClientsAsync();
        Assert.Equal([new SunshineClient("PocketPC A (abc123)", "u-1", true), new SunshineClient("Old", "u-2", true)], clients);

        var pending = await client.GetPendingPairingsAsync();
        Assert.Equal([new SunshinePendingPairing("0123456789abcdef0123456789abcdef", "roth", "192.168.1.42")], pending);
    }

    [Fact]
    public async Task Submits_pin_in_sunshine_format()
    {
        using var client = Client();
        Assert.True(await client.SubmitPinAsync("0123456789abcdef0123456789abcdef", "1234", "PocketPC A (abc123)"));
        Assert.False(await client.SubmitPinAsync("0123456789abcdef0123456789abcdef", "9999", "PocketPC A (abc123)"));
    }

    [Fact]
    public async Task Disable_unpair_close()
    {
        using var client = Client();
        Assert.True(await client.SetClientEnabledAsync("u-1", false));
        Assert.True(await client.UnpairAsync("u-1")); // "true" string tolerated
        await client.CloseRunningAppAsync();
        Assert.Contains("POST /api/clients/update", _requests);
        Assert.Contains("POST /api/clients/unpair", _requests);
        Assert.Contains("POST /api/apps/close", _requests);
    }

    [Fact]
    public async Task Creates_first_login_on_fresh_install()
    {
        _serverLogin = null;
        _clientLogin = null;
        using var client = Client();
        Assert.Equal(SunshineAccess.NeedsInitialSetup, await client.CheckAccessAsync());

        var login = SunshineLoginStore.Generate();
        Assert.True(await client.CreateInitialLoginAsync(login));
        _clientLogin = login;
        Assert.Equal(SunshineAccess.Ok, await client.CheckAccessAsync());
    }
}
