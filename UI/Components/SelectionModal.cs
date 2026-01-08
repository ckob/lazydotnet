using lazydotnet.Core;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace lazydotnet.UI.Components;

public class SelectionModal<T> : Modal
{
    private readonly ScrollableList<(string Label, T Value)> _options = new();
    private readonly Action<T> _onSelected;

    public SelectionModal(string title, string prompt, List<(string Label, T Value)> options, Action<T> onSelected, Action onClose)
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
        }, k => k.Key == ConsoleKey.UpArrow || k.Key == ConsoleKey.K || (k.Modifiers == ConsoleModifiers.Control && k.Key == ConsoleKey.P), false);

        yield return new KeyBinding("j", "down", () =>
        {
            _options.MoveDown();
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.DownArrow || k.Key == ConsoleKey.J || (k.Modifiers == ConsoleModifiers.Control && k.Key == ConsoleKey.N), false);

        yield return new KeyBinding("enter", "select", () =>
        {
            if (_options.Count > 0 && _options.SelectedIndex >= 0)
            {
                _onSelected(_options.SelectedItem.Value);
                OnClose();
            }
            return Task.CompletedTask;
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

        for (int i = 0; i < _options.Count; i++)
        {
            var item = _options.Items[i];
            bool isSelected = i == _options.SelectedIndex;
            string style = isSelected ? "[black on blue]" : "";
            string close = isSelected ? "[/]" : "";
            
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
