using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using PocketPC.Core.Devices;
using PocketPC.Core.Pairing;
using PocketPC.Core.Security;
using PocketPC.Core.Server;
using PocketPC.Core.Sunshine;
using PocketPC.TestHost;

namespace PocketPC.Core.Tests;

public sealed class CompanionServerTests : IAsyncLifetime
{
    private readonly TempDir _dir = new();
    private readonly SimulatedSunshine _sunshine = new();
    private readonly PairingWindow _pairing = new();
    private X509Certificate2 _serverCert = null!;
    private DeviceStore _devices = null!;
    private DeviceRevoker _revoker = null!;
    private CompanionServer _server = null!;

    public async Task InitializeAsync()
    {
        _serverCert = ServerCertificate.LoadOrCreate(_dir.Paths.ServerCertificate, new EphemeralSecretProtector(), "TEST-PC");
        _devices = new DeviceStore(_dir.Paths.Devices);
        _revoker = new DeviceRevoker(_devices, _sunshine);
        _server = new CompanionServer(
            new CompanionServerOptions
            {
                Certificate = _serverCert,
                PcId = "pc-1",
                PcName = () => "Test PC",
                Port = 0,
                Sunshine = () => new SunshineInfo(true, 47989, "sunshine-fp"),
            },
            _devices,
            _pairing,
            new SunshinePairing(_sunshine, _devices, waitForRequest: TimeSpan.FromSeconds(1)),
            _revoker);
        await _server.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        _serverCert.Dispose();
        _dir.Dispose();
    }

    private HttpClient Phone(X509Certificate2? cert) => TestCertificates.PhoneClient(_server.BoundPort, _server.ServerFingerprint, cert);

    private async Task<(HttpClient Client, PairResponse Response, X509Certificate2 Cert)> PairNewPhone(string name = "Pixel")
    {
        var cert = TestCertificates.CreateClient(name);
        var client = Phone(cert);
        var session = _pairing.Open();
        var proof = PairingProof.ForClient(session.Code, Fingerprint.Of(cert), _server.ServerFingerprint);
        var response = await client.PostAsJsonAsync("v1/pair", new PairRequest(name, "Model X", proof));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (client, await response.ReadJson<PairResponse>(), cert);
    }

    [Fact]
    public async Task Hello_works_before_pairing()
    {
        using var client = Phone(TestCertificates.CreateClient());
        var hello = await client.GetFromJsonAsync<HelloResponse>("v1/hello");
        Assert.Equal(new HelloResponse(1, "pc-1", "Test PC", false), hello);
    }

