package com.pocketpc.app;

import android.app.Activity;
import android.app.AlertDialog;
import android.app.Service;
import android.content.ComponentName;
import android.content.Intent;
import android.content.ServiceConnection;
import android.net.Uri;
import android.os.Bundle;
import android.os.IBinder;
import android.view.View;
import android.widget.Button;
import android.widget.EditText;
import android.widget.TextView;
import android.widget.Toast;

import com.google.zxing.integration.android.IntentIntegrator;
import com.google.zxing.integration.android.IntentResult;
import com.limelight.AppView;
import com.limelight.R;
import com.limelight.computers.ComputerManagerService;
import com.limelight.nvstream.http.ComputerDetails;
import com.limelight.preferences.AddComputerManually;
import com.limelight.utils.UiHelper;
import com.pocketpc.core.CompanionClient;
import com.pocketpc.core.PairingCode;
import com.pocketpc.core.PairingUri;

import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * "Pair with your PC": scan the QR code shown by the PC companion (or type its address and code).
 * Also opens for pocketpc://pair links, e.g. when the phone's own camera app scans the QR code.
 */
public class PairActivity extends Activity {
    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private final CountDownLatch serviceReady = new CountDownLatch(1);
    private ComputerManagerService.ComputerManagerBinder managerBinder;
    private boolean busy;

    private Button scanButton;
    private Button pairButton;
    private EditText hostText;
    private EditText codeText;
    private View progressRow;
    private TextView progressText;
    private TextView resultText;

    private final ServiceConnection serviceConnection = new ServiceConnection() {
        @Override
        public void onServiceConnected(ComponentName name, IBinder binder) {
            managerBinder = (ComputerManagerService.ComputerManagerBinder) binder;
            serviceReady.countDown();
        }

        @Override
        public void onServiceDisconnected(ComponentName name) {
            managerBinder = null;
        }
    };

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        UiHelper.setLocale(this);
        setContentView(R.layout.activity_pocketpc_pair);
        UiHelper.notifyNewRootView(this);

        scanButton = findViewById(R.id.pocketpcScanButton);
        pairButton = findViewById(R.id.pocketpcPairButton);
        hostText = findViewById(R.id.pocketpcHost);
        codeText = findViewById(R.id.pocketpcCode);
        progressRow = findViewById(R.id.pocketpcProgressRow);
        progressText = findViewById(R.id.pocketpcProgressText);
        resultText = findViewById(R.id.pocketpcResult);

        scanButton.setOnClickListener(v -> startScan());
        pairButton.setOnClickListener(v -> pairWithTypedCode());
        findViewById(R.id.pocketpcClassicAdd).setOnClickListener(v ->
                startActivity(new Intent(this, AddComputerManually.class)));

        bindService(new Intent(this, ComputerManagerService.class), serviceConnection, Service.BIND_AUTO_CREATE);

