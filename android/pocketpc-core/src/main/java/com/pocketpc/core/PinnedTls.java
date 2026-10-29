package com.pocketpc.core;

import java.net.Socket;
import java.security.GeneralSecurityException;
import java.security.Principal;
import java.security.PrivateKey;
import java.security.SecureRandom;
import java.security.cert.CertificateException;
import java.security.cert.X509Certificate;

import javax.net.ssl.KeyManager;
import javax.net.ssl.SSLContext;
import javax.net.ssl.SSLEngine;
import javax.net.ssl.SSLSocketFactory;
import javax.net.ssl.TrustManager;
import javax.net.ssl.X509ExtendedKeyManager;
import javax.net.ssl.X509ExtendedTrustManager;

/**
 * TLS that trusts exactly one certificate, identified by its SHA-256 fingerprint, and presents
 * this phone's client certificate. PCs use self-signed certificates, so CA validation and host
 * names don't apply; the pinned fingerprint (from the QR code or the pairing proof) is the trust.
 */
final class PinnedTls {
    private static final String ALIAS = "pocketpc";

    private PinnedTls() {
    }

    /** Observes the server certificate of the last handshake. */
    interface Observer {
        void onServerCertificate(String fingerprint);
    }

    /**
     * @param expectedFingerprint the only acceptable server certificate, or null to accept any
     *                            (used once, to learn the fingerprint before a code-based pairing
     *                            proves it)
     */
    static SSLSocketFactory socketFactory(ClientIdentity identity, String expectedFingerprint, Observer observer) {
        try {
            SSLContext context = SSLContext.getInstance("TLS");
            context.init(
                    new KeyManager[] {new IdentityKeyManager(identity)},
                    new TrustManager[] {new FingerprintTrustManager(expectedFingerprint, observer)},
                    new SecureRandom());
            return context.getSocketFactory();
        } catch (GeneralSecurityException e) {
            throw new IllegalStateException(e);
        }
    }

    private static final class FingerprintTrustManager extends X509ExtendedTrustManager {
        private final String expected;
        private final Observer observer;

        FingerprintTrustManager(String expected, Observer observer) {
            this.expected = expected;
            this.observer = observer;
        }

        private void check(X509Certificate[] chain) throws CertificateException {
            if (chain == null || chain.length == 0) {
                throw new CertificateException("PC sent no certificate");
            }
            String actual = Hex.fingerprint(chain[0]);
            if (expected != null && !Hex.constantTimeEquals(expected, actual)) {
                throw new CertificateException("PC certificate does not match the paired PC (possible impostor)");
            }
            if (observer != null) {
                observer.onServerCertificate(actual);
            }
        }

        @Override
        public void checkServerTrusted(X509Certificate[] chain, String authType) throws CertificateException {
            check(chain);
        }

        @Override
        public void checkServerTrusted(X509Certificate[] chain, String authType, Socket socket) throws CertificateException {
            check(chain);
        }

        @Override
        public void checkServerTrusted(X509Certificate[] chain, String authType, SSLEngine engine) throws CertificateException {
            check(chain);
        }

        @Override
        public void checkClientTrusted(X509Certificate[] chain, String authType) throws CertificateException {
            throw new CertificateException("client mode only");
        }

        @Override
        public void checkClientTrusted(X509Certificate[] chain, String authType, Socket socket) throws CertificateException {
            throw new CertificateException("client mode only");
        }

        @Override
        public void checkClientTrusted(X509Certificate[] chain, String authType, SSLEngine engine) throws CertificateException {
            throw new CertificateException("client mode only");
        }

        @Override
        public X509Certificate[] getAcceptedIssuers() {
            return new X509Certificate[0];
        }
    }

    private static final class IdentityKeyManager extends X509ExtendedKeyManager {
        private final ClientIdentity identity;

        IdentityKeyManager(ClientIdentity identity) {
            this.identity = identity;
        }

        @Override
        public String chooseClientAlias(String[] keyType, Principal[] issuers, Socket socket) {
            // The PC doesn't use a CA, so whatever issuer list it sends doesn't apply to us.
            return ALIAS;
        }

        @Override
        public String chooseEngineClientAlias(String[] keyType, Principal[] issuers, SSLEngine engine) {
            return ALIAS;
        }

        @Override
        public X509Certificate[] getCertificateChain(String alias) {
            return ALIAS.equals(alias) ? new X509Certificate[] {identity.certificate()} : null;
        }

        @Override
        public PrivateKey getPrivateKey(String alias) {
            return ALIAS.equals(alias) ? identity.privateKey() : null;
        }

        @Override
        public String[] getClientAliases(String keyType, Principal[] issuers) {
            return new String[] {ALIAS};
        }

        @Override
        public String chooseServerAlias(String keyType, Principal[] issuers, Socket socket) {
            return null;
        }

        @Override
        public String[] getServerAliases(String keyType, Principal[] issuers) {
            return null;
        }
    }
}
