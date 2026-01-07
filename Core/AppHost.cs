using Spectre.Console;
using lazydotnet.UI;

namespace lazydotnet.Core;

public class AppHost(AppLayout layout, IScreen initialScreen)
{
    private readonly AppLayout _layout = layout;
    private IScreen? _currentScreen = initialScreen;
    private readonly Lock _uiLock = new();
    private bool _isRunning = true;
    private int _lastWidth;
    private int _lastHeight;

    public async Task RunAsync()
    {
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            _isRunning = false;
        };

        AnsiConsole.AlternateScreen(() =>
        {
            AnsiConsole.Live(_layout.GetRoot())
                .StartAsync(async ctx =>
                {
                    _currentScreen?.OnEnter();

                    _lastWidth = Console.WindowWidth;
                    _lastHeight = Console.WindowHeight;

                    _layout.OnLog += () =>
                    {
                        lock (_uiLock)
                        {
                            int h = _layout.GetBottomHeight(Console.WindowHeight);
                            _layout.UpdateBottom(Console.WindowWidth, h);
                            if (_currentScreen != null)
                                _layout.UpdateFooter(_currentScreen.GetKeyBindings());
                            ctx.Refresh();
                        }
                    };

                    while (_isRunning && _currentScreen != null)
                    {
                        try
                        {
                            bool needsRefresh = false;

                            int width = Console.WindowWidth;
                            int height = Console.WindowHeight;

                            if (width != _lastWidth || height != _lastHeight)
                            {
                                _lastWidth = width;
                                _lastHeight = height;
                                needsRefresh = true;
                            }

                            if (_currentScreen.OnTick())
                            {
                                needsRefresh = true;
                            }

                            while (Console.KeyAvailable)
                            {
                                var key = Console.ReadKey(true);
                                var nextScreen = await _currentScreen.HandleInputAsync(key, _layout);

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
                                    _currentScreen.Render(_layout, width, height);
                                    int bottomH = _layout.GetBottomHeight(height);
                                    _layout.UpdateBottom(width, bottomH);
                                    _layout.UpdateFooter(_currentScreen.GetKeyBindings());
                                    ctx.Refresh();
                                }
                            }

                            await Task.Delay(33);
                        }
                        catch (Exception ex)
                        {
                            _layout.AddLog($"[red]CRITICAL ERROR: {ex.Message}[/]");
                            lock (_uiLock)
                            {
                                int bottomH = _layout.GetBottomHeight(Console.WindowHeight);
                                _layout.UpdateBottom(Console.WindowWidth, bottomH);
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
