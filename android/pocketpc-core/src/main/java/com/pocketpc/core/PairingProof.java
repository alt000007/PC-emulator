package com.pocketpc.core;

import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;

import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

/**
 * Proves knowledge of the pairing code without sending it: HMAC-SHA256 keyed with the code over
 * both TLS certificate fingerprints, so a proof only works on this exact phone-to-PC connection.
 * Must stay byte-identical to pc/src/PocketPC.Core/Pairing/PairingProof.cs.
 */
public final class PairingProof {
    public static final String CONTEXT = "pocketpc-pair-v1";

    private PairingProof() {
    }

    public static String forClient(String normalizedCode, String clientFingerprint, String serverFingerprint) {
        return compute(normalizedCode, CONTEXT + "|client|" + clientFingerprint + "|" + serverFingerprint);
    }

    public static String forServer(String normalizedCode, String serverFingerprint, String clientFingerprint) {
        return compute(normalizedCode, CONTEXT + "|server|" + serverFingerprint + "|" + clientFingerprint);
    }

    private static String compute(String normalizedCode, String message) {
        try {
            Mac mac = Mac.getInstance("HmacSHA256");
            mac.init(new SecretKeySpec(normalizedCode.getBytes(StandardCharsets.UTF_8), "HmacSHA256"));
            return Hex.encode(mac.doFinal(message.getBytes(StandardCharsets.UTF_8)));
        } catch (GeneralSecurityException e) {
            throw new IllegalStateException(e);
        }
    }
}
