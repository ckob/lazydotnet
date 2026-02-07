using FluentAssertions;
using lazydotnet.Services;

namespace lazydotnet.UnitTests;

public class EditorServiceTests
{
    [Fact]
    public void GetEditorLaunchCommand_ShouldReturnCorrectArgs()
    {
        // Arrange
        var service = new EditorService();
        var filePath = "test.cs";
        var line = 10;

        // Act
        var (command, args) = service.GetEditorLaunchCommand(filePath, line);

        // Assert
        command.Should().NotBeNullOrEmpty();
        args.Should().Contain(a => a.Contains(filePath));
        if (args.Contains("--goto"))
        {
            args.Should().Contain($"{filePath}:{line}");
        }
    }
}
