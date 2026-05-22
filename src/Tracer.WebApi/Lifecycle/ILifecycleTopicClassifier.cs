namespace Tracer.WebApi.Lifecycle;

public interface ILifecycleTopicClassifier
{
    /// <summary>Returns "spawn", "ownership", "destruction", or null.</summary>
    string? Classify(string topic);
}
