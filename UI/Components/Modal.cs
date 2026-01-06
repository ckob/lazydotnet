using lazydotnet.Core;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace lazydotnet.UI.Components;

public class Modal : IKeyBindable
{
    public string Title { get; }
    public IRenderable Content { get; }
    public Action OnClose { get; }

    public Modal(string title, IRenderable content, Action onClose)
    {
        Title = title;
        Content = content;
        OnClose = onClose;
    }

    public virtual IEnumerable<KeyBinding> GetKeyBindings()
    {
        yield return new KeyBinding("Esc", "close", () =>
        {
            OnClose();
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.Escape);
    }

    public int? Width { get; set; }

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
        if (binding != null)
        {
            return binding.Action().ContinueWith(_ => true);
        }
        return Task.FromResult(false);
    }
}
