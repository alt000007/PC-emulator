# PocketPC – Step 0 research report (Oct 2026)

## 1. Streaming (Mode 1)

| Option | Protocol / codecs | Latency (5 GHz LAN) | License | Verdict |
|---|---|---|---|---|
| **Sunshine** (host) + **Moonlight** (client) | GameStream-compatible: RTSP setup, encrypted RTP video/audio, ENet control. H.264 / HEVC / AV1, HDR, 4:4:4 (HEVC/AV1) | ~20–40 ms glass-to-glass | GPL-3.0 (both) | **Use** |
| Apollo / Vibepollo (Sunshine forks) | Same + built-in virtual display (SudoVDA, Windows only) | Same | GPL-3.0 | Fallback host if needed |
| Parsec | Proprietary BUD protocol, H.264/HEVC | Very low | Proprietary, SDK discontinued | Can't integrate |
| Steam Link / Remote Play | Proprietary | Low | Proprietary | Can't integrate; coexists fine |

**Plan:** *control* Sunshine, don't fork it. Install upstream Sunshine and drive it from our tray app through its local
HTTPS config API (apps list, pairing PIN, unpair/revoke, encoder settings), bound to LAN/tailnet only.
*Fork* **moonlight-android**: it already has decoding, the perf overlay, controllers, mouse/keyboard, and basic WoL.
Our app then has to be **GPL-3.0** and open source. That's fine, we just publish the source.

Gaps we build ourselves:
- QR/one-tap pairing that auto-submits Sunshine's PIN
- Game library scan (Steam/Epic/GOG/Xbox) and "sleep when done"
- File transfer and settings sync
- Mic → PC: upstream Sunshine has no mic channel, so we add an Opus stream on our own TLS side channel into a virtual audio device
- Auto quality: Sunshine can't change bitrate mid-stream upstream. v1 runs a network probe before the stream plus a fast
  step-down reconnect (resolution/FPS/bitrate). v2 is a small patch for live bitrate if v1 isn't smooth enough.
- Virtual display: the open-source Virtual Display Driver (MIT), toggled per session by Sunshine prep-commands. This keeps us on upstream Sunshine.

Remote access goes through **Tailscale** (WireGuard). Clients are BSD-3, and Headscale is a self-hosted option.
No UPnP and no port forwarding. Sunshine only listens on the LAN and the `100.x` tailnet interface.

## 2. Local Windows programs (Mode 2)

- **Winlator** (brunodev85, MIT; the newest builds publish their source late) uses this stack:
  - Wine (LGPL)
  - Box64 (MIT) or FEX-Emu (MIT)
  - DXVK (zlib) and VKD3D-Proton (LGPL)
  - Mesa **Turnip** (MIT), a Vulkan driver for Adreno
  - It runs inside a PRoot/glibc rootfs.

  Forks like Ludashi and Cmod add containers, presets, and drivers. **Plan:** take Winlator's last public source as
  the Mode 2 module. MIT code can go into our GPL-3.0 app. Wine and the other components ship as separate binaries,
  with LGPL/GPL notices and a source offer.
- **Realistic performance:** on Snapdragon 8 Gen 2/3/Elite with Turnip, DX9–DX11 games and indie titles from about
  2005–2016 run at 30–60 fps at 720p. DX12 (VKD3D) is hit-and-miss, and modern AAA mostly isn't playable.
  Mali, Xclipse, and PowerVR GPUs have no Turnip and fall back to their vendor Vulkan drivers or VirGL, so expect noticeably worse results.
- **Full Windows VM (QEMU):** Android gives third-party apps no KVM (pKVM/AVF is only for protected Linux VMs), so QEMU
  has to use pure software emulation (TCG). An x86 Windows guest takes minutes to boot and is barely usable. Windows 11
  ARM is only somewhat better, has no GPU acceleration, and needs a Windows license. **Verdict:** ship it as
  "Experimental – desktop apps only, very slow", hidden behind a toggle.

## 3. Which mode for which game

| Game type | Stream (Mode 1) | Local (Mode 2) |
|---|---|---|
| Modern AAA, DX12, ray tracing | ✅ best | ❌ |
| Kernel anti-cheat (Vanguard, FACEIT, EAC/BattlEye kernel, Ricochet) | ✅ (runs on the real PC) | ❌ never; we won't attempt workarounds |
| Older/indie, DX9–11, offline single-player | ✅ | ✅/🟡 with an Adreno GPU |
| Strategy, desktop apps, office | ✅ full desktop | 🟡 lighter apps |
| Competitive shooters | 🟡 fine on LAN, worse over relay | ❌ |

Other Mode 2 blockers:
- DRM with kernel drivers (StarForce, some SecuROM)
- Always-online launchers (flaky)
- Games needing more than about 6 GB of RAM
- VR

## 4. Minimum phone specs

- **Mode 1:** Android 9+ with hardware HEVC decoding (most phones from 2018 on).
  - AV1 decoding (Snapdragon 8 Gen 2+, Tensor, Dimensity 1000+) gives better quality per Mbps.
  - 5 GHz Wi-Fi 5 is the minimum and Wi-Fi 6 is recommended. The PC should be wired to the router.
- **Mode 2:**
  - Minimum: arm64, Android 10+, 8 GB RAM, 10 GB free storage.
  - Recommended: Snapdragon 8 Gen 1 or newer (Adreno 7xx) with 12 GB RAM.

## 5. Recommended architecture

```
Windows PC                                  Android phone (one app, GPL-3.0)
┌─────────────────────────────┐            ┌──────────────────────────────────┐
│ PocketPC Companion (.NET 8  │  TLS side  │ Launcher UI (Kotlin/Compose):    │
│ tray app)                   │◄─channel──►│ PCs, games, profiles, files      │
│ - pairing QR, device revoke │  (mTLS,    │ Stream engine (moonlight fork)   │
│ - game scan, sleep, files   │  pinned    │ + auto quality, overlay, mic     │
│ - mic sink, VDD toggle      │  certs)    │ Touch overlay + editor           │
│ - drives Sunshine API       │            │ Local mode (Winlator-based)      │
│ Sunshine (upstream, service)│◄─stream───►│ WoL, Tailscale-aware discovery   │
└─────────────────────────────┘            └──────────────────────────────────┘
        └──────── Tailscale/WireGuard when away from home ────────┘
```

Settings sync uses the PC companion as the hub, with no cloud. Each device has a certificate, and revoking one in the
tray app deletes it from both Sunshine and the side channel at once.
