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

        yield return new KeyBinding("↑/ctrl+p", "up", () =>
        {
            _searchList.MoveUp();
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.UpArrow || (k.Modifiers == ConsoleModifiers.Control && k.Key == ConsoleKey.P), true);

        yield return new KeyBinding("↓/ctrl+n", "down", () =>
        {
            _searchList.MoveDown();
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.DownArrow || (k.Modifiers == ConsoleModifiers.Control && k.Key == ConsoleKey.N), true);

        yield return new KeyBinding("pgup/ctrl+u", "page up", () =>
        {
            _searchList.PageUp(10);
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.PageUp || (k.Modifiers == ConsoleModifiers.Control && k.Key == ConsoleKey.U), true);

        yield return new KeyBinding("pgdn/ctrl+d", "page down", () =>
        {
            _searchList.PageDown(10);
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.PageDown || (k.Modifiers == ConsoleModifiers.Control && k.Key == ConsoleKey.D), true);
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
        else if (!char.IsControl(key.KeyChar))
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

    private static int GetMaxVersionLength(ScrollableList<SearchResult> searchList, int start, int end)
    {
        var maxLength = 0;
        for (var i = start; i < end; i++)
        {
            var item = searchList.Items[i];
            if (item.LatestVersion.Length > maxLength)
            {
                maxLength = item.LatestVersion.Length;
            }
        }
        return maxLength;
    }

    private static void AddSearchResultRow(Table table, SearchResult item, bool isSelected, int idColWidth)
    {
        var id = item.Id;
        var version = item.LatestVersion;

        if (id.Length > idColWidth - 3)
        {
            id = id[..(idColWidth - 3)] + "...";
        }

        var idMarkup = isSelected
            ? new Markup($"[black on blue]{Markup.Escape(id)}[/]")
            : new Markup(Markup.Escape(id));

        var versionMarkup = isSelected
            ? new Markup($"[black on blue]{Markup.Escape(version)}[/]")
            : new Markup($"[dim]{Markup.Escape(version)}[/]");

        table.AddRow(idMarkup, versionMarkup);
    }

    private Table CreateResultsTable(int height)
    {
        var modalWidth = Width ?? 80;
        var gridAvailableWidth = modalWidth - 8;
        var visibleRows = Math.Min(15, height - 10);
        var (start, end) = _searchList.GetVisibleRange(visibleRows);
        var maxVersionLength = GetMaxVersionLength(_searchList, start, end);
        var versionColWidth = maxVersionLength;
        var idColWidth = gridAvailableWidth - versionColWidth - 3;

        var table = new Table().Border(TableBorder.None).HideHeaders().NoSafeBorder().Width(gridAvailableWidth);
        table.AddColumn(new TableColumn("Id").Width(idColWidth));
        table.AddColumn(new TableColumn("Version").RightAligned().Width(versionColWidth));

        for (var i = start; i < end; i++)
        {
            var item = _searchList.Items[i];
            var isSelected = i == _searchList.SelectedIndex;
            AddSearchResultRow(table, item, isSelected, idColWidth);
        }

        return table;
    }

    private IRenderable GetContent(int height)
    {
        if (_isSearching)
        {
            return new Markup($"[yellow]{SpinnerHelper.GetFrame()} Searching...[/]");
        }

        if (_searchList.Count > 0)
        {
            return CreateResultsTable(height);
        }

        if (!string.IsNullOrEmpty(_statusMessage))
        {
            return new Markup($"[yellow]{Markup.Escape(_statusMessage)}[/]");
        }

        return new Markup("[dim]Type to search packages...[/]");
    }

    public override IRenderable GetRenderable(int width, int height)
    {
        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap());

        grid.AddRow(new Markup($"[blue]Search: [/] {Markup.Escape(_searchQuery)}_"));
        grid.AddRow(Text.Empty);
        grid.AddRow(GetContent(height));

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
