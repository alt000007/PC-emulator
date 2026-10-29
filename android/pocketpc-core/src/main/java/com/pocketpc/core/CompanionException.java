package com.pocketpc.core;

/**
 * An error answer from the PC companion. {@link #error} is a stable machine-readable code
 * (see docs/PAIRING.md), e.g. {@code wrong_code}, {@code not_paired}, {@code sunshine_login_needed}.
 */
public class CompanionException extends Exception {
    private static final long serialVersionUID = 1L;

    public final int httpStatus;
    public final String error;

    public CompanionException(int httpStatus, String error) {
        super("PocketPC companion error " + httpStatus + ": " + error);
        this.httpStatus = httpStatus;
        this.error = error;
    }

    /** True when the PC no longer knows this phone (it was revoked or never paired). */
    public boolean isNotPaired() {
        return httpStatus == 403 || "not_paired".equals(error);
    }
}
