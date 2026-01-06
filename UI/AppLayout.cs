using lazydotnet.Core;
using Spectre.Console;
using Spectre.Console.Rendering;

using lazydotnet.UI.Components;

namespace lazydotnet.UI;

public class AppLayout
{
    private const int TopRatio = 70;
    private const int BottomRatio = 30;

    private readonly Layout _mainLayout = new Layout("MainContainer").SplitRows(
        new Layout("Top").SplitColumns(
            new Layout("Left").Ratio(35),
            new Layout("Right").Ratio(65)
        ).Ratio(TopRatio),
        new Layout("Bottom").Ratio(BottomRatio)
    );

    private readonly Layout _rootLayout;

    public AppLayout()
    {
        _rootLayout = new Layout("Root")
            .SplitRows(
                new Layout("Main").Update(_mainLayout),
                new Layout("Footer").Size(1)
            );
    }

    public void UpdateModal(IRenderable? modalContent)
    {
        if (modalContent != null)
        {
            // Create an overlay that places the modal over the main layout
            _rootLayout["Main"].Update(new Overlay(_mainLayout, modalContent));
        }
        else
        {
            _rootLayout["Main"].Update(_mainLayout);
        }
    }

    public int GetBottomHeight(int totalHeight)
    {
        int availableHeight = totalHeight - 1; // Reserved for footer
        int topHeight = (availableHeight * TopRatio) / (TopRatio + BottomRatio);
        return availableHeight - topHeight;
    }
    public LogViewer LogViewer { get; } = new();
    public TestOutputViewer TestOutputViewer { get; } = new();
    public LogViewer EasyDotnetOutputViewer { get; } = new();
    private int _activePanel = 0;
    private int _bottomActiveTab = 0; // 0 = Log, 1 = Test Output, 2 = EasyDotnet Output

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
        _bottomActiveTab = Math.Clamp(tab, 0, 2);
    }

    public void NextBottomTab()
    {
        _bottomActiveTab = (_bottomActiveTab + 1) % 3;
    }

    public void PreviousBottomTab()
    {
        _bottomActiveTab = (_bottomActiveTab - 1 + 3) % 3;
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

        _mainLayout["Left"].Update(panel);
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

        _mainLayout["Right"].Update(panel);
    }

    public event Action? OnLog;

    public void AddLog(string message)
    {
        LogViewer.AddLog(message);
        OnLog?.Invoke();
    }

    public void AddEasyDotnetLog(string message)
    {
        EasyDotnetOutputViewer.AddLog(message);
        OnLog?.Invoke(); // Reuse OnLog to trigger refresh, or rename/create generic refresh event if needed. 
                         // Assuming OnLog just triggers a repaint or is used for notifications.
                         // Checking usages of OnLog... "AppCli.OnLog += layout.AddLog;" and "AppCli.OnLog += layout.AddLog;"
                         // Actually, OnLog is an event IN AppLayout. It seems it signals "something changed, please redraw".
                         // Wait, in Program.cs: AppCli.OnLog += layout.AddLog;
                         // But AppLayout also has "public event Action? OnLog;". 
                         // And AddLog invokes OnLog?.Invoke().
                         // It seems slightly circular or I'm misreading where AppLayout.OnLog is consumed.
                         // Let's assume OnLog event on AppLayout is observed by AppHost or similar to trigger render loop?
                         // I will check AppHost.cs to be sure.
    }

    public void UpdateBottom(int width, int height)
    {
        var isActive = _activePanel == 2;
        string logTab = _bottomActiveTab == 0 ? "[green]Log[/]" : "[dim]Log[/]";
        string testTab = _bottomActiveTab == 1 ? "[green]Test Output[/]" : "[dim]Test Output[/]";
        string ednTab = _bottomActiveTab == 2 ? "[green]EasyDotnet Output[/]" : "[dim]EasyDotnet Output[/]";

        var header = isActive
            ? $"[green][[3]][/]-{logTab} - {testTab} - {ednTab}"
            : $"[dim][[3]][/]-{logTab} - {testTab} - {ednTab}";

        IRenderable content = _bottomActiveTab switch
        {
            0 => LogViewer.GetContent(height - 2, width, isActive),
            1 => TestOutputViewer.GetContent(height - 2, width, isActive),
            2 => EasyDotnetOutputViewer.GetContent(height - 2, width, isActive),
            _ => new Markup("")
        };

        var panel = new Panel(content)
            .Header(header)
            .Border(BoxBorder.Rounded)
            .BorderColor(isActive ? Color.Green : Color.Grey)
            .Padding(0, 0, 0, 0)
            .Expand();

        _mainLayout["Bottom"].Update(panel);
    }

    public void UpdateFooter(IEnumerable<KeyBinding> bindings)
    {
        var footerBindings = bindings.Where(b => b.ShowInBottomBar).ToList();
        if (footerBindings.Count == 0)
        {
            _rootLayout["Footer"].Update(new Markup(""));
            return;
        }

        var segments = footerBindings.Select(b => $"{Markup.Escape(b.Description)}: [blue]{Markup.Escape(b.Label)}[/]");
        var footerMarkup = " " + string.Join(" [dim]|[/] ", segments);
        _rootLayout["Footer"].Update(new Markup(footerMarkup));
    }
}
