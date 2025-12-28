using Spectre.Console;
using Spectre.Console.Rendering;

using lazydotnet.UI.Components;

namespace lazydotnet.UI;

public class AppLayout
{
    private const int TopRatio = 70;
    private const int BottomRatio = 30;

    private readonly Layout _rootLayout = new Layout("Root")
        .SplitRows(
            new Layout("Top").SplitColumns(
                new Layout("Left").Ratio(35),
                new Layout("Right").Ratio(65)
            ).Ratio(TopRatio),
            new Layout("Bottom").Ratio(BottomRatio)
        );

    public int GetBottomHeight(int totalHeight) => (totalHeight * BottomRatio) / (TopRatio + BottomRatio);
    public LogViewer LogViewer { get; } = new();
    public TestOutputViewer TestOutputViewer { get; } = new();
    private int _activePanel = 0;
    private int _bottomActiveTab = 0; // 0 = Log, 1 = Test Output

    public Layout GetRoot() => _rootLayout;

    public int ActivePanel => _activePanel;
    public int BottomActiveTab => _bottomActiveTab;

    private int _detailsActiveTab = 0;

    public void SetActivePanel(int panel)
    {
        _activePanel = Math.Clamp(panel, 0, 2);
    }

    public void SetBottomActiveTab(int tab)
    {
        _bottomActiveTab = Math.Clamp(tab, 0, 1);
    }

    public void NextBottomTab()
    {
        _bottomActiveTab = (_bottomActiveTab + 1) % 2;
    }

    public void PreviousBottomTab()
    {
        _bottomActiveTab = (_bottomActiveTab - 1 + 2) % 2;
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
            .Padding(0, 0, 0, 0)
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
            .Padding(0, 0, 0, 0)
            .Expand();

        _rootLayout["Right"].Update(panel);
    }

    public event Action? OnLog;

    public void AddLog(string message)
    {
        LogViewer.AddLog(message);
        OnLog?.Invoke();
    }

    public void UpdateBottom(int width, int height)
    {
        var isActive = _activePanel == 2;
        string logTab = _bottomActiveTab == 0 ? "[green]Log[/]" : "[dim]Log[/]";
        string testTab = _bottomActiveTab == 1 ? "[green]Test Output[/]" : "[dim]Test Output[/]";

        var header = isActive
            ? $"[green][[3]][/]-{logTab} - {testTab}"
            : $"[dim][[3]][/]-{logTab} - {testTab}";

        IRenderable content = _bottomActiveTab switch
        {
            0 => LogViewer.GetContent(height - 2, width, isActive),
            1 => TestOutputViewer.GetContent(height - 2, width, isActive),
            _ => new Markup("")
        };

        var panel = new Panel(content)
            .Header(header)
            .Border(BoxBorder.Rounded)
            .BorderColor(isActive ? Color.Green : Color.Grey)
            .Padding(0, 0, 0, 0)
            .Expand();

        _rootLayout["Bottom"].Update(panel);
    }
}
