# SusaPlay SDK for Unity

Unity SDK package for integrating games with the SusaPlay platform.

Latest stable release tag: `v1.2.3`

This package is intended for games that run inside the SusaPlay shell on WebGL today. The current implementation is real and usable, but still evolving. We are intentionally exposing all implemented methods so game teams can integrate early and help us tune the SDK against real game behavior.

## Current Status

`com.susaplay.sdk` is currently best described as:

- usable for WebGL games running inside SusaPlay shell
- suitable for early partner integrations
- still under active API and behavior tuning

If you use a method that exists in the package, you may use it. If a feature is not listed under "Implemented Today", assume it is not production-ready yet unless explicitly coordinated with the platform team.

## Implemented Today

### Core

- `await SusaPlaySDK.Initialize()`
- `SusaPlaySDK.Auth`
- `SusaPlaySDK.CloudSave`
- `SusaPlaySDK.Analytics`
- `SusaPlaySDK.Webhooks`
- `SusaPlaySDK.Purchases`
- `SusaPlaySDK.Api`

### AuthModule

Available properties:

- `Auth.IsGuest`
- `Auth.IsAuthenticated`
- `Auth.Uid`
- `Auth.DisplayName`

Available methods:

- `Auth.GetSessionTokenAsync()` — the player's short-lived, game-scoped session token, for a game
  whose own backend runs outside this platform and needs to know which player is calling it. Send
  it as `Authorization: Bearer {token}`; that backend then asks the platform who it belongs to.
  Returns null for a guest, so a feature built on it must degrade rather than fail.

Notes:

- Auth state is populated from `SDK_READY`
- The current implementation updates player state when `SDK_AUTH_COMPLETE` is received
- Rich auth events and direct provider sign-in methods are not yet part of the package API

### CloudSaveModule

Available methods:

- `Task<SaveResult> Save(string slot, string data)`
- `Task<LoadResult> Load(string slot)`

Notes:

- `data` should be a valid JSON string
- The SDK tracks slot versions locally and sends them with save requests
- Empty cloud saves are handled as version `0`
- Conflict handling is still being tuned across real games
- Save data is stored in Cloud Storage server-side (not Firestore). The API response is unchanged — `data` is always returned as a string in the load response regardless of backend storage

### AnalyticsModule

Available methods:

- `void LogEvent(string name, string parameters = "{}")`
- `Task Flush()`

Notes:

- Events are queued locally and flushed through the platform shell
- The SDK flushes analytics automatically by default: on SDK ready, on app pause/quit, and every 5 minutes
- `Flush()` is still available for tests or important gameplay checkpoints
- The automatic flush interval and lifecycle flushes can be changed in `SusaPlay/Setup`
- Accepted analytics events can be forwarded to webhooks subscribed to `SDK_ANALYTICS_EVENT`
- Automatic event schema validation is still minimal in this version
- Analytics events are now streamed directly to BigQuery on the backend — no Firestore intermediate storage. No SDK change required

### WebhooksModule

Available methods:

- `void SendEvent(string eventName, string payloadJson = "{}")`
- `void SendEvent(string eventName, object payload)`

Use this for custom operational events that should be delivered through configured
developer webhooks. Do not send these through analytics unless you also want aggregate
analytics reporting.
On WebGL, webhook events are sent through the shell bridge as `SDK_CUSTOM_WEBHOOK_EVENT`.

### ApiModule

Available methods:

- `Task<ApiResult> Get(string endpoint)`
- `Task<ApiResult> Get(string endpoint, Dictionary<string, string> data)`
- `Task<ApiResult> Post(string endpoint, string dataJson)`
- `Task<ApiResult> Request(string method, string endpoint, string dataJson = null)`

Use this only for B2B partner iframe embeds where the game needs partner-owned
content or services, such as daily questions, live campaign config, or partner
event data.

The game passes only a relative endpoint:

```csharp
var result = await SusaPlaySDK.Api.Get("/daily/questions");
```

Admins configure the partner base URL, allowed endpoint prefixes, and auth secret
in the SusaPlay admin panel. The secret is injected server-side and is never exposed
to the game.

Notes:

- endpoints must be relative paths, for example `/daily/questions`
- only `GET` and `POST` are supported by the current backend bridge
- the bridge works only in partner iframe embeds
- use `CloudSave` to store player progress based on partner API content

### PurchasesModule

Available methods:

- `Task<PurchaseResult> StartPurchase(bool sandbox = false)`
- `Task<PurchaseResult> StartDirectItemPurchase(string itemId, bool sandbox = false)`
- `Task<PurchaseResult> StartWalletTopupPurchase(string topupPackId, bool sandbox = false)`
- `Task<StoreCatalogResult> GetStoreItems()`
- `Task<PlatformWalletResult> GetPlatformWallet()`
- `Task<TopupPacksResult> GetTopupPacks()`
- `Task<PlatformWalletSpendResult> SpendPlatformWallet(string itemId)`

