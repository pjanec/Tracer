# BATCH-14 — Phase 4: Bundle Packaging + Bundle Validation

**Tasks:** TRC-P4-002, TRC-P4-003  
**Batch type:** New classes in existing assembly + unit tests

---

## Context

`Tracer.Bundle` now has the Format layer (from BATCH-13). This batch adds:
1. **TRC-P4-002** — the I/O layer: `BundleDirectoryWriter`, `BundleZipWriter`, `BundleReader`, `BundleExtractor` (all in `Tracer.Bundle/Packaging/`)
2. **TRC-P4-003** — validation: `BundleValidator`, `ValidationResult`, `ValidationError` (in `Tracer.Bundle/Validation/`)

---

## Prerequisite: project changes

Add `System.IO.Compression.ZipFile` package reference to `Tracer.Bundle.csproj`:

```xml
<PackageReference Include="System.IO.Compression.ZipFile" />
```

The package version is already in `Directory.Packages.props`.

---

## Task 1: TRC-P4-002 — Bundle Packaging

### 1.1 `src/Tracer.Bundle/Packaging/BundleDirectoryWriter.cs`

```csharp
public static class BundleDirectoryWriter
{
    /// <summary>
    /// Finalizes a staging directory into a valid bundle:
    /// writes manifest.json, computes SHA-256 for each file in manifest.Files,
    /// produces checksums.txt in sha256sum-compatible format (64-hex + 2-space + path),
    /// and creates annotations/.keep.
    ///
    /// All file paths listed in manifest.Files MUST already exist in stagingPath.
    /// </summary>
    public static async Task WriteAsync(
        string stagingPath,
        BundleManifest manifest,
        CancellationToken ct = default)
}
```

**Implementation notes:**
- Write `manifest.json` first using `JsonSerializer.SerializeAsync` with `BundleManifest.SerializerOptions`.
- For each entry in `manifest.Files`: compute SHA-256 by reading the file at `Path.Combine(stagingPath, entry.Path)`.
- Write `checksums.txt` with lines in the format `<64-hex>  <relative-path>` (two spaces between hash and path — this is the `sha256sum` convention). Use `\n` line endings (not `\r\n`).
- Create `annotations/.keep` as an empty file if it does not exist.

### 1.2 `src/Tracer.Bundle/Packaging/BundleZipWriter.cs`

```csharp
public static class BundleZipWriter
{
    /// <summary>
    /// Calls BundleDirectoryWriter.WriteAsync on stagingPath, then zips the result
    /// to outputZipPath using System.IO.Compression.ZipFile.
    /// </summary>
    public static async Task WriteAsync(
        string stagingPath,
        BundleManifest manifest,
        string outputZipPath,
        CancellationToken ct = default)
}
```

**Implementation:**
- Call `BundleDirectoryWriter.WriteAsync` first.
- Then call `ZipFile.CreateFromDirectory(stagingPath, outputZipPath)`.
- Use `await Task.Run(() => ZipFile.CreateFromDirectory(...), ct)` to avoid blocking.

### 1.3 `src/Tracer.Bundle/Packaging/BundleReader.cs`

```csharp
public static class BundleReader
{
    /// <summary>
    /// Opens a bundle that is either a directory or a .zip file and returns the manifest.
    /// For zip files, reads manifest.json entry from the archive without extracting.
    /// </summary>
    public static async Task<BundleManifest> ReadManifestAsync(
        string bundlePath,
        CancellationToken ct = default)
}
```

**Implementation:**
- If `bundlePath` is a directory: read `Path.Combine(bundlePath, BundleLayout.ManifestFile)` with `File.OpenRead`.
- If `bundlePath` is a file (zip): open `ZipFile.OpenRead(bundlePath)`, find the `manifest.json` entry at the archive root, open its stream. Deserialize and return — zip stream is closed after reading, no temp files.

### 1.4 `src/Tracer.Bundle/Packaging/BundleExtractor.cs`

