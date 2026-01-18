using lazydotnet.Core;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace lazydotnet.UI.Components;

public class Modal(string title, IRenderable content, Action onClose) : IKeyBindable
{
    protected string Title { get; } = title;
    protected IRenderable Content { get; } = content;
    protected Action OnClose { get; } = onClose;

    protected int? Width { get; set; }

    private readonly List<KeyBinding> _additionalBindings = [];

    public void SetAdditionalKeyBindings(IEnumerable<KeyBinding> bindings)
    {
        _additionalBindings.Clear();
        _additionalBindings.AddRange(bindings);
    }

    public virtual IEnumerable<KeyBinding> GetKeyBindings()
    {
        yield return new KeyBinding("esc", "close", () =>
        {
            OnClose();
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.Escape);

        foreach (var binding in _additionalBindings)
        {
            yield return binding;
        }
    }

    public virtual IRenderable GetRenderable(int width, int height)
    {
        var panel = new Panel(new Padder(Content, new Padding(2, 1, 2, 1)))
        {
            Header = new PanelHeader($"[bold yellow] {Title} [/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Blue),
            Expand = false,
            Width = Width
        };

        return panel;
    }

    public virtual Task<bool> HandleInputAsync(ConsoleKeyInfo key)
    {
        var binding = GetKeyBindings().FirstOrDefault(b => b.Match(key));
        return binding != null ? binding.Action().ContinueWith(_ => true) : Task.FromResult(false);
    }

    public virtual bool OnTick() => false;
}
