# PocketPC

Use your Windows PC and play PC games on your Android phone:

1. **Stream from your PC** (main mode), built on Sunshine + Moonlight.
2. **Run Windows programs locally on the phone**, built on Wine + Box64/FEX (Winlator-based). *(Coming in a later step.)*

## Status

| Step | State |
|---|---|
| 0. Research report | ✅ [docs/RESEARCH.md](docs/RESEARCH.md) (includes decisions for your phone and Roblox) |
| 1. PC companion + pairing | ✅ this version |
| 2. Streaming with auto quality | next |
| 3. Touch controls + editor | – |
| 4. Game launcher + profiles | – |
| 5. Wake-on-LAN + remote access | – |
| 6. Local mode (Mode 2) | – |
| 7. Extras | – |
| 8. Polish | – |

### What works now
- **PC tray app.** It shows a pairing QR code, lists paired phones with a **Revoke** button, can start with Windows, and sets up Sunshine's login for you.
- **Phone app.** Tap **+** → **Scan QR code**. The phone pairs with the PC, then sets up streaming with no PIN typing. After that you see the PC's apps (Desktop, Steam Big Picture…) and can stream them. Streaming itself is Moonlight's proven engine, so it already works with this version.
- **No-camera fallback.** Type the PC's address and the code instead of scanning.
- **Revoking** a phone on the PC blocks it right away and ends its stream.

## Quick start

### On the PC (once)
1. Open this repository's **Actions** tab, open the latest green run, and download **PocketPC-Windows** (or take it from **Releases**). Unzip it.
2. Right-click `install-pc.ps1` → **Run with PowerShell** and approve the admin prompt. It:
   - installs **Sunshine** (with `winget`)
   - installs PocketPC into your user folder and adds a Start-menu shortcut
   - allows PocketPC through the firewall on **private (home) networks only**
3. The PocketPC window opens:
   - At **Sunshine: first-time setup needed**, click **Set up automatically…** → **Create a login for me**.
   - If you already use Sunshine, enter its login instead.

### On the phone
1. Download **PocketPC-Android** from the same Actions run, then open `PocketPC.apk` on the phone to install it. Allow "install unknown apps" when asked.
2. On the PC, click **Pair new phone…**. On the phone, open **PocketPC**, tap **+** → **Scan QR code**.
3. Wait for "Paired with …". Then tap **Desktop** (or a game) to stream.

## Your phone (Redmi Note 14 4G) and Roblox
- **Streaming:** fully supported (hardware HEVC/H.264). Defaults will be 1080p, 60 fps, HEVC.
- **Roblox:** play it by **streaming**. It runs on your real PC, so its anti-cheat works and you get PC graphics. Running the Windows version of Roblox on the phone (Mode 2) isn't possible: Roblox blocks Wine, and we won't work around that. Without the PC, use the official Roblox Android app.
- **Mode 2 on this phone:** expect only light programs. Its Mali GPU can't use the fast Turnip driver. Details are in [docs/RESEARCH.md](docs/RESEARCH.md#6-decisions-after-your-answers).

## Security
- **Pairing is required for every phone.**
  - Pairing only works while the PC's pairing window is open.
  - The code is used once, expires after 5 minutes, and is locked after 5 wrong tries.
  - The QR code pins the PC's certificate.
