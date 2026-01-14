using Spectre.Console;
using lazydotnet.UI;

namespace lazydotnet.Core;

public class AppHost(AppLayout layout, IScreen initialScreen)
{
    private IScreen? _currentScreen = initialScreen;
    private readonly Lock _uiLock = new();
    private bool _isRunning = true;

    public async Task RunAsync()
    {
        Console.CancelKeyPress += (sender, e) =>
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

                    var lastWidth = Console.WindowWidth;
                    var lastHeight = Console.WindowHeight;

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
                        try
                        {
                            var needsRefresh = false;

                            var width = Console.WindowWidth;
                            var height = Console.WindowHeight;

                            if (width != lastWidth || height != lastHeight)
                            {
                                lastWidth = width;
                                lastHeight = height;
                                needsRefresh = true;
                            }

                            if (_currentScreen.OnTick())
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
                            layout.AddLog($"[red]CRITICAL ERROR: {ex.Message}[/]");
                            lock (_uiLock)
                            {
                                var bottomH = AppLayout.GetBottomHeight(Console.WindowHeight);
                                layout.UpdateBottom(Console.WindowWidth, bottomH);
                                ctx.Refresh();
                            }
                            await Task.Delay(1000);
                        }
                    }
                }).GetAwaiter().GetResult();
        });

        await Task.CompletedTask;
    }
}
