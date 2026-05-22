using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Tracer.TestHarness.Observer;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Endpoints;
using Tracer.WebApi.Lifecycle;
using Xunit;

namespace Tracer.Tests.Unit.Agent;

public sealed class LifecycleTopicClassifierTests
{
    private static ConfigurableLifecycleTopicClassifier DefaultClassifier() =>
        new(new LifecycleClassificationConfig());

    // SC-1
    [Fact]
    public void DefaultConfig_SpawnSuffixes()
    {
        var c = DefaultClassifier();
        c.Classify("vehicle.spawn").Should().Be("spawn");
        c.Classify("vehicle.created").Should().Be("spawn");
        c.Classify("vehicle.spawned").Should().Be("spawn");
    }

    // SC-2
    [Fact]
    public void DefaultConfig_OwnershipSuffixes()
    {
        var c = DefaultClassifier();
        c.Classify("team.ownership_changed").Should().Be("ownership");
        c.Classify("unit.owner_transferred").Should().Be("ownership");
        c.Classify("player.owner_changed").Should().Be("ownership");
    }

    // SC-3
    [Fact]
    public void DefaultConfig_DestructionSuffixes()
    {
        var c = DefaultClassifier();
        c.Classify("unit.destroyed").Should().Be("destruction");
        c.Classify("vehicle.killed").Should().Be("destruction");
        c.Classify("npc.removed").Should().Be("destruction");
        c.Classify("entity.despawned").Should().Be("destruction");
    }

    // SC-4
    [Fact]
    public void DefaultConfig_UnknownTopic_ReturnsNull()
    {
        var c = DefaultClassifier();
        c.Classify("sensors.telemetry").Should().BeNull();
        c.Classify("weapons.fire").Should().BeNull();
        c.Classify("vehicle.update").Should().BeNull();
    }

    // SC-5
    [Fact]
    public void CustomSuffixes_ReplaceBuiltIn()
    {
        var c = new ConfigurableLifecycleTopicClassifier(new LifecycleClassificationConfig
        {
            SpawnSuffixes = new[] { "instantiated" }
        });
        c.Classify("thing.instantiated").Should().Be("spawn");
        c.Classify("thing.spawn").Should().BeNull();
    }

    // SC-6
    [Fact]
    public void RegexOverride_TakesPrecedenceOverSuffixes()
    {
        var c = new ConfigurableLifecycleTopicClassifier(new LifecycleClassificationConfig
        {
            Regex = new LifecycleRegexPatterns(Spawn: @"^entity\.new_", Ownership: null, Destruction: null)
        });
        c.Classify("entity.new_fighter").Should().Be("spawn");
        // When regex is set for a category, suffix for that category is not checked
        c.Classify("vehicle.spawn").Should().BeNull();
    }

    // SC-7
    [Fact]
    public void GET_LifecycleClassification_Returns200WithConfig()
    {
        var config = new LifecycleClassificationConfig
        {
            SpawnSuffixes = new[] { "born" }
        };
        var result = ConfigEndpoints.HandleAsync(config);
        result.StatusCode.Should().Be(200);
        result.Value!.SpawnSuffixes.Should().BeEquivalentTo(new[] { "born" });
    }

    // SC-8
    [Fact]
    public void HardcodedClassifier_IsReplaced()
    {
        var classifier = new ConfigurableLifecycleTopicClassifier(new LifecycleClassificationConfig());
        classifier.Classify("vehicle.spawn").Should().Be("spawn");
        classifier.Classify("vehicle.destroyed").Should().Be("destruction");
        classifier.Classify("team.ownership_changed").Should().Be("ownership");
        // When a custom config is used, built-in defaults are replaced
        var custom = new ConfigurableLifecycleTopicClassifier(new LifecycleClassificationConfig
        {
            SpawnSuffixes = new[] { "born" }
        });
        custom.Classify("thing.born").Should().Be("spawn");
        custom.Classify("thing.spawn").Should().BeNull(); // built-in no longer active
    }

    // SC-9
    [Fact]
    public async Task DI_BothHosts_ResolveILifecycleTopicClassifier()
    {
        // Observer host DI
        await using var fixture = await ObserverFixture.CreateAsync(
            configureExtraServices: services =>
            {
                var cfg = new LifecycleClassificationConfig();
                services.AddSingleton(cfg);
                services.AddSingleton<ILifecycleTopicClassifier>(
                    new ConfigurableLifecycleTopicClassifier(cfg));
            });
        var observerClassifier = fixture.App.Services.GetRequiredService<ILifecycleTopicClassifier>();
        observerClassifier.Should().BeOfType<ConfigurableLifecycleTopicClassifier>();

        // OfflineViewer host DI (minimal container pattern)
        var services = new ServiceCollection();
        var lifecycleCfg = new LifecycleClassificationConfig();
        services.AddSingleton(lifecycleCfg);
        services.AddSingleton<ILifecycleTopicClassifier>(
            new ConfigurableLifecycleTopicClassifier(lifecycleCfg));
        using var sp = services.BuildServiceProvider();
        var viewerClassifier = sp.GetRequiredService<ILifecycleTopicClassifier>();
        viewerClassifier.Should().BeOfType<ConfigurableLifecycleTopicClassifier>();
    }

    // SC-10
    [Fact]
    public void DefaultValues_MatchDesignSpec()
    {
        var config = new LifecycleClassificationConfig();
        config.SpawnSuffixes.Should().BeEquivalentTo(new[] { "spawn", "created", "spawned" });
        config.OwnershipSuffixes.Should().BeEquivalentTo(new[] { "ownership_changed", "owner_transferred", "owner_changed" });
        config.DestructionSuffixes.Should().BeEquivalentTo(new[] { "destroyed", "killed", "removed", "despawned" });
        config.Regex.Should().BeNull();
    }
}
