package com.pocketpc.core;

import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.security.cert.CertificateEncodingException;
import java.security.cert.X509Certificate;

/** Lowercase hex and SHA-256 certificate fingerprints, identical to the PC side. */
public final class Hex {
    private static final char[] DIGITS = "0123456789abcdef".toCharArray();

    private Hex() {
    }

    public static String encode(byte[] bytes) {
        char[] out = new char[bytes.length * 2];
        for (int i = 0; i < bytes.length; i++) {
            out[i * 2] = DIGITS[(bytes[i] >> 4) & 0xF];
            out[i * 2 + 1] = DIGITS[bytes[i] & 0xF];
        }
        return new String(out);
    }

    public static String sha256(byte[] data) {
        try {
            return encode(MessageDigest.getInstance("SHA-256").digest(data));
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException(e);
        }
    }

    /** Lowercase hex SHA-256 of the DER certificate: the identity of a PC or phone. */
    public static String fingerprint(X509Certificate certificate) {
        try {
            return sha256(certificate.getEncoded());
        } catch (CertificateEncodingException e) {
            throw new IllegalArgumentException(e);
        }
    }

    /** Constant-time comparison of two hex strings (case-insensitive). */
    public static boolean constantTimeEquals(String a, String b) {
        if (a == null || b == null) {
            return false;
        }
        return MessageDigest.isEqual(
                a.toLowerCase(java.util.Locale.ROOT).getBytes(java.nio.charset.StandardCharsets.US_ASCII),
                b.toLowerCase(java.util.Locale.ROOT).getBytes(java.nio.charset.StandardCharsets.US_ASCII));
    }
}
