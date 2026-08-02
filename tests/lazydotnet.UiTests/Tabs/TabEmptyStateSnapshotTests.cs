using lazydotnet.Services;
using lazydotnet.UI;
using NSubstitute;

namespace lazydotnet.UiTests.Tabs;

public class TabEmptyStateSnapshotTests
{
    [Fact]
    public Task ExecutionTab_NoProject_ShowsHint()
    {
        var tab = new ExecutionTab();
        return Verify(TuiSnapshot.Render(tab.GetContent(TuiSnapshot.DefaultHeight, TuiSnapshot.DefaultWidth, isActive: true)));
    }

    [Fact]
    public Task NuGetDetailsTab_NoProject_ShowsHint()
    {
        var tab = new NuGetDetailsTab();
        return Verify(TuiSnapshot.Render(tab.GetContent(TuiSnapshot.DefaultHeight, TuiSnapshot.DefaultWidth, isActive: true)));
    }

    [Fact]
    public Task TestDetailsTab_NoProject_ShowsHint()
    {
        var tab = new TestDetailsTab(Substitute.For<IEditorService>());
        return Verify(TuiSnapshot.Render(tab.GetContent(TuiSnapshot.DefaultHeight, TuiSnapshot.DefaultWidth, isActive: true)));
    }
}
