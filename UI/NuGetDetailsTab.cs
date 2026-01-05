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
    SearchingNuGet,
    SelectingVersion,
    ConfirmingDelete,
    Busy // Showing spinner/progress
}

public class NuGetDetailsTab(NuGetService nuGetService) : IProjectTab
{
    // Lists
    private readonly ScrollableList<NuGetPackageInfo> _nugetList = new();
    private readonly ScrollableList<SearchResult> _searchList = new();
    private readonly ScrollableList<string> _versionList = new();

    private readonly Lock _lock = new(); // UI Synchronization

    // State
    private AppMode _appMode = AppMode.Normal;
    private string _searchQuery = "";
    private string? _lastSearchQuery;
    private string? _statusMessage;
    private bool _isActionRunning;
    private bool _isLoading;
    private string? _currentProjectPath;
    private string? _currentProjectName;
    private CancellationTokenSource? _loadCts;

    public Action<string>? LogAction { get; set; }
    public Action? RequestRefresh { get; set; }

    public string Title => "NuGets";

    public void MoveUp()
    {
        lock (_lock)
        {
            if (_appMode == AppMode.SearchingNuGet) _searchList.MoveUp();
            else if (_appMode == AppMode.SelectingVersion) _versionList.MoveUp();
            else _nugetList.MoveUp();
        }
    }

    public void MoveDown()
    {
        lock (_lock)
        {
            if (_appMode == AppMode.SearchingNuGet) _searchList.MoveDown();
            else if (_appMode == AppMode.SelectingVersion) _versionList.MoveDown();
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
        }
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
        _nugetList.Clear();

        try
        {
            var packages = await nuGetService.GetPackagesAsync(projectPath, LogAction, ct);
             if (ct.IsCancellationRequested || _currentProjectPath != projectPath)
                return;

            _nugetList.SetItems(packages);
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
        if (_isActionRunning) yield break;

        // Navigation (hidden)
        yield return new KeyBinding("k", "up", () => Task.Run(MoveUp), k => k.Key == ConsoleKey.UpArrow || k.Key == ConsoleKey.K, false);
        yield return new KeyBinding("j", "down", () => Task.Run(MoveDown), k => k.Key == ConsoleKey.DownArrow || k.Key == ConsoleKey.J, false);

        if (_appMode == AppMode.Normal)
        {
            yield return new KeyBinding("a", "add", () =>
            {
                _appMode = AppMode.SearchingNuGet;
                _searchQuery = "";
                _lastSearchQuery = null;
                _searchList.Clear();
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
                    _appMode = AppMode.ConfirmingDelete;
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
        else if (_appMode == AppMode.SearchingNuGet)
        {
            yield return new KeyBinding("Esc", "cancel", () =>
            {
                _appMode = AppMode.Normal;
                _statusMessage = null;
                return Task.CompletedTask;
            }, k => k.Key == ConsoleKey.Escape);

            yield return new KeyBinding("Enter", "search/install", async () =>
            {
                if (_searchQuery != _lastSearchQuery && !string.IsNullOrWhiteSpace(_searchQuery))
                {
                    await PerformSearchAsync(_searchQuery);
                    return;
                }

                if (_searchList.Count > 0 && _searchList.SelectedItem != null)
                {
                    await InstallPackageAsync(_searchList.SelectedItem.Id, null);
                    _appMode = AppMode.Normal;
                    _statusMessage = null;
                }
                else if (!string.IsNullOrWhiteSpace(_searchQuery))
                {
                    await PerformSearchAsync(_searchQuery);
                }
            }, k => k.Key == ConsoleKey.Enter);
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
        else if (_appMode == AppMode.ConfirmingDelete)
        {
            yield return new KeyBinding("y", "confirm delete", () =>
            {
                var p = _nugetList.SelectedItem;
                _appMode = AppMode.Normal;
                return p != null ? RemovePackageAsync(p.Id) : Task.CompletedTask;
            }, k => k.Key == ConsoleKey.Y);

            yield return new KeyBinding("any", "cancel", () =>
            {
                _appMode = AppMode.Normal;
                return Task.CompletedTask;
            }, k => !char.IsControl(k.KeyChar) && k.Key != ConsoleKey.Y);
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

        if (_appMode == AppMode.SearchingNuGet && !char.IsControl(key.KeyChar))
        {
             if (key.Key == ConsoleKey.Backspace && _searchQuery.Length > 0)
             {
                 _searchQuery = _searchQuery[..^1];
             }
             else if (!char.IsControl(key.KeyChar))
             {
                 _searchQuery += key.KeyChar;
             }
             return true;
        }

        return false;
    }

    // Actions

    private async Task PerformSearchAsync(string query)
    {
        _lastSearchQuery = query;
        _isActionRunning = true;
        _statusMessage = "Searching...";

        // Fire and forget
        _ = Task.Run(async () =>
        {
            try
            {
                 var results = await nuGetService.SearchPackagesAsync(query, LogAction);
                 _searchList.SetItems(results);
                 _statusMessage = results.Count == 0 ? "No results." : $"Found {results.Count} packages.";
            }
            catch (Exception ex)
            {
                _statusMessage = $"Search failed: {ex.Message}";
                await Task.Delay(3000);
            }
            finally
            {
                _isActionRunning = false;
                RequestRefresh?.Invoke();
            }
        });
    }

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

            if (_appMode == AppMode.SearchingNuGet)
            {
                RenderSearchOverlay(grid, availableHeight, availableWidth);
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
                 grid.AddRow(new Markup($"[yellow bold]{Markup.Escape(msg)}[/]"));
                 return grid;
            }

            if (_appMode == AppMode.SelectingVersion)
            {
                RenderVersionOverlay(grid, availableHeight, availableWidth);
                return grid;
            }

            if (_appMode == AppMode.ConfirmingDelete)
            {
                grid.AddRow(new Markup($"[red bold]Are you sure you want to remove {_nugetList.SelectedItem?.Id}? (y/n)[/]"));
                grid.AddRow(new Markup($"[dim]Press Y to confirm, any other key to cancel[/]"));
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

     private void RenderSearchOverlay(Grid grid, int height, int width)
    {
        grid.AddRow(new Markup($"[blue]Search NuGet: [/] {Markup.Escape(_searchQuery)}_"));

        if (_isActionRunning)
        {
             var msg = _statusMessage ?? "Searching...";
             grid.AddRow(new Markup($"[yellow bold]{Markup.Escape(msg)}[/]"));
             return;
        }

        if (_searchList.Count > 0)
        {
             var table = new Table().Border(TableBorder.Rounded).Expand();
             table.AddColumn("Id");
             table.AddColumn("Latest");

             int visibleRows = Math.Max(1, height - 3);
             var (start, end) = _searchList.GetVisibleRange(visibleRows);

             for(int i = start; i < end; i++)
             {
                 var item = _searchList.Items[i];
                 bool selected = i == _searchList.SelectedIndex;
                 string style = selected ? "[black on blue]" : "";
                 string closeStyle = selected ? "[/]" : "";

                 table.AddRow(
                     new Markup($"{style}{Markup.Escape(item.Id)}{closeStyle}"),
                     new Markup($"{style}{Markup.Escape(item.LatestVersion)}{closeStyle}")
                 );
             }
             grid.AddRow(table);
        }
        else
        {
            grid.AddRow(new Markup("[dim]Type and press Enter to search...[/]"));
        }
    }

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
            if (!pkg.IsOutdated)
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
