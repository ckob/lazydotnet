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
            new Layout("LeftContainer").SplitRows(
                new Layout("Workspace").Size(3),
                new Layout("Left")
            ).Ratio(33),
            new Layout("Right").Ratio(67)
        ).Ratio(TopRatio),
        new Layout("Bottom").Ratio(BottomRatio)
    );

    private readonly Layout _rootLayout;
    public SearchState SearchState { get; } = new();

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

    public void SetLogViewerSearchCallback(Action callback) => LogViewer.OnSearchRequested = callback;

    public Layout GetRoot() => _rootLayout;

    public int ActivePanel { get; private set; } = 2;

    public void SetActivePanel(int panel)
    {
        ActivePanel = Math.Clamp(panel, 0, 3);
    }

    public void UpdateWorkspace(IRenderable renderable)
    {
        var isActive = ActivePanel == 1;
        var header = isActive
            ? "[green][[1]][/]-[green]Workspace[/]"
            : "[dim][[1]][/]-[green]Workspace[/]";
        var panel = new Panel(renderable)
            .Header(header)
            .Border(BoxBorder.Rounded)
            .BorderColor(isActive ? Color.Green : Color.Grey)
            .Padding(0, 0, 0, 0)
            .Expand();

        _mainLayout["Workspace"].Update(panel);
    }

    public void UpdateLeft(IRenderable renderable)
    {
        var isActive = ActivePanel == 2;
        var header = isActive
            ? "[green][[2]][/]-[green]Explorer[/]"
            : "[dim][[2]][/]-[green]Explorer[/]";
        var panel = new Panel(renderable)
            .Header(header)
            .Border(BoxBorder.Rounded)
            .BorderColor(isActive ? Color.Green : Color.Grey)
            .Padding(0, 0, 0, 0)
            .Expand();

        _mainLayout["Left"].Update(panel);
    }

    public void UpdateRight(IRenderable renderable, string headerText)
    {
        var isActive = ActivePanel == 0;
        var header = isActive ? $"[green][[0]][/]-{headerText}" : $"[dim][[0]][/]-{headerText}";

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
        var isActive = ActivePanel == 3;
        var logTab = "[green]Log[/]";

        if (!LogViewer.IsStreaming)
        {
            logTab = isActive ? "[yellow]Log (Esc to resume Stream)[/]" : "[yellow]Log (Paused)[/]";
        }

        var header = isActive
            ? $"[green][[3]][/]-{logTab}"
            : $"[dim][[3]][/]-{logTab}";

        var content = LogViewer.GetContent(height - 2, width, isActive);

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
        if (SearchState.IsActive)
        {
            _rootLayout["Footer"].Update(new Markup(SearchState.GetStatusText()));
            return;
        }

        var footerBindings = bindings.Where(b => b.ShowInBottomBar).ToList();

        var rawVersion = ThisAssembly.Info.InformationalVersion;
        var version = rawVersion.Contains('+') ? rawVersion.Split('+')[0] : rawVersion;
        var versionText = $"v{version}";

        if (footerBindings.Count == 0)
        {
            _rootLayout["Footer"].Update(new Markup(versionText));
            return;
        }

        var segments = footerBindings.Select(b => $"{Markup.Escape(b.Description)}: [blue]{Markup.Escape(b.Label)}[/]");
        var keybindingsText = " " + string.Join(" [dim]|[/] ", segments);

        var consoleWidth = Console.WindowWidth;
        var visibleLength = Markup.Remove(keybindingsText).Length;
        var paddingSize = Math.Max(0, consoleWidth - visibleLength - versionText.Length - 1);

        var footerContent = keybindingsText + new string(' ', paddingSize) + versionText + " ";
        _rootLayout["Footer"].Update(new Markup(footerContent));
    }
}
