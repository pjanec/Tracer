# BATCH-48 Instructions — TRC-P8-018: Phase 8 Integration & E2E Tests

## Context

You are a developer implementing **TRC-P8-018** in `d:\Work\Tracer`.

**Phase 8 backend unit tests are already complete** (done in BATCH-42, 43, 44). What remains:
- Backend **integration** tests for the round-trip flows
- E2E Playwright **stub** tests (skipped until full stack is running)
- Marking TRC-P8-018 ✅ in TASK-TRACKER

**Solution:** `d:\Work\Tracer\Tracer.sln`  
**Frontend:** `d:\Work\Tracer\tracer-viewer\`

---

## Key Architecture Reference

### Storage and Endpoints
- `Tracer.Storage.Annotations`: `SqliteAnnotationStore`, `BundleAnnotationStore`, `IAnnotationStore`
- `Tracer.Storage.SavedViews`: `SqliteSavedViewStore`, `ISavedViewStore`
- `Tracer.WebApi.Endpoints`: `AnnotationEndpoints`, `SavedViewEndpoints`, `TriggerEvalEndpoints`, `BundleEndpoints`
- `Tracer.WebApi.Queries`: `TriggerEvalService`
- `Tracer.Aggregator`: `AggregationOrchestrator` (takes optional `IAnnotationStore`)
- `Tracer.OfflineViewer`: `OfflineViewerHostBuilder`, `LazyBundleAnnotationStore`, `LazyBundleSavedViewStore`
- `Tracer.TestHarness`: `AggregationFixture`, `ObserverFixture`

### Session ID Strategy for Round-Trip Tests
When `AggregationRequest.TimeRange` is used (not `SessionId`), the aggregation passes `request.SessionId ?? ""` to `AnnotationsExporter.ExportAsync`. This means annotations with `sessionId = ""` are exported. The integration tests use `sessionId = ""` for all annotations to avoid needing the real FakeNode session ID.

### ObserverFixture Pattern for Extra Services
```csharp
_observer = await ObserverFixture.CreateAsync(
    configureExtraServices: services =>
    {
        // Access ObserverConfig to get DataRoot
        services.AddSingleton<IAnnotationStore>(sp =>
        {
            var cfg = sp.GetRequiredService<ObserverConfig>();
            var path = Path.Combine(cfg.DataRoot, "annotations.db");
            var store = new SqliteAnnotationStore(path, NullLogger<SqliteAnnotationStore>.Instance);
            store.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            return store;
        });
        // Other services...
    },
    configureExtraApp: app =>
    {
        AnnotationEndpoints.Map(app);
        BundleEndpoints.Map(app);
    });
```

---

## Tasks

### Task 1 — Update Integration Tests Project (.csproj)

**File: `tests/Tracer.Tests.Integration/Tracer.Tests.Integration.csproj`**

Add the following project references (they are needed for SqliteAnnotationStore and SqliteSavedViewStore):

```xml
<ProjectReference Include="..\..\src\Tracer.Storage.Annotations\Tracer.Storage.Annotations.csproj" />
<ProjectReference Include="..\..\src\Tracer.Storage.SavedViews\Tracer.Storage.SavedViews.csproj" />
<ProjectReference Include="..\..\src\Tracer.Observer\Tracer.Observer.csproj" />
```

Also add a package reference if not already present:
```xml
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
```

### Task 2 — Add TestCollections

**File: `tests/Tracer.Tests.Integration/TestCollections.cs`**

Add these collection definitions at the bottom:

```csharp
[CollectionDefinition("AnnotationsRoundTrip", DisableParallelization = true)]
public sealed class AnnotationsRoundTripCollection { }

[CollectionDefinition("SavedViewsRoundTrip", DisableParallelization = true)]
public sealed class SavedViewsRoundTripCollection { }

[CollectionDefinition("TriggerEvalIntegration")]
public sealed class TriggerEvalIntegrationCollection { }
```

### Task 3 — AnnotationsRoundTripTests.cs

**File: `tests/Tracer.Tests.Integration/AnnotationsRoundTripTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Adapters.Mock.Storage;
using Tracer.Aggregator;
using Tracer.Core.Abstractions;
using Tracer.Observer.Configuration;
using Tracer.OfflineViewer;
using Tracer.Storage.Annotations;
using Tracer.TestHarness;
using Tracer.TestHarness.Observer;
using Tracer.WebApi.Bundles;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Endpoints;
using Xunit;

