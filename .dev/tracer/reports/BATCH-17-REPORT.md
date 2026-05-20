# BATCH-17 Report — TRC-P4-007: tracer-aggregate.exe CLI

## Tasks Completed
- **TRC-P4-007** tracer-aggregate.exe CLI

## Files Created / Modified

### New Project — `src/Tracer.Aggregator.Cli/`
| File | Description |
|------|-------------|
| `Tracer.Aggregator.Cli.csproj` | CLI project: OutputType=Exe, AssemblyName=tracer-aggregate. References Tracer.Aggregator, Tracer.Bundle, Tracer.Adapters.Mock. Packages: System.CommandLine 2.0.0-beta4, Microsoft.Extensions.Logging.Abstractions. InternalsVisibleTo(Tracer.Tests.Integration). |
| `Program.cs` | Entry point: `public static async Task<int> Main(string[] args)` → `BuildRootCommand().InvokeAsync(args)`. Global options: `--nas-root`, `--log-level`. |
| `Logging/CliProgressReporter.cs` | Implements `IAggregationProgressReporter`. Creates log file in `%LOCALAPPDATA%\Tracer\cli-logs\tracer-aggregate-{date}.log`, prints `LOG_FILE=...` to stdout, writes stage messages to stderr. |
| `Commands/BuildCommand.cs` | `build` subcommand. Options: `--session-id`, `--time-range`, `--output` (required), `--nodes`, `--fast-state`, `--fast-state-entities`, `--label`, `--force`. Validates mutual exclusion, output path existence, parses time-range as `start..end`. Builds `AggregationRequest` and calls `AggregationOrchestrator.RunAsync`. |
| `Commands/ValidateCommand.cs` | `validate` subcommand. Argument: `bundle-path`. Option: `--strict`. Reads manifest via `BundleReader.ReadManifestAsync`, calls `BundleValidator.ValidateAsync`. Only supports directory bundles (not zip). |
| `Commands/InspectCommand.cs` | `inspect` subcommand. Reads manifest, prints bundle ID, schema version, creation time, time range, session context, statistics, participating nodes, file listing with sizes and SHA-256 prefix. |

### Solution Wiring
- `Tracer.sln` — Added `Tracer.Aggregator.Cli` project
- `tests/Tracer.Tests.Integration/Tracer.Tests.Integration.csproj` — Added project reference to Tracer.Aggregator.Cli

### New Integration Tests — `tests/Tracer.Tests.Integration/AggregatorEndToEndTests.cs`
7 test methods:
1. `BuildCommand_ProducesValidBundle` — FakeNode scenario → CLI build → `BundleValidator.ValidateAsync` succeeds
2. `BuildCommand_NeitherSessionNorTimeRange_ExitsNonZero` — Missing both flags → exit != 0
3. `BuildCommand_ExistingOutput_WithoutForce_ExitsNonZero` — Pre-existing output dir without `--force` → exit != 0
4. `ValidateCommand_ValidBundle_ExitsZero` — Build then validate → exit 0
5. `ValidateCommand_CorruptedManifest_ExitsOne` — Build, corrupt manifest.json → validate exits 1
6. `InspectCommand_OutputContainsBundleId` — Capture stdout, verify bundle ID present
7. `BuildCommand_LogFileAnnouncedOnStdout` — Verify first stdout line is `LOG_FILE=...`

## Test Results
- **Total before batch:** 284 (243 unit + 41 integration)
- **Total after batch:** 291 (243 unit + 48 integration)
- **New tests:** 7 integration
- **All tests pass:** ✓

## Key Implementation Notes
- `FakeNodeFixture.DisposeAsync` deletes the upload directory — snapshot it before disposal in tests
- Calm scenario's `system.session_start` event has no `sessionId` field in its JSON payload, so `SessionResolver` never extracts session markers; integration tests use `--time-range` derived from `IntervalManifest.IntervalStart/End.ToDateTimeOffset()`
- `BundleManifest` uses `SessionContext` (not `Session`) and `ParticipatingNodes` directly (not via `WriterInfo`)
- `CliProgram` alias needed in tests to avoid `Program` ambiguity with other referenced assemblies
