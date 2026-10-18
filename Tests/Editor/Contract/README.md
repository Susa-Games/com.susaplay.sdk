# SDK contract cases

`cases.json` is a copy of `backend/tests/sdk_contract/cases.json` in the platform repository.

- Copied on 2026-10-06 from the platform's `feature/unity-sandbox` branch, after `ee7339e`, with the
  cases for the fixes in plan S7 (analytics drops an invalid `level_up`) and S11 (the last of an
  item is removed beside others).
- The platform proves every case against its real handlers (`backend/tests/test_sdk_contract.py`).
- `SdkContractTests.cs` runs every case against the Editor Simulator's local backend.

Do not edit the copy. Each SDK release copies the platform's latest file and records the commit
above; a case that then fails means the simulator no longer matches the platform, and the
simulator is what changes.
