# Changelog

All notable changes to `com.susaplay.sdk` should be documented in this file.

## [1.7.0] - 2026-09-21

Added:

- `TopupPackEntry.priceAmount`, `.priceAmountMinor` and `.priceCurrency` — what a top-up pack
  actually costs the player. The server has always sent all three; `TopupPackEntry` declared no
  field for them, so `JsonUtility` dropped them on parse and a game had no way to show a price.

  The fields already on the entry, `amount` and `currency`, are what the player *receives* — 100
  coins. What they *pay* is `priceAmount` in `priceCurrency` — 0.99 USD. A store panel that shows
  `amount` as the price tells the player the wrong number, which is exactly what happened while
  the price fields were missing.

  Use `priceAmountMinor` (99) for comparison or arithmetic and `priceAmount` (0.99) for display.
  Money is whole minor units everywhere on the server, so keeping the integer authoritative is
  what stops a rounding error reaching a charge.

## [1.6.0] - 2026-09-16

Added:

- `SusaPlaySDK.Auth.GetSessionTokenAsync()` — the player's short-lived, game-scoped session token,
  for a game whose own backend runs outside this platform and needs to know which player is calling
  it. The SDK already fetched this token for its own HTTP calls; it simply had no way out, because
  `TokenManager` was held in a private field with no accessor. Nothing about how the token is
  obtained or cached changed.

  Returns null for a guest, who has no platform account and therefore no identity to mint a token
  for. A feature built on this must degrade for guests rather than fail.

  The first caller is NotAloneNever, whose match servers run on AWS: the game sends this token to
  its own match broker, and the broker asks the platform who it belongs to before spending money on
  a server. Without it there was no way for a game's own backend to tell one player from another,
  and no path existed that would ever mint the token — the shell only mints one when the game asks,
  and no game could ask.

Changed:

- `AuthModule.Initialize` takes an optional `TokenManager`. Only `SusaPlaySDK` calls it; a game
  that somehow did will still compile, and `GetSessionTokenAsync` then returns null with a warning
  rather than throwing.

## [1.5.0] - 2026-09-14

Breaking:

- The purchase API is no longer named after a payment provider. `StartXsollaPurchase` is now
  `StartPurchase`, `XsollaPurchaseResult` is now `PurchaseResult`, and `XsollaWalletSnapshot` is
  now `GameWalletSnapshot`. The wire messages `SDK_XSOLLA_PURCHASE` and
  `SDK_XSOLLA_PURCHASE_RESPONSE` are now `SDK_PURCHASE` and `SDK_PURCHASE_RESPONSE`.

  No deprecated aliases were kept. The old names were renamed rather than bridged because no
  published game was using them yet; a game built against 1.x must be rebuilt against 2.0.

Added:

- `PurchaseResult.WalletScope` — "live" or "sandbox", which wallet the balance landed in. The
  server always sent it; nothing read it, so a purchase that credited the developer test wallet
  looked identical to one that did not credit at all.

Removed:

- `StoreItemEntry.xsollaSku` and `TopupPackEntry.xsollaSku`. The server stopped returning the
  provider's internal SKU to clients — it is resolved server-side at checkout — so these fields
  were always empty.

Notes:

- Nothing about the purchase flow changed, only its names. The platform decides which provider
  takes the payment, and the SDK never needed to know which one that was — carrying the name in
  the public API meant that switching provider would have broken every game.

## [1.3.0] - 2026-08-11

Added:

- `session_start` is now emitted automatically during SDK init. Previously nothing in the SDK
  ever queued an event, so a game that never called `LogEvent` produced no analytics at all and
  its DAU / MAU / retention stayed empty.

Fixed:

- `TokenManager.GetTokenAsync` had no timeout. If the shell never sent `SDK_TOKEN_RESPONSE` the
  await never returned, and since every HTTP call goes through it the SDK hung silently with no
  error and no retry. It now times out after 10s and returns null, so the request fails with an
  observable 401 instead.
- `AppCheckManager` used a 5s timeout — the same value the shell uses for its own reply — so a
  slow first load could race and make the SDK give up just before the shell answered. A single
  timeout also disabled App Check permanently for the session. The timeout is now 8s, and App
  Check is only written off after two consecutive silences; any reply (even a null token) resets
  the counter.
- Bridge handlers in `TokenManager`, `AppCheckManager`, `AuthModule`, `ApiModule` and
  `PurchasesModule` unsubscribe before subscribing. A retried init previously left the old module
  attached to `WebGLBridge.OnMessageReceived` for the life of the page, which could call
  `SetResult` twice on the same `TaskCompletionSource`.

Changed:

- Version promoted from `1.3.0-pre.1` to `1.3.0`

## [1.3.0-pre.1] - 2026-08-07

Added:

