using lazydotnet.Services;
using lazydotnet.UI;
using NSubstitute;

namespace lazydotnet.UiTests.Panes;

public class SolutionExplorerSnapshotTests
{
    [Fact]
    public Task SolutionExplorer_RendersProjectTree()
    {
        var explorer = new SolutionExplorer(Substitute.For<IEditorService>());
        explorer.SetSolution(TestData.Solution());

        return Verify(TuiSnapshot.Render(explorer.GetContent(
            availableHeight: TuiSnapshot.DefaultHeight,
            availableWidth: 40,
            isActive: true)));
    }
}
