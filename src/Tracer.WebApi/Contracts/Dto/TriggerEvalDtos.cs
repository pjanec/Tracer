namespace Tracer.WebApi.Contracts.Dto;

public sealed record TriggerEvaluationListDto
{
    public required IReadOnlyList<TriggerEvaluationDto> Evaluations { get; init; }
}

public sealed record TriggerEvaluationDto
{
    public required string EventId { get; init; }
    public required DateTimeOffset EvaluatedAtUtc { get; init; }
    public required string PublisherNode { get; init; }
    public required string TraceId { get; init; }
    public required string TriggerId { get; init; }
    public string? TriggerLabel { get; init; }
    public required string Inputs { get; init; }
    public required string Result { get; init; }
    public string? NextEventId { get; init; }
    public string? Reason { get; init; }
}

public static class TriggerEvalDtoMapper
{
    public static TriggerEvaluationListDto Map(Tracer.WebApi.Queries.TriggerEvalResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new TriggerEvaluationListDto
        {
            Evaluations = result.Evaluations.Select(Map).ToList(),
        };
    }

    public static TriggerEvaluationDto Map(Tracer.WebApi.Queries.TriggerEvaluation e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return new TriggerEvaluationDto
        {
            EventId = e.EventId.ToString(),
            EvaluatedAtUtc = e.EvaluatedAtUtc,
            PublisherNode = e.PublisherNode,
            TraceId = e.TraceId.ToString(),
            TriggerId = e.TriggerId,
            TriggerLabel = e.TriggerLabel,
            Inputs = e.Inputs,
            Result = e.Result.ToString(),
            NextEventId = e.NextEventId.HasValue ? e.NextEventId.Value.ToString() : null,
            Reason = e.Reason,
        };
    }
}
