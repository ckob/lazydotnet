using FluentAssertions;
using lazydotnet.Screens;
using lazydotnet.Services;
using lazydotnet.UI;
using NSubstitute;

namespace lazydotnet.UnitTests;

public class DashboardScreenTests
{
    private static DashboardScreen CreateScreen()
    {
        var editorService = Substitute.For<IEditorService>();
        var solutionService = new SolutionService();
        var explorer = new SolutionExplorer(editorService);
        var options = Microsoft.Extensions.Options.Options.Create(new Core.Configuration.LazydotnetSettings());
        var detailsPane = new ProjectDetailsPane(solutionService, editorService, options);
        var layout = new AppLayout();
        return new DashboardScreen(explorer, detailsPane, layout, solutionService, ".", null);
    }

    [Theory]
    [InlineData('?', ConsoleKey.Oem2, false, false, false, true)]  // Windows: Shift+/
    [InlineData('?', ConsoleKey.Oem2, true, false, false, true)]   // Linux/Mac: no Shift in modifiers
    [InlineData('/', ConsoleKey.Oem2, false, false, false, false)] // Plain / without Shift
    [InlineData('\0', ConsoleKey.Oem2, false, false, true, false)] // Ctrl+Shift+/ → KeyChar is '\0', not '?'
    [InlineData('\0', ConsoleKey.Oem2, false, true, false, false)] // Alt+Shift+/ → KeyChar is '\0', not '?'
    public void HelpKeyBinding_Match_ShouldOnlyTriggerOnQuestionMark(
        char keyChar,
        ConsoleKey key,
        bool shift,
        bool alt,
        bool control,
        bool expected)
    {
        var screen = CreateScreen();
        var binding = screen.GetKeyBindings().First(b => b.Label == "?");
        var keyInfo = new ConsoleKeyInfo(keyChar, key, shift, alt, control);

        binding.Match(keyInfo).Should().Be(expected);
    }
}
