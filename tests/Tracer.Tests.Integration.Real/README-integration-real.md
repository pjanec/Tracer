# Tracer.Tests.Integration.Real — Integration Test Suite

This project contains integration tests that require the customer's simulation harness to be available. Tests are **automatically skipped** (not failed) on developer machines where the harness is not installed.

## Running the Tests

### Prerequisites

1. Simulation harness executable installed and accessible.
2. Environment variable `TRACER_HARNESS_PATH` set to the full path of the harness executable.

### Run All Real Integration Tests

```bash
export TRACER_HARNESS_PATH=/path/to/simulation-harness.exe
dotnet test tests/Tracer.Tests.Integration.Real -c Release
```

### Run Without Harness (Shows All Tests as Skipped)

```bash
dotnet test tests/Tracer.Tests.Integration.Real -c Release
# Expected: 0 Failed, N Skipped
```

### Run Soak Tests (Requires Harness, Runs for 48 h)

```bash
export TRACER_HARNESS_PATH=/path/to/simulation-harness.exe
dotnet test tests/Tracer.Tests.Integration.Real -c Release --filter "Category=SoakTest"
```

## CI Lanes

- **Nightly CI lane:** Runs `RealIntegrationTest` category tests (requires harness environment).
- **PR CI lane:** Does NOT run this project — these tests are skipped on standard PRs.
- **Release gate:** All `RealIntegrationTest` tests must pass before a production release.
- **Soak tests:** Run on-demand or on a weekly schedule; require 48 h of runtime.

## Environment Variables

| Variable | Description | Required |
|----------|-------------|----------|
| `TRACER_HARNESS_PATH` | Full path to simulation harness executable | Yes (tests skip if absent) |

## External Team Requirements

See `docs/phase11-handoff-notes.md` for requirements from the simulation team and sync team.