- **All traffic is encrypted** (mutual TLS for the companion, Sunshine's encrypted stream for video, audio and input). The phone's private key stays in the Android Keystore.
- **Nothing is exposed to the internet.**
  - The companion drops connections from public IP addresses before TLS starts.
  - Its firewall rule only covers private networks.
  - There is no UPnP and no port forwarding.
- **No passwords are stored in plain text.** The Sunshine login and the PC's key are encrypted with Windows DPAPI for your user. You sign in to Steam and other game launchers yourself; PocketPC never sees those passwords.
- Protocol details: [docs/PAIRING.md](docs/PAIRING.md).

## Judgment calls (sensible defaults, change any time)
1. **Phone app = fork of Moonlight for Android v12.2 (GPL-3.0).** This reuses its proven streaming engine, which makes the phone app GPL-3.0 as well. New phone code is Java, to match Moonlight's code. See [android/UPSTREAM.md](android/UPSTREAM.md).
2. **Sunshine isn't forked; the companion controls it** through Sunshine's local admin API. On a fresh Sunshine install, the companion creates a strong random admin login and keeps it encrypted.
3. **Companion port: TCP 47880.** Home network and Tailscale addresses only.
4. **Pairing code:** 12 characters (60 bits), shown as `XXXX-XXXX-XXXX`. Valid for 5 minutes and up to 5 wrong attempts.
5. **One-tap streaming pairing** finds the phone's Sunshine request by the phone's IP address. If something fails halfway, pairing again replaces the old entry.
6. **Revoking on Sunshine versions older than 2026.9** can't end just one phone's stream. In that case it closes the running game for everyone.
7. **App id `com.pocketpc.app`, minimum Android 7.0** (the QR scanner needs it).
8. **CI APKs are signed with a committed, non-secret key** (`android/app/debug-shared.keystore`). That lets each new build install over the previous one. It must never be used for a store release.
9. **PC app:** .NET 10 (LTS) WinForms, published as a single self-contained `PocketPC.exe` (about 64 MB), so you don't need to install .NET. Settings and logs are in `%LOCALAPPDATA%\PocketPC`.
10. **Remote access will use Tailscale** (your answer: "do what you think is best").

## Tests

| Requirement | How it's checked |
|---|---|
| Pairing | Automated: 81 C# tests (`pc/tests`, run on Windows in CI) plus Java tests. **Interop:** CI pairs the Java phone client with the real C# companion over mutual TLS (QR and typed code, wrong code, impostor PC, unknown phone). |
| Revoking blocks immediately | Automated: the very next request on the same connection gets `403`, and the Sunshine client is disabled and unpaired. |
| Launching a game and streaming at stable FPS on home Wi-Fi | **Manual**, needs your PC and phone: pair, tap Desktop, turn on Moonlight's performance overlay. Auto quality comes in step 2. |

## Development

```bash
# PC side (any OS; the tray app builds everywhere but only runs on Windows)
cd pc
dotnet test tests/PocketPC.Core.Tests
dotnet publish src/PocketPC.Companion -c Release -o out    # → out/PocketPC.exe

# Headless companion with a simulated Sunshine (prints a pairing link)
dotnet run --project tools/PocketPC.TestHost -- --port 47880

# Phone pairing protocol (no Android SDK needed); add the env var to run interop tests
cd android/pocketpc-core
POCKETPC_TESTHOST_URI_FILE=/path/to/uri.txt ../gradlew test   # TestHost --uri-file writes it

# Phone app (needs Android SDK 37 + NDK 29.0.14206865, or just open android/ in Android Studio)
cd android && ./gradlew assembleNonRootDebug
```

### Layout
```
pc/src/PocketPC.Core         pairing, phone API (Kestrel, mutual TLS), devices, Sunshine API client
pc/src/PocketPC.Companion    Windows tray app (WinForms)
pc/tools/PocketPC.TestHost   headless companion for CI/dev
pc/tests                     xUnit tests
android/                     phone app (Moonlight fork) + pocketpc-core (pairing protocol)
scripts/install-pc.ps1       PC installer
docs/                        research report, pairing protocol
```

## License and credits
GPL-3.0 (see [LICENSE](LICENSE)). Built on:
- [Moonlight for Android](https://github.com/moonlight-stream/moonlight-android) (GPL-3.0)
- [Sunshine](https://github.com/LizardByte/Sunshine) (GPL-3.0, installed separately, unmodified)
- [QRCoder](https://github.com/codebude/QRCoder) (MIT)
- [zxing-android-embedded](https://github.com/journeyapps/zxing-android-embedded) (Apache-2.0)