namespace Tracer.Tests.Integration;

/// <summary>
/// Integration tests: create annotations in Observer, build bundle, verify in OfflineViewer.
/// TRC-P8-018 SC-13 and SC-14.
/// </summary>
[Collection("AnnotationsRoundTrip")]
public sealed class AnnotationsRoundTripTests : IAsyncLifetime
{
    private AggregationFixture? _nasFixture;
    private ObserverFixture? _observer;
    private WebApplication? _viewerApp;
    private HttpClient? _viewerClient;
    private string _bundlesRoot = "";

    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task InitializeAsync()
    {
        // 1. Set up NAS data source for bundle building
        _nasFixture = await AggregationFixture.InitializeAsync();
        _bundlesRoot = Path.Combine(Path.GetTempPath(), $"annot-rt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_bundlesRoot);

        var nasRoot = _nasFixture.NasRoot;
        var bundlesRoot = _bundlesRoot;

        // 2. Set up Observer with annotation store + bundle build capability
        _observer = await ObserverFixture.CreateAsync(
            configureExtraServices: services =>
            {
                // Annotation store (SQLite)
                services.AddSingleton<IAnnotationStore>(sp =>
                {
                    var cfg = sp.GetRequiredService<ObserverConfig>();
                    var path = Path.Combine(cfg.DataRoot, "annotations.db");
                    var store = new SqliteAnnotationStore(
                        path, NullLogger<SqliteAnnotationStore>.Instance);
                    store.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
                    return store;
                });

                // Bundle catalog and storage reader
                services.AddSingleton<BundleCatalog>(sp =>
                    new BundleCatalog(bundlesRoot, sp.GetRequiredService<ILogger<BundleCatalog>>()));
                services.AddSingleton<ITelemetryStorageReader>(sp =>
                    new LocalFileSystemStorageReader(
                        nasRoot, sp.GetRequiredService<ILogger<LocalFileSystemStorageReader>>()));

                // Aggregation orchestrator — wired with annotation store
                services.AddSingleton<IAggregationOrchestrator>(sp =>
                    new AggregationOrchestrator(
                        sp.GetRequiredService<ITelemetryStorageReader>(),
                        sp.GetRequiredService<ILogger<AggregationOrchestrator>>(),
                        sp.GetRequiredService<IAnnotationStore>()));

                services.AddSingleton<BundleBuildService>();
            },
            configureExtraApp: app =>
            {
                AnnotationEndpoints.Map(app);
                BundleEndpoints.Map(app);
            });

        // 3. Create 3 annotations via Observer HTTP API (sessionId = "" → exported via TimeRange build)
        var a1 = new CreateAnnotationDto { SessionId = "", Kind = "Event", EventId = "0000000000000001", Body = "First annotation" };
        var a2 = new CreateAnnotationDto { SessionId = "", Kind = "Event", EventId = "0000000000000002", Body = "Second annotation" };
        var a3 = new CreateAnnotationDto { SessionId = "", Kind = "Event", EventId = "0000000000000003", Body = "Third annotation" };

        (await _observer.Client.PostAsJsonAsync("/api/annotations", a1)).EnsureSuccessStatusCode();
        (await _observer.Client.PostAsJsonAsync("/api/annotations", a2)).EnsureSuccessStatusCode();
        (await _observer.Client.PostAsJsonAsync("/api/annotations", a3)).EnsureSuccessStatusCode();

        // 4. Trigger bundle build using NAS time range (SessionId stays null → "" used for export)
        var nasRange = _nasFixture.NasTimeRange;
        var buildResp = await _observer.Client.PostAsJsonAsync("/api/bundles/build",
            new BundleBuildRequestDto
            {
                TimeRange = new TimeRangeDto
                {
                    StartUtc = nasRange.StartUtc.ToDateTimeOffset(),
                    EndUtc = nasRange.EndUtc.ToDateTimeOffset(),
                }
            });
        buildResp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var accepted = await buildResp.Content.ReadFromJsonAsync<BundleBuildAcceptedDto>();
        accepted.Should().NotBeNull();

        var status = await PollBuildUntilDoneAsync(accepted!.BundleId, timeoutSeconds: 90);
        status.State.Should().Be("Completed",
            $"Bundle build failed: {status.Error ?? "(no error)"}");

        // 5. Open OfflineViewer on completed bundle
        _viewerApp = OfflineViewerHostBuilder.Build(status.OutputPath);
        await _viewerApp.StartAsync();

        var viewerCfg = _viewerApp.Services.GetRequiredService<OfflineViewerConfig>();
        _viewerClient = new HttpClient
        {
            BaseAddress = new Uri($"http://localhost:{viewerCfg.HttpPort}/"),
            Timeout = TimeSpan.FromSeconds(30),
        };

        await WaitForBundleLoadedAsync(_viewerClient, accepted.BundleId);
    }

    public async Task DisposeAsync()
    {
        _viewerClient?.Dispose();
        if (_viewerApp is not null)
        {
            await _viewerApp.StopAsync();
            await _viewerApp.DisposeAsync();
        }
        if (_observer is not null)
            await _observer.DisposeAsync();
        if (_nasFixture is not null)
            await _nasFixture.DisposeAsync();
        try { if (Directory.Exists(_bundlesRoot)) Directory.Delete(_bundlesRoot, recursive: true); } catch { }
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>SC-13: 3 annotations created live survive bundle build and appear in offline viewer.</summary>
    [Fact]
    public async Task AnnotationsRoundTrip_LiveToBundleToOffline()
    {
        var resp = await _viewerClient!.GetAsync("api/annotations");
        resp.EnsureSuccessStatusCode();

        var annotations = await resp.Content.ReadFromJsonAsync<AnnotationDto[]>(CamelCaseOptions);
        annotations.Should().NotBeNull();
        annotations!.Should().HaveCount(3,
            "3 annotations created in observer must survive the round-trip to offline viewer");

        annotations.Select(a => a.Body).Should().BeEquivalentTo(
            new[] { "First annotation", "Second annotation", "Third annotation" },
            opts => opts.WithoutStrictOrdering());
    }

    /// <summary>SC-14: POST to annotations in bundle mode returns 405.</summary>
    [Fact]
    public async Task AnnotationsRoundTrip_BundleMode_PostReturns405()
    {
        var dto = new CreateAnnotationDto
        {
            SessionId = "",
            Kind = "Event",
            EventId = "0000000000000099",
            Body = "Should be rejected"
        };
        var resp = await _viewerClient!.PostAsJsonAsync("api/annotations", dto);
        ((int)resp.StatusCode).Should().Be(405,
            "bundle mode must reject annotation writes with HTTP 405");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<BundleBuildStatusDto> PollBuildUntilDoneAsync(
        string bundleId, int timeoutSeconds)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var r = await _observer!.Client.GetAsync($"/api/bundles/{bundleId}/status");
            if (r.IsSuccessStatusCode)
            {
                var status = await r.Content.ReadFromJsonAsync<BundleBuildStatusDto>();
                if (status?.State is "Completed" or "Failed")
                    return status;
            }
            await Task.Delay(500);
        }
        throw new TimeoutException($"Bundle {bundleId} did not complete within {timeoutSeconds}s");
    }

    private static async Task WaitForBundleLoadedAsync(HttpClient client, string bundleId, int timeoutSeconds = 15)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var res = await client.GetAsync("api/bundle/current");
                if (res.IsSuccessStatusCode)
                {
                    var json = await res.Content.ReadAsStringAsync();
                    if (json.Contains(bundleId, StringComparison.Ordinal))
                        return;
                }
            }
            catch { /* retry */ }
            await Task.Delay(200);
        }
        throw new TimeoutException($"OfflineViewer did not load bundle '{bundleId}' within {timeoutSeconds}s");
    }
}
```

### Task 4 — SavedViewsRoundTripTests.cs

**File: `tests/Tracer.Tests.Integration/SavedViewsRoundTripTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Observer.Configuration;
using Tracer.Storage.SavedViews;
using Tracer.TestHarness.Observer;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Endpoints;
using Xunit;

