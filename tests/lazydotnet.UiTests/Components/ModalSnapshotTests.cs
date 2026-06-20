using lazydotnet.UI.Components;

namespace lazydotnet.UiTests.Components;

/// <summary>
/// Snapshot regression for the modal renderables. These assert the exact rendered layout (borders,
/// header, padding, key hints) so a stray styling/layout change shows up as a reviewable diff.
/// </summary>
public class ModalSnapshotTests
{
    [Fact]
    public Task ConfirmationModal_RendersTitleMessageAndKeyHints()
    {
        var modal = new ConfirmationModal(
            "Delete project",
            "Remove [bold]SimpleLibrary[/] from the solution?",
            onConfirm: () => Task.CompletedTask,
            onClose: () => { });

        var output = TuiSnapshot.Render(modal.GetRenderable(TuiSnapshot.DefaultWidth, TuiSnapshot.DefaultHeight));

        return Verify(output);
    }

    [Fact]
    public Task Modal_BaseRender_WrapsContentInBorderedPanel()
    {
        var modal = new Modal("Info", new Spectre.Console.Markup("Hello world"), onClose: () => { });

        var output = TuiSnapshot.Render(modal.GetRenderable(TuiSnapshot.DefaultWidth, TuiSnapshot.DefaultHeight));

        return Verify(output);
    }
}
