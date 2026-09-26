# Changelog

All notable changes to the [`PWFAuth`](https://www.nuget.org/packages/PWFAuth) package.
Versions follow [semantic versioning](https://semver.org).

## 1.1.0 — 2026-09-26

### Added

- `PwfClient.ResetHardwareIdAsync(licenseKey, reason)` — instant self-service hardware-ID
  reset (`POST /api/customer/reset-hwid.php`), so a customer can move a license to a new
  PC without waiting for the developer. It unbinds every device of the key and ends all of
  its sessions. Subject to the application's policy: the developer can turn it off and sets
  the cooldown between resets (12 hours by default). Typical use: `LoginAsync` fails with
  `HWID_MISMATCH` or `DEVICE_LIMIT` → `ResetHardwareIdAsync` → `LoginAsync` again.
- `PwfSecurityException` (a `PwfException`), raised when an encrypted endpoint's reply is
  not encrypted but claims success.
- `PwfClientOptions.RaiseEventsOnCapturedContext` (default `true`).
- `PwfClientOptions.MaxRateLimitedBeats` (default 10): how many heartbeats in a row may be
  answered with a plain HTTP 429 before the session ends with `NETWORK_LOST`.
- `PwfErrorCodes`: `DeviceLimit` (`DEVICE_LIMIT`), `OpenAccessLimit` (`OPEN_ACCESS_LIMIT`),
  `KeyNotActive` (`KEY_NOT_ACTIVE`), `NoHwid` (`NO_HWID`), `RateLimited` (`RATE_LIMITED`),
  `SelfResetDisabled` (`SELF_RESET_DISABLED`). None of them ends a session.
- `PwfErrorCodes.ClockSkew` (`CLOCK_SKEW`), raised by the client through `SessionEnded` when
  the server keeps refusing heartbeats because this computer's clock is wrong.
- Every request sends `User-Agent: PWFAuth-dotnet/1.1.0 (+https://pwfauth.com)`. It is set
  per request; a caller-supplied `HttpClient` keeps its `DefaultRequestHeaders`.
- Unit tests (`tests/PWFAuth.Tests`), run by the publish workflow before packing.

### Changed

- **Security:** `PostEnvelopeAsync` and `GetEnvelopeAsync` — and so `LoginAsync`,
  `CheckKeyAsync`, `HeartbeatAsync`, `LogoutAsync`, `GetAppInfoAsync`, `GetTextsAsync`,
  `GetSlidesAsync`, `CheckUpdateAsync` and `TrackSocialClickAsync` — throw
  `PwfSecurityException` when a reply is plain JSON with `"success": true`. The server
  encrypts every reply of these endpoints once it has verified the request, so such a reply
  came from a proxy, a hosts-file redirect or a fake server; before 1.1.0 it signed the
  user in. Inside the heartbeat it counts as a failed beat. Plain failure replies are still
  returned as failed responses.
- `SessionEnded` is raised through the `SynchronizationContext` that was current when
  `StartHeartbeat()` (or `RunHeartbeatAsync`) was called — the UI thread in WinForms/WPF,
  so handlers may touch controls directly. Without a context (console apps, services) it is
  raised on the heartbeat thread, as before.
- A caller-supplied `HttpClient` is no longer modified: its `Timeout` is left as it is
  (setting it threw once that client had sent a request). `PwfClientOptions.Timeout` now
  applies only to the `HttpClient` the client creates; set your own client's timeout yourself.

### Deprecated

- `RequestHardwareResetAsync` — it queues a request that the PWF Auth dashboard does not
  show yet, so nobody can approve it. It still works; use `ResetHardwareIdAsync`.

### Fixed

- **Security — the kill switch could be dodged by changing the clock.** The heartbeat
  treated plain (unencrypted) refusals as transient and reset its failure count on them.
  The server's replay check answers a clock more than five minutes off with exactly such a
  refusal (HTTP 400 `CRYPTO_ERROR`, "Request expired"), so moving the clock after signing in
  kept the application running forever and deaf to bans; a proxy answering plain failures
  did the same. Now only an encrypted reply counts as an answer and resets the counts:
  `MaxHeartbeatFailures` beats in a row without one end the session with `CLOCK_SKEW` when
  every plain refusal among them blamed the clock, otherwise with `NETWORK_LOST`. Plain
  HTTP 429 replies (and non-JSON 429 pages) use the separate `MaxRateLimitedBeats` budget,
  and the two kinds never reset each other. Encrypted replies with an unknown error code
  stay transient.
- The `LogoutAsync` and `Dispose` docs said logging out frees the device seat. It ends the
  session only; the key stays bound to the machine. `ResetHardwareIdAsync` moves it.
- The samples read the test app's secret and license key from the `PWFAUTH_APP_SECRET` and
  `PWFAUTH_TEST_KEY` environment variables instead of hard-coding them.

## 1.0.1 — 2026-08-02

Documentation only — no code change from 1.0.0.

- README: "What the login reply contains" — the licence fields `LoginAsync` returns
  (expiry, days remaining, status, feature flags), with C# and VB.NET samples that handle
  the `null` `expires_at` of lifetime keys correctly.

## 1.0.0 — 2026-08-02

First stable release; no API change from 1.0.0-preview.1.

- License-key activation with hardware-ID binding and multi-device support.
- Encrypted sessions over the AES-256-CBC + HMAC-SHA256 envelope.
- Server-driven kill switch (ban, pause, expire, HWID reset, maintenance) with a
  consecutive-failure guard, so an unreachable server ends the session instead of leaving
  the app running.
- Free trials, remote texts and slides, OTA update checks.

## 1.0.0-preview.1 — 2026-08-02

First public preview. Session kill switch (ban/pause/expire/HWID-reset/maintenance) plus a
consecutive-failure guard so an unreachable server ends the session instead of leaving the
app running.
