# SusaPlay SDK for Unity

Unity SDK package for integrating games with the SusaPlay platform.

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

### AuthModule

Available properties:

- `Auth.IsGuest`
- `Auth.IsAuthenticated`
- `Auth.Uid`
- `Auth.DisplayName`

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

### AnalyticsModule

Available methods:

- `void LogEvent(string name, string parameters = "{}")`
- `Task Flush()`

Notes:

- Events are queued locally and flushed through the platform shell
- Automatic event schema validation is still minimal in this version

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
- purchase / economy modules
- stronger save conflict resolution and merge helpers
- better initialization result objects and diagnostics
- stronger validation in editor setup flow

## Installation

Use Unity Package Manager with a Git URL pinned to a tag:

```json
{
  "dependencies": {
    "com.susaplay.sdk": "https://github.com/YOUR_ORG/com.susaplay.sdk.git#v1.0.0"
  }
}
```

You can also use:

- Unity -> Window -> Package Manager -> Add package from git URL

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
await SusaPlaySDK.Analytics.Flush();
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
- `v1.0.1`
- `v1.1.0`

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
