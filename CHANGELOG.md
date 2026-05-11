# Changelog

All notable changes to `com.susaplay.sdk` should be documented in this file.

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
