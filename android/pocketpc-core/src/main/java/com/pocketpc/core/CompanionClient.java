package com.pocketpc.core;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.URI;
import java.net.URISyntaxException;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;

import javax.net.ssl.HttpsURLConnection;
import javax.net.ssl.SSLSocketFactory;

import org.json.JSONException;
import org.json.JSONObject;

/**
 * Client for the PocketPC companion API on the PC (see docs/PAIRING.md).
 * Every request uses mutual TLS: the PC is pinned by fingerprint, the phone presents its identity.
 * Calls block; run them off the UI thread.
 */
public final class CompanionClient {
    public static final int DEFAULT_PORT = 47880;

    private final ClientIdentity identity;
    private final String host;
    private final int port;
    private volatile String pinnedFingerprint;
    private volatile String observedFingerprint;
    private volatile SSLSocketFactory socketFactory;
    private int connectTimeoutMs = 4000;
    private int readTimeoutMs = 45000;

    /**
     * @param pinnedFingerprint the PC's certificate fingerprint (from the QR code or an earlier
     *                          pairing), or null when pairing with a typed code: the first
     *                          connection learns it and the pairing proof then verifies it.
     */
    public CompanionClient(ClientIdentity identity, String host, int port, String pinnedFingerprint) {
        this.identity = identity;
        this.host = host;
        this.port = port;
        setPin(pinnedFingerprint);
    }

    /** Tries each host in order (home network first, then Tailscale) and returns the first that answers. */
    public static CompanionClient connectFirstReachable(
            ClientIdentity identity, List<String> hosts, int port, String pinnedFingerprint) throws IOException, CompanionException {
        List<String> failures = new ArrayList<>();
        for (String h : hosts) {
            CompanionClient client = new CompanionClient(identity, h, port, pinnedFingerprint);
            client.connectTimeoutMs = 2500;
            try {
                client.hello();
                client.connectTimeoutMs = 4000;
                return client;
            } catch (IOException e) {
                failures.add(h + ": " + e.getMessage());
            }
        }
        throw new IOException("Could not reach the PC at " + String.join(", ", failures));
    }

    public String host() {
        return host;
    }

    public int port() {
        return port;
    }

    /** The PC certificate fingerprint seen on the last connection. */
    public String serverFingerprint() {
        return pinnedFingerprint != null ? pinnedFingerprint : observedFingerprint;
    }

    public void setTimeouts(int connectMs, int readMs) {
        this.connectTimeoutMs = connectMs;
        this.readTimeoutMs = readMs;
    }

    /** Unauthenticated: who is this PC and is it currently accepting pairings? */
    public Hello hello() throws IOException, CompanionException {
        JSONObject json = request("GET", "/v1/hello", null);
        return new Hello(json.getInt("apiVersion"), json.getString("pcId"), json.getString("pcName"), json.getBoolean("pairingOpen"));
    }

    /**
     * Pairs this phone with the PC using the code it shows. Both sides prove they know the code;
     * the PC's proof is checked here, so a different machine pretending to be the PC is rejected.
     */
    public PairResult pair(String code, String deviceName, String deviceModel) throws IOException, CompanionException {
        String normalized = PairingCode.normalize(code);
        if (normalized == null) {
            throw new CompanionException(0, "invalid_code");
        }

        if (pinnedFingerprint == null) {
            // Typed-code pairing: learn the certificate, then pin it for the pairing request itself.
            hello();
            setPin(observedFingerprint);
        }

        String serverFp = pinnedFingerprint;
        String clientFp = identity.fingerprint();
        JSONObject body = new JSONObject()
                .put("deviceName", deviceName)
                .put("deviceModel", deviceModel)
                .put("proof", PairingProof.forClient(normalized, clientFp, serverFp));

        JSONObject json = request("POST", "/v1/pair", body);
        String expected = PairingProof.forServer(normalized, serverFp, clientFp);
        if (!Hex.constantTimeEquals(expected, json.optString("serverProof", ""))) {
            throw new CompanionException(0, "server_proof_mismatch");
        }

        JSONObject sunshine = json.getJSONObject("sunshine");
        return new PairResult(
                json.getString("deviceId"),
                json.getString("pcId"),
                json.getString("pcName"),
                serverFp,
                sunshine.getBoolean("installed"),
                sunshine.getInt("httpPort"),
                sunshine.isNull("certificateFingerprint") ? null : sunshine.getString("certificateFingerprint"));
    }

    /**
     * Asks the companion to enter this PIN for the Moonlight pairing the phone just started.
     * Blocks until Sunshine finished the handshake (up to ~30 s).
     *
     * @return null on success, otherwise the companion's error code
     */
    public String pairSunshine(String pin) throws IOException {
        try {
            request("POST", "/v1/sunshine/pair", new JSONObject().put("pin", pin));
            return null;
        } catch (CompanionException e) {
            return e.error;
        }
    }

