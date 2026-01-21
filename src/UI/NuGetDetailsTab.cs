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

public class NuGetDetailsTab : IProjectTab
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

    public static string Title => "NuGets";

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

    public bool IsLoaded(string projectPath) => _currentProjectPath == projectPath && !_isLoading;

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

        await CancelExistingLoadAsync();

        PrepareForNewLoad(projectPath, projectName);

        if (Directory.Exists(projectPath) && !projectPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            _statusMessage = "Select a project to see NuGet packages.";
            _isLoading = false;
            RequestRefresh?.Invoke();
            return;
        }

        try
        {
            var packages = await NuGetService.GetPackagesAsync(projectPath, LogAction, _loadCts!.Token);
            if (_loadCts.Token.IsCancellationRequested || _currentProjectPath != projectPath)
                return;

            lock (_lock)
            {
                _nugetList.SetItems(packages);
            }

            RequestRefresh?.Invoke();

            StartBackgroundFetch(projectPath);
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

    private async Task CancelExistingLoadAsync()
    {
        if (_loadCts != null)
        {
            await _loadCts.CancelAsync();
            _loadCts.Dispose();
        }
    }

    private void PrepareForNewLoad(string projectPath, string projectName)
    {
        _loadCts = new CancellationTokenSource();
        _currentProjectPath = projectPath;
        _currentProjectName = projectName;
        _isLoading = true;
        _isFetchingLatest = false;
        _statusMessage = null;
        _nugetList.Clear();
    }

    private void StartBackgroundFetch(string projectPath)
    {
        _isFetchingLatest = true;
        var ct = _loadCts!.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                var latestVersions = await NuGetService.GetLatestVersionsAsync(projectPath, LogAction, ct);
                if (ct.IsCancellationRequested || _currentProjectPath != projectPath)
                    return;

                UpdatePackageListWithLatest(latestVersions);
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

    private void UpdatePackageListWithLatest(Dictionary<string, string> latestVersions)
    {
        lock (_lock)
        {
            var currentList = _nugetList.Items.ToList();
            var updatedList = currentList.Select(p =>
                    latestVersions.TryGetValue(p.Id, out var latest)
                        ? p with { LatestVersion = latest }
                        : p with { LatestVersion = p.ResolvedVersion }
            ).ToList();

            _nugetList.SetItems(updatedList);
            _isFetchingLatest = false;
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

        foreach (var b in GetNavigationBindings()) yield return b;

        if (_appMode != AppMode.Normal)
            yield break;

        foreach (var b in GetActionBindings()) yield return b;
    }

    private IEnumerable<KeyBinding> GetNavigationBindings()
    {
        yield return new KeyBinding("k", "up", () => Task.Run(MoveUp),
            k => k.Key == ConsoleKey.UpArrow || k.Key == ConsoleKey.K ||
                 k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.P }, false);
        yield return new KeyBinding("j", "down", () => Task.Run(MoveDown),
            k => k.Key == ConsoleKey.DownArrow || k.Key == ConsoleKey.J ||
                 k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.N }, false);
    }

    private IEnumerable<KeyBinding> GetActionBindings()
    {
        var isSolution = _currentProjectPath?.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) == true;

        if (!isSolution)
        {
            yield return GetAddBinding();
        }

        if (_nugetList.SelectedItem != null)
        {
            foreach (var b in GetSelectedItemBindings(isSolution)) yield return b;
        }

        if (_nugetList.Items.Any(p => p.IsOutdated))
        {
            yield return new KeyBinding("U", "update all", UpdateAllOutdatedAsync, k => k.KeyChar == 'U');
        }
    }

    private KeyBinding GetAddBinding()
    {
        return new KeyBinding("a", "add", () =>
        {
            var modal = new NuGetSearchModal(
                async selected => { await InstallPackageAsync(selected.Id, null); },
                () => RequestModal?.Invoke(null!),
                LogAction,
                () => RequestRefresh?.Invoke()
            );
            RequestModal?.Invoke(modal);
            return Task.CompletedTask;
        }, k => k.KeyChar == 'a');
    }

    private IEnumerable<KeyBinding> GetSelectedItemBindings(bool isSolution)
    {
        var pkg = _nugetList.SelectedItem!;

        if (pkg.IsOutdated)
        {
            yield return new KeyBinding("u", "update", () =>
                    UpdatePackageAsync(pkg.Id, pkg.LatestVersion!),
                k => k.KeyChar == 'u');
        }

        if (!isSolution)
        {
            yield return GetDeleteBinding(pkg);
        }

        yield return GetVersionsBinding(pkg);
    }

    private KeyBinding GetDeleteBinding(NuGetPackageInfo pkg)
    {
        return new KeyBinding("d", "delete", () =>
        {
            var confirm = new ConfirmationModal(
                "Remove Package",
                $"Are you sure you want to remove package [bold]{Markup.Escape(pkg.Id)}[/]?",
                async () => { await RemovePackageAsync(pkg.Id); },
                () => RequestModal?.Invoke(null!)
            );

            RequestModal?.Invoke(confirm);
            return Task.CompletedTask;
        }, k => k.KeyChar == 'd');
    }

    private KeyBinding GetVersionsBinding(NuGetPackageInfo pkg)
    {
        return new KeyBinding("enter", "versions", () =>
        {
            var modal = new NuGetVersionSelectionModal(
                pkg.Id,
                pkg.ResolvedVersion,
                pkg.LatestVersion,
                async v => await UpdatePackageAsync(pkg.Id, v),
                () => RequestModal?.Invoke(null!),
                LogAction,
                () => RequestRefresh?.Invoke()
            );
            RequestModal?.Invoke(modal);
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.Enter);
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
                        new Markup(Markup.Escape(pkg.Id)),
                        new Markup(Markup.Escape(pkg.ResolvedVersion)),
                        new Markup(latestText)
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
        var color = GetUpdateColor(updateType);
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
                AppendColoredPart(result, latestPart, color, i > 0, isFirstColored);
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

    private static string GetUpdateColor(VersionUpdateType updateType) => updateType switch
    {
        VersionUpdateType.Major => "red",
        VersionUpdateType.Minor => "yellow",
        VersionUpdateType.Patch => "green",
        _ => "white"
    };

    private static void AppendColoredPart(System.Text.StringBuilder sb, string part, string color, bool includeDot, bool isFirstColored)
    {
        if (includeDot)
        {
            if (isFirstColored)
                sb.Append('.');
            else
                sb.Append($"[{color}].[/]");
        }

        sb.Append($"[{color}]{Markup.Escape(part)}[/]");
    }
}