    [Fact]
    public async Task Connection_without_client_certificate_is_refused()
    {
        using var client = Phone(null);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => client.GetAsync("v1/hello"));
    }

    [Fact]
    public async Task Pairing_registers_device_and_proves_server_identity()
    {
        var (client, response, cert) = await PairNewPhone();
        using var _ = client;

        var session = Assert.Single(_devices.All);
        Assert.Equal(response.DeviceId, session.Id);
        Assert.Equal(Fingerprint.Of(cert), session.CertificateFingerprint);
        Assert.Equal("pc-1", response.PcId);
        Assert.Equal("sunshine-fp", response.Sunshine.CertificateFingerprint);
        Assert.Null(_pairing.Current); // window closes after one pairing

        var status = await client.GetFromJsonAsync<StatusResponse>("v1/status");
        Assert.Equal(response.DeviceId, status!.DeviceId);
        Assert.Equal("Pixel", status.DeviceName);
    }

    [Fact]
    public async Task Server_proof_matches_what_the_phone_expects()
    {
        var cert = TestCertificates.CreateClient();
        using var client = Phone(cert);
        var session = _pairing.Open();
        var clientFp = Fingerprint.Of(cert);
        var response = await client.PostAsJsonAsync("v1/pair",
            new PairRequest("Phone", null, PairingProof.ForClient(session.Code, clientFp, _server.ServerFingerprint)));

        var body = await response.ReadJson<PairResponse>();
        Assert.Equal(PairingProof.ForServer(session.Code, _server.ServerFingerprint, clientFp), body.ServerProof);
    }

    [Fact]
    public async Task Wrong_code_is_rejected_then_locked_out()
    {
        using var client = Phone(TestCertificates.CreateClient());
        _pairing.Open();
        for (var i = 1; i < PairingWindow.MaxFailedAttempts; i++)
        {
            var wrong = await client.PostAsJsonAsync("v1/pair", new PairRequest("Phone", null, new string('0', 64)));
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        var locked = await client.PostAsJsonAsync("v1/pair", new PairRequest("Phone", null, new string('0', 64)));
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.Empty(_devices.All);
    }

    [Fact]
    public async Task Pairing_is_impossible_unless_the_window_is_open()
    {
        using var client = Phone(TestCertificates.CreateClient());
        var response = await client.PostAsJsonAsync("v1/pair", new PairRequest("Phone", null, new string('0', 64)));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_certificate_cannot_use_device_api()
    {
        using var client = Phone(TestCertificates.CreateClient());
        var response = await client.GetAsync("v1/status");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var pin = await client.PostAsJsonAsync("v1/sunshine/pair", new SunshinePairRequest("1234"));
        Assert.Equal(HttpStatusCode.Forbidden, pin.StatusCode);
        Assert.Empty(_sunshine.SubmittedPins);
    }

    [Fact]
    public async Task Revoking_blocks_the_device_immediately_on_the_same_connection()
    {
        var (client, response, _) = await PairNewPhone();
        using var _ = client;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("v1/status")).StatusCode);

        var result = await _revoker.RevokeAsync(response.DeviceId);

        Assert.Equal(RevokeOutcome.RevokedNoSunshineClient, result.Outcome);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("v1/status")).StatusCode);
    }

    [Fact]
    public async Task One_tap_sunshine_pairing_enters_the_pin_for_the_phone()
    {
        var (client, response, _) = await PairNewPhone("Redmi");
        using var _ = client;
        _sunshine.AddPendingRequest("127.0.0.1");

        var result = await client.PostAsJsonAsync("v1/sunshine/pair", new SunshinePairRequest("4821"));

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.True((await result.ReadJson<SunshinePairResponse>()).Paired);
        Assert.Equal(["4821"], _sunshine.SubmittedPins);
        var sunshineClient = Assert.Single(_sunshine.Clients);
        Assert.StartsWith("PocketPC Redmi (", sunshineClient.Name);
        Assert.Equal(sunshineClient.Uuid, _devices.FindById(response.DeviceId)!.SunshineClientUuid);
    }

    [Fact]
    public async Task Sunshine_pairing_ignores_requests_from_other_addresses()
    {
        var (client, _, _) = await PairNewPhone();
        using var _ = client;
        _sunshine.AddPendingRequest("192.168.1.99");

        var result = await client.PostAsJsonAsync("v1/sunshine/pair", new SunshinePairRequest("4821"));

        Assert.Equal(HttpStatusCode.BadGateway, result.StatusCode);
        Assert.Equal("no_pairing_request", (await result.ReadJson<SunshinePairResponse>()).Error);
        Assert.Empty(_sunshine.SubmittedPins);
    }

    [Fact]
    public async Task Invalid_pin_is_rejected()
    {
        var (client, _, _) = await PairNewPhone();
        using var _ = client;
        var result = await client.PostAsJsonAsync("v1/sunshine/pair", new SunshinePairRequest("12a4"));
        Assert.Equal("invalid_pin", (await result.ReadJson<SunshinePairResponse>()).Error);
    }

    [Fact]
    public async Task Revoking_removes_streaming_access_in_sunshine_too()
    {
        var (client, response, _) = await PairNewPhone();
        using var _ = client;
        _sunshine.AddPendingRequest("127.0.0.1");
        await client.PostAsJsonAsync("v1/sunshine/pair", new SunshinePairRequest("1111"));
        Assert.Single(_sunshine.Clients);

        var result = await _revoker.RevokeAsync(response.DeviceId);

        Assert.Equal(RevokeOutcome.Revoked, result.Outcome);
        Assert.Empty(_sunshine.Clients);
        Assert.Equal(0, _sunshine.CloseRunningAppCalls);
    }

    [Fact]
    public async Task Revoking_on_old_sunshine_also_stops_the_stream()
    {
        _sunshine.SupportsClientUpdate = false;
        var (client, response, _) = await PairNewPhone();
        using var _ = client;
        _sunshine.AddPendingRequest("127.0.0.1");
        await client.PostAsJsonAsync("v1/sunshine/pair", new SunshinePairRequest("1111"));

        await _revoker.RevokeAsync(response.DeviceId);

        Assert.Empty(_sunshine.Clients);
        Assert.Equal(1, _sunshine.CloseRunningAppCalls);
    }

    [Fact]
    public async Task Legacy_sunshine_pairing_without_request_list()
    {
        _sunshine.SupportsPendingList = false;
        _sunshine.AutoCreatePendingRequests = true;
        var (client, _, _) = await PairNewPhone();
        using var _ = client;

        var result = await client.PostAsJsonAsync("v1/sunshine/pair", new SunshinePairRequest("2222"));

        Assert.True((await result.ReadJson<SunshinePairResponse>()).Paired);
        Assert.Single(_sunshine.Clients);
    }

    [Fact]
    public async Task Phone_can_unpair_itself()
    {
        var (client, _, _) = await PairNewPhone();
        using var _ = client;

        var result = await client.PostAsync("v1/unpair", null);

        Assert.Equal(HttpStatusCode.NoContent, result.StatusCode);
        Assert.Empty(_devices.All);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("v1/status")).StatusCode);
    }

    [Fact]
    public async Task Phone_rejects_impostor_server()
    {
        // A phone that pinned some other fingerprint (e.g. from the QR) refuses to talk to this server.
        using var client = TestCertificates.PhoneClient(_server.BoundPort, new string('f', 64), TestCertificates.CreateClient());
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => client.GetAsync("v1/hello"));
    }
}
