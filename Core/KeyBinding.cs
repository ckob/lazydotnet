namespace lazydotnet.Core;

public record KeyBinding(
    string Label,
    string Description,
    Func<Task> Action,
    Func<ConsoleKeyInfo, bool> Match,
    bool ShowInBottomBar = true
);

public interface IKeyBindable
{
    IEnumerable<KeyBinding> GetKeyBindings();
}
