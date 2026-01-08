using lazydotnet.Core;
using Spectre.Console.Rendering;

namespace lazydotnet.UI.Components;

public interface IProjectTab : IKeyBindable
{
    Action<Modal>? RequestModal { get; set; }
    Action? RequestRefresh { get; set; }
    Action<string>? RequestSelectProject { get; set; }
    string Title { get; }
    Task LoadAsync(string projectPath, string projectName, bool force = false);
    IRenderable GetContent(int height, int width, bool isActive);
    Task<bool> HandleKeyAsync(ConsoleKeyInfo key);
    void MoveUp();
    void MoveDown();
    string? GetScrollIndicator();
    void ClearData();
    bool OnTick() => false;
}