        handleLink(getIntent());
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        handleLink(intent);
    }

    @Override
    protected void onDestroy() {
        super.onDestroy();
        worker.shutdownNow();
        unbindService(serviceConnection);
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        IntentResult scan = IntentIntegrator.parseActivityResult(requestCode, resultCode, data);
        if (scan == null) {
            super.onActivityResult(requestCode, resultCode, data);
            return;
        }
        if (scan.getContents() == null) {
            return; // cancelled
        }

        PairingUri uri = PairingUri.parse(scan.getContents().trim());
        if (uri == null) {
            Toast.makeText(this, R.string.pocketpc_not_a_pairing_qr, Toast.LENGTH_LONG).show();
            return;
        }
        // The user just scanned the QR on their own PC screen: no extra confirmation needed.
        startFlow(flow -> flow.runWithQr(uri));
    }

    private void handleLink(Intent intent) {
        Uri data = intent == null ? null : intent.getData();
        if (data == null || !PairingUri.SCHEME.equals(data.getScheme())) {
            return;
        }

        PairingUri uri = PairingUri.parse(data.toString());
        if (uri == null) {
            Toast.makeText(this, R.string.pocketpc_not_a_pairing_qr, Toast.LENGTH_LONG).show();
            return;
        }

        // Links can come from anywhere (another app, a web page), so ask first.
        new AlertDialog.Builder(this)
                .setTitle(getString(R.string.pocketpc_confirm_title, uri.pcName))
                .setMessage(getString(R.string.pocketpc_confirm_message, uri.pcName))
                .setPositiveButton(R.string.pocketpc_pair_button, (d, w) -> startFlow(flow -> flow.runWithQr(uri)))
                .setNegativeButton(android.R.string.cancel, null)
                .show();
    }

    private void startScan() {
        IntentIntegrator integrator = new IntentIntegrator(this);
        integrator.setDesiredBarcodeFormats(IntentIntegrator.QR_CODE);
        integrator.setPrompt(getString(R.string.pocketpc_scan_prompt));
        integrator.setBeepEnabled(false);
        integrator.setOrientationLocked(false);
        integrator.initiateScan();
    }

    private void pairWithTypedCode() {
        String rawHost = hostText.getText().toString().trim();
        String code = PairingCode.normalize(codeText.getText().toString());
        if (rawHost.isEmpty() || code == null) {
            Toast.makeText(this, R.string.pocketpc_enter_host_and_code, Toast.LENGTH_LONG).show();
            return;
        }

        String host = rawHost;
        int port = CompanionClient.DEFAULT_PORT;
        // "host:port" (but not a bare IPv6 address, which has several colons)
        int colon = rawHost.lastIndexOf(':');
        if (colon > 0 && rawHost.indexOf(':') == colon) {
            try {
                port = Integer.parseInt(rawHost.substring(colon + 1));
                host = rawHost.substring(0, colon);
            } catch (NumberFormatException ignored) {
                // Not a port; treat the whole input as the host name.
            }
        }
        if (host.startsWith("[") && host.endsWith("]")) {
            host = host.substring(1, host.length() - 1);
        }

        final String finalHost = host;
        final int finalPort = port;
        startFlow(flow -> flow.runWithCode(finalHost, finalPort, code));
    }

    private interface FlowStarter {
        void start(PairingFlow flow);
    }

    private void startFlow(FlowStarter starter) {
        if (busy) {
            return;
        }
        setBusy(true, getString(R.string.pocketpc_progress_connecting));
        resultText.setVisibility(View.GONE);

        worker.execute(() -> {
            try {
                serviceReady.await();
            } catch (InterruptedException e) {
                return;
            }
            starter.start(new PairingFlow(this, managerBinder, new PairingFlow.Listener() {
                @Override
                public void onProgress(String message) {
                    runOnUiThread(() -> progressText.setText(message));
                }

                @Override
                public void onSuccess(ComputerDetails computer) {
                    runOnUiThread(() -> {
                        setBusy(false, null);
                        Toast.makeText(PairActivity.this, getString(R.string.pocketpc_paired_toast, computer.name), Toast.LENGTH_LONG).show();
                        Intent i = new Intent(PairActivity.this, AppView.class);
                        i.putExtra(AppView.NAME_EXTRA, computer.name);
                        i.putExtra(AppView.UUID_EXTRA, computer.uuid);
                        i.putExtra(AppView.NEW_PAIR_EXTRA, true);
                        i.putExtra(AppView.SHOW_HIDDEN_APPS_EXTRA, false);
                        startActivity(i);
                        finish();
                    });
                }

                @Override
                public void onPartialSuccess(String message) {
                    runOnUiThread(() -> showResult(message, false));
                }

                @Override
                public void onFailure(String message) {
                    runOnUiThread(() -> showResult(message, true));
                }
            }));
        });
    }

    private void showResult(String message, boolean error) {
        setBusy(false, null);
        resultText.setText(message);
        resultText.setTextColor(error ? 0xFFE57373 : 0xFFFFB74D);
        resultText.setVisibility(View.VISIBLE);
    }

    private void setBusy(boolean isBusy, String message) {
        busy = isBusy;
        scanButton.setEnabled(!isBusy);
        pairButton.setEnabled(!isBusy);
        hostText.setEnabled(!isBusy);
        codeText.setEnabled(!isBusy);
        progressRow.setVisibility(isBusy ? View.VISIBLE : View.GONE);
        if (message != null) {
            progressText.setText(message);
        }
    }
}