namespace Tracer.Tests.Integration;

/// <summary>
/// Integration tests for SavedViews in observer mode (live CRUD)
/// and bundle mode (read-only enforcement).
/// TRC-P8-018 backend integration scope.
/// </summary>
[Collection("SavedViewsRoundTrip")]
public sealed class SavedViewsRoundTripTests : IAsyncLifetime
{
    private ObserverFixture? _observer;

    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task InitializeAsync()
    {
        _observer = await ObserverFixture.CreateAsync(
            configureExtraServices: services =>
            {
                services.AddSingleton<ISavedViewStore>(sp =>
                {
                    var cfg = sp.GetRequiredService<ObserverConfig>();
                    var path = System.IO.Path.Combine(cfg.DataRoot, "saved-views.db");
                    var store = new SqliteSavedViewStore(
                        path, NullLogger<SqliteSavedViewStore>.Instance);
                    store.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
                    return store;
                });
            },
            configureExtraApp: app => SavedViewEndpoints.Map(app));
    }

    public async Task DisposeAsync()
    {
        if (_observer is not null)
            await _observer.DisposeAsync();
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SavedViews_LiveMode_CreateListDelete()
    {
        // Create
        var dto = new CreateSavedViewDto
        {
            SessionId = "sess-int-test",
            Kind = "SavedView",
            Persona = "engineer",
            Label = "Integration test view",
            Url = "/v/timeline/sess-int-test?topic=game.tick",
        };
        var createResp = await _observer!.Client.PostAsJsonAsync("/api/saved-views", dto);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created,
            "creating a saved view in live mode must return 201");

        var created = await createResp.Content.ReadFromJsonAsync<SavedViewDto>(CamelCaseOptions);
        created.Should().NotBeNull();
        created!.SavedViewId.Should().NotBeEmpty();
        created.Label.Should().Be("Integration test view");

        // List
        var listResp = await _observer.Client.GetAsync(
            $"/api/saved-views?sessionId=sess-int-test");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<SavedViewDto[]>(CamelCaseOptions);
        list.Should().NotBeNull();
        list!.Should().Contain(v => v.SavedViewId == created.SavedViewId,
            "listed saved views must include the newly created one");

        // Delete
        var deleteResp = await _observer.Client.DeleteAsync(
            $"/api/saved-views/{created.SavedViewId}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "deleting a saved view must return 204");

        // Verify gone
        var listAfterResp = await _observer.Client.GetAsync(
            $"/api/saved-views?sessionId=sess-int-test");
        listAfterResp.EnsureSuccessStatusCode();
        var listAfter = await listAfterResp.Content.ReadFromJsonAsync<SavedViewDto[]>(CamelCaseOptions);
        listAfter!.Should().NotContain(v => v.SavedViewId == created.SavedViewId,
            "deleted saved view must not appear in subsequent list");
    }

    [Fact]
    public async Task SavedViews_BundleMode_WriteReturns405()
    {
        // The LazyBundleSavedViewStore throws InvalidOperationException for writes.
        // Test this directly via the store.
        var bundleStore = new LazyBundleSavedViewStore();
        var createAction = async () => await bundleStore.CreateAsync(
            new SavedViewRecord
            {
                SavedViewId = "test",
                SessionId = "s1",
                Kind = SavedViewKind.SavedView,
                Persona = "engineer",
                Label = "test",
                Url = "/",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }, CancellationToken.None);

        await createAction.Should().ThrowAsync<InvalidOperationException>(
            "bundle saved view store must reject writes with InvalidOperationException");
    }
}
```

**Note:** You need to add `SavedViewDto`, `CreateSavedViewDto` to `Tracer.WebApi.Contracts.Dto` if they don't exist. Check `src/Tracer.WebApi/Contracts/Dto/` for existing DTO files. If they already exist (from BATCH-43), just use them.

**Also check `SavedViewEndpoints.cs`** in `src/Tracer.WebApi/Endpoints/` to verify the route paths before writing the test (`/api/saved-views`).

### Task 5 — TriggerEvalIntegrationTests.cs

**File: `tests/Tracer.Tests.Integration/TriggerEvalIntegrationTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.TestHarness.Observer;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Endpoints;
using Tracer.WebApi.Queries;
using Xunit;

namespace Tracer.Tests.Integration;

/// <summary>
/// Integration tests: push synthetic trigger_evaluated events into Observer,
/// query via HTTP, verify filters work correctly.
/// TRC-P8-018 backend integration scope.
/// </summary>
[Collection("TriggerEvalIntegration")]
public sealed class TriggerEvalIntegrationTests : IAsyncLifetime
{
    private ObserverFixture? _observer;

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

