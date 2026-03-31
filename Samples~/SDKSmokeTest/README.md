# SDK Smoke Test Sample

This sample creates a minimal Unity project flow to validate SusaPlay SDK end-to-end on your platform.

## What this sample does

- Calls `await SusaPlaySDK.Initialize()`
- Logs auth state (`IsGuest`, `IsAuthenticated`, `Uid`, `DisplayName`)
- Runs positive scenarios:
  - Sends multiple analytics events with random payload
  - Calls `Analytics.Flush()`
  - `Load(slot)` before save
  - `Save(slot, randomData)`
  - `Load(slot)` after save
- Runs negative/malformed scenarios (toggleable):
  - Empty slot / null slot
  - Malformed JSON body
  - Empty JSON body
  - Wrong-type payload
  - Large payload
  - Unicode slot
  - Analytics with empty name / malformed params
- Runs stress scenarios (toggleable):
  - Concurrent CloudSave writes
  - Rapid analytics queue + flush
  - Re-initialize after ready
- Logs every response to Unity Console with `[SusaPlay SmokeTest]` prefix

## Files

- `SusaPlaySdkSmokeTester.cs` (runtime MonoBehaviour)
- `Editor/SusaPlaySmokeTestTools.cs` (menu tool to create scene quickly)

## Setup steps

1. Import this sample from Package Manager:
   - `com.susaplay.sdk` -> `Samples` -> `SDKSmokeTest` -> `Import`
2. Set your game key:
   - Unity menu -> `SusaPlay` -> `Setup`
3. Create test scene:
   - Unity menu -> `SusaPlay` -> `Smoke Test` -> `Create Test Scene`
4. Open the created scene:
   - `Assets/Scenes/SusaPlaySmokeTest.unity`
5. Build WebGL and upload to your platform as a real game build.

## Runtime usage

- On start, smoke test runs automatically.
- You can also run manually from component context menu:
  - `Run Smoke Test Now`
- Optional repeated run:
  - enable `Repeat Run`
  - set `Repeat Every Seconds`
- Coverage controls in inspector:
  - `Run Negative Cases`
  - `Run Stress Cases`
  - `Concurrent Cloud Save Ops`
  - `Stress Iterations`
  - `Default Timeout Ms`

## Expected logs

Look for:
- `SMOKE TEST START`
- `CASE PASS [...]`
- `CASE FAIL [...]`
- `CASE [...] (observation)`
- `SMOKE TEST SUMMARY ... pass=... fail=... observe=...`
- `SMOKE TEST PASSED` (or failed summary)

## Notes

- For WebGL testing, run this build through SusaPlay shell (player portal or game-shell), not direct `file://`.
- If auth token is unavailable (guest mode), some protected APIs may return expected auth errors; this is still useful signal.
