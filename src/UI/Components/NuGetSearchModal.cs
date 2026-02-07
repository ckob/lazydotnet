using lazydotnet.Core;
using lazydotnet.Services;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace lazydotnet.UI.Components;

public class NuGetSearchModal : Modal
{
    private readonly Func<SearchResult, Task> _onSelected;
    private readonly Action<string>? _logAction;
    private readonly Action _requestRefresh;

    private string _searchQuery = "";
    private readonly ScrollableList<SearchResult> _searchList = new();
    private bool _isSearching;
    private string? _statusMessage;
    private int _lastFrameIndex = -1;
    private CancellationTokenSource? _searchCts;

    public NuGetSearchModal(
        Func<SearchResult, Task> onSelected,
        Action onClose,
        Action<string>? logAction,
        Action requestRefresh)
        : base("NuGet Search", new Markup("Type to search packages..."), onClose)
    {
        _onSelected = onSelected;
        _logAction = logAction;
        _requestRefresh = requestRefresh;
        Width = 80;
    }

    public override IEnumerable<KeyBinding> GetKeyBindings()
    {
        foreach (var b in base.GetKeyBindings()) yield return b;

        yield return new KeyBinding("enter", "install", () =>
        {
            if (_searchList.SelectedItem != null)
            {
                _onSelected(_searchList.SelectedItem);
                OnClose();
            }
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.Enter);

        yield return new KeyBinding("up", "up", () =>
        {
            _searchList.MoveUp();
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.UpArrow || (k.Modifiers == ConsoleModifiers.Control && (k.Key == ConsoleKey.P || k.Key == ConsoleKey.K)), false);

        yield return new KeyBinding("down", "down", () =>
        {
            _searchList.MoveDown();
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.DownArrow || (k.Modifiers == ConsoleModifiers.Control && (k.Key == ConsoleKey.N || k.Key == ConsoleKey.J)), false);

        yield return new KeyBinding("pgup", "page up", () =>
        {
            _searchList.PageUp(10);
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.PageUp || (k.Modifiers == ConsoleModifiers.Control && k.Key == ConsoleKey.U), false);

        yield return new KeyBinding("pgdn", "page down", () =>
        {
            _searchList.PageDown(10);
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.PageDown || (k.Modifiers == ConsoleModifiers.Control && k.Key == ConsoleKey.D), false);
    }

    public override async Task<bool> HandleInputAsync(ConsoleKeyInfo key)
    {
        if (await base.HandleInputAsync(key))
        {
            if (_searchCts != null)
            {
                await _searchCts.CancelAsync();
            }
            return true;
        }

        var changed = false;
        if (key.Key is ConsoleKey.Backspace or ConsoleKey.Delete)
        {
            if (_searchQuery.Length > 0)
            {
                _searchQuery = _searchQuery[..^1];
                changed = true;
            }
        }
        else if (!char.IsControl(key.KeyChar) && key.Modifiers == 0)
        {
            _searchQuery += key.KeyChar;
            changed = true;
        }

        if (!changed)
            return false;

        _searchList.Clear();
        TriggerSearch();
        return true;

    }

    private void TriggerSearch()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        if (string.IsNullOrWhiteSpace(_searchQuery))
        {
            _statusMessage = null;
            _isSearching = false;
            _requestRefresh();
            return;
        }

        _isSearching = true;
        _statusMessage = "Typing...";
        _requestRefresh();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, token);
                if (token.IsCancellationRequested) return;

                _statusMessage = "Searching...";
                _requestRefresh();

                var results = await NuGetService.SearchPackagesAsync(_searchQuery, _logAction, token);

                if (token.IsCancellationRequested) return;

                _searchList.SetItems(results);
                _statusMessage = results.Count == 0 ? "No results." : $"Found {results.Count} packages.";
            }
            catch (OperationCanceledException)
            {
                // Search was cancelled, ignore.
            }
            catch (Exception ex)
            {
                _statusMessage = $"Search failed: {ex.Message}";
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    _isSearching = false;
                    _requestRefresh();
                }
            }
        }, token);
    }

    public override bool OnTick()
    {
        if (_isSearching)
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

        grid.AddRow(new Markup($"[blue]Search: [/] {Markup.Escape(_searchQuery)}_"));
        grid.AddRow(Text.Empty);

        if (_isSearching)
        {
            grid.AddRow(new Markup($"[yellow]{SpinnerHelper.GetFrame()} Searching...[/]"));
        }
        else if (_searchList.Count > 0)
        {
            var modalWidth = Width ?? 80;
            var gridAvailableWidth = modalWidth - 8;

            var visibleRows = Math.Min(15, height - 10);
            var (start, end) = _searchList.GetVisibleRange(visibleRows);

            var table = new Table().Border(TableBorder.None).HideHeaders().NoSafeBorder().Expand();
            table.AddColumn("Id");
            table.AddColumn("Version");

            for (var i = start; i < end; i++)
            {
                var item = _searchList.Items[i];
                var isSelected = i == _searchList.SelectedIndex;

                var id = item.Id;
                var version = item.LatestVersion;

                var maxIdWidth = gridAvailableWidth - version.Length - 4;
                if (id.Length > maxIdWidth)
                {
                    id = id[..(maxIdWidth - 3)] + "...";
                }

                if (isSelected)
                {
                    table.AddRow(
                        new Markup($"[black on blue]{Markup.Escape(id)}[/]"),
                        new Markup($"[black on blue]{Markup.Escape(version)}[/]")
                    );
                }
                else
                {
                    table.AddRow(
                        new Markup(Markup.Escape(id)),
                        new Markup($"[dim]{Markup.Escape(version)}[/]")
                    );
                }
            }
            grid.AddRow(table);
        }
        else if (!string.IsNullOrEmpty(_statusMessage))
        {
            grid.AddRow(new Markup($"[yellow]{Markup.Escape(_statusMessage)}[/]"));
        }
        else
        {
            grid.AddRow(new Markup("[dim]Type to search packages...[/]"));
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
