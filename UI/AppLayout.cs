using Spectre.Console;
using Spectre.Console.Rendering;

using lazydotnet.UI.Components;

namespace lazydotnet.UI;

public class AppLayout
{
    private readonly Layout _rootLayout;
    public LogViewer LogViewer { get; } = new();
    private int _activePanel = 0;

    public AppLayout()
    {
        _rootLayout = new Layout("Root")
            .SplitRows(
                new Layout("Top").SplitColumns(
                    new Layout("Left").Ratio(4),
                    new Layout("Right").Ratio(6)
                ),
                new Layout("Bottom").Size(12) 
            );
    }

    public Layout GetRoot() => _rootLayout;

    public int ActivePanel => _activePanel;
    
    private int _detailsActiveTab = 0;

    public void SetActivePanel(int panel)
    {
        _activePanel = Math.Clamp(panel, 0, 2);
    }

    public void SetDetailsActiveTab(int tab)
    {
        _detailsActiveTab = Math.Clamp(tab, 0, 2);
    }

    public void UpdateLeft(IRenderable renderable)
    {
        var isActive = _activePanel == 0;
        var header = isActive 
            ? "[green][[1]][/]-[green]Explorer[/]" 
            : "[dim][[1]][/]-[green]Explorer[/]";
        var panel = new Panel(renderable)
            .Header(header)
            .Border(BoxBorder.Rounded)
            .BorderColor(isActive ? Color.Green : Color.Grey)
            .Expand();
            
        _rootLayout["Left"].Update(panel);
    }

    public void UpdateRight(IRenderable renderable)
    {
        var isActive = _activePanel == 1;
        // Refs (0), NuGets (1), Tests (2)
        string refsTab = _detailsActiveTab == 0 ? "[green]Project References[/]" : "[dim]Project References[/]";
        string nugetTab = _detailsActiveTab == 1 ? "[green]NuGets[/]" : "[dim]NuGets[/]";
        string testsTab = _detailsActiveTab == 2 ? "[green]Tests[/]" : "[dim]Tests[/]";
        
        var header = isActive
            ? $"[green][[2]][/]-{refsTab} - {nugetTab} - {testsTab}"
            : $"[dim][[2]][/]-{refsTab} - {nugetTab} - {testsTab}";
        var panel = new Panel(renderable)
            .Header(header)
            .Border(BoxBorder.Rounded)
            .BorderColor(isActive ? Color.Green : Color.Grey)
            .Expand();
            
        _rootLayout["Right"].Update(panel);
    }

    public event Action? OnLog;

    public void AddLog(string message)
    {
        LogViewer.AddLog(message);
        OnLog?.Invoke();
    }
    
    public void UpdateBottom()
    {
        _rootLayout["Bottom"].Update(LogViewer.GetContent(12, _activePanel == 2));
    }
}