```csharp
public static class BundleExtractor
{
    /// <summary>
    /// Unzips a .tracerbundle.zip to targetDirectory using ZipFile.ExtractToDirectory.
    /// </summary>
    public static async Task ExtractAsync(
        string zipPath,
        string targetDirectory,
        CancellationToken ct = default)
}
```

**Implementation:**
- Use `await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, targetDirectory), ct)`.

### 1.5 Unit tests

**File:** `tests/Tracer.Tests.Unit/Bundle/BundleDirectoryWriterTests.cs`

Test methods (9):

1. **`WriteAsync_CreatesManifestJson`** — after write, `manifest.json` exists in the output directory.

2. **`WriteAsync_CreatesChecksumsFileWithOneLinePerManifestFile`** — `checksums.txt` has one non-empty line per entry in `manifest.Files`.

3. **`WriteAsync_ChecksumsMatchActualFileHashes`** — for each line in `checksums.txt`, compute SHA-256 of the corresponding file and assert it matches the hex in that line.

4. **`WriteAsync_CreatesAnnotationsKeep`** — `annotations/.keep` exists after write.

5. **`BundleZipWriter_ProducesReadableZip`** — produced `.zip` file opens with `ZipArchive` without exception.

6. **`BundleZipWriter_ZipContainsManifestAtRoot`** — the archive contains an entry named `"manifest.json"` (at root level, no path prefix).

7. **`BundleReader_Directory_ReturnsMatchingManifest`** — read-back `BundleManifest` from directory; assert `bundleId` matches the one written.

8. **`BundleReader_Zip_ReturnsMatchingManifest`** — same via zip path; assert `bundleId` matches.

9. **`BundleExtractor_ExtractsManifestToTargetDirectory`** — `manifest.json` exists at `Path.Combine(targetDir, "manifest.json")` after extraction.

**Helper function** for tests:

```csharp
private static async Task<(string stagingPath, BundleManifest manifest)> CreateValidStagingAsync()
{
    var staging = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(staging);

    // Create the data files the manifest will reference
    await File.WriteAllBytesAsync(Path.Combine(staging, "events.duckdb"), new byte[] { 1, 2, 3, 4, 5 });
    await File.WriteAllBytesAsync(Path.Combine(staging, "slow_state.duckdb"), new byte[] { 6, 7, 8 });

    var manifest = new BundleManifest
    {
        BundleId = Ulid.NewUlid().ToString(),
        SchemaVersion = BundleSchemaV1.CurrentVersion,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        TracerVersion = "1.0.0",
        Writer = new BundleWriterInfo { Tool = "test", Version = "1.0", Host = "test-host" },
        TimeRange = new BundleTimeRange { StartUtc = DateTimeOffset.UtcNow, EndUtc = DateTimeOffset.UtcNow },
        SessionContext = new BundleSessionContext { SessionId = "s1", ScenarioId = "scenario1" },
        ParticipatingNodes = new[] { "node1" },
        FastStateScope = "none",
        FastStateEntities = Array.Empty<string>(),
        Statistics = new BundleStatistics { TotalEvents = 0, TotalSlowStateSamples = 0, TotalFastStateRows = 0, UncompressedBytes = 0 },
        Files = new[]
        {
            new BundleFileEntry { Path = "events.duckdb",    SizeBytes = 5, Sha256 = "" },
            new BundleFileEntry { Path = "slow_state.duckdb", SizeBytes = 3, Sha256 = "" },
        },
    };

    return (staging, manifest);
}
```

After each test that creates staging directories, call `Directory.Delete(staging, recursive: true)` in a `finally` block or use `try/finally` to clean up.

---

## Task 2: TRC-P4-003 — Bundle Validation

### 2.1 `src/Tracer.Bundle/Validation/ValidationError.cs`

```csharp
namespace Tracer.Bundle.Validation;

public record ValidationError(string Code, string Message);
```

### 2.2 `src/Tracer.Bundle/Validation/ValidationResult.cs`

```csharp
namespace Tracer.Bundle.Validation;

public record ValidationResult(IReadOnlyList<ValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
```

