using lazydotnet.Services;
using lazydotnet.UI.Components;

namespace lazydotnet.UiTests.Components;

public class PickerModalSnapshotTests
{
    [Fact]
    public Task ProjectPickerModal_ListsProjects()
    {
        var modal = new ProjectPickerModal(
            "Run project",
            [
                TestData.Project("SampleApp", "/repo/src/SampleApp/SampleApp.csproj", runnable: true),
                TestData.Project("SampleLibrary", "/repo/src/SampleLibrary/SampleLibrary.csproj")
            ],
            rootPath: "/repo",
            onSelected: _ => Task.CompletedTask,
            onClose: () => { });

        return Verify(TuiSnapshot.Render(modal.GetRenderable(TuiSnapshot.DefaultWidth, TuiSnapshot.DefaultHeight)));
    }

    [Fact]
    public Task SelectionModal_RendersPromptAndOptions()
    {
        var modal = new SelectionModal<string>(
            "Pick a framework",
            "Choose the target test framework:",
            [("xUnit", "xunit"), ("NUnit", "nunit"), ("MSTest", "mstest")],
            onSelected: _ => Task.CompletedTask,
            onClose: () => { });

        return Verify(TuiSnapshot.Render(modal.GetRenderable(TuiSnapshot.DefaultWidth, TuiSnapshot.DefaultHeight)));
    }
}
