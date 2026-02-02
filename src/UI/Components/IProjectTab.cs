using lazydotnet.Core;
using Spectre.Console.Rendering;

namespace lazydotnet.UI.Components;

public interface IProjectTab : IKeyBindable
{
    string Title { get; }
    Action<Modal>? RequestModal { get; set; }
    Action? RequestRefresh { get; set; }
    Action<string>? RequestSelectProject { get; set; }
    Task LoadAsync(string projectPath, string projectName, bool force = false);
    IRenderable GetContent(int height, int width, bool isActive);
    void ClearData();
    bool IsLoaded(string projectPath);
    bool OnTick() => false;
}
