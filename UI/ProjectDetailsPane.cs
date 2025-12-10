using Spectre.Console;
using Spectre.Console.Rendering;
using lazydotnet.Services;
using lazydotnet.UI.Components;
using CliWrap;
using CliWrap.Buffered;

namespace lazydotnet.UI;

public enum AppMode
{
    Normal,
    SearchingNuGet,
    SelectingVersion,
    ConfirmingDelete,
    Busy // Showing spinner/progress
}

public class ProjectDetailsPane
{
    private readonly NuGetService _nugetService;
    private readonly SolutionService _solutionService;
    
    private readonly TabbedPane _tabs = new("NuGets", "Project References");
    private readonly ScrollableList<NuGetPackageInfo> _nugetList = new();
    private readonly ScrollableList<string> _refsList = new();
    
    // New State Fields
    private AppMode _appMode = AppMode.Normal;
    private readonly ScrollableList<SearchResult> _searchList = new();
    private readonly ScrollableList<string> _versionList = new();
    private string _searchQuery = "";
    private string? _lastSearchQuery;
    private string? _statusMessage;
    private bool _isActionRunning;

    
    private bool _isLoading;
    private string? _currentProjectPath;
    private string? _currentProjectName;
    private CancellationTokenSource? _loadCts;
    
    public Action<string>? LogAction { get; set; }

    public ProjectDetailsPane(NuGetService nugetService, SolutionService solutionService)
    {
        _nugetService = nugetService;
        _solutionService = solutionService;
    }

    public int ActiveTab => _tabs.ActiveTab;

    public void NextTab() => _tabs.NextTab();

    public void PreviousTab() => _tabs.PreviousTab();

    public void MoveUp()
    {
        if (_tabs.ActiveTab == 0)
            _nugetList.MoveUp();
        else
            _refsList.MoveUp();
    }

    public void MoveDown()
    {
        if (_tabs.ActiveTab == 0)
            _nugetList.MoveDown();
        else
            _refsList.MoveDown();
    }

    public string? GetCounter()
    {
        if (_currentProjectPath == null || _isLoading) return null;
        
        if (_tabs.ActiveTab == 0)
        {
            if (_nugetList.Count == 0) return null;
            return $"{_nugetList.SelectedIndex + 1} of {_nugetList.Count}";
        }
        else
        {
            if (_refsList.Count == 0) return null;
            return $"{_refsList.SelectedIndex + 1} of {_refsList.Count}";
        }
    }

    public void ClearData()
    {
        _nugetList.Clear();
        _refsList.Clear();
        _isLoading = true;
    }

    public void ClearForNonProject()
    {
        _nugetList.Clear();
        _refsList.Clear();
        _currentProjectPath = null;
        _currentProjectName = null;
        _isLoading = false;
    }

    public async Task LoadProjectDataAsync(string projectPath, string projectName)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;
        
        _currentProjectPath = projectPath;
        _currentProjectName = projectName;
        _isLoading = true;
        _nugetList.Clear();
        _refsList.Clear();

        try
        {
            var packagesTask = _nugetService.GetPackagesAsync(projectPath, LogAction, ct);
            var referencesTask = _solutionService.GetProjectReferencesAsync(projectPath);

            await Task.WhenAll(packagesTask, referencesTask);

            if (ct.IsCancellationRequested || _currentProjectPath != projectPath)
                return;

            _nugetList.SetItems(await packagesTask);
            _refsList.SetItems(await referencesTask);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
        finally
        {
            if (_currentProjectPath == projectPath)
                _isLoading = false;
        }
    }

    public async Task<bool> HandleKey(ConsoleKeyInfo key)
    {
        if (_isActionRunning) return true;

        switch (_appMode)
        {
            case AppMode.Normal:
                return await HandleNormalMode(key);
            case AppMode.SearchingNuGet:
                return await HandleSearchMode(key);
            case AppMode.SelectingVersion:
                return await HandleVersionMode(key);
            case AppMode.ConfirmingDelete:
                return await HandleConfirmDelete(key);
        }
        return false;
    }

