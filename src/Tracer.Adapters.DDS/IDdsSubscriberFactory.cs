using Tracer.Adapters.DDS.Configuration;

namespace Tracer.Adapters.DDS;

/// <summary>
/// Creates DDS topic subscribers. Abstracted for testability.
/// </summary>
public interface IDdsSubscriberFactory
{
    /// <summary>
    /// Creates a subscriber for the given topic.
    /// Calls <paramref name="onSample"/> for each received sample.
    /// The returned <see cref="IDisposable"/> stops the subscription when disposed.
    /// </summary>
    IDisposable Create(DdsTopicSubscription topicSub, Type sampleType, Action<IDdsSample> onSample);
}