- `SusaPlaySDK.LiveOps.RefreshAsync()` for cached public-manifest delivery
- Explicit models for missions, single offers, chained offers, and boosted IAP offers
- Persistent 15-minute cache with offline fallback and origin cache-busting support
- `SDKConfig.LiveOpsContentBaseUrl` and setup-wizard configuration

Changed:

- SDK prerelease version bumped to `1.3.0-pre.1`

## [1.2.3] - 2026-07-21

Added:

- `AppCheckManager` — automatically requests a Firebase App Check token from the shell before every HTTP call via new bridge messages `SDK_GET_APP_CHECK_TOKEN` / `SDK_APP_CHECK_TOKEN_RESPONSE`. Token cached for 50 minutes. No game-side configuration required.
- `HttpClient` now attaches `X-Firebase-AppCheck` header when an App Check token is available

Changed:

- `HttpClient` constructor accepts an optional `AppCheckManager` parameter (backward compatible — defaults to null)
- `SusaPlaySDK` initializes `AppCheckManager` alongside `TokenManager` on startup
- SDK version bumped to `1.2.3`

Notes:

- If the shell does not respond to `SDK_GET_APP_CHECK_TOKEN` within 5 seconds (older shells), the SDK proceeds without the header — backend currently allows this
- When `APP_CHECK_ENFORCEMENT=true` is set on backend Cloud Functions, requests without a valid App Check token will return 401
- Game shell must handle `SDK_GET_APP_CHECK_TOKEN` and return `SDK_APP_CHECK_TOKEN_RESPONSE` — updated game-shell includes this handler with lazy Firebase App Check initialization
- Cloud save `data` field is unchanged in the API response regardless of backend storage changes (GCS migration is transparent)
- Analytics events now stream to BigQuery directly on the backend — no SDK change required

## [Unreleased]

Added:

- Automatic analytics flushing on SDK ready, app pause/quit, and a configurable interval that defaults to 5 minutes
- `SusaPlaySDK.Webhooks.SendEvent(...)` for custom operational webhook events
- `PurchasesModule.GetStoreItems()`
- `PurchasesModule.GetTopupPacks()`
- `PurchasesModule.GetPlatformWallet()`
- `PurchasesModule.SpendPlatformWallet(string itemId)`
- `PurchasesModule.StartDirectItemPurchase(string itemId, bool sandbox = false)`
- `PurchasesModule.StartWalletTopupPurchase(string topupPackId, bool sandbox = false)`
- `SusaPlaySDK.Api` for B2B partner API bridge calls from partner iframe embeds

Changed:

- WebGL analytics now sends `SDK_LOG_EVENT` through the shell bridge instead of using the hardcoded production Functions URL
- WebGL custom webhook events now send `SDK_CUSTOM_WEBHOOK_EVENT` through the shell bridge
- Webhook subscriptions now use `CUSTOM_WEBHOOK_EVENT` for `SusaPlaySDK.Webhooks.SendEvent(...)`
- `AnalyticsModule.LogB2BEvent(...)` is deprecated; use `SusaPlaySDK.Webhooks.SendEvent(...)`
- Xsolla purchase messaging now supports explicit purchase intents
- README examples now describe direct-item purchases and wallet top-ups
- platform commerce contract now includes store catalog fetch for games
- README now documents partner API bridge usage and CloudSave progress persistence

Notes:

- `StartXsollaPurchase(bool sandbox = false)` remains for backward compatibility
- explicit direct-item and wallet-topup methods are now the recommended path
- partner API bridge calls require admin-configured partner `externalApi` settings

## [1.2.0] - 2026-04-30

Added today:

- `AnalyticsModule.LogB2BEvent(...)` for webhook-bound B2B JSON payloads
- updated package metadata and README examples for `v1.2.2`

## [1.1.1] - 2026-04-07

Fixed today:

- added missing Unity `.meta` files for package root docs and newly added SDK assets
- aligned package version metadata with the published Git tag
- updated README install examples to point to the latest stable tag

Notes:

- this is a packaging and release-hygiene patch with no intended runtime API changes

## [1.1.0] - 2026-04-04

Added today:

- `SusaPlaySDK.Purchases`
- `PurchasesModule.StartXsollaPurchase(bool sandbox = false)`
- purchase flow README guidance for Xsolla integration

Notes:

- Git install instructions now point to the `v1.1.0` release tag
- Package metadata is aligned with the current release version

## [1.0.0] - 2026-03-31

Initial Git-package release baseline from the platform monorepo.

Included today:

- `SusaPlaySDK.Initialize()`
- auth state surface
- cloud save load/save
- analytics queue + flush
- WebGL bridge
- setup wizard
- smoke test sample

Notes:

- This is an early integration release intended for real game usage and feedback
- Some wider platform capabilities are still planned or partially stubbed and are not guaranteed as stable SDK features in this tag