    private async Task<bool> HandleNormalMode(ConsoleKeyInfo key)
    {
        if (_tabs.ActiveTab != 0) return false;

        switch (key.KeyChar)
        {
            case 'a':
                _appMode = AppMode.SearchingNuGet;
                _searchQuery = "";
                _lastSearchQuery = null;
                _searchList.Clear();
                return true;
            case 'u':
                if (_nugetList.SelectedItem != null && _nugetList.SelectedItem.IsOutdated)
                {
                    _ = InstallPackage(_nugetList.SelectedItem.Id, _nugetList.SelectedItem.LatestVersion);
                }
                return true;
            case 'd':
                if (_nugetList.SelectedItem != null)
                {
                    _appMode = AppMode.ConfirmingDelete;
                }
                return true;
            case 'U':
                _ = UpdateAllOutdated();
                return true;
        }

        if (key.Key == ConsoleKey.Enter)
        {
            if (_nugetList.SelectedItem != null)
            {
                _ = ShowVersionsForPackage(_nugetList.SelectedItem.Id);
            }
            return true;
        }

        return false;
    }

    private async Task<bool> HandleSearchMode(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape)
        {
            _appMode = AppMode.Normal;
            _statusMessage = null;
            return true;
        }

        if (key.Key == ConsoleKey.Enter)
        {

            if (_searchQuery != _lastSearchQuery && !string.IsNullOrWhiteSpace(_searchQuery))
            {
                 _ = PerformSearch(_searchQuery);
                 return true;
            }

            if (_searchList.Count > 0)
            {

                var selected = _searchList.SelectedItem;
                if (selected != null)
                {
                    _ = InstallPackage(selected.Id, null);
                    _appMode = AppMode.Normal;
                    _statusMessage = null;
                }
            }
            else if (!string.IsNullOrWhiteSpace(_searchQuery))
            {

                _ = PerformSearch(_searchQuery);
            }
            return true;
        }
        
        if (key.Key == ConsoleKey.UpArrow || (key.Key == ConsoleKey.P && (key.Modifiers & ConsoleModifiers.Control) != 0))
        {
            _searchList.MoveUp();
            return true;
        }
        if (key.Key == ConsoleKey.DownArrow || (key.Key == ConsoleKey.N && (key.Modifiers & ConsoleModifiers.Control) != 0))
        {
            _searchList.MoveDown();
            return true;
        }

        // Typing logic
        if (key.Key == ConsoleKey.Backspace && _searchQuery.Length > 0)
        {
            _searchQuery = _searchQuery[..^1];
            return true;
        }
        if (!char.IsControl(key.KeyChar))
        {
            _searchQuery += key.KeyChar;
            return true;
        }

