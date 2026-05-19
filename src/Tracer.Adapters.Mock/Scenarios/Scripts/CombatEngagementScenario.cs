using System.Runtime.CompilerServices;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;

namespace Tracer.Adapters.Mock.Scenarios.Scripts;

/// <summary>
/// Produces a three-phase combat sequence (approach -> engagement -> withdrawal).
/// During the engagement phase, fired shots produce causal chains:
/// shot_fired -> projectile_spawn -> projectile_impact -> damage_applied,
/// all sharing a single TraceId and each carrying a ParentEventId
/// referencing the preceding event in the chain.
/// Parents are always yielded before their children.
/// </summary>
public sealed class CombatEngagementScenario : IScenarioScript
{
    private const string PhaseApproach = "approach";
    private const string PhaseEngagement = "engagement";
    private const string PhaseWithdrawal = "withdrawal";

    private const double HeartbeatIntervalSeconds = 0.1;
    private const double ShotIntervalSeconds = 2.0;

    private static readonly TimeSpan ChainEventInterval = TimeSpan.FromMilliseconds(5);

    private static readonly AgentId[] Nodes =
    [
        new AgentId("blue-cmd"),
        new AgentId("blue-veh"),
        new AgentId("red-cmd"),
        new AgentId("red-veh"),
    ];

    private static readonly EntityId[] Entities =
    [
        new EntityId("vehicle:blue:0"),
        new EntityId("vehicle:blue:1"),
        new EntityId("vehicle:red:0"),
        new EntityId("vehicle:red:1"),
    ];

    public string Name => "CombatEngagement";

    public async IAsyncEnumerable<DiagnosticRecord> ExecuteAsync(
        ScenarioContext ctx,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var startTime = ctx.Clock.Now;
        var duration = ctx.Config.Duration;
        var phaseLength = TimeSpan.FromTicks(duration.Ticks / 3);
        var approachEnd = startTime + phaseLength;
        var engagementEnd = startTime + TimeSpan.FromTicks(phaseLength.Ticks * 2);
        var withdrawalEnd = startTime + duration;

        ulong sequence = 0;

        while (ctx.Clock.Now.CompareTo(approachEnd) < 0 && !ct.IsCancellationRequested)
        {
            yield return MakeHeartbeat(ctx, sequence++, PhaseApproach);
            ctx.Clock.Advance(TimeSpan.FromSeconds(HeartbeatIntervalSeconds));
            await Task.Yield();
        }

        while (ctx.Clock.Now.CompareTo(engagementEnd) < 0 && !ct.IsCancellationRequested)
        {
            var chain = MakeShotChain(ctx, sequence);
            sequence += (ulong)chain.Count;
            foreach (var record in chain)
                yield return record;

            ctx.Clock.Advance(TimeSpan.FromSeconds(ShotIntervalSeconds));
            await Task.Yield();
        }

        while (ctx.Clock.Now.CompareTo(withdrawalEnd) < 0 && !ct.IsCancellationRequested)
        {
            yield return MakeHeartbeat(ctx, sequence++, PhaseWithdrawal);
            ctx.Clock.Advance(TimeSpan.FromSeconds(HeartbeatIntervalSeconds));
            await Task.Yield();
        }
    }

    private static EventRecord MakeHeartbeat(ScenarioContext ctx, ulong sequenceNumber, string phase)
    {
        var node = Nodes[ctx.Random.Next(Nodes.Length)];
        var entity = Entities[ctx.Random.Next(Entities.Length)];
        return new EventRecord
        {
            EventId = ctx.TraceIdGen.NewEvent(),
            TraceId = ctx.TraceIdGen.NewTrace(),
            ParentEventId = null,
            SequenceNumber = sequenceNumber,
            PublishWallclock = ctx.Clock.Now,
            ReceiveWallclock = ctx.Clock.Now + TimeSpan.FromMilliseconds(1),
            PublisherNode = node,
            SubscriberNode = node,
            Topic = new TopicName("scenario.heartbeat"),
            EntityId = entity,
            OwningPlayerId = null,
            ScenarioPhase = phase,
            Severity = null,
            NotableLabel = null,
            PayloadJson = $"{{\"kind\":\"heartbeat\",\"phase\":\"{phase}\"}}",
        };
    }

