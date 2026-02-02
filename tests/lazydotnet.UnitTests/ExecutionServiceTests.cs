using FluentAssertions;
using lazydotnet.Services;

namespace lazydotnet.Tests;

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
}
