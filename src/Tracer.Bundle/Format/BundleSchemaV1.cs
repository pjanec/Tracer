namespace Tracer.Bundle.Format;

public static class BundleSchemaV1
{
    public const int CurrentVersion = 1;

    private static readonly IReadOnlySet<int> _recognized = new HashSet<int> { 1 };

    public static bool IsRecognized(int version) => _recognized.Contains(version);
}
