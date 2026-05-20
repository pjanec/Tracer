using Tracer.Core.Domain;

namespace Tracer.Aggregator.Discovery;

/// <summary>
/// An interval that has been extracted from its ZIP archive into a local staging directory.
/// </summary>
public sealed record ExtractedInterval(
    string NodeId,
    IntervalDescriptor Descriptor,
    string Directory);
