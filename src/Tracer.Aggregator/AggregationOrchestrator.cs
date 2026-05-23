using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Aggregator.Configuration;
using Tracer.Aggregator.Consolidation;
using Tracer.Aggregator.Discovery;
using Tracer.Aggregator.Progress;
using Tracer.Aggregator.Staging;
using Tracer.Bundle.Format;
using Tracer.Bundle.Packaging;
using Tracer.Core.Abstractions;
using Tracer.Core.Time;
using Tracer.Storage.Annotations;
using Tracer.Storage.SavedViews;

namespace Tracer.Aggregator;

/// <summary>
/// The single public entry point for the aggregation library.
/// Orchestrates the nine-stage bundle build process.
/// </summary>
public sealed class AggregationOrchestrator : IAggregationOrchestrator
{
    private readonly ITelemetryStorageReader _nasReader;
    private readonly ILogger<AggregationOrchestrator> _logger;
    private readonly IAnnotationStore? _annotationStore;
    private readonly ISavedViewStore? _savedViewStore;

    public AggregationOrchestrator(
        ITelemetryStorageReader nasReader,
        ILogger<AggregationOrchestrator> logger,
        IAnnotationStore? annotationStore = null,
        ISavedViewStore? savedViewStore = null)
    {
        ArgumentNullException.ThrowIfNull(nasReader);
        ArgumentNullException.ThrowIfNull(logger);
        _nasReader = nasReader;
        _logger = logger;
        _annotationStore = annotationStore;
        _savedViewStore = savedViewStore;
    }

    public AggregationOrchestrator(ITelemetryStorageReader nasReader)
        : this(nasReader, NullLogger<AggregationOrchestrator>.Instance) { }

    /// <summary>
    /// Runs the full aggregation pipeline and returns an <see cref="AggregationResult"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// When <paramref name="request"/> specifies neither <c>TimeRange</c> nor <c>SessionId</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// When no intervals are found for the resolved time range (message contains "No intervals found").
    /// </exception>
    public async Task<AggregationResult> RunAsync(
        AggregationRequest request,
        IAggregationProgressReporter? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        progress?.Report(AggregationStage.Started, "Aggregation starting");
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            // 1. Resolve time range
            var timeRange = await ResolveTimeRangeAsync(request, ct);
            progress?.Report(AggregationStage.TimeRangeResolved,
                $"Time range: {timeRange.StartUtc.ToDateTimeOffset():O} to {timeRange.EndUtc.ToDateTimeOffset():O}");

            // 2. Discover overlapping intervals
            var discovered = await IntervalDiscovery.FindOverlappingAsync(
                _nasReader, timeRange, request.NodeFilter, ct);
            progress?.Report(AggregationStage.IntervalsDiscovered,
                $"Found {discovered.Count} interval(s) across {discovered.NodeCount} node(s)");

            if (discovered.Count == 0)
                throw new InvalidOperationException(
                    "No intervals found overlapping the requested time range.");

            // 3. Extract interval archives to staging
            await using var staging = await StagingDirectory.CreateAsync(request.OutputPath, ct);
            var extracted = await ExtractAllAsync(discovered, staging, progress, ct);
            progress?.Report(AggregationStage.IntervalsExtracted,
                $"Extracted {extracted.Count} interval(s)");

            // 4. Consolidate events
            var eventsOutputPath = Path.Combine(staging.BundleStagingPath, "events.duckdb");
            var eventsStats = await EventsConsolidator.ConsolidateAsync(
                extracted, eventsOutputPath, timeRange, progress, ct);
            progress?.Report(AggregationStage.EventsConsolidated,
                $"Wrote {eventsStats.TotalEvents:N0} events");

            // 5. Consolidate slow state
            var slowStatePath = Path.Combine(staging.BundleStagingPath, "slow_state.duckdb");
            var slowStats = await SlowStateConsolidator.ConsolidateAsync(
                extracted, slowStatePath, timeRange, progress, ct);
            progress?.Report(AggregationStage.SlowStateConsolidated,
                $"Wrote {slowStats.TotalSamples:N0} slow-state samples");

            // 6. Copy fast state
            var fastStats = await FastStateCopier.CopyAsync(
                extracted, staging.BundleStagingPath,
                request.FastStateScope, request.FastStateEntities, timeRange, progress, ct);
            progress?.Report(AggregationStage.FastStateCopied,
                $"Copied {fastStats.TotalRowCount:N0} fast-state rows for {fastStats.EntityCount} entities");

            // 7. Write metadata (scenario.json, topology.json, source_intervals.json)
            var scenario = await ScenarioMetadataCollector.CollectAsync(eventsOutputPath, timeRange, ct);
            var topology = TopologyExtractor.Extract(extracted, timeRange);
            var sourceIntervals = SourceIntervalsBuilder.Build(extracted);
            await BundleMetadataWriter.WriteAsync(staging.BundleStagingPath, scenario, topology, sourceIntervals, ct);
            progress?.Report(AggregationStage.MetadataWritten, "Metadata files written");

            // 7b. Export annotations (if live store provided)
            if (_annotationStore is not null)
            {
                await AnnotationsExporter.ExportAsync(
                    _annotationStore, request.SessionId ?? "", staging.BundleStagingPath, ct);
                progress?.Report(AggregationStage.AnnotationsExported, "Annotations exported into bundle");
            }

            // 7c. Export saved views (if live store provided)
            if (_savedViewStore is not null)
            {
                await SavedViewsExporter.ExportAsync(
                    _savedViewStore, request.SessionId ?? "", staging.BundleStagingPath, ct);
                progress?.Report(AggregationStage.SavedViewsExported, "Saved views exported into bundle");
            }

            // 8. Build manifest (computes SHA-256 per file) and write checksums / manifest.json
            var bundleStatistics = new BundleStatistics
            {
                TotalEvents = eventsStats.TotalEvents,
                TotalSlowStateSamples = slowStats.TotalSamples,
                TotalFastStateRows = fastStats.TotalRowCount,
                UncompressedBytes = ComputeUncompressedSize(staging.BundleStagingPath),
            };
            var manifest = await ManifestBuilder.BuildAsync(
                staging.BundleStagingPath, request, timeRange, scenario, bundleStatistics, ct);
            await BundleDirectoryWriter.WriteAsync(staging.BundleStagingPath, manifest, ct);
            progress?.Report(AggregationStage.ManifestWritten, $"Bundle ID: {manifest.BundleId}");

            // 9. Finalize: move or zip to output path
            var finalPath = await FinalizeAsync(staging, request.OutputPath, ct);
            progress?.Report(AggregationStage.Completed, $"Bundle complete: {finalPath}");

            return new AggregationResult
            {
                BundleId = manifest.BundleId,
                OutputPath = finalPath,
                TimeRange = timeRange,
                Statistics = bundleStatistics,
                Duration = DateTimeOffset.UtcNow - startedAt,
                SourceIntervalsUsed = extracted.Count,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            progress?.Report(AggregationStage.Failed, ex.Message);
            throw;
        }
    }

