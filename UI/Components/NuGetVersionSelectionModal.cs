using lazydotnet.Core;
using lazydotnet.Services;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace lazydotnet.UI.Components;

public class NuGetVersionSelectionModal : Modal
{
    private readonly NuGetService _nuGetService;
    private readonly string _packageId;
    private readonly string _currentVersion;
    private readonly string? _latestVersion;
    private readonly Action<string> _onSelected;
    private readonly Action<string>? _logAction;
    private readonly Action _requestRefresh;

    private readonly ScrollableList<string> _versionList = new();
    private bool _isLoading = true;
    private string? _statusMessage;
    private int _lastFrameIndex = -1;

    public NuGetVersionSelectionModal(
        NuGetService nuGetService,
        string packageId,
        string currentVersion,
        string? latestVersion,
        Action<string> onSelected,
        Action onClose,
        Action<string>? logAction,
        Action requestRefresh)
        : base($"Select Version: {packageId}", new Markup("Loading versions..."), onClose)
    {
        _nuGetService = nuGetService;
        _packageId = packageId;
        _currentVersion = currentVersion;
        _latestVersion = latestVersion;
        _onSelected = onSelected;
        _logAction = logAction;
        _requestRefresh = requestRefresh;
        Width = 60;

        // Start loading immediately
        LoadVersionsAsync();
    }

    private void LoadVersionsAsync()
    {
        var ct = new CancellationTokenSource().Token;

        _isLoading = true;
        _statusMessage = "Fetching versions...";
        _requestRefresh();

        _ = Task.Run(async () =>
        {
            try
            {
                var versions = await _nuGetService.GetPackageVersionsAsync(_packageId, _logAction, ct);

                if (ct.IsCancellationRequested) return;

                if (versions.Count == 0)
                {
                    _statusMessage = "No versions found.";
                }
                else
                {
                    var reversed = versions.ToList();
                    reversed.Reverse();
                    _versionList.SetItems(reversed);

                    var index = reversed.IndexOf(_currentVersion);
                    if (index >= 0)
                    {
                        _versionList.Select(index);
                    }
                    else
                    {
                         if (_latestVersion != null)
                         {
                             var latestIndex = reversed.IndexOf(_latestVersion);
                             if (latestIndex >= 0) _versionList.Select(latestIndex);
                         }
                    }

                    _statusMessage = null;
                }
            }
            catch (Exception ex)
            {
                _statusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                if (!ct.IsCancellationRequested)
                {
                    _isLoading = false;
                    _requestRefresh();
                }
            }
        }, ct);
    }

    public override IEnumerable<KeyBinding> GetKeyBindings()
    {
        foreach (var b in base.GetKeyBindings())
            yield return b;

        if (_isLoading || _versionList.Count <= 0)
            yield break;

        yield return new KeyBinding("k", "up", () =>
        {
            _versionList.MoveUp();
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.UpArrow || k.Key == ConsoleKey.K || (k.Modifiers == ConsoleModifiers.Control && k.Key == ConsoleKey.P), false);

        yield return new KeyBinding("j", "down", () =>
        {
            _versionList.MoveDown();
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.DownArrow || k.Key == ConsoleKey.J || (k.Modifiers == ConsoleModifiers.Control && k.Key == ConsoleKey.N), false);

        yield return new KeyBinding("enter", "select", () =>
        {
            if (_versionList.SelectedItem != null)
            {
                _onSelected(_versionList.SelectedItem);
                OnClose();
            }
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.Enter);
    }

    public override bool OnTick()
    {
        if (_isLoading)
        {
            var currentFrame = SpinnerHelper.GetCurrentFrameIndex();
            if (currentFrame != _lastFrameIndex)
            {
                _lastFrameIndex = currentFrame;
                return true;
            }
        }
        return false;
    }

    public override IRenderable GetRenderable(int width, int height)
    {
        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap());

        if (_isLoading)
        {
            grid.AddRow(new Markup($"[yellow]{SpinnerHelper.GetFrame()} {Markup.Escape(_statusMessage ?? "Loading...")}[/]"));
        }
        else if (!string.IsNullOrEmpty(_statusMessage))
        {
            grid.AddRow(new Markup($"[red]{Markup.Escape(_statusMessage)}[/]"));
        }
        else if (_versionList.Count == 0)
        {
            grid.AddRow(new Markup("[dim]No versions available.[/]"));
        }
        else
        {
            var visibleRows = Math.Min(20, height - 10);
            var (start, end) = _versionList.GetVisibleRange(visibleRows);

            var table = new Table().Border(TableBorder.None).HideHeaders().NoSafeBorder().Expand();
            table.AddColumn("Version");

            for (var i = start; i < end; i++)
            {
                var v = _versionList.Items[i];
                var isSelected = i == _versionList.SelectedIndex;

                string style;

                if (v == _currentVersion)
                {
                    style = isSelected ? "[black on green]" : "[green]";
                }
                else if (v == _latestVersion)
                {
                    style = isSelected ? "[black on yellow]" : "[yellow]";
                }
                else
                {
                    style = isSelected ? "[black on blue]" : "";
                }

                var empty = string.IsNullOrEmpty(style) ? "" : "[/]";
                var closeStyle = isSelected ? "[/]" : empty;

                table.AddRow(new Markup($"{style}{Markup.Escape(v)}{closeStyle}"));
            }
            grid.AddRow(table);

            var indicator = _versionList.GetScrollIndicator(visibleRows);
            if (indicator != null)
            {
                grid.AddRow(new Markup($"[dim]{indicator}[/]"));
            }
        }

        var panel = new Panel(new Padder(grid, new Padding(2, 1, 2, 1)))
        {
            Header = new PanelHeader($"[bold yellow] {Title} [/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Blue),
            Expand = false,
            Width = Width
        };

        return panel;
    }
}