    private static List<EventRecord> MakeShotChain(ScenarioContext ctx, ulong startSequence)
    {
        ulong seq = startSequence;

        var shooterNode = Nodes[ctx.Random.Next(Nodes.Length)];
        var targetNode = Nodes[ctx.Random.Next(Nodes.Length)];
        var targetEntity = Entities[ctx.Random.Next(Entities.Length)];
        var traceId = ctx.TraceIdGen.NewTrace();

        var shotTime = ctx.Clock.Now;
        ctx.Clock.Advance(ChainEventInterval);
        var spawnTime = ctx.Clock.Now;
        ctx.Clock.Advance(ChainEventInterval);
        var impactTime = ctx.Clock.Now;
        ctx.Clock.Advance(ChainEventInterval);
        var damageTime = ctx.Clock.Now;

        var shotFiredId = ctx.TraceIdGen.NewEvent();
        var spawnId = ctx.TraceIdGen.NewEvent();
        var impactId = ctx.TraceIdGen.NewEvent();
        var damageId = ctx.TraceIdGen.NewEvent();

        return
        [
            new EventRecord
            {
                EventId = shotFiredId,
                TraceId = traceId,
                ParentEventId = null,
                SequenceNumber = seq++,
                PublishWallclock = shotTime,
                ReceiveWallclock = shotTime,
                PublisherNode = shooterNode,
                SubscriberNode = shooterNode,
                Topic = new TopicName("shot_fired"),
                EntityId = targetEntity,
                OwningPlayerId = null,
                ScenarioPhase = PhaseEngagement,
                Severity = null,
                NotableLabel = null,
                PayloadJson = "{\"kind\":\"shot_fired\"}",
            },
            new EventRecord
            {
                EventId = spawnId,
                TraceId = traceId,
                ParentEventId = shotFiredId,
                SequenceNumber = seq++,
                PublishWallclock = spawnTime,
                ReceiveWallclock = spawnTime,
                PublisherNode = shooterNode,
                SubscriberNode = shooterNode,
                Topic = new TopicName("projectile_spawn"),
                EntityId = targetEntity,
                OwningPlayerId = null,
                ScenarioPhase = PhaseEngagement,
                Severity = null,
                NotableLabel = null,
                PayloadJson = "{\"kind\":\"projectile_spawn\"}",
            },
            new EventRecord
            {
                EventId = impactId,
                TraceId = traceId,
                ParentEventId = spawnId,
                SequenceNumber = seq++,
                PublishWallclock = impactTime,
                ReceiveWallclock = impactTime,
                PublisherNode = targetNode,
                SubscriberNode = targetNode,
                Topic = new TopicName("projectile_impact"),
                EntityId = targetEntity,
                OwningPlayerId = null,
                ScenarioPhase = PhaseEngagement,
                Severity = null,
                NotableLabel = null,
                PayloadJson = "{\"kind\":\"projectile_impact\"}",
            },
            new EventRecord
            {
                EventId = damageId,
                TraceId = traceId,
                ParentEventId = impactId,
                SequenceNumber = seq,
                PublishWallclock = damageTime,
                ReceiveWallclock = damageTime,
                PublisherNode = targetNode,
                SubscriberNode = targetNode,
                Topic = new TopicName("damage_applied"),
                EntityId = targetEntity,
                OwningPlayerId = null,
                ScenarioPhase = PhaseEngagement,
                Severity = null,
                NotableLabel = null,
                PayloadJson = $"{{\"kind\":\"damage_applied\",\"entity\":\"{targetEntity.Value}\"}}",
            },
        ];
    }
}