    private async Task<TimeRange> ResolveTimeRangeAsync(AggregationRequest request, CancellationToken ct)
    {
        if (request.TimeRange is not null) return request.TimeRange;

        if (request.SessionId is not null)
        {
            var range = await SessionResolver.ResolveAsync(_nasReader, request.SessionId, ct);
            return range ?? throw new InvalidOperationException(
                $"Session '{request.SessionId}' not found in any reachable interval");
        }

        throw new ArgumentException(
            "Aggregation request must specify either TimeRange or SessionId.",
            nameof(request));
    }

    private async Task<List<ExtractedInterval>> ExtractAllAsync(
        DiscoveredIntervals discovered,
        StagingDirectory staging,
        IAggregationProgressReporter? progress,
        CancellationToken ct)
    {
        var result = new List<ExtractedInterval>();
        foreach (var di in discovered.Intervals)
        {
            var zipPath = _nasReader.GetIntervalZipPath(di.NodeId, di.Descriptor);
            var extractDir = Path.Combine(staging.SourcesPath, di.NodeId, di.Descriptor.Timestamp.Value);
            Directory.CreateDirectory(extractDir);
            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, extractDir), ct);
            result.Add(new ExtractedInterval(di.NodeId, di.Descriptor, extractDir));
        }
        return result;
    }

    private static async Task<string> FinalizeAsync(
        StagingDirectory staging,
        string requestedOutputPath,
        CancellationToken ct)
    {
        if (requestedOutputPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Run(() => ZipFile.CreateFromDirectory(staging.BundleStagingPath, requestedOutputPath), ct);
            return requestedOutputPath;
        }
        else
        {
            if (Directory.Exists(requestedOutputPath))
                await Task.Run(() => Directory.Delete(requestedOutputPath, recursive: true), ct);
            await MoveDirectoryAsync(staging.BundleStagingPath, requestedOutputPath, ct);
            return requestedOutputPath;
        }
    }

    private static async Task MoveDirectoryAsync(string source, string dest, CancellationToken ct)
    {
        try
        {
            Directory.Move(source, dest);
        }
        catch (IOException)
        {
            // Cross-device move: copy then delete
            await Task.Run(() =>
            {
                CopyDirectory(source, dest);
                Directory.Delete(source, recursive: true);
            }, ct);
        }
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var destFile = Path.Combine(dest, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.Copy(file, destFile, overwrite: true);
        }
    }

    private static long ComputeUncompressedSize(string path)
    {
        return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Sum(f => new FileInfo(f).Length);
    }
}