        return true;
    }

    private async Task<bool> HandleVersionMode(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape)
        {
            _appMode = AppMode.Normal;
            return true;
        }

        if (key.Key == ConsoleKey.UpArrow)
        {
            _versionList.MoveUp();
            return true;
        }
        if (key.Key == ConsoleKey.DownArrow)
        {
            _versionList.MoveDown();
            return true;
        }

        if (key.Key == ConsoleKey.Enter)
        {
            var selectedVersion = _versionList.SelectedItem;
            var selectedPackage = _nugetList.SelectedItem;
            
            if (selectedVersion != null && selectedPackage != null)
            {
                _ = InstallPackage(selectedPackage.Id, selectedVersion);
                _appMode = AppMode.Normal;
            }
            return true;
        }

        return true;
    }

    private Task<bool> HandleConfirmDelete(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Y)
        {
             _ = RemovePackage(_nugetList.SelectedItem!.Id);
             _appMode = AppMode.Normal;
             return Task.FromResult(true);
        }
        
        _appMode = AppMode.Normal;
        return Task.FromResult(true);
    }

    private async Task PerformSearch(string query)
    {
        _lastSearchQuery = query;
        _isActionRunning = true;
        _statusMessage = "Searching...";

        _ = Task.Run(async () => 
        {
            try
            {
                 var results = await _nugetService.SearchPackagesAsync(query, LogAction);
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
            }
        });
    }

    private async Task ShowVersionsForPackage(string packageId)
    {
        _isActionRunning = true;
        _statusMessage = "Fetching versions...";
        _ = Task.Run(async () => 
        {
            try
            {
                var pkg = _nugetList.SelectedItem;
                if (pkg == null) return;
                var versions = await _nugetService.GetPackageVersionsAsync(pkg.Id, LogAction);

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
            }
        });
    }

    private async Task InstallPackage(string packageId, string? version)
    {
        if (_currentProjectPath == null) return;

        _isActionRunning = true;
        _statusMessage = $"Installing {packageId} {(version ?? "latest")}...";
        try
        {
            await _nugetService.InstallPackageAsync(_currentProjectPath, packageId, version, false, LogAction);
            await ReloadData();
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
        }
    }

    private async Task RemovePackage(string packageId)
    {
        if (_currentProjectPath == null) return;

        _isActionRunning = true;
        _statusMessage = $"Removing {packageId}...";
        try
        {
            await _nugetService.RemovePackageAsync(_currentProjectPath, packageId, LogAction);
            await ReloadData();
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
        }
    }

    private async Task UpdateAllOutdated()
    {
        if (_currentProjectPath == null) return;
        
        var outdated = _nugetList.Items.Where(p => p.IsOutdated).ToList();
        if (!outdated.Any()) return;

        _isActionRunning = true;
        try
        {
            for (int i = 0; i < outdated.Count; i++)
            {
                var pkg = outdated[i];
                _statusMessage = $"Updating {i + 1}/{outdated.Count}: {pkg.Id}...";

                
                await _nugetService.InstallPackageAsync(_currentProjectPath, pkg.Id, null, noRestore: true, logger: LogAction);
            }

            _statusMessage = "Finalizing restore...";

             var pipe = LogAction != null ? PipeTarget.ToDelegate(s => LogAction(Markup.Escape(s))) : PipeTarget.Null;
             LogAction?.Invoke($"[blue]Running: dotnet restore \"{Markup.Escape(_currentProjectPath)}\"[/]");
             await CliWrap.Cli.Wrap("dotnet")
                 .WithArguments($"restore \"{_currentProjectPath}\"")
                 .WithStandardOutputPipe(pipe)
                 .WithStandardErrorPipe(pipe)
                 .ExecuteAsync();

             await ReloadData();
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
        }
    }

    private async Task ReloadData()
    {
        if (_currentProjectPath != null && _currentProjectName != null)
        {
            await LoadProjectDataAsync(_currentProjectPath, _currentProjectName);
        }
    }

    public IRenderable GetContent(int availableHeight, int availableWidth)
    {
        var grid = new Grid();
        grid.AddColumn();

        if (_currentProjectPath == null)
        {
            grid.AddRow(new Markup("[dim]Select a project to see details...[/]"));
            return grid;
        }


        if (_appMode == AppMode.SearchingNuGet)
        {
            RenderSearchOverlay(grid, availableHeight, availableWidth);
            return grid;
        }

        if (_isLoading || _isActionRunning)
        {

            if (_tabs.ActiveTab == 0)
            {
                RenderNuGetTab(grid, availableHeight, availableWidth);
            }
            else
            {
                RenderReferencesTab(grid, availableHeight);
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
            grid.AddRow(new Markup($"[red bold]Are you sure you want to remove {_nugetList.SelectedItem?.Id}? (y/n)[/]"));
        }

        if (_tabs.ActiveTab == 0)
        {
            RenderNuGetTab(grid, availableHeight, availableWidth);
        }
        else
        {
            RenderReferencesTab(grid, availableHeight);
        }

        if (_statusMessage != null)
        {

             grid.AddRow(new Markup($"[yellow bold]{Markup.Escape(_statusMessage)}[/]"));
        }

        return grid;
    }

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


        int visibleRows = Math.Max(1, maxRows - 4);
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

    private void RenderReferencesTab(Grid grid, int maxRows)
    {
        if (_refsList.Count == 0)
        {
            grid.AddRow(new Markup("[dim]No project references found.[/]"));
            return;
        }

        int visibleRows = Math.Max(1, maxRows - 2);
        var (start, end) = _refsList.GetVisibleRange(visibleRows);

        for (int i = start; i < end; i++)
        {
            var refName = _refsList.Items[i];
            bool isSelected = i == _refsList.SelectedIndex;

            if (isSelected)
            {
                grid.AddRow(new Markup($"[black on blue]  → {Markup.Escape(refName)}[/]"));
            }
            else
            {
                grid.AddRow(new Markup($"  [green]→[/] {Markup.Escape(refName)}"));
            }
        }

        var indicator = _refsList.GetScrollIndicator(visibleRows);
        if (indicator != null)
        {
            grid.AddRow(new Markup($"[dim]{indicator}[/]"));
        }
    }
}