    private static ulong _nextId = 77_000_000;

    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromUnixNanoseconds(dto.ToUnixTimeMilliseconds() * 1_000_000L);

    public async Task InitializeAsync()
    {
        _observer = await ObserverFixture.CreateAsync(
            configureExtraServices: services =>
                services.AddSingleton<TriggerEvalService>(),
            configureExtraApp: app =>
                TriggerEvalEndpoints.Map(app));
    }

    public async Task DisposeAsync()
    {
        if (_observer is not null)
            await _observer.DisposeAsync();
    }

    private EventRecord MakeTriggerEvent(
        string triggerId,
        string result,
        DateTimeOffset? at = null)
    {
        var id = _nextId++;
        var payload =
            $"{{\"triggerId\":\"{triggerId}\",\"inputs\":{{\"v\":1}},\"result\":\"{result}\"}}";
        return new EventRecord
        {
            SequenceNumber = id,
            PublishWallclock = At(at ?? BaseTime),
            ReceiveWallclock = At(at ?? BaseTime),
            PublisherNode = new AgentId("node-trig"),
            SubscriberNode = new AgentId("node-trig"),
            Topic = new TopicName("scenario.trigger_evaluated"),
            EventId = new EventId(id),
            TraceId = new TraceId(id),
            PayloadJson = payload,
        };
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TriggerEval_HTTP_ReturnsEvents()
    {
        var suffix = _nextId.ToString();
        await _observer!.PushAsync(new[]
        {
            MakeTriggerEvent($"trigger-A-{suffix}", "fired"),
            MakeTriggerEvent($"trigger-A-{suffix}", "not-fired"),
            MakeTriggerEvent($"trigger-B-{suffix}", "fired"),
        });

        var from = BaseTime.AddSeconds(-1).ToUnixTimeMilliseconds() * 1_000_000L;
        var to   = BaseTime.AddSeconds(60).ToUnixTimeMilliseconds() * 1_000_000L;

        var resp = await _observer.Client.GetAsync(
            $"/api/scenario/triggers?sessionId=s1&fromNs={from}&toNs={to}&limit=100");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<TriggerEvaluationListDto>(CamelCaseOptions);
        body.Should().NotBeNull();
        body!.Evaluations.Should().NotBeEmpty(
            "pushed trigger events must appear in the HTTP response");
    }

    [Fact]
    public async Task TriggerEval_HTTP_FilterByTriggerId()
    {
        var suffix = _nextId.ToString();
        await _observer!.PushAsync(new[]
        {
            MakeTriggerEvent($"trigger-X-{suffix}", "fired"),
            MakeTriggerEvent($"trigger-Y-{suffix}", "fired"),
        });

        var from = BaseTime.AddSeconds(-1).ToUnixTimeMilliseconds() * 1_000_000L;
        var to   = BaseTime.AddSeconds(60).ToUnixTimeMilliseconds() * 1_000_000L;

        var resp = await _observer.Client.GetAsync(
            $"/api/scenario/triggers?sessionId=s1&fromNs={from}&toNs={to}&triggerId=trigger-X-{suffix}&limit=100");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<TriggerEvaluationListDto>(CamelCaseOptions);
        body!.Evaluations.Should().NotBeEmpty();
        body.Evaluations.Should().AllSatisfy(e =>
            e.TriggerId.Should().Contain($"trigger-X-{suffix}"),
            "filtering by triggerId must exclude other triggers");
    }

    [Fact]
    public async Task TriggerEval_HTTP_FilterByResult()
    {
        var suffix = _nextId.ToString();
        await _observer!.PushAsync(new[]
        {
            MakeTriggerEvent($"fired-trig-{suffix}", "fired"),
            MakeTriggerEvent($"fired-trig-{suffix}", "fired"),
            MakeTriggerEvent($"notfired-trig-{suffix}", "not-fired"),
        });

        var from = BaseTime.AddSeconds(-1).ToUnixTimeMilliseconds() * 1_000_000L;
        var to   = BaseTime.AddSeconds(60).ToUnixTimeMilliseconds() * 1_000_000L;

        var resp = await _observer.Client.GetAsync(
            $"/api/scenario/triggers?sessionId=s1&fromNs={from}&toNs={to}&result=Fired&limit=100");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<TriggerEvaluationListDto>(CamelCaseOptions);
        body!.Evaluations.Should().NotBeEmpty();
        body.Evaluations.Should().AllSatisfy(e =>
            e.Result.Should().Be("Fired"),
            "result=Fired filter must only return Fired evaluations");
    }
}
```

**Note:** Check the `TriggerEvalEndpoints` route to verify query param names: look at `src/Tracer.WebApi/Endpoints/TriggerEvalEndpoints.cs`. The params might be `fromNs`, `toNs`, `triggerId`, `result`. Adjust the URL accordingly.

Also check `TriggerEvaluationListDto` and `TriggerEvaluationDto` in `src/Tracer.WebApi/Contracts/Dto/` to get the field names for `Result`.

### Task 6 — E2E Playwright Stubs

Create these three stub test files using the established `test.skip` pattern from existing E2E tests.

**File: `tracer-viewer/tests/e2e/annotations-flow.spec.ts`**

```typescript
import { test, expect } from '@playwright/test';

test.describe('Annotations Flow', () => {
  test('E2E_CreateAnnotation_PersistsAfterReload', async ({ page }) => {
    test.skip(process.env['E2E'] !== 'true', 'Requires live Observer + SPA; set E2E=true to run');
    // Open event inspector for a known event
    await page.goto('http://localhost:5300/v/timeline/test-session');
    // Open inspector for first event
    await page.locator('.timeline-event').first().click();
    // Click "Add note"
    await page.locator('button:has-text("Add note")').click();
    // Fill body
    await page.locator('.annotation-editor__body').fill('Integration test annotation');
    // Save
    await page.locator('button:has-text("Save")').click();
    // Reload
    await page.reload();
    // Marker should be visible
    await expect(page.locator('.annotation-marker')).toBeVisible({ timeout: 5_000 });
  });
});
```

**File: `tracer-viewer/tests/e2e/saved-views-flow.spec.ts`**

```typescript
import { test, expect } from '@playwright/test';

test.describe('Saved Views Flow', () => {
  test('E2E_SavedView_RestoresFilterState', async ({ page }) => {
    test.skip(process.env['E2E'] !== 'true', 'Requires live Observer + SPA; set E2E=true to run');
    // Navigate to timeline with a filter
    await page.goto('http://localhost:5300/v/timeline/test-session?topic=weapons.fire');
    // Click "Save view"
    await page.locator('button:has-text("Save view")').click();
    // Accept the dialog
    await page.locator('button:has-text("Save")').click();
    // Navigate to saved views
    await page.goto('http://localhost:5300/v/saved-views/test-session');
    await expect(page.locator('.saved-views-view__item').first()).toBeVisible({ timeout: 5_000 });
    // Click first saved view
    await page.locator('.saved-views-view__item').first().click();
    // URL must contain the filter param
    await expect(page).toHaveURL(/topic=weapons\.fire/);
  });
});
```

**File: `tracer-viewer/tests/e2e/persona-switcher.spec.ts`**

```typescript
import { test, expect } from '@playwright/test';

test.describe('Persona Switcher', () => {
  test('E2E_PersonaSwitcher_EngineerLandsOnTimeline', async ({ page }) => {
    test.skip(process.env['E2E'] !== 'true', 'Requires live Observer + SPA; set E2E=true to run');
    await page.goto('http://localhost:5300/sessions');
    // Set Engineer persona
    await page.locator('.persona-switcher__btn:has-text("Engineer")').click();
    // Click first session card
    await page.locator('.session-card').first().click();
    // Should navigate to /v/timeline/
    await expect(page).toHaveURL(/\/v\/timeline\//);
  });

  test('E2E_PersonaSwitcher_ScenarioAuthorLandsOnScenario', async ({ page }) => {
    test.skip(process.env['E2E'] !== 'true', 'Requires live Observer + SPA; set E2E=true to run');
    await page.goto('http://localhost:5300/sessions');
    // Set Scenario Author persona
    await page.locator('.persona-switcher__btn:has-text("Scenario Author")').click();
    // Click first session card
    await page.locator('.session-card').first().click();
    // Should navigate to /v/scenario/
    await expect(page).toHaveURL(/\/v\/scenario\//);
  });
});
```

### Task 7 — Check and Verify DTOs

Before finalizing, verify these DTO types exist and have the correct shape:

1. **`CreateSavedViewDto`** in `src/Tracer.WebApi/Contracts/Dto/SavedViewDtos.cs`:
   - Must have: `SessionId`, `Kind`, `Persona`, `Label`, `Url`

2. **`SavedViewDto`** in the same file:
   - Must have: `SavedViewId`, `Label`, `Url`

3. **`TriggerEvaluationListDto`** and `TriggerEvaluationDto` in `src/Tracer.WebApi/Contracts/Dto/TriggerEvalDtos.cs`:
   - `TriggerEvaluationListDto.Evaluations` — list of evaluations
   - `TriggerEvaluationDto.TriggerId`, `TriggerEvaluationDto.Result` (string)

4. **`SavedViewEndpoints` route** — verify the endpoint path is `/api/saved-views` (not `/api/savedviews` or similar).

5. **`TriggerEvalEndpoints` query params** — check the exact param names used in `GET /api/scenario/triggers`.

Adjust the test code to match actual DTO field names and endpoint paths.

---

## Verification

### Backend Integration Tests

First, kill any stale test processes:
```powershell
Get-Process -Name "testhost" -ErrorAction SilentlyContinue | Stop-Process -Force
```

Run integration tests (may take 2-5 minutes each):
```powershell
cd d:\Work\Tracer
dotnet build Tracer.sln -c Release --no-incremental 2>&1 | Select-Object -Last 5

# Run new integration tests only (filter by class name)
dotnet test tests\Tracer.Tests.Integration -c Release --no-build --filter "FullyQualifiedName~AnnotationsRoundTrip OR FullyQualifiedName~SavedViewsRoundTrip OR FullyQualifiedName~TriggerEvalIntegration" --logger "console;verbosity=normal" 2>&1 | Select-Object -Last 30
```

If integration tests time out or fail, check:
1. Build errors: `dotnet build Tracer.sln -c Release --no-incremental 2>&1 | Select-Object -Last 20`
2. Individual test failure messages

### Full Unit Test Suite (ensure no regressions)

```powershell
dotnet test tests\Tracer.Tests.Unit -c Release --no-build --filter "FullyQualifiedName!~Publish_ProducesExpectedLayout" 2>&1 | Select-Object -Last 10
```

### Frontend Tests (E2E stubs shouldn't affect Vitest)

```powershell
cd d:\Work\Tracer\tracer-viewer
pnpm test:unit -- --reporter=verbose 2>&1 | Select-Object -Last 8
```

---

## TASK-TRACKER Update

After tests pass, update `docs/TASK-TRACKER.md`:
Change:
```
- [ ] **TRC-P8-018** Phase 8 Tests (Backend Unit, Integration, Frontend) [details](./TASK-DETAIL.md#trc-p8-018--phase-8-tests-backend-unit-integration-frontend)
```
To:
```
- [x] **TRC-P8-018** Phase 8 Tests (Backend Unit, Integration, Frontend) [details](./TASK-DETAIL.md#trc-p8-018--phase-8-tests-backend-unit-integration-frontend)
```

---

## Report

Write your report to `d:\WORK\Tracer\.dev\tracer\reports\BATCH-48-REPORT.md`. Include:
1. Confirmation of each file created/modified
2. Any DTO name/path adjustments made (from Task 7 checks)
3. Integration test results (number of tests, pass/fail)
4. Any issues encountered and how they were resolved
5. Final build status (`dotnet build` exit code)
6. Final test counts

---

## Important Notes

- **DO NOT** re-implement backend unit tests that already exist in `Tracer.Tests.Unit` (SqliteAnnotationStoreTests, BundleAnnotationStoreTests, SqliteSavedViewStoreTests, AnnotationEndpointsTests, TriggerEvalServiceTests — all done in BATCH-42/43/44)
- The E2E tests **must use `test.skip`** since there is no running full stack in this environment
- Integration tests run against real DuckDB (via TestHarness) — they may take 60-90 seconds each
- If the `AnnotationsRoundTripTests` bundle build fails due to NAS data issues, check `AggregationFixture` initialization — it uses FakeNode CalmScenario with a fresh temp directory
- The `LazyBundleSavedViewStore` in `Tracer.OfflineViewer.WebApi` is a no-op store; you don't need to change it
