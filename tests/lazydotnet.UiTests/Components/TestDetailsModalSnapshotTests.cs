using lazydotnet.UI.Components;

namespace lazydotnet.UiTests.Components;

public class TestDetailsModalSnapshotTests
{
    [Fact]
    public Task FailedTest_RendersErrorStackAndOutput()
    {
        var modal = new TestDetailsModal(TestData.FailedTest(), onClose: () => { });

        return Verify(TuiSnapshot.Render(modal.GetRenderable(TuiSnapshot.DefaultWidth, TuiSnapshot.DefaultHeight)));
    }
}
