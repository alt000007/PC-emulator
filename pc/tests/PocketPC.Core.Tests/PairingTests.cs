using Microsoft.Extensions.Time.Testing;
using PocketPC.Core.Pairing;

namespace PocketPC.Core.Tests;

public class PairingCodeTests
{
    [Fact]
    public void Generated_codes_are_valid_and_random()
    {
        var a = PairingCode.Generate();
        var b = PairingCode.Generate();
        Assert.Equal(PairingCode.Length, a.Length);
        Assert.Equal(a, PairingCode.Normalize(a));
        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData("7K3M-Q9XR-T2HB", "7K3MQ9XRT2HB")]
    [InlineData("7k3m q9xr t2hb", "7K3MQ9XRT2HB")]
    [InlineData("OIL0-0000-0000", "011000000000")]
    public void Normalize_accepts_human_input(string input, string expected) =>
        Assert.Equal(expected, PairingCode.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("7K3M-Q9XR")]
    [InlineData("7K3M-Q9XR-T2HBX")]
    [InlineData("7K3M-Q9XR-T2H!")]
    [InlineData("UUUU-UUUU-UUUU")]
    public void Normalize_rejects_invalid(string? input) => Assert.Null(PairingCode.Normalize(input));

    [Fact]
    public void Format_groups_in_fours() => Assert.Equal("7K3M-Q9XR-T2HB", PairingCode.Format("7K3MQ9XRT2HB"));
}

public class PairingProofTests
{
    // Same vector is asserted by the Android tests (PairingProofTest.java): both sides must agree byte for byte.
    private const string Code = "7K3MQ9XRT2HB";
    private static readonly string ClientFp = new('a', 64);
    private static readonly string ServerFp = new('b', 64);

    [Fact]
    public void Known_answer_vectors()
    {
        Assert.Equal("9ef5fab108e671099a9b7cdbc93375f0d05f3ad519ed875af6c626097ecf5c46",
            PairingProof.ForClient(Code, ClientFp, ServerFp));
        Assert.Equal("0541a0436931476f402879c3f08d6652b3b6fdaac4c734e8bcddad6d521b4765",
            PairingProof.ForServer(Code, ServerFp, ClientFp));
    }

    [Fact]
    public void Proofs_are_bound_to_both_certificates()
    {
        var proof = PairingProof.ForClient(Code, ClientFp, ServerFp);
        Assert.NotEqual(proof, PairingProof.ForClient(Code, ClientFp, new string('c', 64)));
        Assert.NotEqual(proof, PairingProof.ForClient(Code, new string('c', 64), ServerFp));
        Assert.NotEqual(proof, PairingProof.ForServer(Code, ClientFp, ServerFp));
    }

    [Fact]
    public void Verify_is_case_insensitive_and_rejects_garbage()
    {
        var proof = PairingProof.ForClient(Code, ClientFp, ServerFp);
        Assert.True(PairingProof.Verify(proof, proof.ToUpperInvariant()));
        Assert.False(PairingProof.Verify(proof, null));
        Assert.False(PairingProof.Verify(proof, "00"));
        Assert.False(PairingProof.Verify(proof, new string('0', proof.Length)));
    }
}

public class PairingWindowTests
{
    private const string ClientFp = "client";
    private const string ServerFp = "server";

    [Fact]
    public void Accepts_correct_proof_once()
    {
        var window = new PairingWindow();
        var session = window.Open();
        var proof = PairingProof.ForClient(session.Code, ClientFp, ServerFp);

        Assert.Equal(PairingAttemptResult.Accepted, window.TryAccept(ClientFp, ServerFp, proof, out var serverProof));
        Assert.Equal(PairingProof.ForServer(session.Code, ServerFp, ClientFp), serverProof);

        // Single use: the same code can't pair a second phone.
        Assert.Equal(PairingAttemptResult.NotOpen, window.TryAccept(ClientFp, ServerFp, proof, out _));
        Assert.Null(window.Current);
    }

    [Fact]
    public void Rejects_when_closed() =>
        Assert.Equal(PairingAttemptResult.NotOpen, new PairingWindow().TryAccept(ClientFp, ServerFp, "x", out _));

    [Fact]
    public void Expires()
    {
        var time = new FakeTimeProvider();
        var window = new PairingWindow(time);
        var session = window.Open(TimeSpan.FromMinutes(5));
        time.Advance(TimeSpan.FromMinutes(5));

        var proof = PairingProof.ForClient(session.Code, ClientFp, ServerFp);
        Assert.Null(window.Current);
        Assert.Equal(PairingAttemptResult.Expired, window.TryAccept(ClientFp, ServerFp, proof, out _));
    }

    [Fact]
    public void Locks_out_after_too_many_wrong_codes()
    {
        var window = new PairingWindow();
        var session = window.Open();
        for (var i = 1; i < PairingWindow.MaxFailedAttempts; i++)
        {
            Assert.Equal(PairingAttemptResult.WrongCode, window.TryAccept(ClientFp, ServerFp, "bad", out _));
        }

        Assert.Equal(PairingAttemptResult.LockedOut, window.TryAccept(ClientFp, ServerFp, "bad", out _));

        // Even the right code no longer works: the user must open a new window.
        var proof = PairingProof.ForClient(session.Code, ClientFp, ServerFp);
        Assert.Equal(PairingAttemptResult.NotOpen, window.TryAccept(ClientFp, ServerFp, proof, out _));
    }

    [Fact]
    public void Proof_for_other_server_certificate_is_rejected()
    {
        var window = new PairingWindow();
        var session = window.Open();
        var proofForAttacker = PairingProof.ForClient(session.Code, ClientFp, "attacker");
        Assert.Equal(PairingAttemptResult.WrongCode, window.TryAccept(ClientFp, ServerFp, proofForAttacker, out _));
    }
}

public class PairingUriTests
{
    [Fact]
    public void Round_trips()
    {
        var uri = new PairingUri("0123456789abcdef", "Gaming PC & Co", ["192.168.1.20", "100.101.102.103"], 47880, new string('a', 64), "7K3MQ9XRT2HB");
        var parsed = PairingUri.TryParse(uri.ToString());

        Assert.NotNull(parsed);
        Assert.Equal(uri.PcId, parsed.PcId);
        Assert.Equal(uri.PcName, parsed.PcName);
        Assert.Equal(uri.Hosts, parsed.Hosts);
        Assert.Equal(uri.Port, parsed.Port);
        Assert.Equal(uri.ServerFingerprint, parsed.ServerFingerprint);
        Assert.Equal(uri.Code, parsed.Code);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("pocketpc://pair?v=2&id=a&n=b&h=c&p=1&fp=d&c=7K3MQ9XRT2HB")]
    [InlineData("pocketpc://pair?v=1&id=a&n=b&h=c&p=x&fp=d&c=7K3MQ9XRT2HB")]
    [InlineData("pocketpc://pair?v=1&id=a&n=b&h=c&p=1&fp=d&c=short")]
    public void Rejects_invalid(string text) => Assert.Null(PairingUri.TryParse(text));
}
