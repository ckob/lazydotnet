using lazydotnet.Core;
using Spectre.Console;
using Spectre.Console.Rendering;
using lazydotnet.Services;
using lazydotnet.UI.Components;
using CliWrap;

namespace lazydotnet.UI;

public enum AppMode
{
    Normal,
    Busy // Showing spinner/progress
}

public class NuGetDetailsTab(NuGetService nuGetService) : IProjectTab
{
    private readonly ScrollableList<NuGetPackageInfo> _nugetList = new();

    private readonly Lock _lock = new();

    private AppMode _appMode = AppMode.Normal;
    private string? _statusMessage;
    private bool _isActionRunning;
    private bool _isLoading;
    private bool _isFetchingLatest;
    private int _lastFrameIndex = -1;
    private string? _currentProjectPath;
    private string? _currentProjectName;
    private CancellationTokenSource? _loadCts;

    public Action<string>? LogAction { get; set; }
    public Action? RequestRefresh { get; set; }
    public Action<Modal>? RequestModal { get; set; }
    public Action<string>? RequestSelectProject { get; set; }

    public string Title => "NuGets";

    public void MoveUp()
    {
        lock (_lock)
        {
            _nugetList.MoveUp();
        }
    }

    public void MoveDown()
    {
        lock (_lock)
        {
            _nugetList.MoveDown();
        }
    }

    public string? GetScrollIndicator()
    {
        lock (_lock)
        {
            if (_currentProjectPath == null || _isLoading) return null;
            return _nugetList.Count == 0 ? null : $"{_nugetList.SelectedIndex + 1} of {_nugetList.Count}";
        }
    }

    public void ClearData()
    {
        lock (_lock)
        {
            _nugetList.Clear();
            _currentProjectPath = null;
            _currentProjectName = null;
            _isLoading = false;
            _appMode = AppMode.Normal;
            _statusMessage = null;
            _isFetchingLatest = false;
        }
    }

    public bool OnTick()
    {
        if (!_isFetchingLatest && !_isLoading && !_isActionRunning) return false;
        var currentFrame = SpinnerHelper.GetCurrentFrameIndex();
        if (currentFrame == _lastFrameIndex) return false;
        _lastFrameIndex = currentFrame;
        return true;
    }

    public async Task LoadAsync(string projectPath, string projectName, bool force = false)
    {
        if (!force && _currentProjectPath == projectPath && !_isLoading) return;

        if (_loadCts != null)
        {
            await _loadCts.CancelAsync();
        }

        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        _currentProjectPath = projectPath;
        _currentProjectName = projectName;
        _isLoading = true;
        _isFetchingLatest = false;
        _statusMessage = null; // Clear previous status
        _nugetList.Clear();

        try
        {
            // Step 1: Load installed packages (local, fast)
            var packages = await nuGetService.GetPackagesAsync(projectPath, LogAction, ct);
            if (ct.IsCancellationRequested || _currentProjectPath != projectPath)
                return;

            lock (_lock)
            {
                _nugetList.SetItems(packages);
            }

            RequestRefresh?.Invoke();

            // Step 2: Fetch latest versions in background
            _isFetchingLatest = true;
            _ = Task.Run(async () =>
            {
                try
                {
                    var latestVersions = await nuGetService.GetLatestVersionsAsync(projectPath, LogAction, ct);
                    if (ct.IsCancellationRequested || _currentProjectPath != projectPath)
                        return;

                    lock (_lock)
                    {
                        var currentList = _nugetList.Items.ToList();
                        var updatedList = currentList.Select(p =>
                                latestVersions.TryGetValue(p.Id, out var latest)
                                    ? p with { LatestVersion = latest }
                                    : p with
                                    {
                                        LatestVersion = p.ResolvedVersion
                                    } // If not in outdated, it's up to date
                        ).ToList();

                        _nugetList.SetItems(updatedList);
                        _isFetchingLatest = false;
                    }

                    RequestRefresh?.Invoke();
                }
                catch (OperationCanceledException)
                {
                    // Ignore
                }
                catch (Exception ex)
                {
                    _statusMessage = $"Error fetching latest versions: {ex.Message}";
                    _isFetchingLatest = false;
                    RequestRefresh?.Invoke();
                }
            }, ct);
        }
        catch (OperationCanceledException)
        {
            // Ignore
        }
        catch (Exception ex)
        {
            _statusMessage = $"Error loading packages: {ex.Message}";
        }
        finally
        {
            if (_currentProjectPath == projectPath)
                _isLoading = false;
        }
    }

