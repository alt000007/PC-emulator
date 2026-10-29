package com.pocketpc.app;

import android.content.Context;
import android.content.SharedPreferences;

import com.limelight.LimeLog;
import com.pocketpc.core.PairedPc;

import org.json.JSONArray;
import org.json.JSONException;

import java.util.ArrayList;
import java.util.List;

/** PCs this phone paired with through PocketPC. Saved automatically; holds no secrets. */
public final class PairedPcStore {
    private static final String PREFS = "pocketpc_paired_pcs";
    private static final String KEY = "pcs";

    private PairedPcStore() {
    }

    public static synchronized List<PairedPc> all(Context context) {
        List<PairedPc> result = new ArrayList<>();
        String raw = prefs(context).getString(KEY, "[]");
        try {
            JSONArray array = new JSONArray(raw);
            for (int i = 0; i < array.length(); i++) {
                result.add(PairedPc.fromJson(array.getJSONObject(i)));
            }
        } catch (JSONException e) {
            LimeLog.warning("PocketPC: ignoring unreadable paired PC list: " + e.getMessage());
        }
        return result;
    }

    public static synchronized PairedPc findById(Context context, String pcId) {
        for (PairedPc pc : all(context)) {
            if (pc.pcId.equals(pcId)) {
                return pc;
            }
        }
        return null;
    }

    public static synchronized PairedPc findByMoonlightUuid(Context context, String uuid) {
        for (PairedPc pc : all(context)) {
            if (uuid != null && uuid.equals(pc.moonlightUuid)) {
                return pc;
            }
        }
        return null;
    }

    /** Adds or replaces (same pcId) a PC. */
    public static synchronized void save(Context context, PairedPc pc) {
        List<PairedPc> pcs = all(context);
        for (int i = 0; i < pcs.size(); i++) {
            if (pcs.get(i).pcId.equals(pc.pcId)) {
                pcs.remove(i);
                break;
            }
        }
        pcs.add(pc);
        write(context, pcs);
    }

    public static synchronized void remove(Context context, String pcId) {
        List<PairedPc> pcs = all(context);
        for (int i = 0; i < pcs.size(); i++) {
            if (pcs.get(i).pcId.equals(pcId)) {
                pcs.remove(i);
                write(context, pcs);
                return;
            }
        }
    }

    private static void write(Context context, List<PairedPc> pcs) {
        JSONArray array = new JSONArray();
        try {
            for (PairedPc pc : pcs) {
                array.put(pc.toJson());
            }
        } catch (JSONException e) {
            throw new IllegalStateException(e);
        }
        prefs(context).edit().putString(KEY, array.toString()).apply();
    }

    private static SharedPreferences prefs(Context context) {
        return context.getApplicationContext().getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }
}
