using Spectre.Console;
using lazydotnet.UI;
using lazydotnet.Services;

namespace lazydotnet.Core;

public class AppHost(AppLayout layout, IScreen initialScreen)
{
    private IScreen? _currentScreen = initialScreen;
    private readonly Lock _uiLock = new();
    private bool _isRunning = true;

    public async Task RunAsync()
    {
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            _isRunning = false;
        };

        AnsiConsole.AlternateScreen(() =>
        {
            AnsiConsole.Live(layout.GetRoot())
                .StartAsync(async ctx =>
                {
                    _currentScreen?.OnEnter();

                    _lastWidth = Console.WindowWidth;
                    _lastHeight = Console.WindowHeight;

                    layout.OnLog += () =>
                    {
                        lock (_uiLock)
                        {
                            var h = AppLayout.GetBottomHeight(Console.WindowHeight);
                            layout.UpdateBottom(Console.WindowWidth, h);
                            if (_currentScreen != null)
                                layout.UpdateFooter(_currentScreen.GetKeyBindings());
                            ctx.Refresh();
                        }
                    };

                    while (_isRunning && _currentScreen != null)
                    {
                        await ProcessTickAsync(ctx);
                    }

                    await ExecutionService.Instance.StopAllAsync();
                }).GetAwaiter().GetResult();
        });

        await Task.CompletedTask;
    }

    private async Task ProcessTickAsync(LiveDisplayContext ctx)
    {
        try
        {
            var needsRefresh = false;

            var width = Console.WindowWidth;
            var height = Console.WindowHeight;

            if (width != _lastWidth || height != _lastHeight)
            {
                _lastWidth = width;
                _lastHeight = height;
                needsRefresh = true;
            }

            if (_currentScreen!.OnTick())
            {
                needsRefresh = true;
            }

            while (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                var nextScreen = await _currentScreen.HandleInputAsync(key, layout);

                if (nextScreen == null)
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
        catch (Exception ex)
        {
            layout.AddLog($"[red]CRITICAL ERROR: {Markup.Escape(ex.Message)}[/]");
            lock (_uiLock)
            {
                var bottomH = AppLayout.GetBottomHeight(Console.WindowHeight);
                layout.UpdateBottom(Console.WindowWidth, bottomH);
                ctx.Refresh();
            }
            await Task.Delay(1000);
        }
    }

    private int _lastWidth;
    private int _lastHeight;
}
