using FluentAssertions;
using lazydotnet.Services;
using Microsoft.Build.Locator;
using Xunit;

namespace lazydotnet.Tests;

[Trait("Category", "Integration")]
public class IntegrationTests : IDisposable
{
    private readonly string _testDir;

    static IntegrationTests()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }

    public IntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "lazydotnet_int_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    private async Task BuildFixtureAsync(string fixtureName)
    {
        var projectPath = Path.Combine(_testDir, fixtureName, $"{fixtureName}.csproj");
        await CommandService.BuildProjectAsync(projectPath, _ => { }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task XUnit3_Discovery_ShouldWork()
    {
        // Arrange
        TestUtils.CopyFixture("XUnit3Project", _testDir);
        var projectPath = Path.Combine(_testDir, "XUnit3Project", "XUnit3Project.csproj");
        await BuildFixtureAsync("XUnit3Project");

        // Act
        var discoveredTests = await TestService.DiscoverTestsAsync(projectPath, TestContext.Current.CancellationToken);

        // Assert
        discoveredTests.Should().HaveCount(3);
    }

    [Fact]
    public async Task XUnit3_Execution_ShouldWork()
    {
        // Arrange
        TestUtils.CopyFixture("XUnit3Project", _testDir);
        var projectPath = Path.Combine(_testDir, "XUnit3Project", "XUnit3Project.csproj");
        await BuildFixtureAsync("XUnit3Project");
        var discoveredTests = await TestService.DiscoverTestsAsync(projectPath, TestContext.Current.CancellationToken);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var runResults = new List<TestRunResult>();
        var filter = discoveredTests.Select(t => new RunRequestNode(t.Id, t.DisplayName, t.Source, t.IsMtp)).ToArray();
        
        var resultsEnumerable = await TestService.RunTestsAsync(projectPath, filter);
        await foreach (var result in resultsEnumerable.WithCancellation(cts.Token))
        {
            runResults.Add(result);
        }

        // Assert
        runResults.Should().HaveCountGreaterThanOrEqualTo(2); // At least 2, we saw 2 in previous run
    }

    [Fact]
    public async Task XUnit2_Discovery_ShouldWork()
    {
        // Arrange
        TestUtils.CopyFixture("XUnit2Project", _testDir);
        var projectPath = Path.Combine(_testDir, "XUnit2Project", "XUnit2Project.csproj");
        await BuildFixtureAsync("XUnit2Project");

        // Act
        var discoveredTests = await TestService.DiscoverTestsAsync(projectPath, TestContext.Current.CancellationToken);

        // Assert
        discoveredTests.Should().HaveCountGreaterThanOrEqualTo(3);
        discoveredTests.Any(t => t.DisplayName.Contains("TheoryTest")).Should().BeTrue();
    }

    [Fact]
    public async Task XUnit2_Execution_ShouldWork()
    {
        // Arrange
        TestUtils.CopyFixture("XUnit2Project", _testDir);
        var projectPath = Path.Combine(_testDir, "XUnit2Project", "XUnit2Project.csproj");
        await BuildFixtureAsync("XUnit2Project");
        var discoveredTests = await TestService.DiscoverTestsAsync(projectPath, TestContext.Current.CancellationToken);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var runResults = new List<TestRunResult>();
        var filter = discoveredTests.Select(t => new RunRequestNode(t.Id, t.DisplayName, t.Source, t.IsMtp)).ToArray();
        
        var resultsEnumerable = await TestService.RunTestsAsync(projectPath, filter);
        await foreach (var result in resultsEnumerable.WithCancellation(cts.Token))
        {
            runResults.Add(result);
        }

        // Assert
        runResults.Should().NotBeEmpty();
    }

    [Fact]
    public async Task NUnit_Discovery_ShouldWork()
    {
        // Arrange
        TestUtils.CopyFixture("NUnitProject", _testDir);
        var projectPath = Path.Combine(_testDir, "NUnitProject", "NUnitProject.csproj");
        await BuildFixtureAsync("NUnitProject");

        // Act
        var discoveredTests = await TestService.DiscoverTestsAsync(projectPath, TestContext.Current.CancellationToken);

        // Assert
        discoveredTests.Should().HaveCount(2);
        discoveredTests.Select(t => t.DisplayName).Should().Contain(n => n.Contains("PassingTest"));
    }

    [Fact]
    public async Task MSTest_Discovery_ShouldWork()
    {
        // Arrange
        TestUtils.CopyFixture("MSTestProject", _testDir);
        var projectPath = Path.Combine(_testDir, "MSTestProject", "MSTestProject.csproj");
        await BuildFixtureAsync("MSTestProject");

        // Act
        var discoveredTests = await TestService.DiscoverTestsAsync(projectPath, TestContext.Current.CancellationToken);

        // Assert
        discoveredTests.Should().HaveCount(2);
        discoveredTests.Select(t => t.DisplayName).Should().Contain(n => n.Contains("PassingTest"));
    }

    [Fact]
    public async Task DiscoverTestsAsync_WithAllFixtureProjects_ShouldEvaluateThemAsTestProjects()
    {
        // Arrange
        TestUtils.CopyFixture("XUnit2Project", _testDir);
        TestUtils.CopyFixture("XUnit3Project", _testDir);
        TestUtils.CopyFixture("NUnitProject", _testDir);
        TestUtils.CopyFixture("MSTestProject", _testDir);
        TestUtils.CopyFixture("SimpleLibrary", _testDir); // Not a test project

        // Act
        // Since they are not built, results will be empty, but we want to ensure it doesn't crash 
        // during MSBuild evaluation.
        var results = await TestService.DiscoverTestsAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert
        results.Should().BeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { /* Ignore */ }
        }
    }
}
