package com.pocketpc.core;

import java.io.UnsupportedEncodingException;
import java.net.URLDecoder;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;

/**
 * The contents of the PC's pairing QR code:
 * {@code pocketpc://pair?v=1&id=<pcId>&n=<name>&h=<host1,host2>&p=<port>&fp=<sha256>&c=<code>}.
 * Mirrors pc/src/PocketPC.Core/Pairing/PairingUri.cs.
 */
public final class PairingUri {
    public static final String SCHEME = "pocketpc";
    private static final String PREFIX = SCHEME + "://pair?";

    public final String pcId;
    public final String pcName;
    public final List<String> hosts;
    public final int port;
    public final String serverFingerprint;
    public final String code;

    public PairingUri(String pcId, String pcName, List<String> hosts, int port, String serverFingerprint, String code) {
        this.pcId = pcId;
        this.pcName = pcName;
        this.hosts = Collections.unmodifiableList(new ArrayList<>(hosts));
        this.port = port;
        this.serverFingerprint = serverFingerprint;
        this.code = code;
    }

    /** @return the parsed link, or null if the text isn't a valid PocketPC pairing link */
    public static PairingUri parse(String text) {
        if (text == null || !text.startsWith(PREFIX)) {
            return null;
        }

        Map<String, String> query = new HashMap<>();
        for (String part : text.substring(PREFIX.length()).split("&")) {
            int eq = part.indexOf('=');
            if (eq > 0 && !query.containsKey(part.substring(0, eq))) {
                query.put(part.substring(0, eq), part.substring(eq + 1));
            }
        }

        try {
            if (!"1".equals(query.get("v"))) {
                return null;
            }
            String id = decode(query.get("id"));
            String name = decode(query.get("n"));
            String hostList = query.get("h");
            String fp = query.get("fp");
            String code = PairingCode.normalize(query.get("c"));
            String portText = query.get("p");
            if (id == null || name == null || hostList == null || fp == null || code == null || portText == null) {
                return null;
            }

            int port = Integer.parseInt(portText);
            List<String> hosts = new ArrayList<>();
            for (String h : hostList.split(",")) {
                if (!h.isEmpty()) {
                    hosts.add(decode(h));
                }
            }
            if (hosts.isEmpty() || port <= 0 || port > 65535 || fp.length() != 64) {
                return null;
            }
            return new PairingUri(id, name, hosts, port, fp.toLowerCase(Locale.ROOT), code);
        } catch (UnsupportedEncodingException | IllegalArgumentException e) {
            return null;
        }
    }

    private static String decode(String s) throws UnsupportedEncodingException {
        // URLDecoder treats '+' as space; the PC side percent-encodes spaces, so protect literal '+'.
        return s == null ? null : URLDecoder.decode(s.replace("+", "%2B"), "UTF-8");
    }
}
