package com.pocketpc.app;

import android.content.Context;
import android.os.Build;
import android.provider.Settings;

import com.limelight.LimeLog;
import com.limelight.R;
import com.limelight.binding.PlatformBinding;
import com.limelight.computers.ComputerManagerService;
import com.limelight.nvstream.http.ComputerDetails;
import com.limelight.nvstream.http.NvHTTP;
import com.limelight.nvstream.http.PairingManager;
import com.limelight.utils.ServerHelper;
import com.pocketpc.core.CompanionClient;
import com.pocketpc.core.CompanionException;
import com.pocketpc.core.Hex;
import com.pocketpc.core.PairedPc;
import com.pocketpc.core.PairingUri;

import java.io.IOException;
import java.security.GeneralSecurityException;
import java.security.cert.X509Certificate;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.FutureTask;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;

/**
 * Pairs the phone with a PC in two steps, without the user typing a PIN anywhere:
 * <ol>
 *   <li>PocketPC pairing with the companion (QR or typed code, mutual TLS + proofs).</li>
 *   <li>Moonlight pairing with Sunshine: we start it with a random PIN and hand that PIN to the
 *       companion over the now-trusted channel, which enters it in Sunshine for us.</li>
 * </ol>
 * Runs on a background thread; results are reported through {@link Listener}.
 */
public final class PairingFlow {
    public interface Listener {
        void onProgress(String message);

        /** Fully paired; the PC is in Moonlight's list and ready to stream. */
        void onSuccess(ComputerDetails computer);

        /** Paired with PocketPC, but streaming setup must be finished later. */
        void onPartialSuccess(String message);

        void onFailure(String message);
    }

    private final Context context;
    private final ComputerManagerService.ComputerManagerBinder binder;
    private final Listener listener;

    public PairingFlow(Context context, ComputerManagerService.ComputerManagerBinder binder, Listener listener) {
        this.context = context.getApplicationContext();
        this.binder = binder;
        this.listener = listener;
    }

    /** From a scanned QR code: the PC's certificate is pinned from the start. */
    public void runWithQr(PairingUri uri) {
        run(uri.hosts, uri.port, uri.serverFingerprint, uri.code);
    }

    /** From a typed address and code: the certificate is learned, then verified by the PC's proof. */
    public void runWithCode(String host, int port, String code) {
        run(Collections.singletonList(host), port, null, code);
    }

    private void run(List<String> hosts, int port, String pinnedFingerprint, String code) {
        try {
            listener.onProgress(context.getString(R.string.pocketpc_progress_connecting));
            DeviceIdentity identity = DeviceIdentity.get();
            CompanionClient client = CompanionClient.connectFirstReachable(identity, hosts, port, pinnedFingerprint);

            listener.onProgress(context.getString(R.string.pocketpc_progress_pairing));
            CompanionClient.PairResult result = client.pair(code, deviceName(), Build.MANUFACTURER + " " + Build.MODEL);

            List<String> knownHosts = new ArrayList<>(hosts);
            PairedPc pc = new PairedPc(result.pcId, result.pcName, knownHosts, port, result.serverFingerprint,
                    result.deviceId, result.sunshineHttpPort, null, System.currentTimeMillis())
                    .withPreferredHost(client.host());
            PairedPcStore.save(context, pc);
            LimeLog.info("PocketPC: paired with " + result.pcName + " as device " + result.deviceId);

            if (!result.sunshineInstalled) {
                listener.onPartialSuccess(context.getString(R.string.pocketpc_partial_no_sunshine, result.pcName));
                return;
            }

            listener.onProgress(context.getString(R.string.pocketpc_progress_streaming));
            ComputerDetails computer = pairStreaming(client, result);
            if (computer != null) {
                PairedPcStore.save(context, pc.withMoonlightUuid(computer.uuid));
                listener.onSuccess(computer);
            }
        } catch (CompanionException e) {
            listener.onFailure(ErrorMessages.forCompanionError(context, e.error));
        } catch (IOException e) {
            LimeLog.warning("PocketPC: pairing failed: " + e);
            listener.onFailure(context.getString(R.string.pocketpc_error_unreachable, e.getMessage()));
        } catch (GeneralSecurityException e) {
            listener.onFailure(context.getString(R.string.pocketpc_error_keystore, e.getMessage()));
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
        }
    }