### 2.3 `src/Tracer.Bundle/Validation/BundleValidator.cs`

```csharp
public static class BundleValidator
{
    public static async Task<ValidationResult> ValidateAsync(
        string bundleDirectory,
        BundleManifest manifest,
        bool strict = false,
        CancellationToken ct = default)
}
```

**Validation steps (collect all errors, do not short-circuit):**

1. **Schema version**: if `!BundleSchemaV1.IsRecognized(manifest.SchemaVersion)`, add error with code `"SCHEMA_VERSION"`.

2. **Files exist**: for each entry in `manifest.Files`, check `File.Exists(Path.Combine(bundleDirectory, entry.Path))`. If missing, add error with code `"FILE_MISSING"` and message identifying the path.

3. **File sizes**: for each existing file, check `new FileInfo(...).Length == entry.SizeBytes`. If mismatch, add error with code `"SIZE_MISMATCH"`.

4. **Checksums (strict mode only)**: if `strict`, for each existing file compute SHA-256 and compare against `entry.Sha256`. If mismatch, add error with code `"CHECKSUM_MISMATCH"`.

5. **checksums.txt consistency**: read `checksums.txt` from the bundle directory. For each manifest file, find its line in checksums.txt and assert the hash there matches `entry.Sha256`. If `entry.Sha256` is empty (e.g., in tests where manifest was built without hashes), skip this step.

Actually, keep the checksums.txt check simpler: just skip the SHA256 consistency check if `entry.Sha256` is empty. The full checksums.txt check is tested indirectly via the `BundleDirectoryWriter` tests.

**Revised validation steps** (simpler, covering the test requirements):

1. Schema version recognized → error code `"SCHEMA_VERSION"` if not.
2. Each file in `manifest.Files` exists → error code `"FILE_MISSING"` if not.
3. Each existing file's size matches `entry.SizeBytes` → error code `"SIZE_MISMATCH"` if not.
4. In strict mode only: SHA-256 of each existing file matches `entry.Sha256` (only if `entry.Sha256` is non-empty) → error code `"CHECKSUM_MISMATCH"` if not.

All errors are collected into a `List<ValidationError>` and returned as `ValidationResult`.

### 2.4 Unit tests

**File:** `tests/Tracer.Tests.Unit/Bundle/BundleValidatorTests.cs`

Test methods (7):

1. **`ValidBundle_PassesValidation`** — create a bundle with `BundleDirectoryWriter.WriteAsync` (which computes real SHA256 hashes), then validate with `strict: false`; `IsValid` is `true`.

2. **`MissingFile_FailsWithFileNotFoundError`** — after writing a bundle, delete one of the data files; validate; `IsValid` is `false` and an error's `Message` contains the file name.

3. **`UnrecognizedSchemaVersion_FailsValidation`** — create a manifest with `SchemaVersion = 99`; validate; `IsValid` is `false` with a schema-version error.

4. **`CorruptedContent_NonStrictMode_Passes`** — after writing a bundle (with real SHA256 hashes in manifest), overwrite a data file with different bytes of the same size; validate with `strict: false`; `IsValid` is `true`.

5. **`CorruptedContent_StrictMode_FailsWithChecksumError`** — same corruption; validate with `strict: true`; `IsValid` is `false`.

6. **`SizeMismatch_FailsInBothModes`** — truncate a data file; validate with `strict: false`; `IsValid` is `false`.

7. **`MultipleErrors_AllReported`** — delete 2 listed files; validate; `result.Errors.Count >= 2`.

**Important for tests 4 and 5**: To test strict mode vs non-strict with real SHA256 values, you need a bundle where the manifest's `Files[].Sha256` entries are the real hashes of the original file contents. Use `BundleDirectoryWriter.WriteAsync` to create the bundle, then read the manifest back with `BundleReader.ReadManifestAsync` (which will have the correct hashes, since `WriteAsync` populates them), then corrupt a file. Wait — `WriteAsync` as spec'd writes the manifest.json and checksums.txt but does NOT update `manifest.Files[].Sha256` in the passed-in manifest (it reads from existing manifest). 

