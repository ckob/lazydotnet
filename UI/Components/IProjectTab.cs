using Spectre.Console.Rendering;

namespace lazydotnet.UI.Components;

public interface IProjectTab
{
    Action? RequestRefresh { get; set; }
    string Title { get; }
    Task LoadAsync(string projectPath, string projectName, bool force = false);
    IRenderable GetContent(int height, int width);
    Task<bool> HandleKeyAsync(ConsoleKeyInfo key);
    void MoveUp();
    void MoveDown();
    string? GetScrollIndicator();
    void ClearData();
}
