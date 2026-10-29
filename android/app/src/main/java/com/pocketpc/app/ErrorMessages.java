package com.pocketpc.app;

import android.content.Context;

import com.limelight.R;

/** Turns the companion's error codes (docs/PAIRING.md) into plain-language messages. */
final class ErrorMessages {
    private ErrorMessages() {
    }

    static String forCompanionError(Context context, String error) {
        switch (error == null ? "" : error) {
            case "wrong_code":
                return context.getString(R.string.pocketpc_error_wrong_code);
            case "invalid_code":
                return context.getString(R.string.pocketpc_error_invalid_code);
            case "too_many_attempts":
                return context.getString(R.string.pocketpc_error_too_many_attempts);
            case "pairing_closed":
            case "pairing_expired":
                return context.getString(R.string.pocketpc_error_pairing_closed);
            case "server_proof_mismatch":
                return context.getString(R.string.pocketpc_error_proof_mismatch);
            case "not_paired":
                return context.getString(R.string.pocketpc_error_not_paired);
            default:
                return context.getString(R.string.pocketpc_error_generic, error);
        }
    }

    static String forSunshinePairError(Context context, String error, String pcName) {
        if ("sunshine_login_needed".equals(error)) {
            return context.getString(R.string.pocketpc_partial_sunshine_login, pcName);
        }
        return context.getString(R.string.pocketpc_partial_streaming_failed, pcName, error);
    }
}
