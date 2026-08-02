using lazydotnet.UI.Components;

namespace lazydotnet.UiTests.Components;

public class NotificationSnapshotTests
{
    [Fact]
    public Task InfoNotification_RendersBlueRoundedPanel()
    {
        Notification.Show("Build succeeded", NotificationType.Info);
        try
        {
            var renderable = Notification.GetRenderable();
            Assert.NotNull(renderable);
            return Verify(TuiSnapshot.RenderWithColor(renderable));
        }
        finally
        {
            Notification.Clear();
        }
    }

    [Fact]
    public Task ErrorNotification_RendersRedRoundedPanel()
    {
        Notification.Show("Build failed", NotificationType.Error);
        try
        {
            var renderable = Notification.GetRenderable();
            Assert.NotNull(renderable);
            return Verify(TuiSnapshot.RenderWithColor(renderable));
        }
        finally
        {
            Notification.Clear();
        }
    }
}
