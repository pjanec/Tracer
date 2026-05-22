namespace Tracer.Adapters.DDS;

/// <summary>
/// Dictionary-backed catalog of known DDS topics and their metadata.
/// </summary>
public sealed class DdsTopicRegistry
{
    private readonly Dictionary<string, DdsTopicMetadata> _byName;

    public DdsTopicRegistry(IEnumerable<DdsTopicMetadata> topics)
    {
        _byName = topics.ToDictionary(t => t.TopicName, StringComparer.Ordinal);
    }

    public DdsTopicMetadata? Lookup(string topicName) =>
        _byName.GetValueOrDefault(topicName);

    public IReadOnlyCollection<DdsTopicMetadata> All => _byName.Values;
}
