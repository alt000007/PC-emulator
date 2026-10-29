package com.pocketpc.core;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

/** A PC this phone paired with. Stored on the phone; contains no secrets. */
public final class PairedPc {
    public final String pcId;
    public final String name;
    public final List<String> hosts;
    public final int port;
    public final String serverFingerprint;
    public final String deviceId;
    public final int sunshineHttpPort;
    /** Moonlight's computer uuid (Sunshine's unique id) once streaming pairing finished, else null. */
    public final String moonlightUuid;
    public final long pairedAtMillis;

    public PairedPc(String pcId, String name, List<String> hosts, int port, String serverFingerprint,
                    String deviceId, int sunshineHttpPort, String moonlightUuid, long pairedAtMillis) {
        this.pcId = pcId;
        this.name = name;
        this.hosts = Collections.unmodifiableList(new ArrayList<>(hosts));
        this.port = port;
        this.serverFingerprint = serverFingerprint;
        this.deviceId = deviceId;
        this.sunshineHttpPort = sunshineHttpPort;
        this.moonlightUuid = moonlightUuid;
        this.pairedAtMillis = pairedAtMillis;
    }

    public PairedPc withMoonlightUuid(String uuid) {
        return new PairedPc(pcId, name, hosts, port, serverFingerprint, deviceId, sunshineHttpPort, uuid, pairedAtMillis);
    }

    /** Moves the host that just worked to the front, so it's tried first next time. */
    public PairedPc withPreferredHost(String host) {
        List<String> reordered = new ArrayList<>();
        reordered.add(host);
        for (String h : hosts) {
            if (!h.equals(host)) {
                reordered.add(h);
            }
        }
        return new PairedPc(pcId, name, reordered, port, serverFingerprint, deviceId, sunshineHttpPort, moonlightUuid, pairedAtMillis);
    }

    public JSONObject toJson() throws JSONException {
        return new JSONObject()
                .put("pcId", pcId)
                .put("name", name)
                .put("hosts", new JSONArray(hosts))
                .put("port", port)
                .put("serverFingerprint", serverFingerprint)
                .put("deviceId", deviceId)
                .put("sunshineHttpPort", sunshineHttpPort)
                .put("moonlightUuid", moonlightUuid == null ? JSONObject.NULL : moonlightUuid)
                .put("pairedAt", pairedAtMillis);
    }

    public static PairedPc fromJson(JSONObject json) throws JSONException {
        JSONArray hostArray = json.getJSONArray("hosts");
        List<String> hosts = new ArrayList<>();
        for (int i = 0; i < hostArray.length(); i++) {
            hosts.add(hostArray.getString(i));
        }
        return new PairedPc(
                json.getString("pcId"),
                json.getString("name"),
                hosts,
                json.getInt("port"),
                json.getString("serverFingerprint"),
                json.getString("deviceId"),
                json.optInt("sunshineHttpPort", 47989),
                json.isNull("moonlightUuid") ? null : json.optString("moonlightUuid", null),
                json.optLong("pairedAt", 0));
    }
}
