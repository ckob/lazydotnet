using FluentAssertions;
using lazydotnet.Services;

namespace lazydotnet.UnitTests;

public class NuGetServiceTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.1", VersionUpdateType.Patch)]
    [InlineData("1.0.0", "1.1.0", VersionUpdateType.Minor)]
    [InlineData("1.0.0", "2.0.0", VersionUpdateType.Major)]
    [InlineData("1.0.0", "1.0.0", VersionUpdateType.None)]
    [InlineData("1.0.0", "1.0.2", VersionUpdateType.Patch)]
    [InlineData("1.0.0", "1.2.0", VersionUpdateType.Minor)]
    [InlineData("2.0.0", "2.0.1", VersionUpdateType.Patch)]
    [InlineData("2.0.0", "2.1.0", VersionUpdateType.Minor)]
    [InlineData("2.0.0", "3.0.0", VersionUpdateType.Major)]
    [InlineData("1.0.0-beta", "1.0.0", VersionUpdateType.Major)]
    [InlineData("1.0.0", "1.0.0-beta", VersionUpdateType.Major)]
    [InlineData("1.0.0-beta", "1.0.0-rc1", VersionUpdateType.Major)]
    [InlineData("1.0.0", "2.0.0-beta", VersionUpdateType.Major)]
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
    public void NuGetPackageInfo_IsOutdated_ShouldReturnTrue_WhenLatestDiffers()
    {
        var pkg = new NuGetPackageInfo("TestPkg", "1.0.0", "1.0.1");
        pkg.IsOutdated.Should().BeTrue();
    }

    [Fact]
    public void NuGetPackageInfo_IsOutdated_ShouldReturnFalse_WhenSame()
    {
        var pkg = new NuGetPackageInfo("TestPkg", "1.0.0", "1.0.0");
        pkg.IsOutdated.Should().BeFalse();
    }

    [Fact]
    public void NuGetPackageInfo_IsOutdated_ShouldReturnFalse_WhenLatestIsNull()
    {
        var pkg = new NuGetPackageInfo("TestPkg", "1.0.0", null);
        pkg.IsOutdated.Should().BeFalse();
    }
}