Actually re-reading the design: `BundleDirectoryWriter.WriteAsync(stagingPath, manifest, ct)` writes the manifest's content as-is to `manifest.json`, then computes SHA256 for each file listed in `manifest.Files` and writes those to `checksums.txt`. The manifest record itself is immutable (records with `init` properties).

For the validator tests, the validator checks `entry.Sha256` from the manifest (which comes from reading `manifest.json`). So:
- When you call `WriteAsync`, it serializes the manifest as-is (with whatever `Sha256` was in `manifest.Files`)
- The `checksums.txt` gets the real SHA256 values
- But the manifest.json has whatever was in `manifest.Files[].Sha256` at write time

For the test to work with strict mode (test 5), the manifest's `Files[].Sha256` must contain the real pre-corruption hash. 

**Solution**: Create a `BuildManifestWithRealHashes` helper that:
1. Creates staging dir with data files
2. Computes real SHA256 for each file
3. Returns a manifest where `Files[].Sha256` contains the real hashes
4. Calls `WriteAsync` with this manifest

This way, the manifest.json in the bundle has the correct SHA256, and the validator's strict check (`entry.Sha256` vs file hash) works.

Here's the helper approach:

```csharp
private static async Task<string> ComputeSha256Async(string filePath)
{
    using var stream = File.OpenRead(filePath);
    var bytes = await SHA256.HashDataAsync(stream);
    return Convert.ToHexString(bytes).ToLowerInvariant();
}

private static async Task<(string stagingPath, BundleManifest manifest)> CreateBundleWithRealHashesAsync()
{
    var staging = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(staging);

    var eventsData = new byte[] { 1, 2, 3, 4, 5 };
    var slowData = new byte[] { 6, 7, 8 };
    await File.WriteAllBytesAsync(Path.Combine(staging, "events.duckdb"), eventsData);
    await File.WriteAllBytesAsync(Path.Combine(staging, "slow_state.duckdb"), slowData);

    var eventsHash = await ComputeSha256Async(Path.Combine(staging, "events.duckdb"));
    var slowHash = await ComputeSha256Async(Path.Combine(staging, "slow_state.duckdb"));

    var manifest = new BundleManifest
    {
        BundleId = Ulid.NewUlid().ToString(),
        SchemaVersion = BundleSchemaV1.CurrentVersion,
        // ... (same as before)
        Files = new[]
        {
            new BundleFileEntry { Path = "events.duckdb",    SizeBytes = 5, Sha256 = eventsHash },
            new BundleFileEntry { Path = "slow_state.duckdb", SizeBytes = 3, Sha256 = slowHash },
        },
    };

    await BundleDirectoryWriter.WriteAsync(staging, manifest);
    return (staging, manifest);
}
```

Use this helper for tests 1, 4, 5 that need strict mode to work.

---

## Build & Test Validation

After completing both tasks:

1. `dotnet build Tracer.sln --configuration Release` — exit 0, 0 warnings, 0 errors.
2. `dotnet test Tracer.sln --configuration Release` — all 241 existing tests pass; the new 16 tests (9 BundleDirectoryWriter + 7 BundleValidator) also pass; total is 257.

---

## Suggested Commit Message

```
feat(bundle): Bundle Packaging and Bundle Validation (TRC-P4-002, TRC-P4-003)

TRC-P4-002:
- Add src/Tracer.Bundle/Packaging/ (BundleDirectoryWriter, BundleZipWriter, BundleReader, BundleExtractor)
- Add tests/Tracer.Tests.Unit/Bundle/BundleDirectoryWriterTests.cs (9 tests)

TRC-P4-003:
- Add src/Tracer.Bundle/Validation/ (BundleValidator, ValidationResult, ValidationError)
- Add tests/Tracer.Tests.Unit/Bundle/BundleValidatorTests.cs (7 tests)

Totals: 257 tests (216 unit + 41 integration) — 0 failures. Build: 0 warnings.
```