    private async Task ReloadDataAsync()
    {
        if (_currentProjectPath != null && _currentProjectName != null)
        {
            await LoadAsync(_currentProjectPath, _currentProjectName, force: true);
        }
    }

    public IEnumerable<KeyBinding> GetKeyBindings()
    {
        if (_isActionRunning || _isLoading) yield break;

        // Navigation (hidden)
        yield return new KeyBinding("k", "up", () => Task.Run(MoveUp),
            k => k.Key == ConsoleKey.UpArrow || k.Key == ConsoleKey.K ||
                 k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.P }, false);
        yield return new KeyBinding("j", "down", () => Task.Run(MoveDown),
            k => k.Key == ConsoleKey.DownArrow || k.Key == ConsoleKey.J ||
                 k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.N }, false);

        if (_appMode != AppMode.Normal)
            yield break;

        var isSolution = _currentProjectPath?.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) == true;

        if (!isSolution)
        {
            yield return new KeyBinding("a", "add", () =>
            {
                var modal = new NuGetSearchModal(
                    nuGetService,
                    async selected => { await InstallPackageAsync(selected.Id, null); },
                    () => RequestModal?.Invoke(null!),
                    LogAction,
                    () => RequestRefresh?.Invoke()
                );
                RequestModal?.Invoke(modal);
                return Task.CompletedTask;
            }, k => k.KeyChar == 'a');
        }

        if (_nugetList.SelectedItem != null)
        {
            if (_nugetList.SelectedItem.IsOutdated)
            {
                yield return new KeyBinding("u", "update", () =>
                        UpdatePackageAsync(_nugetList.SelectedItem.Id, _nugetList.SelectedItem.LatestVersion!),
                    k => k.KeyChar == 'u');
            }

            if (!isSolution)
            {
                yield return new KeyBinding("d", "delete", () =>
                {
                    var p = _nugetList.SelectedItem;
                    if (p == null) return Task.CompletedTask;

                    var confirm = new ConfirmationModal(
                        "Remove Package",
                        $"Are you sure you want to remove package [bold]{Markup.Escape(p.Id)}[/]?",
                        async () => { await RemovePackageAsync(p.Id); },
                        () => RequestModal?.Invoke(null!)
                    );

                    RequestModal?.Invoke(confirm);
                    return Task.CompletedTask;
                }, k => k.KeyChar == 'd');
            }

            yield return new KeyBinding("enter", "versions", () =>
            {
                var p = _nugetList.SelectedItem;
                if (p == null) return Task.CompletedTask;

                var modal = new NuGetVersionSelectionModal(
                    nuGetService,
                    p.Id,
                    p.ResolvedVersion,
                    p.LatestVersion,
                    async v => await UpdatePackageAsync(p.Id, v),
                    () => RequestModal?.Invoke(null!),
                    LogAction,
                    () => RequestRefresh?.Invoke()
                );
                RequestModal?.Invoke(modal);
                return Task.CompletedTask;
            }, k => k.Key == ConsoleKey.Enter);
        }

        if (_nugetList.Items.Any(p => p.IsOutdated))
        {
            yield return new KeyBinding("U", "update all", UpdateAllOutdatedAsync, k => k.KeyChar == 'U');
        }
    }

    public async Task<bool> HandleKeyAsync(ConsoleKeyInfo key)
    {
        var binding = GetKeyBindings().FirstOrDefault(b => b.Match(key));
        if (binding == null)
            return false;

        await binding.Action();
        return true;
    }


    private async Task InstallPackageAsync(string packageId, string? version)
    {
        if (_currentProjectPath == null) return;

        _isActionRunning = true;
        _statusMessage = $"Installing {packageId} {version ?? "latest"}...";
        RequestRefresh?.Invoke();
        try
        {
            await NuGetService.InstallPackageAsync(_currentProjectPath, packageId, version, false, LogAction);
        }
        catch (Exception ex)
        {
            _statusMessage = $"Install failed: {ex.Message}";
            await Task.Delay(3000);
        }
        finally
        {
            _isActionRunning = false;
            _statusMessage = null;
            await ReloadDataAsync();
            RequestRefresh?.Invoke();
        }
    }

    private async Task UpdatePackageAsync(string packageId, string version)
    {
        if (_currentProjectPath == null) return;

        _isActionRunning = true;
        _statusMessage = $"Updating {packageId} to {version}...";
        RequestRefresh?.Invoke();
        try
        {
            await NuGetService.UpdatePackageAsync(_currentProjectPath, packageId, version, false, LogAction);
        }
        catch (Exception ex)
        {
            _statusMessage = $"Update failed: {ex.Message}";
            await Task.Delay(3000);
        }
        finally
        {
            _isActionRunning = false;
            _statusMessage = null;
            await ReloadDataAsync();
            RequestRefresh?.Invoke();
        }
    }

    private async Task RemovePackageAsync(string packageId)
    {
        if (_currentProjectPath == null) return;

        _isActionRunning = true;
        _statusMessage = $"Removing {packageId}...";
        RequestRefresh?.Invoke();
        try
        {
            await NuGetService.RemovePackageAsync(_currentProjectPath, packageId, LogAction);
        }
        catch (Exception ex)
        {
            _statusMessage = $"Remove failed: {ex.Message}";
            await Task.Delay(3000);
        }
        finally
        {
            _isActionRunning = false;
            _statusMessage = null;
            await ReloadDataAsync();
            RequestRefresh?.Invoke();
        }
    }

    private Task UpdateAllOutdatedAsync()
    {
        if (_currentProjectPath == null) return Task.CompletedTask;

        var options = new List<(string, VersionLock)>
        {
            ("Patch (Safe) - Bug fixes only", VersionLock.Minor),
            ("Minor (Non-breaking) - New features", VersionLock.Major),
            ("Major (Breaking) - Upgrade everything", VersionLock.None)
        };

        var modal = new SelectionModal<VersionLock>(
            "Update Strategy",
            "Choose how to update outdated packages:",
            options,
            async strategy =>
            {
                RequestModal?.Invoke(null!); // Close modal first
                await PerformUpdateAllAsync(strategy);
            },
            () => RequestModal?.Invoke(null!)
        );

        RequestModal?.Invoke(modal);
        return Task.CompletedTask;
    }

    private async Task PerformUpdateAllAsync(VersionLock strategy)
    {
        if (_currentProjectPath == null) return;

        _isActionRunning = true;
        _statusMessage = "Updating all packages...";
        RequestRefresh?.Invoke();
        try
        {
            await NuGetService.UpdateAllPackagesAsync(_currentProjectPath, strategy, noRestore: true,
                logger: LogAction);

            _statusMessage = "Finalizing restore...";
            RequestRefresh?.Invoke();

            var pipe = LogAction != null ? PipeTarget.ToDelegate(s => LogAction(Markup.Escape(s))) : PipeTarget.Null;

            var command = Cli.Wrap("dotnet")
                .WithArguments($"restore \"{_currentProjectPath}\"")
                .WithStandardOutputPipe(pipe)
                .WithStandardErrorPipe(pipe);

            await AppCli.RunAsync(command);
        }
        catch (Exception ex)
        {
            _statusMessage = $"Update All failed: {ex.Message}";
            await Task.Delay(3000);
        }
        finally
        {
            _isActionRunning = false;
            _statusMessage = null;
            await ReloadDataAsync();
            RequestRefresh?.Invoke();
        }
    }

    public IRenderable GetContent(int height, int width, bool isActive)
    {
        lock (_lock)
        {
            var grid = new Grid();
            grid.AddColumn();

            if (_currentProjectPath == null)
            {
                grid.AddRow(new Markup("[dim]Select a project...[/]"));
                return grid;
            }

            if (_isLoading || _isActionRunning)
            {
                if (_nugetList.Count > 0)
                {
                    RenderNuGetTab(grid, height, width, isActive);
                }

                var msg = _statusMessage ?? "Loading...";
                grid.AddRow(new Markup($"[yellow bold]{SpinnerHelper.GetFrame()} {Markup.Escape(msg)}[/]"));
                return grid;
            }

            RenderNuGetTab(grid, height, width, isActive);

            if (_statusMessage != null)
            {
                grid.AddRow(new Markup($"[yellow bold]{Markup.Escape(_statusMessage)}[/]"));
            }

            return grid;
        }
    }

    private void RenderNuGetTab(Grid grid, int maxRows, int width, bool isActive)
    {
        if (_nugetList.Count == 0)
        {
            grid.AddRow(new Markup("[dim]No packages found.[/]"));
            return;
        }

        var visibleRows = Math.Max(1, maxRows - 4); // Reserve space for borders/headers/status
        var (start, end) = _nugetList.GetVisibleRange(visibleRows);

        var currentWidth = Math.Max(7, _nugetList.Items.Max(p => p.ResolvedVersion.Length) + 2);
        var latestWidth = Math.Max(6, _nugetList.Items.Max(p => (p.LatestVersion ?? p.ResolvedVersion).Length) + 2);
        var packageWidth = Math.Max(10, width - currentWidth - latestWidth - 12);

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Expand()
            .AddColumn(new TableColumn("Package").Width(packageWidth))
            .AddColumn(new TableColumn("Current").Width(currentWidth).RightAligned())
            .AddColumn(new TableColumn("Latest").Width(latestWidth).RightAligned());

        for (var i = start; i < end; i++)
        {
            var pkg = _nugetList.Items[i];
            var isSelected = i == _nugetList.SelectedIndex;

            string latestText;
            if (pkg.LatestVersion == null && _isFetchingLatest)
            {
                latestText = $"[yellow]{SpinnerHelper.GetFrame()}[/]";
            }
            else if (!pkg.IsOutdated)
            {
                latestText = $"[dim]{Markup.Escape(pkg.ResolvedVersion)}[/]";
            }
            else
            {
                latestText = FormatColoredVersion(pkg.ResolvedVersion, pkg.LatestVersion!, pkg.GetUpdateType());
            }

            if (isSelected)
            {
                if (isActive)
                {
                    table.AddRow(
                        new Markup($"[black on blue]{Markup.Escape(pkg.Id)}[/]"),
                        new Markup($"[black on blue]{Markup.Escape(pkg.ResolvedVersion)}[/]"),
                        new Markup($"[black on blue]{Markup.Remove(latestText)}[/]")
                    );
                }
                else
                {
                    table.AddRow(
                        new Markup($"[bold white]{Markup.Escape(pkg.Id)}[/]"),
                        new Markup($"[bold white]{Markup.Escape(pkg.ResolvedVersion)}[/]"),
                        new Markup($"[bold white]{Markup.Remove(latestText)}[/]")
                    );
                }
            }
            else
            {
                table.AddRow(
                    new Markup(Markup.Escape(pkg.Id)),
                    new Markup(Markup.Escape(pkg.ResolvedVersion)),
                    new Markup(latestText)
                );
            }
        }

        grid.AddRow(table);

        var indicator = _nugetList.GetScrollIndicator(visibleRows);
        if (indicator != null)
        {
            grid.AddRow(new Markup($"[dim]{indicator}[/]"));
        }
    }

    private static string FormatColoredVersion(string current, string latest, VersionUpdateType updateType)
    {
        var color = updateType switch
        {
            VersionUpdateType.Major => "red",
            VersionUpdateType.Minor => "yellow",
            VersionUpdateType.Patch => "green",
            _ => "white"
        };

        var currentParts = current.Split('.');
        var latestParts = latest.Split('.');

        var result = new System.Text.StringBuilder();
        var startColoring = false;

        var isFirstColored = true;
        for (var i = 0; i < latestParts.Length; i++)
        {
            var latestPart = latestParts[i];
            var currentPart = i < currentParts.Length ? currentParts[i] : null;

            if (!startColoring && currentPart != latestPart)
            {
                startColoring = true;
            }

            if (startColoring)
            {
                if (i > 0)
                {
                    if (isFirstColored)
                        result.Append('.');
                    else
                        result.Append($"[{color}].[/]");
                }

                result.Append($"[{color}]{Markup.Escape(latestPart)}[/]");
                isFirstColored = false;
            }
            else
            {
                if (i > 0) result.Append('.');
                result.Append(Markup.Escape(latestPart));
            }
        }

        return result.ToString();
    }
}