using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Time;

namespace Tracer.Core.Abstractions;

/// <summary>
/// Hands completed intervals to the upload pipeline.
/// Production: HTTP calls to sync master. Development: local filesystem copy.
/// </summary>
public interface ITelemetryUploadService
{
    /// <summary>
    /// Request that the named interval be uploaded.
    /// Returns when the request is queued (not when upload completes).
    /// </summary>
    Task<UploadIntentId> RequestUploadAsync(UploadRequest request, CancellationToken ct);

    /// <summary>
    /// Check the status of a previously-requested upload.
    /// </summary>
    Task<UploadStatus> GetStatusAsync(UploadIntentId intentId, CancellationToken ct);
}

/// <summary>
/// Describes an upload request for a completed interval.
/// </summary>
public sealed record UploadRequest
{
    public required AgentId NodeId { get; init; }
    public required IntervalTimestamp Interval { get; init; }
    public required WallclockTime IntervalStartUtc { get; init; }
    public required WallclockTime IntervalEndUtc { get; init; }
    public required IReadOnlyList<FileToUpload> Files { get; init; }
}

/// <summary>
/// Describes a single file to include in an upload.
/// </summary>
public sealed record FileToUpload
{
    public required string Path { get; init; }
    public required long SizeBytes { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Opaque identifier for a queued upload intent.
/// </summary>
public readonly record struct UploadIntentId(string Value);

/// <summary>
/// Status of a queued upload.
/// </summary>
public enum UploadStatus
{
    Unknown,
    Pending,
    InProgress,
    Complete,
    Failed
}
