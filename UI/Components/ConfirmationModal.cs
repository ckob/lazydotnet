using lazydotnet.Core;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace lazydotnet.UI.Components;

public class ConfirmationModal(string title, string message, Action onConfirm, Action onClose)
    : Modal(title, new Markup(message), onClose)
{
    public override IEnumerable<KeyBinding> GetKeyBindings()
    {
        foreach (var b in base.GetKeyBindings()) yield return b;

        yield return new KeyBinding("y", "confirm", () =>
        {
            onConfirm();
            OnClose();
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.Y);

        yield return new KeyBinding("n", "cancel", () =>
        {
            OnClose();
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.N);
    }

    public override IRenderable GetRenderable(int width, int height)
    {
        var grid = new Grid();
        grid.AddColumn(new GridColumn().Centered());
        grid.AddRow(Content);
        grid.AddRow(Text.Empty);
        grid.AddRow(new Markup("[blue][[y]][/]es / [blue][[n]][/]o"));

        var panel = new Panel(new Padder(grid, new Padding(4, 2, 4, 2)))
        {
            Header = new PanelHeader($"[bold yellow] {Title} [/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Red),
            Expand = false,
            Width = 40
        };

        return panel;
    }
}
