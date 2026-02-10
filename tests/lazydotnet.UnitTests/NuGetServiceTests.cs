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
    public void NuGetPackageInfo_GetUpdateType_ShouldReturnCorrectType(string current, string latest, VersionUpdateType expected)
    {
        // Arrange
        var pkg = new NuGetPackageInfo("TestPkg", current, latest);

        // Act
        var result = pkg.GetUpdateType();

        // Assert
        result.Should().Be(expected);
    }
}
