package com.pocketpc.app;

import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;

import com.pocketpc.core.ClientIdentity;

import java.io.IOException;
import java.math.BigInteger;
import java.security.GeneralSecurityException;
import java.security.KeyPairGenerator;
import java.security.KeyStore;
import java.security.PrivateKey;
import java.security.cert.X509Certificate;
import java.security.spec.ECGenParameterSpec;
import java.util.Calendar;

import javax.security.auth.x500.X500Principal;

/**
 * This phone's PocketPC identity: a P-256 key generated inside the Android Keystore. The private
 * key never leaves the secure hardware/keystore and is never written to app storage.
 */
public final class DeviceIdentity implements ClientIdentity {
    private static final String KEYSTORE = "AndroidKeyStore";
    private static final String ALIAS = "pocketpc-device-v1";

    private static DeviceIdentity instance;

    private final X509Certificate certificate;
    private final PrivateKey privateKey;

    private DeviceIdentity(X509Certificate certificate, PrivateKey privateKey) {
        this.certificate = certificate;
        this.privateKey = privateKey;
    }

    public static synchronized DeviceIdentity get() throws GeneralSecurityException, IOException {
        if (instance == null) {
            instance = loadOrCreate();
        }
        return instance;
    }

    private static DeviceIdentity loadOrCreate() throws GeneralSecurityException, IOException {
        KeyStore keyStore = KeyStore.getInstance(KEYSTORE);
        keyStore.load(null);

        if (!keyStore.containsAlias(ALIAS)) {
            Calendar notBefore = Calendar.getInstance();
            notBefore.add(Calendar.DAY_OF_YEAR, -1);
            Calendar notAfter = Calendar.getInstance();
            notAfter.add(Calendar.YEAR, 30);

            KeyPairGenerator generator = KeyPairGenerator.getInstance(KeyProperties.KEY_ALGORITHM_EC, KEYSTORE);
            generator.initialize(new KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_SIGN)
                    .setAlgorithmParameterSpec(new ECGenParameterSpec("secp256r1"))
                    // TLS stacks hash first and ask for a raw signature, so DIGEST_NONE is required.
                    .setDigests(KeyProperties.DIGEST_NONE, KeyProperties.DIGEST_SHA256,
                            KeyProperties.DIGEST_SHA384, KeyProperties.DIGEST_SHA512)
                    .setCertificateSubject(new X500Principal("CN=PocketPC phone"))
                    .setCertificateSerialNumber(BigInteger.valueOf(System.currentTimeMillis()))
                    .setCertificateNotBefore(notBefore.getTime())
                    .setCertificateNotAfter(notAfter.getTime())
                    .build());
            generator.generateKeyPair();
        }

        PrivateKey key = (PrivateKey) keyStore.getKey(ALIAS, null);
        X509Certificate cert = (X509Certificate) keyStore.getCertificate(ALIAS);
        if (key == null || cert == null) {
            throw new GeneralSecurityException("PocketPC device key is missing from the Android Keystore");
        }
        return new DeviceIdentity(cert, key);
    }

    @Override
    public X509Certificate certificate() {
        return certificate;
    }

    @Override
    public PrivateKey privateKey() {
        return privateKey;
    }
}
