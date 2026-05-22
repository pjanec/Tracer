using CycloneDDS.Runtime;
using Microsoft.Extensions.Logging;
using Tracer.Adapters.DDS.Configuration;

namespace Tracer.Adapters.DDS;

/// <summary>
/// Production <see cref="IDdsSubscriberFactory"/> backed by the CycloneDDS.NET binding.
/// For each configured topic, creates a <c>DdsReader&lt;T&gt;</c> via reflection and
/// polls it in a background task using <c>WaitDataAsync()</c>.
/// </summary>
public sealed class DdsSubscriberFactory : IDdsSubscriberFactory
{
    private readonly DdsParticipant _participant;
    private readonly ILogger<DdsSubscriberFactory> _logger;

    public DdsSubscriberFactory(DdsParticipant participant, ILogger<DdsSubscriberFactory> logger)
    {
        _participant = participant;
        _logger = logger;
    }

    /// <inheritdoc/>
    public IDisposable Create(DdsTopicSubscription topicSub, Type sampleType, Action<IDdsSample> onSample)
    {
        ArgumentNullException.ThrowIfNull(topicSub);
        ArgumentNullException.ThrowIfNull(sampleType);
        ArgumentNullException.ThrowIfNull(onSample);

        // Create DdsReader<T> via reflection since sampleType is known only at runtime.
        var readerType = typeof(DdsReader<>).MakeGenericType(sampleType);
        var reader = (IDisposable)Activator.CreateInstance(readerType, _participant, topicSub.TopicName)!;

        var cts = new CancellationTokenSource();
        var task = PollReaderAsync(reader, sampleType, onSample, topicSub.TopicName, cts.Token);

        return new SubscriberHandle(reader, cts, task, _logger, topicSub.TopicName);
    }

    private async Task PollReaderAsync(
        IDisposable reader,
        Type sampleType,
        Action<IDdsSample> onSample,
        string topicName,
        CancellationToken ct)
    {
        // Resolve WaitDataAsync and Take methods via reflection once per reader.
        var waitDataAsync = reader.GetType().GetMethod("WaitDataAsync", Type.EmptyTypes)
            ?? throw new InvalidOperationException($"DdsReader<{sampleType.Name}> missing WaitDataAsync");
        var take = reader.GetType().GetMethod("Take", new[] { typeof(int) })
            ?? reader.GetType().GetMethod("Take", Type.EmptyTypes)
            ?? throw new InvalidOperationException($"DdsReader<{sampleType.Name}> missing Take");

        ulong sequenceCounter = 0;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // await reader.WaitDataAsync()
                var waitTask = (Task<bool>?)waitDataAsync.Invoke(reader, null);
                if (waitTask is null) break;

                bool hasData = false;
                try { hasData = await waitTask.WaitAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }

                if (!hasData) continue;

                // using var loan = reader.Take()
                var loanArgs = take.GetParameters().Length == 1
                    ? new object[] { int.MaxValue }
                    : Array.Empty<object>();

                var loan = take.Invoke(reader, loanArgs) as IDisposable;
                if (loan is null) continue;

                using (loan)
                {
                    // Iterate loan as IEnumerable
                    var enumerable = (System.Collections.IEnumerable)loan;
                    foreach (var rawSample in enumerable)
                    {
                        // Each rawSample has IsValid, Data properties
                        var isValidProp = rawSample.GetType().GetProperty("IsValid");
                        var dataProp = rawSample.GetType().GetProperty("Data");

                        if (isValidProp?.GetValue(rawSample) is not true) continue;
                        if (dataProp?.GetValue(rawSample) is not { } payload) continue;

                        var wrapper = new ReflectedDdsSample(
                            payload,
                            DateTimeOffset.UtcNow,
                            ++sequenceCounter);

                        try { onSample(wrapper); }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "onSample callback threw for topic {Topic}", topicName);
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "DDS polling loop terminated unexpectedly for topic {Topic}", topicName);
        }
    }

    private sealed class SubscriberHandle : IDisposable
    {
        private readonly IDisposable _reader;
        private readonly CancellationTokenSource _cts;
        private readonly Task _task;
        private readonly ILogger _logger;
        private readonly string _topicName;

        public SubscriberHandle(IDisposable reader, CancellationTokenSource cts, Task task,
            ILogger logger, string topicName)
        {
            _reader = reader;
            _cts = cts;
            _task = task;
            _logger = logger;
            _topicName = topicName;
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _task.Wait(TimeSpan.FromSeconds(2)); }
            catch (AggregateException) { }
            catch (OperationCanceledException) { }
            _cts.Dispose();
            _reader.Dispose();
            _logger.LogDebug("DDS subscriber for topic {Topic} disposed", _topicName);
        }
    }

    /// <summary>Wraps a reflectively-obtained DDS payload as <see cref="IDdsSample"/>.</summary>
    private sealed class ReflectedDdsSample : IDdsSample
    {
        private readonly object _payload;

        public ReflectedDdsSample(object payload, DateTimeOffset sourceTimestamp, ulong sequenceNumber)
        {
            _payload = payload;
            SourceTimestamp = sourceTimestamp;
            SequenceNumber = sequenceNumber;
        }

        public DateTimeOffset SourceTimestamp { get; }
        public ulong SequenceNumber { get; }
        public object GetPayload() => _payload;
    }
}
