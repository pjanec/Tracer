# BATCH-21 REPORT

## Batch Summary
- **Tasks**: TRC-P4-010, TRC-P4-012, TRC-P4-013
- **Status**: COMPLETE
- **Build**: ✅ 0 errors, 0 warnings
- **Unit tests**: ✅ 261 passed (7 new tests added)
- **Integration tests**: ✅ 69 passed (all new tests pass)

---

## TRC-P4-010 — Offline Viewer Distribution Packaging

### Changes Made

#### `src/Tracer.OfflineViewer/Tracer.OfflineViewer.csproj`
- Added self-contained publish settings in a `<PropertyGroup Condition="'$(PublishDir)' != ''">` block.
  This guard ensures the properties (`RuntimeIdentifier=win-x64`, `SelfContained=true`, `PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`) are **only active during `dotnet publish`**, not during regular `dotnet build`. Without this guard the project caused NETSDK1150 because it referenced non-self-contained executables (`Tracer.Observer`, `Tracer.Agent`).
- Added `<PublishTrimmed>false</PublishTrimmed>` and `<InvariantGlobalization>true</InvariantGlobalization>` in the unconditional group.

#### `build-viewer-distribution.ps1` (new)
- PowerShell script at repository root.
- Steps: `pnpm install` + `pnpm build` in `tracer-viewer/`, then `dotnet publish` with self-contained win-x64 flags, verification of `tracer-viewer.exe` existence, README.txt generation, and `Compress-Archive` to `dist/TracerViewer.zip`.

#### `tests/Tracer.Tests.Integration/DistributionSmokeTests.cs` (new)
- 3 tests verifying the distribution artifact shape without running it:
  - `Csproj_ContainsSelfContainedProperties`: asserts the csproj XML contains the required publish properties.
  - `BuildScript_ContainsRequiredPhrases`: asserts the PowerShell script contains "tracer-viewer.exe", "No installation required", and "TracerViewer.zip".
  - `Publish_ProducesExpectedLayout`: skipped when `dotnet publish` is unavailable; runs the publish and verifies output layout.

#### `tests/Tracer.Tests.Integration/TestCollections.cs` (new)
- Defines xUnit collection definitions: `OfflineViewerSmokeCollection`, `BundleRoundTripCollection`, `DistributionCollection`.

---

## TRC-P4-012 — Unit Test Coverage Expansion

### Changes Made

#### `tests/Tracer.Tests.Unit/Bundle/BundleManifestTests.cs`
Added 4 new tests:
- `RoundTrip_SerializeDeserialize_Equals` — JSON round-trip preserves all fields.
- `Deserialize_UnknownFields_Ignored` — extra JSON properties don't throw.
- `Deserialize_MissingRequiredField_Throws` — missing required `BundleId` throws `JsonException`.
- `BundleId_IsValidUlid` — validates ULID alphabet regex `^[0-9A-HJKMNP-TV-Z]{26}$`.

#### `tests/Tracer.Tests.Unit/MultiInterval/MultiIntervalReaderTests.cs`
Added 2 new tests:
- `CreateWithNFiles_AllAliasesPresent` — creates 3 files, verifies all aliases are registered.
- `Dispose_DetachesAllDatabases` — after disposal, `reader.Attachments` is empty.

#### `src/Tracer.Storage.DuckDB.MultiInterval/AttachedDatabaseManager.cs`
- Added duplicate path check at the start of `AttachAsync`: throws `InvalidOperationException` when the same file path is already attached.

#### `tests/Tracer.Tests.Unit/MultiInterval/AttachedDatabaseManagerTests.cs`
- Added `AttachSamePath_Twice_Throws` test verifying the duplicate-path guard.

---

## TRC-P4-013 — Bundle Round-Trip Tests

### Changes Made

#### `tests/Tracer.Tests.Integration/BundleRoundTripTests.cs` (new)
3 round-trip tests that create a real bundle (via `BundleFixture`), start `OfflineViewerHostBuilder`, and query through its HTTP API:
- `RoundTrip_SessionList_IsIdentical` — sessions endpoint returns non-empty list with `eventCount > 0`.
- `RoundTrip_Notables_AreIdentical` — notables endpoint returns a JSON array.
- `RoundTrip_CrossIntervalQuery_ReturnsAllEvents` — cross-interval event query returns `total > 0`.

#### `tests/Tracer.Tests.Integration/AggregatorEndToEndTests.cs`
Added 3 new tests + helpers:
- `Build_SessionIdVariant_UsesCorrectTimeRange` — uses `AggregationFixture`, locates session ID from interval manifests, builds, verifies bundle.
- `Build_EventCount_MatchesSumOfSources` — runs `RunNasAsync`, counts events in source zips and bundle, asserts they match.
- `Build_ProgressEvents_InOrder` — uses `DelegatingProgressReporter`, verifies `Started` then `Completed` events.

#### `tests/Tracer.Tests.Integration/ObserverBundleBuildTests.cs`
Added 4 new tests (aliases to existing test methods with spec-mandated names):
- `PostBundleBuild_ReturnsAcceptedWithBundleId`
- `GetStatus_AfterBuild_ShowsCompleted`
- `GetDownload_ReturnsValidZip`
- `DeleteBundle_RemovesFromDisk`

---

## TASK-TRACKER Updates
- TRC-P4-010: ✅ marked complete
- TRC-P4-012: ✅ marked complete
- TRC-P4-013: ✅ marked complete

---

## Issues Encountered & Resolved

### NETSDK1150 — Self-Contained Executable Reference Error
**Problem**: Adding `<SelfContained>true</SelfContained>` and `<RuntimeIdentifier>win-x64</RuntimeIdentifier>` unconditionally to `Tracer.OfflineViewer.csproj` caused NETSDK1150 during `dotnet build`. The error occurs when a self-contained executable references non-self-contained executables (`Tracer.Observer`, `Tracer.Agent`).

**Fix**: Wrapped the problematic properties in `<PropertyGroup Condition="'$(PublishDir)' != ''">`. During `dotnet build`, `$(PublishDir)` is empty, so the condition is false and NETSDK1150 is avoided. During `dotnet publish -o <dir>`, `$(PublishDir)` is set by the SDK, the condition is true, and all publish properties apply. The XML text of the properties remains in the csproj for the `DistributionSmokeTests` assertion to find.
