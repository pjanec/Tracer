namespace Tracer.Storage.SavedViews;

public sealed record SavedViewFilter
{
    public string? SessionId { get; init; }
    public SavedViewKind? Kind { get; init; }
    public string? ViewType { get; init; }
    public string? Persona { get; init; }
    public string OrderBy { get; init; } = "created";
    public int Limit { get; init; } = 100;
}
