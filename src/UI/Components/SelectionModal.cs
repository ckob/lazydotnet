using lazydotnet.Core;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace lazydotnet.UI.Components;

public class SelectionModal<T> : Modal
{
    private readonly ScrollableList<(string Label, T Value)> _options = new();
    private readonly Func<T, Task> _onSelected;

    public SelectionModal(string title, string prompt, List<(string Label, T Value)> options, Func<T, Task> onSelected,
        Action onClose)
        : base(title, new Markup(prompt), onClose)
    {
        _options.SetItems(options);
        _onSelected = onSelected;
        Width = 50;
    }

    public override IEnumerable<KeyBinding> GetKeyBindings()
    {
        foreach (var b in base.GetKeyBindings()) yield return b;

        yield return new KeyBinding("k", "up", () =>
            {
                _options.MoveUp();
                return Task.CompletedTask;
            },
            k => k.Key == ConsoleKey.UpArrow ||
                 k.Key == ConsoleKey.K ||
                 k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.P },
            ShowInBottomBar: false);

        yield return new KeyBinding("j", "down", () =>
            {
                _options.MoveDown();
                return Task.CompletedTask;
            },
            k => k.Key == ConsoleKey.DownArrow ||
                 k.Key == ConsoleKey.J ||
                 k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.N },
            ShowInBottomBar: false);

        yield return new KeyBinding("pgup", "page up", () =>
            {
                _options.PageUp(10);
                return Task.CompletedTask;
            },
            k => k.Key == ConsoleKey.PageUp ||
                 k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.U },
            ShowInBottomBar: false);

        yield return new KeyBinding("pgdn", "page down", () =>
            {
                _options.PageDown(10);
                return Task.CompletedTask;
            },
            k => k.Key == ConsoleKey.PageDown ||
                 k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.D },
            ShowInBottomBar: false);

        yield return new KeyBinding("enter", "select", async () =>
        {
            if (_options is not { Count: > 0, SelectedIndex: >= 0 })
                return;

            await _onSelected(_options.SelectedItem.Value);
            OnClose();
        }, k => k.Key == ConsoleKey.Enter);
    }

    public override IRenderable GetRenderable(int width, int height)
    {
        var grid = new Grid();
        grid.AddColumn();

        grid.AddRow(Content);
        grid.AddRow(Text.Empty);

        var table = new Table().Border(TableBorder.None).HideHeaders().NoSafeBorder().Expand();
        table.AddColumn("Option");

        for (var i = 0; i < _options.Count; i++)
        {
            var item = _options.Items[i];
            var isSelected = i == _options.SelectedIndex;
            var style = isSelected ? "[black on blue]" : "";
            var close = isSelected ? "[/]" : "";

            table.AddRow(new Markup($"{style}{Markup.Escape(item.Label)}{close}"));
        }

        grid.AddRow(table);

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