    /** @return the Moonlight computer when streaming is ready, or null after reporting why not */
    private ComputerDetails pairStreaming(CompanionClient client, CompanionClient.PairResult result) throws InterruptedException {
        ComputerDetails computer = new ComputerDetails();
        computer.manualAddress = new ComputerDetails.AddressTuple(client.host(), result.sunshineHttpPort);
        if (!binder.addComputerBlocking(computer)) {
            listener.onPartialSuccess(context.getString(R.string.pocketpc_partial_sunshine_unreachable, result.pcName));
            return null;
        }

        try {
            NvHTTP http = new NvHTTP(ServerHelper.getCurrentAddressFromComputer(computer), computer.httpsPort,
                    binder.getUniqueId(), computer.serverCert, PlatformBinding.getCryptoProvider(context));
            if (http.getPairState() == PairingManager.PairState.PAIRED) {
                return computer;
            }

            final String pin = PairingManager.generatePinString();
            final PairingManager pairingManager = http.getPairingManager();
            final String serverInfo = http.getServerInfo(true);

            // Moonlight's pair() blocks until Sunshine gets the PIN, so it runs on its own thread
            // while we ask the companion to enter that PIN. Daemon: if the companion fails,
            // Sunshine eventually times the request out and the thread ends by itself.
            FutureTask<PairingManager.PairState> moonlightPairing =
                    new FutureTask<>(() -> pairingManager.pair(serverInfo, pin));
            Thread thread = new Thread(moonlightPairing, "PocketPC-MoonlightPair");
            thread.setDaemon(true);
            thread.start();

            String companionError = client.pairSunshine(pin);
            if (companionError != null) {
                listener.onPartialSuccess(ErrorMessages.forSunshinePairError(context, companionError, result.pcName));
                return null;
            }

            PairingManager.PairState state = moonlightPairing.get(30, TimeUnit.SECONDS);
            if (state != PairingManager.PairState.PAIRED) {
                listener.onPartialSuccess(context.getString(R.string.pocketpc_partial_streaming_failed, result.pcName, state.name()));
                return null;
            }

            X509Certificate sunshineCert = pairingManager.getPairedCert();
            if (result.sunshineCertificateFingerprint != null && sunshineCert != null
                    && !Hex.constantTimeEquals(result.sunshineCertificateFingerprint, Hex.fingerprint(sunshineCert))) {
                // The streaming server isn't the Sunshine the companion vouched for: undo.
                http.unpair();
                listener.onFailure(context.getString(R.string.pocketpc_error_sunshine_mismatch));
                return null;
            }

            ComputerDetails tracked = binder.getComputer(computer.uuid);
            if (tracked != null) {
                tracked.serverCert = sunshineCert;
            }
            binder.invalidateStateForComputer(computer.uuid);
            return tracked != null ? tracked : computer;
        } catch (ExecutionException | TimeoutException | IOException | org.xmlpull.v1.XmlPullParserException e) {
            LimeLog.warning("PocketPC: Moonlight pairing failed: " + e);
            listener.onPartialSuccess(context.getString(R.string.pocketpc_partial_streaming_failed, result.pcName, e.getMessage()));
            return null;
        }
    }

    private String deviceName() {
        String name = null;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.N_MR1) {
            name = Settings.Global.getString(context.getContentResolver(), Settings.Global.DEVICE_NAME);
        }
        if (name == null || name.trim().isEmpty()) {
            name = Build.MANUFACTURER + " " + Build.MODEL;
        }
        return name.trim();
    }
}
