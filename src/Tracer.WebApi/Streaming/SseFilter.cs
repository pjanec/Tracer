namespace Tracer.WebApi.Streaming;

public sealed record SseFilter(bool NotablesOnly = false, string? SessionId = null);
