# Upstream: Moonlight for Android

The phone app is a fork of [moonlight-stream/moonlight-android](https://github.com/moonlight-stream/moonlight-android)
(GPL-3.0). Because of that, PocketPC's phone app is also GPL-3.0, and its full source is in this repository.

| | |
|---|---|
| Upstream tag | `v12.2` |
| Upstream commit | `b48494cb96bff23d8886c4775cc4f39a1075495d` |
| moonlight-common-c | `874ac9548f1bd6f095ef2b435c42cdde460e7821` (with `enet` `aca8784`, `nanors` `b1e3c22`) |
| Imported | 2026-10-29 |

These were left out of the copy: `.git`, `fastlane/`, `store-assets/`, `appveyor.yml`, and the nested CI folders. They hold
Moonlight's store listings and CI, which don't apply here.

## What PocketPC changed in upstream files

Changes to Moonlight files are kept small so later upstream releases are easy to merge. New code
goes in `app/src/main/java/com/pocketpc/` and `pocketpc-core/`.

| File | Change |
|---|---|
| `settings.gradle` | Project name changed; includes the `pocketpc-core` build |
| `app/build.gradle` | New app id `com.pocketpc.app` (as upstream asks of forks), minSdk 21 → 24 (needed by the QR scanner), "PocketPC" app name, shared debug signing key, two new dependencies |
| `app/src/main/AndroidManifest.xml` | Registers `PairActivity` (with the `pocketpc://pair` link), the QR scanner orientation, and camera as optional |
| `app/src/main/java/com/limelight/PcView.java` | The "+" button opens PocketPC pairing (classic "add PC" is linked from there) |

## Added

- `app/src/main/java/com/pocketpc/app/`: pairing screen, pairing flow, phone identity (Android Keystore), and the paired-PC store
- `app/src/main/res/layout/activity_pocketpc_pair.xml`, `app/src/main/res/values/strings_pocketpc.xml`
- `app/debug-shared.keystore`: a non-secret signing key, so CI builds install over each other
- `pocketpc-core/`: the pairing protocol in plain Java, unit- and interop-tested without the Android SDK

## Updating to a newer Moonlight

1. Diff the new upstream tag against the table above.
2. Re-apply the four small changes.
3. Update this file.
