package com.pocketpc.core;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Collections;

import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.condition.EnabledIfEnvironmentVariable;

/**
 * Pairs this Java client with the real C# companion API (pc/tools/PocketPC.TestHost), proving
 * both implementations agree on TLS, proofs and JSON. CI starts the host and sets
 * POCKETPC_TESTHOST_URI_FILE; the host re-opens a pairing window after each successful pairing.
 */
@EnabledIfEnvironmentVariable(named = "POCKETPC_TESTHOST_URI_FILE", matches = ".+")
class PairingInteropTest {
    private PairingUri uri;

    @BeforeEach
    void readPairingLink() throws IOException {
        Path file = Path.of(System.getenv("POCKETPC_TESTHOST_URI_FILE"));
        uri = PairingUri.parse(new String(Files.readAllBytes(file), StandardCharsets.UTF_8).trim());
        assertNotNull(uri, "test host wrote an invalid pairing link");
    }

    @Test
    void qrPairingThenStreamingPairingThenRevoke() throws Exception {
        TestIdentity phone = new TestIdentity("Interop QR phone");
        CompanionClient client = CompanionClient.connectFirstReachable(phone, uri.hosts, uri.port, uri.serverFingerprint);

        CompanionClient.Hello hello = client.hello();
        assertEquals(uri.pcId, hello.pcId);
        assertTrue(hello.pairingOpen);

        CompanionClient.PairResult result = client.pair(uri.code, "Redmi Note 14", "24117RN76E");
        assertEquals(uri.pcId, result.pcId);
        assertEquals(uri.serverFingerprint, result.serverFingerprint);
        assertTrue(result.sunshineInstalled);

        CompanionClient.Status status = client.status();
        assertEquals(result.deviceId, status.deviceId);
        assertEquals("Redmi Note 14", status.deviceName);

        // The test host simulates a Sunshine that accepts the PIN.
        assertNull(client.pairSunshine("4821"));
        assertEquals("invalid_pin", client.pairSunshine("12a4"));

        client.unpair();
        CompanionException revoked = assertThrows(CompanionException.class, client::status);
        assertTrue(revoked.isNotPaired());
    }

    @Test
    void typedCodePairingLearnsAndVerifiesTheCertificate() throws Exception {
        TestIdentity phone = new TestIdentity("Interop typed phone");
        CompanionClient client = new CompanionClient(phone, uri.hosts.get(0), uri.port, null);

        // A wrong code is refused (and counts toward the lockout)...
        CompanionException wrong = assertThrows(CompanionException.class,
                () -> client.pair("0000-0000-0000", "Typed", null));
        assertEquals("wrong_code", wrong.error);

        // ...the right one pairs and the PC's proof pins the certificate we saw.
        CompanionClient.PairResult result = client.pair(PairingCode.format(uri.code), "Typed", null);
        assertEquals(uri.serverFingerprint, result.serverFingerprint);
        assertEquals(uri.serverFingerprint, client.serverFingerprint());
        client.unpair();
    }

    @Test
    void refusesAPcWithADifferentCertificate() throws Exception {
        TestIdentity phone = new TestIdentity("Interop wary phone");
        CompanionClient client = new CompanionClient(phone, uri.hosts.get(0), uri.port, "f".repeat(64));
        assertThrows(IOException.class, client::hello);
        assertThrows(IOException.class, () -> CompanionClient.connectFirstReachable(
                phone, Collections.singletonList(uri.hosts.get(0)), uri.port, "f".repeat(64)));
    }

    @Test
    void unknownPhoneCannotUseTheDeviceApi() throws Exception {
        CompanionClient stranger = new CompanionClient(new TestIdentity("Stranger"), uri.hosts.get(0), uri.port, uri.serverFingerprint);
        CompanionException e = assertThrows(CompanionException.class, stranger::status);
        assertTrue(e.isNotPaired());
        assertEquals("not_paired", stranger.pairSunshine("1234"));
    }
}
