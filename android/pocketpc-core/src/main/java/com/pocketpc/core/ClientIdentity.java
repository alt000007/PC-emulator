package com.pocketpc.core;

import java.security.PrivateKey;
import java.security.cert.X509Certificate;

/**
 * This phone's TLS client identity. On Android the private key lives in the Android Keystore
 * (non-exportable); tests use an in-memory key.
 */
public interface ClientIdentity {
    X509Certificate certificate();

    PrivateKey privateKey();

    default String fingerprint() {
        return Hex.fingerprint(certificate());
    }
}
