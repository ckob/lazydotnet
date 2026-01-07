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
    SelectingVersion,
    Busy // Showing spinner/progress
}

public class NuGetDetailsTab(NuGetService nuGetService) : IProjectTab
{
    // Lists
    private readonly ScrollableList<NuGetPackageInfo> _nugetList = new();
    private readonly ScrollableList<string> _versionList = new();

    private readonly Lock _lock = new(); // UI Synchronization

    // State
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
            if (_appMode == AppMode.SelectingVersion) _versionList.MoveUp();
            else _nugetList.MoveUp();
        }
    }

    public void MoveDown()
    {
        lock (_lock)
        {
            if (_appMode == AppMode.SelectingVersion) _versionList.MoveDown();
            else _nugetList.MoveDown();
        }
    }

    public string? GetScrollIndicator()
    {
        lock (_lock)
        {
            if (_currentProjectPath == null || _isLoading) return null;
            if (_nugetList.Count == 0) return null;
            return $"{_nugetList.SelectedIndex + 1} of {_nugetList.Count}";
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
        if (_isFetchingLatest || _isLoading || _isActionRunning)
        {
            int currentFrame = SpinnerHelper.GetCurrentFrameIndex();
            if (currentFrame != _lastFrameIndex)
            {
                _lastFrameIndex = currentFrame;
                return true;
            }
        }
        return false;
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
        _nugetList.Clear();

        if (projectPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            _isLoading = false;
            _statusMessage = "Select a project to see packages.";
            RequestRefresh?.Invoke();
            return;
        }

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
                                : p with { LatestVersion = p.ResolvedVersion } // If not in outdated, it's up to date
                        ).ToList();

                        _nugetList.SetItems(updatedList);
                        _isFetchingLatest = false;
                    }
                    RequestRefresh?.Invoke();
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _statusMessage = $"Error fetching latest versions: {ex.Message}";
                    _isFetchingLatest = false;
                    RequestRefresh?.Invoke();
                }
            }, ct);
        }
        catch (OperationCanceledException) { }
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
        yield return new KeyBinding("k", "up", () => Task.Run(MoveUp), k => k.Key == ConsoleKey.UpArrow || k.Key == ConsoleKey.K, false);
        yield return new KeyBinding("j", "down", () => Task.Run(MoveDown), k => k.Key == ConsoleKey.DownArrow || k.Key == ConsoleKey.J, false);

        if (_appMode == AppMode.Normal)
        {
            yield return new KeyBinding("a", "add", () =>
            {
                var modal = new NuGetSearchModal(
                    nuGetService,
                    async selected =>
                    {
                        await InstallPackageAsync(selected.Id, null);
                    },
                    () => RequestModal?.Invoke(null!),
                    LogAction,
                    () => RequestRefresh?.Invoke()
                );
                RequestModal?.Invoke(modal);
                return Task.CompletedTask;
            }, k => k.KeyChar == 'a');

            if (_nugetList.SelectedItem != null)
            {
                if (_nugetList.SelectedItem.IsOutdated)
                {
                    yield return new KeyBinding("u", "update", () =>
                        InstallPackageAsync(_nugetList.SelectedItem.Id, _nugetList.SelectedItem.LatestVersion),
                        k => k.KeyChar == 'u');
                }

                yield return new KeyBinding("d", "delete", () =>
                {
                    var p = _nugetList.SelectedItem;
                    if (p == null) return Task.CompletedTask;

                    var confirm = new ConfirmationModal(
                        "Remove Package",
                        $"Are you sure you want to remove package [bold]{Markup.Escape(p.Id)}[/]?",
                        async () =>
                        {
                            await RemovePackageAsync(p.Id);
                        },
                        () => RequestModal?.Invoke(null!)
                    );

                    RequestModal?.Invoke(confirm);
                    return Task.CompletedTask;
                }, k => k.KeyChar == 'd');

                yield return new KeyBinding("Enter", "versions", () =>
                    ShowVersionsForPackageAsync(_nugetList.SelectedItem.Id),
                    k => k.Key == ConsoleKey.Enter);
            }

            if (_nugetList.Items.Any(p => p.IsOutdated))
            {
                yield return new KeyBinding("U", "update all", UpdateAllOutdatedAsync, k => k.KeyChar == 'U');
            }
        }
        else if (_appMode == AppMode.SelectingVersion)
        {
            yield return new KeyBinding("Esc", "cancel", () =>
            {
                _appMode = AppMode.Normal;
                return Task.CompletedTask;
            }, k => k.Key == ConsoleKey.Escape);

            if (_versionList.SelectedItem != null && _nugetList.SelectedItem != null)
            {
                yield return new KeyBinding("Enter", "install version", () =>
                {
                    var v = _versionList.SelectedItem;
                    var p = _nugetList.SelectedItem;
                    _appMode = AppMode.Normal;
                    return InstallPackageAsync(p.Id, v);
                }, k => k.Key == ConsoleKey.Enter);
            }
        }
    }

    public async Task<bool> HandleKeyAsync(ConsoleKeyInfo key)
    {
        var binding = GetKeyBindings().FirstOrDefault(b => b.Match(key));
        if (binding != null)
        {
            await binding.Action();
            return true;
        }

        return false;
    }

    // Actions

    private async Task ShowVersionsForPackageAsync(string packageId)
    {
        _isActionRunning = true;
        _statusMessage = "Fetching versions...";
        RequestRefresh?.Invoke();
        _ = Task.Run(async () =>
        {
            try
            {
                var pkg = _nugetList.SelectedItem;
                if (pkg == null) return;
                var versions = await nuGetService.GetPackageVersionsAsync(pkg.Id, LogAction);

                _versionList.SetItems(versions);
                if (versions.Count > 0) _appMode = AppMode.SelectingVersion;
                else _statusMessage = "No versions found.";
            }
            catch (Exception ex)
            {
                _statusMessage = $"Failed to get versions: {ex.Message}";
                await Task.Delay(3000);
            }
            finally
            {
                _isActionRunning = false;
                RequestRefresh?.Invoke();
            }
        });
    }

    private async Task InstallPackageAsync(string packageId, string? version)
    {
        if (_currentProjectPath == null) return;

        _isActionRunning = true;
        _statusMessage = $"Installing {packageId} {version ?? "latest"}...";
        RequestRefresh?.Invoke();
        try
        {
            await nuGetService.InstallPackageAsync(_currentProjectPath, packageId, version, false, LogAction);
            await ReloadDataAsync();
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
            await nuGetService.RemovePackageAsync(_currentProjectPath, packageId, LogAction);
            await ReloadDataAsync();
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
            RequestRefresh?.Invoke();
        }
    }

    private async Task UpdateAllOutdatedAsync()
    {
        if (_currentProjectPath == null) return;

        var outdated = _nugetList.Items.Where(p => p.IsOutdated).ToList();
        if (outdated.Count == 0) return;

        _isActionRunning = true;
        RequestRefresh?.Invoke();
        try
        {
            for (int i = 0; i < outdated.Count; i++)
            {
                var pkg = outdated[i];
                _statusMessage = $"Updating {i + 1}/{outdated.Count}: {pkg.Id}...";
                RequestRefresh?.Invoke();
                await nuGetService.InstallPackageAsync(_currentProjectPath, pkg.Id, null, noRestore: true, logger: LogAction);
            }

            _statusMessage = "Finalizing restore...";
            RequestRefresh?.Invoke();

             var pipe = LogAction != null ? PipeTarget.ToDelegate(s => LogAction(Markup.Escape(s))) : PipeTarget.Null;

             var command = CliWrap.Cli.Wrap("dotnet")
                 .WithArguments($"restore \"{_currentProjectPath}\"")
                 .WithStandardOutputPipe(pipe)
                 .WithStandardErrorPipe(pipe);

             await AppCli.RunAsync(command);

             await ReloadDataAsync();
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
            RequestRefresh?.Invoke();
        }
    }

    public IRenderable GetContent(int availableHeight, int availableWidth)
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
                // Only show list if we have data (e.g. background update)
                if (_nugetList.Count > 0)
                {
                    RenderNuGetTab(grid, availableHeight, availableWidth);
                }
                 var msg = _statusMessage ?? "Loading...";
                 grid.AddRow(new Markup($"[yellow bold]{SpinnerHelper.GetFrame()} {Markup.Escape(msg)}[/]"));
                 return grid;
            }

            if (_appMode == AppMode.SelectingVersion)
            {
                RenderVersionOverlay(grid, availableHeight, availableWidth);
                return grid;
            }

            RenderNuGetTab(grid, availableHeight, availableWidth);

            if (_statusMessage != null)
            {
                 grid.AddRow(new Markup($"[yellow bold]{Markup.Escape(_statusMessage)}[/]"));
            }

            return grid;
        }
    }

    // Rendering Helpers (Copied and adapted)

    private void RenderVersionOverlay(Grid grid, int height, int width)
    {
        var pkg = _nugetList.SelectedItem;
        if (pkg == null) return;

        grid.AddRow(new Markup($"[blue]Select version for {Markup.Escape(pkg.Id)}:[/]"));

        var table = new Table().Border(TableBorder.Rounded).Expand();
        table.AddColumn("Version");

        int visibleRows = Math.Max(1, height - 3);
        var (start, end) = _versionList.GetVisibleRange(visibleRows);

        for (int i = start; i < end; i++)
        {
             var v = _versionList.Items[i];
             bool selected = i == _versionList.SelectedIndex;
             string style = selected ? "[black on blue]" : "";
             string closeStyle = selected ? "[/]" : "";


             if (v == pkg.ResolvedVersion) style = selected ? "[black on green]" : "[green]";
             if (v == pkg.LatestVersion) style = selected ? "[black on yellow]" : "[yellow]";

             table.AddRow(new Markup($"{style}{Markup.Escape(v)}{closeStyle}"));
        }
        grid.AddRow(table);
    }

    private void RenderNuGetTab(Grid grid, int maxRows, int width)
    {
        if (_nugetList.Count == 0)
        {
            grid.AddRow(new Markup("[dim]No packages found.[/]"));
            return;
        }

        int visibleRows = Math.Max(1, maxRows - 4); // Reserve space for borders/headers/status
        var (start, end) = _nugetList.GetVisibleRange(visibleRows);

        int currentWidth = Math.Max(7, _nugetList.Items.Max(p => p.ResolvedVersion.Length) + 2);
        int latestWidth = Math.Max(6, _nugetList.Items.Max(p => (p.LatestVersion ?? p.ResolvedVersion).Length) + 2);
        int packageWidth = Math.Max(10, width - currentWidth - latestWidth - 12);

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Expand()
            .AddColumn(new TableColumn("Package").Width(packageWidth))
            .AddColumn(new TableColumn("Current").Width(currentWidth).RightAligned())
            .AddColumn(new TableColumn("Latest").Width(latestWidth).RightAligned());

        for (int i = start; i < end; i++)
        {
            var pkg = _nugetList.Items[i];
            bool isSelected = i == _nugetList.SelectedIndex;

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
                table.AddRow(
                    new Markup($"[black on blue]{Markup.Escape(pkg.Id)}[/]"),
                    new Markup($"[black on blue]{Markup.Escape(pkg.ResolvedVersion)}[/]"),
                    new Markup($"[black on blue]{Markup.Remove(latestText)}[/]")
                );
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
        bool startColoring = false;

        bool isFirstColored = true;
        for (int i = 0; i < latestParts.Length; i++)
        {
            string latestPart = latestParts[i];
            string? currentPart = i < currentParts.Length ? currentParts[i] : null;

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
