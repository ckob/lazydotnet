using FluentAssertions;
using lazydotnet.Services;
using Spectre.Console;

namespace lazydotnet.Tests;

public class CommandServiceTests
{
    [Fact]
    public async Task BuildProjectAsync_ShouldLogRunningCommand()
    {
        // Arrange
        var loggedMessages = new List<string>();
        AppCli.OnLog += s => loggedMessages.Add(s);
        var ct = new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token;

        // Act
        // We use a non-existent project to keep it fast, or a dummy one.
        // BuildProjectAsync will fail but we want to see if it starts.
        try 
        {
            await CommandService.BuildProjectAsync("non-existent.csproj", _ => {}, ct);
        }
        catch
        {
            // Expected failure
        }

        // Assert
        loggedMessages.Should().Contain(m => m.Contains("Running: dotnet build"));
    }
}
