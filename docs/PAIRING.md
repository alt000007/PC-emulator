# Pairing and the phone ↔ PC API

How a phone becomes trusted by a PC, and how that trust is removed again.
Implementations: `pc/src/PocketPC.Core` (C#) and `android/pocketpc-core` (Java). Their tests share
the same known-answer vectors, and CI pairs the Java client against the real C# server.

## Identities

| Who | Key | Where it lives | Fingerprint |
|---|---|---|---|
| PC companion | ECDSA P-256, self-signed, 20 years | `%LOCALAPPDATA%\PocketPC\server-identity.bin`, encrypted with DPAPI for the Windows user | SHA-256 of the DER certificate, lowercase hex |
| Phone | ECDSA P-256, self-signed, 30 years | Android Keystore (non-exportable) | same |

Every request uses **mutual TLS** (TLS 1.2/1.3):
- The phone pins the PC's fingerprint.
- The PC identifies the phone by the fingerprint of its client certificate.

The PC drops connections from non-private addresses before the TLS handshake. Allowed addresses are LAN, link-local, Tailscale `100.64/10`, IPv6 ULA, and loopback.

## Pairing (one round trip)

1. **On the PC**, the user clicks **Pair new phone…**. The companion opens a pairing window with a one-time **code**:
   - 12 Crockford base32 characters (60 bits), shown as `XXXX-XXXX-XXXX`.
   - Valid for 5 minutes and for a single successful pairing.
   - Closed after 5 wrong attempts.
2. **The PC shows a QR code:**
   `pocketpc://pair?v=1&id=<pcId>&n=<name>&h=<host1,host2>&p=<port>&fp=<serverFp>&c=<code>`
3. **The phone gets the PC's details:**
   - *From the QR:* it knows `serverFp` up front and pins it.
   - *From a typed code:* it connects once to learn the fingerprint, pins that for the next request, and relies on step 5 to prove it.
4. **The phone sends** `POST /v1/pair {deviceName, deviceModel, proof}`, where
   `proof = HMAC-SHA256(key=code, "pocketpc-pair-v1|client|" + clientFp + "|" + serverFp)`.
5. **The PC checks the proof** against the fingerprints it sees on this TLS connection.
   - If it matches, the PC stores the device and replies with
     `serverProof = HMAC-SHA256(key=code, "pocketpc-pair-v1|server|" + serverFp + "|" + clientFp)`.
   - **The phone checks the server proof.** This defeats a machine that pretends to be the PC: it would present a different certificate, so neither proof would match.

The code itself never goes over the network. With a typed code, a man-in-the-middle on the LAN could try to brute-force the client proof offline. That means searching 2^60 HMACs within the 5-minute window, so it isn't practical.

## Streaming pairing (Sunshine), no PIN typing

1. **The phone adds the PC to Moonlight** and starts a normal Moonlight pairing with a random 4-digit PIN.
2. **The phone sends that PIN to the companion:** `POST /v1/sunshine/pair {pin}`. This request is mutually authenticated.
3. **The companion finds the pending request:** it looks in Sunshine's pending pairing requests (`GET /api/pin`) for the one from the phone's IP address. Then it submits the PIN (`POST /api/pin {pairing_id, pin, name}`).
   - Older Sunshine versions don't list pending requests. With those, the companion submits `{pin, name}` directly.
4. **Name and record:** the client name in Sunshine is `PocketPC <phone name> (<id6>)`. The companion stores the Sunshine client UUID on the device record.
5. **Optional extra check:** the companion includes the fingerprint of Sunshine's certificate in the pair response, if it can read it. The phone compares it with the certificate Moonlight paired with, and undoes the pairing on a mismatch.

## Revocation

On the PC, **Revoke** does three things:
- **Removes the device record.** From the next request on (even on an open keep-alive connection), the device API answers `403 not_paired`.
- **Disables the device's Sunshine client** (`POST /api/clients/update {enabled:false}`). Sunshine ends that client's running stream immediately. Then it **unpairs** the client (`POST /api/clients/unpair`).
- **Older Sunshine fallback:** if Sunshine has no `update` endpoint, the running app is closed instead.

The phone's "forget this PC" calls `POST /v1/unpair`, which does the same thing.

## API summary

| Method | Path | Auth | Purpose |
|---|---|---|---|
| GET | `/v1/hello` | any client cert | `{apiVersion, pcId, pcName, pairingOpen}` |
| POST | `/v1/pair` | any client cert + code proof | pair this phone |
| GET | `/v1/status` | paired | who am I to this PC |
| POST | `/v1/sunshine/pair` | paired | enter a Moonlight PIN in Sunshine |
| POST | `/v1/unpair` | paired | remove this phone |

Error bodies are `{"error": "<code>"}`. The codes are:
- **Pairing:** `wrong_code` (401), `too_many_attempts` (429), `pairing_expired` (410), `pairing_closed` (409).
- **Device API:** `not_paired` (403).
- **Sunshine step:** `invalid_pin`, `no_pairing_request`, `sunshine_login_needed`, `sunshine_unavailable`, `sunshine_rejected`.
