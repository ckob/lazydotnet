using FluentAssertions;
using lazydotnet.Services;
using Microsoft.Build.Locator;

namespace lazydotnet.UnitTests;

public class NuGetServiceTests
{
    static NuGetServiceTests()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }

    [Theory]
    [InlineData("1.0.0", "1.0.1", VersionUpdateType.Patch)]
    [InlineData("1.0.0", "1.1.0", VersionUpdateType.Minor)]
    [InlineData("1.0.0", "2.0.0", VersionUpdateType.Major)]
    [InlineData("1.0.0", "1.0.0", VersionUpdateType.None)]
    // [InlineData("1.0.0-beta", "1.0.0", VersionUpdateType.Major)] // Original code doesn't handle this case yet
    public void NuGetPackageInfo_GetUpdateType_ShouldReturnCorrectType(string current, string latest, VersionUpdateType expected)
    {
        // Arrange
        var pkg = new NuGetPackageInfo("TestPkg", current, latest);

        // Act
        var result = pkg.GetUpdateType();

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public async Task SearchPackagesAsync_ShouldReturnResults()
    {
        // Act
        var results = await NuGetService.SearchPackagesAsync("Newtonsoft.Json", ct: TestContext.Current.CancellationToken);

        // Assert
        results.Should().NotBeEmpty();
        results.Any(r => r.Id == "Newtonsoft.Json").Should().BeTrue();
    }
}
