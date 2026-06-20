using Spectre.Console;
using lazydotnet.UI;
using lazydotnet.UI.Components;
using lazydotnet.Services;

namespace lazydotnet.Core;

public class AppHost(AppLayout layout, IScreen initialScreen)
{
    private IScreen? _currentScreen = initialScreen;
    private readonly Lock _uiLock = new();
    private bool _isRunning = true;
    private string? _pendingLog;
    private bool _hadNotification;
    private long _lastNotificationVersion;
    private volatile bool _suspended;

    public async Task RunAsync()
    {
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            _isRunning = false;
        };

        AnsiConsole.AlternateScreen(() =>
        {
            AnsiConsole.Live(layout.GetRootWithNotification())
                .StartAsync(async ctx =>
                {
                    _currentScreen?.OnEnter();

                    _lastWidth = Console.WindowWidth;
                    _lastHeight = Console.WindowHeight;

                    TuiSuspender.SetHandler(action => SuspendUiAsync(ctx, action));
                    layout.OnLog += () => OnLogPending(ctx);

                    while (_isRunning && _currentScreen != null)
                    {
                        await ProcessTickAsync(ctx);
                    }

                    await ExecutionService.Instance.StopAllAsync();
                    TuiSuspender.SetHandler(null);
                }).GetAwaiter().GetResult();
        });

        await Task.CompletedTask;
    }

    private const int MinWidth = 20;
    private const int MinHeight = 5;

    private void HandlePendingLog(LiveDisplayContext ctx)
    {
        var pendingLog = Interlocked.Exchange(ref _pendingLog, null);
        if (pendingLog is null) return;

        lock (_uiLock)
        {
            layout.AddLog(pendingLog);
            var h = AppLayout.GetBottomHeight(Console.WindowHeight);
            layout.UpdateBottom(Console.WindowWidth, h);
            if (_currentScreen is not null)
                layout.UpdateFooter(_currentScreen.GetKeyBindings());
            ctx.Refresh();
        }
    }

    private bool HandleNotificationChange()
    {
        var hasNotification = Notification.HasActiveNotification;
        var version = Notification.Version;
        // Redraw when a notification appears or is replaced (version bump) and when
        // it expires (active flag flips). Covers background-thread Show() calls that
        // would otherwise not surface until the next unrelated refresh.
        var changed = hasNotification != _hadNotification || version != _lastNotificationVersion;
        _hadNotification = hasNotification;
        _lastNotificationVersion = version;
        return changed;
    }

    private async Task ProcessTickAsync(LiveDisplayContext ctx)
    {
        if (_suspended)
        {
            await Task.Delay(50);
            return;
        }

        HandlePendingLog(ctx);

        var width = Console.WindowWidth;
        var height = Console.WindowHeight;

        if (width < MinWidth || height < MinHeight)
        {
            await Task.Delay(100);
            return;
        }

        try
        {
            var needsRefresh = false;

            if (width != _lastWidth || height != _lastHeight)
            {
                _lastWidth = width;
                _lastHeight = height;
                needsRefresh = true;
            }

            if (_currentScreen!.OnTick())
                needsRefresh = true;

            if (HandleNotificationChange())
                needsRefresh = true;

            while (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                var nextScreen = await _currentScreen.HandleInputAsync(key, layout);

                if (nextScreen is null)
                {
                    _isRunning = false;
                    break;
                }

                if (nextScreen != _currentScreen)
                {
                    _currentScreen = nextScreen;
                    _currentScreen.OnEnter();
                }

                needsRefresh = true;
            }

            if (needsRefresh)
            {
                lock (_uiLock)
                {
                    _currentScreen.Render(layout, width, height);
                    var bottomH = AppLayout.GetBottomHeight(height);
                    layout.UpdateBottom(width, bottomH);
                    layout.UpdateFooter(_currentScreen.GetKeyBindings());
                    ctx.Refresh();
                }
            }

            await Task.Delay(33);
        }
        catch
        {
            // Silently ignore rendering errors (e.g., terminal too small)
        }
    }

    private int _lastWidth;
    private int _lastHeight;

    public void Log(string message)
    {
        Interlocked.Exchange(ref _pendingLog, message);
    }

    private void OnLogPending(LiveDisplayContext ctx)
    {
        try
        {
            lock (_uiLock)
            {
                if (_suspended) return;
                var h = AppLayout.GetBottomHeight(Console.WindowHeight);
                layout.UpdateBottom(Console.WindowWidth, h);
                if (_currentScreen != null)
                    layout.UpdateFooter(_currentScreen.GetKeyBindings());
                ctx.Refresh();
            }
        }
        catch
        {
            // Silently ignore rendering errors
        }
    }

#pragma warning disable S6966
    private async Task SuspendUiAsync(LiveDisplayContext ctx, Func<Task> action)
    {
        _suspended = true;

        try
        {
            lock (_uiLock)
            {
                Console.Out.Write("\x1b[?1049l\x1b[?25h");
                Console.Out.Flush();
            }

            await action();
        }
        finally
        {
            lock (_uiLock)
            {
                Console.Out.Write("\x1b[?1049h\x1b[?25l");
                Console.Out.Flush();
                _suspended = false;
                ResumeRender(ctx);
            }
        }
    }
#pragma warning restore S6966

    private void ResumeRender(LiveDisplayContext ctx)
    {
        try
        {
            var width = Console.WindowWidth;
            var height = Console.WindowHeight;
            _lastWidth = width;
            _lastHeight = height;
            _currentScreen?.Render(layout, width, height);
            var bottomH = AppLayout.GetBottomHeight(height);
            layout.UpdateBottom(width, bottomH);
            if (_currentScreen != null)
                layout.UpdateFooter(_currentScreen.GetKeyBindings());
            ctx.Refresh();
        }
        catch
        {
            // Ignore rendering errors during resume
        }
    }
}
