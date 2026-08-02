using lazydotnet.Services;
using lazydotnet.UI;
using NSubstitute;

namespace lazydotnet.UiTests.Tabs;

/// <summary>
/// Snapshots the "no project selected" default state of each project tab. Populated states are
/// driven by service I/O (MSBuild / NuGet / the test platform) and a time-based spinner, so they are
/// not deterministic here — that coverage belongs in the IntegrationTests project against real
/// fixtures. These guard the placeholder layout users see before selecting a project.
/// </summary>
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