Notes:

- The platform decides which payment provider takes the payment; the game never names one
- The shell opens the checkout and returns a structured `SDK_PURCHASE_RESPONSE`
- Successful results may include a refreshed game wallet or platform wallet snapshot
- Direct item purchases grant inventory after webhook confirmation
- Wallet top-ups credit the platform wallet after webhook confirmation

Example:

```csharp
var catalog = await SusaPlaySDK.Purchases.GetStoreItems();
if (!catalog.Success || catalog.Items.Length == 0)
{
    Debug.LogError("No store items found.");
    return;
}

var result = await SusaPlaySDK.Purchases.StartDirectItemPurchase(
    catalog.Items[0].itemId,
    true
);

if (result.Success)
{
    Debug.Log("Purchase status: " + result.Status);
}
else
{
    Debug.LogError($"Purchase failed: {result.ErrorCode} - {result.ErrorMessage}");
}
```

Current behavior notes:

- `sandbox = true` should be used during integration testing
- the shell may receive a checkout `return` / `close` message before the final
  webhook is fully reflected in UI state, so the shell now checks backend purchase
  status before responding to the SDK
- use `GetStoreItems()` to build your in-game shop UI from platform data
- use `GetTopupPacks()` to build SusaPlay wallet top-up UI
- use `SpendPlatformWallet(itemId)` only for items that are wallet-eligible

### App Check (v1.2.3+)

The SDK automatically requests a Firebase App Check token from the shell before every API call. No game-side configuration is required.

How it works:

- SDK sends `SDK_GET_APP_CHECK_TOKEN` to the shell bridge
- Shell calls Firebase App Check JS SDK and returns the token via `SDK_APP_CHECK_TOKEN_RESPONSE`
- SDK attaches the token as `X-Firebase-AppCheck` header on all backend requests
- Token is cached for 50 minutes; refreshed automatically

If the shell does not respond within 5 seconds (older shells), the SDK proceeds without the header — the backend currently allows this. When `APP_CHECK_ENFORCEMENT=true` is set on the backend functions, requests without a valid App Check token will be rejected with 401.

### Editor Tooling

Available menu:

- `SusaPlay -> Setup`

This creates or updates:

- `Assets/Resources/PlatformConfig.asset`

## Planned / In Progress

These features are planned, partially stubbed in the wider platform, or expected to evolve soon. Do not build hard dependencies on them yet unless you are coordinating directly with us.

- rewarded ads and banner ads
- richer auth flows and auth callbacks
- custom event helpers beyond raw analytics
- mobile runtime path
- broader purchase / economy modules
- stronger save conflict resolution and merge helpers
- better initialization result objects and diagnostics
- stronger validation in editor setup flow

## Installation

Use Unity Package Manager with a Git URL pinned to a release tag:

```json
{
  "dependencies": {
    "com.susaplay.sdk": "https://github.com/Susa-Games/com.susaplay.sdk.git#v1.5.0"
  }
}
```

You can also use:

- Unity -> Window -> Package Manager -> Add package from git URL
- `https://github.com/Susa-Games/com.susaplay.sdk.git#v1.5.0`

Versioning notes:

- Use `#v1.2.3` or another tag when you want a reproducible release install
- Use `#main` only if you intentionally want the moving head of development
- Use `#latest` for the newest published SDK branch maintained by SusaPlay
- Use `#release` for the current stable SDK branch maintained by SusaPlay

## Unity Version

- Unity `2021.3+`

## Setup

1. Install the package.
2. Open Unity menu `SusaPlay -> Setup`.
3. Paste your game key.
4. Click `Save`.
5. Confirm `Assets/Resources/PlatformConfig.asset` exists.

Notes:

- The game key is a public identifier, not a secret
- Player auth tokens are provided by the shell, not embedded in the game

## Basic Usage

### Initialize once

```csharp
using UnityEngine;
using susaplay.SDK;

public class GameBootstrap : MonoBehaviour
{
    private async void Start()
    {
        await SusaPlaySDK.Initialize();

        Debug.Log("Guest: " + SusaPlaySDK.Auth.IsGuest);
        Debug.Log("Authenticated: " + SusaPlaySDK.Auth.IsAuthenticated);
        Debug.Log("UID: " + SusaPlaySDK.Auth.Uid);
        Debug.Log("DisplayName: " + SusaPlaySDK.Auth.DisplayName);
    }
}
```

### Save game data

```csharp
var saveJson = "{\"level\":3,\"coins\":120}";
var saveResult = await SusaPlaySDK.CloudSave.Save("save_data", saveJson);

if (!saveResult.Success)
{
    Debug.LogError("Save failed: " + saveResult.Error);
}
```

