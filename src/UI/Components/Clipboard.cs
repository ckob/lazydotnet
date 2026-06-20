using TextCopy;

namespace lazydotnet.UI.Components;

/// <summary>
/// Clipboard access that never blocks the UI thread. The underlying provider
/// (e.g. pbcopy on macOS) shells out to an external process; if it hangs, a
/// synchronous call from a render/input thread — especially one holding a UI
/// lock — would freeze the whole TUI. So copying runs on the thread pool with a
/// timeout, and the result is reported via a (thread-safe) notification.
/// </summary>
public static class Clipboard
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public static void Copy(string text, string successMessage)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(Timeout);
                await ClipboardService.SetTextAsync(text, cts.Token);
                Notification.Show(successMessage);
            }
            catch (OperationCanceledException)
            {
                Notification.Show("Clipboard timed out", NotificationType.Error);
            }
            catch (Exception ex)
            {
                Notification.Show($"Clipboard failed: {ex.Message}", NotificationType.Error);
            }
        });
    }
}
