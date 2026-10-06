# SDK contract cases

`cases.json` is a copy of `backend/tests/sdk_contract/cases.json` in the platform repository.

- Copied from platform commit `ee7339e` ("test: add SDK contract cases for the 13 routes the Unity SDK calls").
- The platform proves every case against its real handlers (`backend/tests/test_sdk_contract.py`).
- `SdkContractTests.cs` runs every case against the Editor Simulator's local backend.

Do not edit the copy. Each SDK release copies the platform's latest file and records the commit
above; a case that then fails means the simulator no longer matches the platform, and the
simulator is what changes.
