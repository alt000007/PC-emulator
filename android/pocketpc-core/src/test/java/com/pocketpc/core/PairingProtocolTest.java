package com.pocketpc.core;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;

import java.util.Arrays;

import org.json.JSONObject;
import org.junit.jupiter.api.Test;

class PairingProtocolTest {
    private static final String CODE = "7K3MQ9XRT2HB";
    private static final String CLIENT_FP = "a".repeat(64);
    private static final String SERVER_FP = "b".repeat(64);

    @Test
    void proofMatchesThePcImplementation() {
        // Same vectors as pc/tests/PocketPC.Core.Tests/PairingTests.cs.
        assertEquals("9ef5fab108e671099a9b7cdbc93375f0d05f3ad519ed875af6c626097ecf5c46",
                PairingProof.forClient(CODE, CLIENT_FP, SERVER_FP));
        assertEquals("0541a0436931476f402879c3f08d6652b3b6fdaac4c734e8bcddad6d521b4765",
                PairingProof.forServer(CODE, SERVER_FP, CLIENT_FP));
    }

    @Test
    void proofDependsOnBothCertificates() {
        String proof = PairingProof.forClient(CODE, CLIENT_FP, SERVER_FP);
        assertNotEquals(proof, PairingProof.forClient(CODE, CLIENT_FP, "c".repeat(64)));
        assertNotEquals(proof, PairingProof.forClient(CODE, "c".repeat(64), SERVER_FP));
    }

    @Test
    void normalizesTypedCodes() {
        assertEquals(CODE, PairingCode.normalize("7k3m-q9xr t2hb"));
        assertEquals("011000000000", PairingCode.normalize("OIL0-0000-0000"));
        assertNull(PairingCode.normalize("7K3M-Q9XR"));
        assertNull(PairingCode.normalize("UUUU-UUUU-UUUU"));
        assertNull(PairingCode.normalize(null));
        assertEquals("7K3M-Q9XR-T2HB", PairingCode.format(CODE));
    }

    @Test
    void parsesQrFromThePc() {
        // Exactly what pc/src/PocketPC.Core/Pairing/PairingUri.cs produces.
        String fp = "7a191dbfaa9da18cf344ea1b3330660397dc07964947e5dc53fd83f87ec049df";
        PairingUri uri = PairingUri.parse("pocketpc://pair?v=1&id=0d0f8e31e7e52d9b&n=Gaming%20PC%20%26%20Co"
                + "&h=192.168.1.20,100.101.102.103&p=47880&fp=" + fp + "&c=092Q98S9GP8F");
        assertNotNull(uri);
        assertEquals("0d0f8e31e7e52d9b", uri.pcId);
        assertEquals("Gaming PC & Co", uri.pcName);
        assertEquals(Arrays.asList("192.168.1.20", "100.101.102.103"), uri.hosts);
        assertEquals(47880, uri.port);
        assertEquals(fp, uri.serverFingerprint);
        assertEquals("092Q98S9GP8F", uri.code);
    }

    @Test
    void rejectsOtherQrCodes() {
        assertNull(PairingUri.parse("https://example.com"));
        assertNull(PairingUri.parse("pocketpc://pair?v=2&id=a&n=b&h=c&p=1&fp=" + "a".repeat(64) + "&c=7K3MQ9XRT2HB"));
        assertNull(PairingUri.parse("pocketpc://pair?v=1&id=a&n=b&h=c&p=x&fp=" + "a".repeat(64) + "&c=7K3MQ9XRT2HB"));
        assertNull(PairingUri.parse("pocketpc://pair?v=1&id=a&n=b&h=c&p=1&fp=short&c=7K3MQ9XRT2HB"));
        assertNull(PairingUri.parse("pocketpc://pair?v=1&id=a&n=b&h=c&p=1&fp=" + "a".repeat(64) + "&c=nope"));
    }

    @Test
    void pairedPcRoundTripsThroughJson() throws Exception {
        PairedPc pc = new PairedPc("id", "PC", Arrays.asList("10.0.0.2", "100.64.0.9"), 47880, SERVER_FP, "dev", 47989, null, 42L);
        PairedPc back = PairedPc.fromJson(new JSONObject(pc.toJson().toString()));
        assertEquals(pc.hosts, back.hosts);
        assertEquals(pc.serverFingerprint, back.serverFingerprint);
        assertNull(back.moonlightUuid);
        assertEquals("uuid", back.withMoonlightUuid("uuid").moonlightUuid);
        assertEquals(Arrays.asList("100.64.0.9", "10.0.0.2"), back.withPreferredHost("100.64.0.9").hosts);
    }
}
