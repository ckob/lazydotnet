using FluentAssertions;
using lazydotnet.Services;
using CliWrap;

namespace lazydotnet.Tests;

public class AppCliTests
{
    [Fact]
    public async Task RunAsync_ShouldInvokeOnLog()
    {
        // Arrange
        var logged = false;
        AppCli.OnLog += _ => logged = true;
        var cmd = Cli.Wrap("dotnet").WithArguments("--version");

        // Act
        await AppCli.RunAsync(cmd, TestContext.Current.CancellationToken);

        // Assert
        logged.Should().BeTrue();
    }
}