### Load game data

```csharp
var loadResult = await SusaPlaySDK.CloudSave.Load("save_data");

if (loadResult.Success)
{
    Debug.Log("Loaded json: " + loadResult.Data);
    Debug.Log("Loaded version: " + loadResult.Version);
}
else
{
    Debug.LogError("Load failed: " + loadResult.Error);
}
```

### Log analytics

```csharp
SusaPlaySDK.Analytics.LogEvent("level_started", "{\"level\":3}");

// Optional: the SDK flushes automatically by default.
await SusaPlaySDK.Analytics.Flush();
```

### Send custom webhook data

```csharp
SusaPlaySDK.Webhooks.SendEvent(
    "score_threshold",
    "{\"matchId\":\"abc123\",\"score\":4200,\"threshold\":1000}"
);
```

### Fetch partner-owned data in a B2B embed

```csharp
var question = await SusaPlaySDK.Api.Get("/daily/questions");

if (!question.Success)
{
    Debug.LogError(question.ErrorCode + ": " + question.ErrorMessage);
    return;
}

Debug.Log("Question JSON: " + question.Data);

var progressJson = "{\"questionId\":\"q_2026_05_11\",\"seen\":true}";
await SusaPlaySDK.CloudSave.Save("daily_question_progress", progressJson);
```

For POST requests:

```csharp
var answerJson = "{\"questionId\":\"q_2026_05_11\",\"answer\":\"Paris\"}";
var result = await SusaPlaySDK.Api.Post("/daily/answer", answerJson);
```

## Commerce Quick Start

### Fetch game store items

```csharp
var catalog = await SusaPlaySDK.Purchases.GetStoreItems();
if (catalog.Success)
{
    foreach (var item in catalog.Items)
    {
        // `price` is what the platform wallet is charged. A real-money direct
        // purchase is priced in the payment provider's own catalogue and is not
        // returned here, so do not present this figure as a cash price.
        Debug.Log($"{item.name}: {item.price.amount} {item.price.currency}");
    }
}
```

### Top up the SusaPlay wallet

A pack carries two different numbers, and showing the wrong one tells the player a
price you are not about to charge them:

| Field | Meaning |
| --- | --- |
| `amount` + `currency` | what the player **receives** — e.g. 100 coins |
| `priceAmount` + `priceCurrency` | what the player **pays** — e.g. 0.99 USD |
| `priceAmountMinor` | the same cost in whole minor units — 99 |

Use `priceAmount` for display and `priceAmountMinor` for any comparison or
arithmetic; money is held as whole minor units on the server, so keeping the
integer authoritative is what stops a rounding error reaching a charge.

```csharp
var packs = await SusaPlaySDK.Purchases.GetTopupPacks();
if (packs.Success)
{
    foreach (var pack in packs.Packs)
    {
        // "Coin 100 — 0.99 USD for 100 coins"
        Debug.Log($"{pack.name} — {pack.priceAmount} {pack.priceCurrency} for {pack.amount} {pack.currency}");
    }

    if (packs.Packs.Length > 0)
    {
        await SusaPlaySDK.Purchases.StartWalletTopupPurchase(packs.Packs[0].topupPackId, true);
    }
}
```

`priceAmount`, `priceAmountMinor` and `priceCurrency` need SDK 1.7.0 or newer. On
an older SDK the fields do not exist and the price cannot be read at all.

### Spend platform wallet on a supported item

```csharp
var spend = await SusaPlaySDK.Purchases.SpendPlatformWallet("starter_pack");
if (spend.Success && spend.Wallet != null)
{
    Debug.Log("Platform coins after spend: " + spend.Wallet.coins);
}
```

## WebGL Runtime Expectations

This package currently depends on the SusaPlay WebGL shell contract:

- the game runs inside SusaPlay shell
- the shell responds to `SDK_INIT`
- the shell returns `SDK_READY`
- the shell provides tokens through `SDK_GET_TOKEN`
- API calls are proxied through shell/backend routing

Direct standalone WebGL hosting is not the supported path for this package today.

## Samples

Included sample:

- `SDK Smoke Test`

Import it from Unity Package Manager samples to validate the integration inside the SusaPlay shell.

## Versioning

We recommend pinned Git tags:

- `v1.0.0`
- `v1.1.0`
- `v1.1.1`
- `v1.2.3`

Guidelines:

- patch: fixes and behavior tuning
- minor: additive APIs
- major: breaking API changes

## Support Expectations

During this phase, the SDK should be treated as an integration partner package:

- implemented APIs are available for use
- behavior may be tuned based on real game feedback
- planned APIs may appear in later tagged releases

When reporting issues, include:

- game key
- Unity version
- SDK tag
- WebGL build version
- console logs from both Unity and shell
