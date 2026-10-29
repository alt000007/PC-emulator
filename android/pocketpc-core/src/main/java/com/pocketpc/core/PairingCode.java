package com.pocketpc.core;

import java.util.Locale;

/**
 * One-time pairing codes: 12 Crockford base32 characters shown as XXXX-XXXX-XXXX.
 * Mirrors pc/src/PocketPC.Core/Pairing/PairingCode.cs.
 */
public final class PairingCode {
    public static final int LENGTH = 12;
    private static final String ALPHABET = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private PairingCode() {
    }

    /**
     * Accepts what a human might type (lower case, dashes, spaces, O for 0, I/L for 1).
     *
     * @return the canonical code, or null if the input can't be a valid code
     */
    public static String normalize(String input) {
        if (input == null) {
            return null;
        }
        StringBuilder sb = new StringBuilder(LENGTH);
        for (char raw : input.toCharArray()) {
            if (raw == '-' || raw == ' ' || raw == '\t') {
                continue;
            }
            char c = Character.toUpperCase(raw);
            if (c == 'O') {
                c = '0';
            } else if (c == 'I' || c == 'L') {
                c = '1';
            }
            if (ALPHABET.indexOf(c) < 0) {
                return null;
            }
            sb.append(c);
        }
        return sb.length() == LENGTH ? sb.toString() : null;
    }

    public static String format(String code) {
        return String.format(Locale.ROOT, "%s-%s-%s", code.substring(0, 4), code.substring(4, 8), code.substring(8, 12));
    }
}
