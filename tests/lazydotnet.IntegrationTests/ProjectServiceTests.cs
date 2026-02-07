using FluentAssertions;
using lazydotnet.Services;
using Microsoft.Build.Locator;

namespace lazydotnet.IntegrationTests;

public sealed class ProjectServiceTests : IDisposable
{
    private readonly string _testDir;

    static ProjectServiceTests()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }

    public ProjectServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "lazydotnet_project_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }

    [Fact]
    public async Task GetProjectReferencesAsync_WithNoReferences_ShouldReturnEmpty()
    {
        // Arrange
        TestUtils.CopyFixture("SimpleLibrary", _testDir);
        var projectPath = Path.Combine(_testDir, "SimpleLibrary", "SimpleLibrary.csproj");

        // Act
        var result = await ProjectService.GetProjectReferencesAsync(projectPath);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProjectReferencesAsync_WithMultipleReferences_ShouldReturnAll()
    {
        // Arrange
        TestUtils.CopyFixture("SimpleApp", _testDir);
        TestUtils.CopyFixture("SimpleLibrary", _testDir);

        // We'll create a third project dynamically just for this test to have 2 refs
        var lib2 = TestUtils.CreateTestProject(_testDir, "Lib2");
        var mainProj = Path.Combine(_testDir, "SimpleApp", "SimpleApp.csproj");
        var lib1 = Path.Combine(_testDir, "SimpleLibrary", "SimpleLibrary.csproj");

        await ProjectService.AddProjectReferenceAsync(mainProj, lib1);
        await ProjectService.AddProjectReferenceAsync(mainProj, lib2);

        // Act
        var result = await ProjectService.GetProjectReferencesAsync(mainProj);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(Path.GetFullPath(lib1));
        result.Should().Contain(Path.GetFullPath(lib2));
    }

    [Fact]
    public async Task RemoveProjectReferenceAsync_ShouldRemoveReference()
    {
        // Arrange
        TestUtils.CopyFixture("SimpleApp", _testDir);
        TestUtils.CopyFixture("SimpleLibrary", _testDir);
        var mainProj = Path.Combine(_testDir, "SimpleApp", "SimpleApp.csproj");
        var libProj = Path.Combine(_testDir, "SimpleLibrary", "SimpleLibrary.csproj");
        await ProjectService.AddProjectReferenceAsync(mainProj, libProj);

        // Act
        await ProjectService.RemoveProjectReferenceAsync(mainProj, libProj);

        // Assert
        var references = await ProjectService.GetProjectReferencesAsync(mainProj);
        references.Should().NotContain(Path.GetFullPath(libProj));
    }
}
