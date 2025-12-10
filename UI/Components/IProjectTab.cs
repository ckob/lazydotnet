using Spectre.Console.Rendering;

namespace lazydotnet.UI.Components;

public interface IProjectTab
{
    string Title { get; }
    Task LoadAsync(string projectPath, string projectName);
    IRenderable GetContent(int height, int width);
    Task<bool> HandleKey(ConsoleKeyInfo key);
    void MoveUp();
    void MoveDown();
    string? GetScrollIndicator();
    void ClearData();
}
