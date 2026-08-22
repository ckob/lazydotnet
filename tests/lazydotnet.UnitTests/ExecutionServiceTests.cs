using FluentAssertions;
using lazydotnet.Core.Configuration;
using lazydotnet.Services;

namespace lazydotnet.UnitTests;

public class ExecutionServiceTests
{
    [Fact]
    public void GetOrCreateState_ShouldReturnNewState()
    {
        // Act
        var state = ExecutionService.Instance.GetOrCreateState("path/to/proj", "Proj");

        // Assert
        state.Should().NotBeNull();
        state.ProjectName.Should().Be("Proj");
        state.Status.Should().Be(ExecutionStatus.Idle);
    }

    [Fact]
    public void IsRunning_ShouldReturnFalseForIdle()
    {
        // Act
        var running = ExecutionService.Instance.IsRunning("non-existent");

        // Assert
        running.Should().BeFalse();
    }

    [Fact]
    public void ProjectExecutionState_AddLog_ShouldStripAnsiAndLimitCount()
    {
        // Arrange
        var state = new ProjectExecutionState();
        var ansiMessage = "\x1B[31mHello\x1B[0m";

        // Act
        state.AddLog(ansiMessage);

        // Assert
        state.Logs.Should().Contain("Hello");
        state.Logs[0].Should().NotContain("\x1B[");
    }

    [Fact]
    public async Task StopAllAsync_ShouldNotThrow()
    {
        // Act
        var act = () => ExecutionService.Instance.StopAllAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartProjectAsync_WithoutRunArguments_ShouldLogPlainDotnetRunCommand()
    {
        // Arrange
        const string projectPath = "non-existent-no-args.csproj";
        var loggedMessages = new List<string>();
        AppCli.OnLog += s => loggedMessages.Add(s);

        // Act
        await ExecutionService.Instance.StartProjectAsync(projectPath, "Proj");
        var state = ExecutionService.Instance.GetOrCreateState(projectPath, "Proj");
        await state.ExecutionTask!;

        // Assert
        loggedMessages.Should().Contain(m => m.Contains("Running: dotnet"));
        loggedMessages.Should().Contain(m => m.Contains("run --project") && m.Contains(projectPath));
        loggedMessages.Should().NotContain(m => m.Contains("--no-build"));
    }

    [Fact]
    public async Task StartProjectAsync_WithRunArguments_ShouldAppendThemToRunCommand()
    {
        // Arrange
        const string projectPath = "non-existent-with-args.csproj";
        var loggedMessages = new List<string>();
        AppCli.OnLog += s => loggedMessages.Add(s);

        // Act
        await ExecutionService.Instance.StartProjectAsync(projectPath, "Proj", "--configuration Debug");
        var state = ExecutionService.Instance.GetOrCreateState(projectPath, "Proj");
        await state.ExecutionTask!;

        // Assert
        loggedMessages.Should().Contain(m =>
            m.Contains("Running: dotnet") &&
            m.Contains("run --project") &&
            m.Contains(projectPath) &&
            m.Contains("--configuration Debug"));
    }

    [Fact]
    public void CommandsSettings_Run_ShouldDefaultToEmptyArguments()
    {
        // Arrange
        var commands = new CommandsSettings();

        // Assert
        commands.Run.Arguments.Should().BeEmpty();
        commands.Build.Arguments.Should().Be("--verbosity minimal");
    }
}
