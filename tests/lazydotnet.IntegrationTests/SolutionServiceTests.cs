using FluentAssertions;
using lazydotnet.Services;
using Microsoft.Build.Locator;

namespace lazydotnet.IntegrationTests;

public sealed class SolutionServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly SolutionService _service;

    static SolutionServiceTests()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }

    public SolutionServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "lazydotnet_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _service = new SolutionService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }

    [Fact]
    public async Task FindAndParseSolutionAsync_WithCsproj_ShouldReturnSolutionInfo()
    {
        // Arrange
        TestUtils.CopyFixture("SimpleApp", _testDir);
        var projectPath = Path.Combine(_testDir, "SimpleApp", "SimpleApp.csproj");

        // Act
        var result = await _service.FindAndParseSolutionAsync(projectPath);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("SimpleApp");
        result.Projects.Should().HaveCount(1);
        result.Projects[0].Name.Should().Be("SimpleApp");
        result.Projects[0].IsRunnable.Should().BeTrue();
    }

    [Fact]
    public async Task FindAndParseSolutionAsync_WithSln_ShouldReturnSolutionInfoWithProjects()
    {
        // Arrange
        TestUtils.CopyFixture("SimpleLibrary", _testDir);
        TestUtils.CopyFixture("SimpleApp", _testDir);
        TestUtils.CopyFixture("XUnit2Project", _testDir);
        TestUtils.CopyFixture("XUnit3Project", _testDir);
        TestUtils.CopyFixture("NUnitProject", _testDir);
        TestUtils.CopyFixture("MSTestProject", _testDir);
        var slnPath = TestUtils.CopyFixture("SimpleSolution.sln", _testDir);

        // Act
        var result = await _service.FindAndParseSolutionAsync(slnPath);

        // Assert
        result.Should().NotBeNull();
        result!.Projects.Should().HaveCount(6);
        result.Projects.Select(p => p.Name).Should().Contain(["SimpleLibrary", "SimpleApp", "XUnit2Project", "XUnit3Project", "NUnitProject", "MSTestProject"]);
    }

    [Fact]
    public async Task DiscoverWorkspacesAsync_ShouldFindProjectsAndSolutions()
    {
        // Arrange
        TestUtils.CopyFixture("SimpleLibrary", _testDir);
        TestUtils.CopyFixture("SimpleApp", _testDir);

        // Act
        var results = await SolutionService.DiscoverWorkspacesAsync(_testDir);

        // Assert
        results.Should().HaveCountGreaterThanOrEqualTo(2);
        results.Select(r => r.Name).Should().Contain(["SimpleLibrary.csproj", "SimpleApp.csproj"]);
    }

    [Fact]
    public async Task FindAndParseSolutionAsync_WithNonExistentPath_ShouldReturnNull()
    {
        // Act
        var result = await _service.FindAndParseSolutionAsync("non-existent-path");

        // Assert
        result.Should().BeNull();
    }
}
