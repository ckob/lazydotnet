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
                        try
                        {
                            lock (_uiLock)
                            {
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

    private const int MinWidth = 20;
    private const int MinHeight = 5;

    private async Task ProcessTickAsync(LiveDisplayContext ctx)
    {
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
        catch
        {
            // Silently ignore rendering errors (e.g., terminal too small)
        }
    }

    private int _lastWidth;
    private int _lastHeight;
}
