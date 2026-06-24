using FluentAssertions;
using lazydotnet.Services;

namespace lazydotnet.UnitTests;

public class TestRunAggregatorTests
{
    [Fact]
    public void AggregateProgress_NoActivity_ReturnsNull()
    {
        TestRunAggregator.AggregateProgress([], expectedTotal: 0).Should().BeNull();
    }

    [Fact]
    public void AggregateProgress_BeforeAnyReport_KeepsExpectedTotal()
    {
        var result = TestRunAggregator.AggregateProgress([], expectedTotal: 5);

        result.Should().NotBeNull();
        result!.Completed.Should().Be(0);
        result.Total.Should().Be(5);
    }

    [Fact]
    public void AggregateProgress_MultipleSources_SumsCounts()
    {
        var perSource = new[]
        {
            new TestRunProgress("A", Completed: 2, Passed: 2, Failed: 0, Skipped: 0, Total: 3),
            new TestRunProgress("B", Completed: 1, Passed: 0, Failed: 1, Skipped: 0, Total: 2)
        };

        var result = TestRunAggregator.AggregateProgress(perSource, expectedTotal: 5);

        result.Should().NotBeNull();
        result!.Completed.Should().Be(3);
        result.Passed.Should().Be(2);
        result.Failed.Should().Be(1);
        result.Total.Should().Be(5);
    }

    [Fact]
    public void AggregateProgress_PartialSources_DenominatorStaysAtExpectedTotal()
    {
        // Only one of two sources has reported its per-source total so far.
        var perSource = new[]
        {
            new TestRunProgress("A", Completed: 1, Passed: 1, Failed: 0, Skipped: 0, Total: 3)
        };

        var result = TestRunAggregator.AggregateProgress(perSource, expectedTotal: 5);

        result!.Total.Should().Be(5);
    }

    [Fact]
    public void AggregateCompleted_NotAllSourcesFinished_ReturnsNull()
    {
        var completed = new[]
        {
            new TestRunCompleted("A", Passed: 3, Failed: 0, Skipped: 0, DurationMs: 100)
        };

        TestRunAggregator.AggregateCompleted(completed, startedSourceCount: 2).Should().BeNull();
    }

    [Fact]
    public void AggregateCompleted_NothingStarted_ReturnsNull()
    {
        TestRunAggregator.AggregateCompleted([], startedSourceCount: 0).Should().BeNull();
    }

    [Fact]
    public void AggregateCompleted_AllSourcesFinished_SumsAndTakesMaxDuration()
    {
        var completed = new[]
        {
            new TestRunCompleted("A", Passed: 3, Failed: 1, Skipped: 0, DurationMs: 100),
            new TestRunCompleted("B", Passed: 2, Failed: 0, Skipped: 1, DurationMs: 250)
        };

        var result = TestRunAggregator.AggregateCompleted(completed, startedSourceCount: 2);

        result.Should().NotBeNull();
        result!.Passed.Should().Be(5);
        result.Failed.Should().Be(1);
        result.Skipped.Should().Be(1);
        result.DurationMs.Should().Be(250);
    }

    [Fact]
    public void AggregateCompleted_NoDurations_ReturnsNullDuration()
    {
        var completed = new[]
        {
            new TestRunCompleted("A", Passed: 1, Failed: 0, Skipped: 0, DurationMs: null)
        };

        var result = TestRunAggregator.AggregateCompleted(completed, startedSourceCount: 1);

        result!.DurationMs.Should().BeNull();
    }
}
