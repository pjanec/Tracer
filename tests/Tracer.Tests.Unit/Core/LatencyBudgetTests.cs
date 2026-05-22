using FluentAssertions;
using Tracer.Core.Domain;
using Xunit;

namespace Tracer.Tests.Unit.Core;

public sealed class LatencyBudgetTests
{
    [Fact]
    public void LatencyBudget_RequiredTopic_ConstructsCorrectly()
    {
        var budget = new LatencyBudget
        {
            Topic = "weapons.fire",
            P99BudgetMs = 50.0,
            AbsoluteMaxMs = 200.0,
        };

        budget.Topic.Should().Be("weapons.fire");
        budget.P99BudgetMs.Should().Be(50.0);
        budget.AbsoluteMaxMs.Should().Be(200.0);
    }

    [Fact]
    public void LatencyBudget_NullableBudgets_AreNull()
    {
        var budget = new LatencyBudget { Topic = "physics.update" };

        budget.P99BudgetMs.Should().BeNull();
        budget.AbsoluteMaxMs.Should().BeNull();
    }

    [Fact]
    public void LatencyBudget_Equality_SameValues()
    {
        var a = new LatencyBudget { Topic = "nav.position", P99BudgetMs = 10.0, AbsoluteMaxMs = 50.0 };
        var b = new LatencyBudget { Topic = "nav.position", P99BudgetMs = 10.0, AbsoluteMaxMs = 50.0 };

        (a == b).Should().BeTrue();
    }

    [Fact]
    public void LatencyBudget_Equality_DifferentTopic()
    {
        var a = new LatencyBudget { Topic = "topic-a" };
        var b = new LatencyBudget { Topic = "topic-b" };

        (a == b).Should().BeFalse();
    }

    [Fact]
    public void LatencyBudget_NoBudget_NullIsDistinctFromZero()
    {
        var withNull = new LatencyBudget { Topic = "t", P99BudgetMs = null };
        var withZero = new LatencyBudget { Topic = "t", P99BudgetMs = 0.0 };

        withNull.P99BudgetMs.Should().BeNull();
        withZero.P99BudgetMs.Should().Be(0.0);
        (withNull == withZero).Should().BeFalse();
    }
}
