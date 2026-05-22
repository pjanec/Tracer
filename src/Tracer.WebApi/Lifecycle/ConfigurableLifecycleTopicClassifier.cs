namespace Tracer.WebApi.Lifecycle;

public sealed class ConfigurableLifecycleTopicClassifier : ILifecycleTopicClassifier
{
    private readonly LifecycleClassificationConfig _config;

    public ConfigurableLifecycleTopicClassifier(LifecycleClassificationConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
    }

    public string? Classify(string topic)
    {
        ArgumentNullException.ThrowIfNull(topic);

        var regex = _config.Regex;

        // Suffix matching against the last dot-segment
        var suffix = topic.Contains('.') ? topic[(topic.LastIndexOf('.') + 1)..] : topic;

        // Spawn: regex takes precedence. If regex configured, skip suffix for this category.
        if (regex?.Spawn is { } spawnPattern)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(topic, spawnPattern))
                return "spawn";
            // regex configured but didn't match — do NOT check spawn suffix
        }
        else
        {
            if (_config.SpawnSuffixes.Contains(suffix, StringComparer.OrdinalIgnoreCase))
                return "spawn";
        }

        // Ownership: regex takes precedence. If regex configured, skip suffix for this category.
        if (regex?.Ownership is { } ownerPattern)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(topic, ownerPattern))
                return "ownership";
        }
        else
        {
            if (_config.OwnershipSuffixes.Contains(suffix, StringComparer.OrdinalIgnoreCase))
                return "ownership";
        }

        // Destruction: regex takes precedence. If regex configured, skip suffix for this category.
        if (regex?.Destruction is { } destroyPattern)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(topic, destroyPattern))
                return "destruction";
        }
        else
        {
            if (_config.DestructionSuffixes.Contains(suffix, StringComparer.OrdinalIgnoreCase))
                return "destruction";
        }

        return null;
    }
}
