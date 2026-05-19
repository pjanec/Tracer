using System.Runtime.CompilerServices;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;

namespace Tracer.Adapters.Mock.Scenarios.Scripts;

/// <summary>
/// Produces a steady baseline heartbeat load preceded by a session-start marker.
/// Deterministic given a seed. Terminates when the simulated clock reaches
/// <c>config.StartTime + config.Duration</c>.
/// </summary>
public sealed class CalmScenario : IScenarioScript
{
    private const string ScenarioPhaseName = "calm";
    private const string SessionStartTopic = "system.session_start";
    private const string HeartbeatTopic = "scenario.heartbeat";
    private const string SessionStartLabel = "Calm session started";

    public string Name => "Calm";

    public async IAsyncEnumerable<DiagnosticRecord> ExecuteAsync(
        ScenarioContext ctx,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var endTime = ctx.Clock.Now + ctx.Config.Duration;
        var nodes = BuildNodes(ctx.Config.NodeCount);
        var entities = BuildEntities(ctx.Config.EntityCount);
        var intervalSec = 1.0 / ctx.Config.EventsPerSecond;
        ulong sequence = 0;

        // First record: session start event.
        yield return MakeSessionStart(ctx, sequence++, nodes[0]);

        while (ctx.Clock.Now.CompareTo(endTime) < 0 && !ct.IsCancellationRequested)
        {
            var node = nodes[ctx.Random.Next(nodes.Length)];
            var entity = entities[ctx.Random.Next(entities.Length)];

            yield return new EventRecord
            {
                EventId = ctx.TraceIdGen.NewEvent(),
                TraceId = ctx.TraceIdGen.NewTrace(),
                ParentEventId = null,
                SequenceNumber = sequence++,
                PublishWallclock = ctx.Clock.Now,
                ReceiveWallclock = ctx.Clock.Now + TimeSpan.FromMilliseconds(1),
                PublisherNode = node,
                SubscriberNode = node,
                Topic = new TopicName(HeartbeatTopic),
                EntityId = entity,
                OwningPlayerId = null,
                ScenarioPhase = ScenarioPhaseName,
                Severity = null,
                NotableLabel = null,
                PayloadJson = $"{{\"kind\":\"heartbeat\",\"node\":\"{node.Value}\"}}",
            };

            ctx.Clock.Advance(TimeSpan.FromSeconds(intervalSec));
            await Task.Yield();
        }
    }

    private static EventRecord MakeSessionStart(ScenarioContext ctx, ulong sequence, AgentId node) =>
        new()
        {
            EventId = ctx.TraceIdGen.NewEvent(),
            TraceId = ctx.TraceIdGen.NewTrace(),
            ParentEventId = null,
            SequenceNumber = sequence,
            PublishWallclock = ctx.Clock.Now,
            ReceiveWallclock = ctx.Clock.Now,
            PublisherNode = node,
            SubscriberNode = node,
            Topic = new TopicName(SessionStartTopic),
            EntityId = null,
            OwningPlayerId = null,
            ScenarioPhase = ScenarioPhaseName,
            Severity = null,
            NotableLabel = SessionStartLabel,
            PayloadJson = "{\"scenarioId\":\"calm\",\"label\":\"Calm scenario test session\"}",
        };

    private static AgentId[] BuildNodes(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new AgentId($"node-{i:D2}"))
            .ToArray();

    private static EntityId[] BuildEntities(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new EntityId($"entity:{i:D3}"))
            .ToArray();
}
