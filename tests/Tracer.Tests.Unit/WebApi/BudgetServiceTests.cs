using System.Text.Json;
using FluentAssertions;
using Tracer.Core.Domain;
using Tracer.WebApi.Queries;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

public sealed class BudgetServiceTests
{
    [Fact]
    public async Task LiveMode_Registry_EmptyByDefault()
    {
        var registry = new InMemoryBudgetRegistry();
        var svc = new BudgetService(getBundleWorkingDirectory: null, registry: registry);

        var budgets = await svc.GetBudgetsAsync("any", CancellationToken.None);

        budgets.Should().BeEmpty();
    }

    [Fact]
    public async Task LiveMode_Registry_ReturnsRegistered()
    {
        var registry = new InMemoryBudgetRegistry();
        registry.Register(new LatencyBudget { Topic = "my.topic", P99BudgetMs = 10.0, AbsoluteMaxMs = 50.0 });

        var svc = new BudgetService(getBundleWorkingDirectory: null, registry: registry);

        var budgets = await svc.GetBudgetsAsync("any", CancellationToken.None);

        budgets.Should().HaveCount(1);
        budgets[0].Topic.Should().Be("my.topic");
        budgets[0].P99BudgetMs.Should().Be(10.0);
        budgets[0].AbsoluteMaxMs.Should().Be(50.0);
    }

    [Fact]
    public async Task BundleMode_MetadataJson_Parsed()
    {
        var dir = Directory.CreateTempSubdirectory("budget_test").FullName;
        try
        {
            var meta = new
            {
                latencyBudgets = new object[]
                {
                    new { topic = "t1", p99BudgetMs = 5.0, absoluteMaxMs = 20.0 },
                    new { topic = "t2", p99BudgetMs = (double?)null, absoluteMaxMs = (double?)null },
                }
            };
            await File.WriteAllTextAsync(Path.Combine(dir, "metadata.json"),
                JsonSerializer.Serialize(meta));

            var svc = new BudgetService(getBundleWorkingDirectory: () => dir);

            var budgets = await svc.GetBudgetsAsync("any", CancellationToken.None);

            budgets.Should().HaveCount(2);
            budgets[0].Topic.Should().Be("t1");
            budgets[0].P99BudgetMs.Should().Be(5.0);
            budgets[0].AbsoluteMaxMs.Should().Be(20.0);
            budgets[1].Topic.Should().Be("t2");
            budgets[1].P99BudgetMs.Should().BeNull();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task BundleMode_MissingFile_Empty()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), $"no_such_dir_{Guid.NewGuid()}");
        var svc = new BudgetService(getBundleWorkingDirectory: () => nonExistent);

        var budgets = await svc.GetBudgetsAsync("any", CancellationToken.None);

        budgets.Should().BeEmpty();
    }

    [Fact]
    public async Task BundleMode_NoLatencyBudgetsProperty_Empty()
    {
        var dir = Directory.CreateTempSubdirectory("budget_noprop").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "metadata.json"),
                JsonSerializer.Serialize(new { someOtherProp = 42 }));

            var svc = new BudgetService(getBundleWorkingDirectory: () => dir);
            var budgets = await svc.GetBudgetsAsync("any", CancellationToken.None);

            budgets.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task BundleMode_MalformedJson_ReturnsEmpty()
    {
        var dir = Directory.CreateTempSubdirectory("budget_bad").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "metadata.json"), "NOT_JSON {{{");

            var svc = new BudgetService(getBundleWorkingDirectory: () => dir);
            var budgets = await svc.GetBudgetsAsync("any", CancellationToken.None);

            budgets.Should().BeEmpty(); // caught and swallowed
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task NoBundleDir_EmptyWithoutRegistry()
    {
        // No registry, no working directory
        var svc = new BudgetService(getBundleWorkingDirectory: null, registry: null);

        var budgets = await svc.GetBudgetsAsync("any", CancellationToken.None);

        budgets.Should().BeEmpty();
    }

    [Fact]
    public void InMemoryRegistry_Register_Multiple()
    {
        var registry = new InMemoryBudgetRegistry();
        registry.Register(new LatencyBudget { Topic = "t1" });
        registry.Register(new LatencyBudget { Topic = "t2" });
        registry.Register(new LatencyBudget { Topic = "t3" });

        registry.GetAll().Should().HaveCount(3);
        registry.GetAll().Select(b => b.Topic).Should().Contain(["t1", "t2", "t3"]);
    }

    [Fact]
    public async Task BundleMode_NullWorkDir_Empty()
    {
        var svc = new BudgetService(getBundleWorkingDirectory: () => null);

        var budgets = await svc.GetBudgetsAsync("any", CancellationToken.None);

        budgets.Should().BeEmpty();
    }
}
