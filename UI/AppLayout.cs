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
            _rootLayout["Main"].Update(new Overlay(_mainLayout, modalContent));
        }
        else
        {
            _rootLayout["Main"].Update(_mainLayout);
        }
    }

    public static int GetBottomHeight(int totalHeight)
    {
        var availableHeight = totalHeight - 1;
        var topHeight = availableHeight * TopRatio / (TopRatio + BottomRatio);
        return availableHeight - topHeight;
    }
    public LogViewer LogViewer { get; } = new();
    public TestOutputViewer TestOutputViewer { get; } = new();

    public Layout GetRoot() => _rootLayout;

    public int ActivePanel { get; private set; }

    public int BottomActiveTab { get; private set; }

    private int _detailsActiveTab;

    public void SetActivePanel(int panel)
    {
        ActivePanel = Math.Clamp(panel, 0, 2);
    }

    public void NextBottomTab()
    {
        BottomActiveTab = (BottomActiveTab + 1) % 2;
    }

    public void PreviousBottomTab()
    {
        BottomActiveTab = (BottomActiveTab - 1 + 2) % 2;
    }

    public void SetDetailsActiveTab(int tab)
    {
        _detailsActiveTab = Math.Clamp(tab, 0, 2);
    }

    public void UpdateLeft(IRenderable renderable)
    {
        var isActive = ActivePanel == 0;
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
        var isActive = ActivePanel == 1;
        var refsTab = _detailsActiveTab == 0 ? "[green]Project References[/]" : "[dim]Project References[/]";
        var nugetTab = _detailsActiveTab == 1 ? "[green]NuGets[/]" : "[dim]NuGets[/]";
        var testsTab = _detailsActiveTab == 2 ? "[green]Tests[/]" : "[dim]Tests[/]";

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

    public void UpdateBottom(int width, int height)
    {
        var isActive = ActivePanel == 2;
        var logTab = BottomActiveTab == 0 ? "[green]Log[/]" : "[dim]Log[/]";
        var testTab = BottomActiveTab == 1 ? "[green]Test Output[/]" : "[dim]Test Output[/]";

        var header = isActive
            ? $"[green][[3]][/]-{logTab} - {testTab}"
            : $"[dim][[3]][/]-{logTab} - {testTab}";

        var content = BottomActiveTab switch
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
