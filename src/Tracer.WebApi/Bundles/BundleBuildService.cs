using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Tracer.Aggregator;
using Tracer.Aggregator.Configuration;
using Tracer.Aggregator.Progress;
using Tracer.Core.Time;
using Tracer.WebApi.Contracts.Dto;

namespace Tracer.WebApi.Bundles;

public sealed class BundleBuildService
{
    private readonly IAggregationOrchestrator _aggregator;
    private readonly BundleCatalog _catalog;
    private readonly ConcurrentDictionary<string, BundleBuildStatusDto> _statuses = new();
    private readonly SemaphoreSlim _serializeBuilds = new(1, 1);
    private readonly ILogger<BundleBuildService> _logger;

    public BundleBuildService(
        IAggregationOrchestrator aggregator,
        BundleCatalog catalog,
        ILogger<BundleBuildService> logger)
    {
        _aggregator = aggregator;
        _catalog = catalog;
        _logger = logger;
    }

    public Task<string> QueueBuildAsync(BundleBuildRequestDto request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SessionId) && request.TimeRange is null)
            throw new ArgumentException("Either SessionId or TimeRange must be specified.");

        var bundleId = Ulid.NewUlid().ToString();
        var outputPath = Path.Combine(_catalog.BundlesRoot, $"{bundleId}.tracerbundle");

        var status = new BundleBuildStatusDto
        {
            BundleId = bundleId,
            State = "Queued",
            QueuedAtUtc = DateTimeOffset.UtcNow,
            OutputPath = outputPath
        };
        _statuses[bundleId] = status;

        _ = Task.Run(async () => await RunBuildAsync(bundleId, request, outputPath, CancellationToken.None));

        return Task.FromResult(bundleId);
    }

    private async Task RunBuildAsync(string bundleId, BundleBuildRequestDto request,
        string outputPath, CancellationToken ct)
    {
        await _serializeBuilds.WaitAsync(ct);
        try
        {
            UpdateStatus(bundleId, s => s with { State = "InProgress", StartedAtUtc = DateTimeOffset.UtcNow });

            _ = Enum.TryParse<FastStateScope>(request.FastStateScope ?? "None", ignoreCase: true, out var scope);
            var aggregationRequest = new AggregationRequest
            {
                SessionId = request.SessionId,
                TimeRange = request.TimeRange is null ? null : new TimeRange(
                    WallclockTime.FromDateTimeOffset(request.TimeRange.StartUtc),
                    WallclockTime.FromDateTimeOffset(request.TimeRange.EndUtc)),
                NodeFilter = request.NodeFilter,
                FastStateScope = scope,
                FastStateEntities = request.FastStateEntities,
                OutputPath = outputPath,
                LabelOverride = request.LabelOverride,
                WriterTool = "tracer-observer",
            };

            var result = await _aggregator.RunAsync(aggregationRequest, null, ct);

            UpdateStatus(bundleId, s => s with
            {
                State = "Completed",
                CompletedAtUtc = DateTimeOffset.UtcNow,
                OutputPath = result.OutputPath
            });
            await _catalog.RegisterAsync(bundleId, result.OutputPath, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bundle build {BundleId} failed", bundleId);
            UpdateStatus(bundleId, s => s with
            {
                State = "Failed",
                Error = ex.Message,
                CompletedAtUtc = DateTimeOffset.UtcNow
            });
        }
        finally { _serializeBuilds.Release(); }
    }

    public BundleBuildStatusDto GetStatus(string bundleId)
        => _statuses.TryGetValue(bundleId, out var s)
            ? s
            : new BundleBuildStatusDto { BundleId = bundleId, State = "Unknown", QueuedAtUtc = DateTimeOffset.MinValue };

    private void UpdateStatus(string bundleId, Func<BundleBuildStatusDto, BundleBuildStatusDto> mutator)
    {
        _statuses.AddOrUpdate(bundleId,
            _ => mutator(new BundleBuildStatusDto { BundleId = bundleId, State = "Unknown", QueuedAtUtc = DateTimeOffset.UtcNow }),
            (_, existing) => mutator(existing));
    }
}
