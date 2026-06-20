using lazydotnet.UI.Components;

namespace lazydotnet.UiTests.Components;

public class LogViewerSnapshotTests
{
    [Fact]
    public Task LogViewer_RendersAppendedLines()
    {
        var viewer = new LogViewer();
        viewer.AddLog("Restoring packages...");
        viewer.AddLog("Build succeeded.");
        viewer.AddLog("    0 Warning(s)");
        viewer.AddLog("    0 Error(s)");

        return Verify(TuiSnapshot.Render(viewer.GetContent(TuiSnapshot.DefaultHeight, TuiSnapshot.DefaultWidth, isActive: true)));
    }
}
