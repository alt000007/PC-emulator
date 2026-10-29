using PocketPC.Core.Devices;
using PocketPC.Core.Pairing;
using QRCoder;

namespace PocketPC.Companion;

/// <summary>
/// Shows the pairing QR and the typed-code fallback. Pairing is only possible while this window
/// is open; closing it (or the 5-minute timeout) ends the pairing window.
/// </summary>
internal sealed class PairingForm : Form
{
    private readonly CompanionHost _host;
    private readonly PictureBox _qr = new() { SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(320, 320), Anchor = AnchorStyles.None };
    private readonly Label _code = new() { AutoSize = true, Anchor = AnchorStyles.None };
    private readonly Label _status = new() { AutoSize = true, Anchor = AnchorStyles.None, ForeColor = Color.DimGray };
    private readonly Label _where = new() { AutoSize = true, Anchor = AnchorStyles.None, ForeColor = Color.DimGray };
    private readonly System.Windows.Forms.Timer _countdown = new() { Interval = 1000 };
    private PairingSession? _session;
    private bool _paired;

    public PairingForm(CompanionHost host)
    {
        _host = host;
        Text = "Pair new phone";
        Icon = AppIcon.Get();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        var instructions = new Label
        {
            Text = "On your phone: open PocketPC → tap + → \"Scan QR code\".\nNo camera? Choose \"Enter code\" and type the code below.",
            AutoSize = true,
            Anchor = AnchorStyles.None,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        _code.Font = new Font("Consolas", 22, FontStyle.Bold);

        var newCode = new Button { Text = "New code", AutoSize = true };
        newCode.Click += (_, _) => Begin();
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        CancelButton = close;
        var buttons = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.None };
        buttons.Controls.AddRange([newCode, close]);

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        layout.Controls.AddRange([instructions, _qr, _code, _where, _status, buttons]);
        Controls.Add(layout);

        _countdown.Tick += (_, _) => UpdateCountdown();
        _host.Server.DevicePaired += OnDevicePaired;
        Begin();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _host.Server.DevicePaired -= OnDevicePaired;
        _countdown.Stop();
        if (!_paired)
        {
            _host.Pairing.Close();
        }

        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _countdown.Dispose();
            _qr.Image?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Begin()
    {
        var (session, uri) = _host.StartPairing();
        _session = session;
        _paired = false;

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(uri.ToString(), QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(8);
        using var stream = new MemoryStream(png);
        using var decoded = Image.FromStream(stream);
        _qr.Image?.Dispose();
        _qr.Image = new Bitmap(decoded);

        _code.Text = PairingCode.Format(session.Code);
        _where.Text = $"PC: {Environment.MachineName}   ·   {string.Join(", ", uri.Hosts)}";
        _countdown.Start();
        UpdateCountdown();
    }

    private void UpdateCountdown()
    {
        if (_paired || _session is null)
        {
            return;
        }

        var left = _session.ExpiresAt - DateTimeOffset.UtcNow;
        if (left <= TimeSpan.Zero || _host.Pairing.Current is null)
        {
            _countdown.Stop();
            _qr.Enabled = false;
            _status.Text = "This code expired or was used up. Click \"New code\" to try again.";
            _status.ForeColor = Color.DarkOrange;
            return;
        }

        _status.Text = $"Code works once and expires in {left:m\\:ss}.";
        _status.ForeColor = Color.DimGray;
    }

    private void OnDevicePaired(object? sender, PairedDevice device)
    {
        BeginInvoke(() =>
        {
            _paired = true;
            _countdown.Stop();
            _status.Text = $"✔ Paired with {device.Name}. The phone is now finishing streaming setup.";
            _status.ForeColor = Color.SeaGreen;
            var closeTimer = new System.Windows.Forms.Timer { Interval = 3500 };
            closeTimer.Tick += (_, _) =>
            {
                closeTimer.Dispose();
                Close();
            };
            closeTimer.Start();
        });
    }
}
