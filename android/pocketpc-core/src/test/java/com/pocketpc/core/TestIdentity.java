package com.pocketpc.core;

import java.math.BigInteger;
import java.security.KeyPair;
import java.security.KeyPairGenerator;
import java.security.PrivateKey;
import java.security.spec.ECGenParameterSpec;
import java.security.cert.X509Certificate;
import java.util.Date;

import javax.security.auth.x500.X500Principal;

import org.bouncycastle.cert.jcajce.JcaX509CertificateConverter;
import org.bouncycastle.cert.jcajce.JcaX509v3CertificateBuilder;
import org.bouncycastle.operator.jcajce.JcaContentSignerBuilder;

/** In-memory P-256 identity, like the one the Android Keystore creates on the phone. */
final class TestIdentity implements ClientIdentity {
    private final X509Certificate certificate;
    private final PrivateKey privateKey;

    TestIdentity(String name) throws Exception {
        KeyPairGenerator generator = KeyPairGenerator.getInstance("EC");
        generator.initialize(new ECGenParameterSpec("secp256r1"));
        KeyPair pair = generator.generateKeyPair();
        X500Principal subject = new X500Principal("CN=" + name);
        long now = System.currentTimeMillis();
        JcaX509v3CertificateBuilder builder = new JcaX509v3CertificateBuilder(
                subject, BigInteger.valueOf(now), new Date(now - 86_400_000L), new Date(now + 86_400_000L * 365), subject, pair.getPublic());
        certificate = new JcaX509CertificateConverter()
                .getCertificate(builder.build(new JcaContentSignerBuilder("SHA256withECDSA").build(pair.getPrivate())));
        privateKey = pair.getPrivate();
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