    public Status status() throws IOException, CompanionException {
        JSONObject json = request("GET", "/v1/status", null);
        return new Status(json.getString("pcId"), json.getString("pcName"), json.getString("deviceId"), json.getString("deviceName"));
    }

    /** Removes this phone from the PC (and from Sunshine). */
    public void unpair() throws IOException, CompanionException {
        request("POST", "/v1/unpair", null);
    }

    private void setPin(String fingerprint) {
        pinnedFingerprint = fingerprint;
        socketFactory = PinnedTls.socketFactory(identity, fingerprint, fp -> observedFingerprint = fp);
    }

    private JSONObject request(String method, String path, JSONObject body) throws IOException, CompanionException {
        HttpsURLConnection conn;
        try {
            // URI adds the [] around IPv6 literals.
            conn = (HttpsURLConnection) new URI("https", null, host, port, path, null, null).toURL().openConnection();
        } catch (URISyntaxException e) {
            throw new IOException("Invalid PC address: " + host, e);
        }
        conn.setSSLSocketFactory(socketFactory);
        // The certificate is pinned by fingerprint, so host names (often bare IPs) aren't checked.
        conn.setHostnameVerifier((hostname, session) -> true);
        conn.setConnectTimeout(connectTimeoutMs);
        conn.setReadTimeout(readTimeoutMs);
        conn.setRequestMethod(method);
        conn.setRequestProperty("Accept", "application/json");
        conn.setUseCaches(false);
        try {
            if (body != null || "POST".equals(method)) {
                byte[] bytes = (body == null ? "{}" : body.toString()).getBytes(StandardCharsets.UTF_8);
                conn.setDoOutput(true);
                conn.setRequestProperty("Content-Type", "application/json");
                // Not streaming mode: the JDK drops the error body of a 401 for streamed requests.
                try (OutputStream out = conn.getOutputStream()) {
                    out.write(bytes);
                }
            }

            int status = conn.getResponseCode();
            String text = readAll(status >= 400 ? conn.getErrorStream() : conn.getInputStream());
            if (status >= 200 && status < 300) {
                return text.isEmpty() ? new JSONObject() : new JSONObject(text);
            }

            String error = "http_" + status;
            try {
                error = new JSONObject(text).optString("error", error);
            } catch (JSONException ignored) {
                // Not a PocketPC error body; keep the HTTP status.
            }
            throw new CompanionException(status, error);
        } catch (JSONException e) {
            throw new IOException("Unexpected reply from the PC", e);
        } finally {
            conn.disconnect();
        }
    }

    private static String readAll(InputStream in) throws IOException {
        if (in == null) {
            return "";
        }
        try (InputStream stream = in) {
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            byte[] buffer = new byte[4096];
            int n;
            while ((n = stream.read(buffer)) > 0) {
                out.write(buffer, 0, n);
            }
            return out.toString("UTF-8");
        }
    }

    public static final class Hello {
        public final int apiVersion;
        public final String pcId;
        public final String pcName;
        public final boolean pairingOpen;

        Hello(int apiVersion, String pcId, String pcName, boolean pairingOpen) {
            this.apiVersion = apiVersion;
            this.pcId = pcId;
            this.pcName = pcName;
            this.pairingOpen = pairingOpen;
        }
    }

    public static final class PairResult {
        public final String deviceId;
        public final String pcId;
        public final String pcName;
        public final String serverFingerprint;
        public final boolean sunshineInstalled;
        public final int sunshineHttpPort;
        /** Sunshine's streaming certificate, if the companion could read it; checked after Moonlight pairing. */
        public final String sunshineCertificateFingerprint;

        PairResult(String deviceId, String pcId, String pcName, String serverFingerprint,
                   boolean sunshineInstalled, int sunshineHttpPort, String sunshineCertificateFingerprint) {
            this.deviceId = deviceId;
            this.pcId = pcId;
            this.pcName = pcName;
            this.serverFingerprint = serverFingerprint;
            this.sunshineInstalled = sunshineInstalled;
            this.sunshineHttpPort = sunshineHttpPort;
            this.sunshineCertificateFingerprint = sunshineCertificateFingerprint;
        }
    }

    public static final class Status {
        public final String pcId;
        public final String pcName;
        public final String deviceId;
        public final String deviceName;

        Status(String pcId, String pcName, String deviceId, String deviceName) {
            this.pcId = pcId;
            this.pcName = pcName;
            this.deviceId = deviceId;
            this.deviceName = deviceName;
        }
    }
}
