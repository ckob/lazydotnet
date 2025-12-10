using Spectre.Console;
using Spectre.Console.Rendering;

namespace lazydotnet.UI;

public class AppLayout
{
    private readonly Layout _rootLayout;
    private readonly Queue<string> _logs = new();
    private readonly object _logLock = new();
    private const int MaxLogLines = 100;

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

    public void UpdateLeft(IRenderable renderable)
    {

        var panel = new Panel(renderable)
            .Header("[bold blue]Explorer[/]")
            .Border(BoxBorder.Rounded)
            .Expand();
            
        _rootLayout["Left"].Update(panel);
    }

    public void UpdateRight(IRenderable renderable)
    {
        var panel = new Panel(renderable)
            .Header("[bold]Details[/]")
            .Border(BoxBorder.Rounded)
            .Expand();
            
        _rootLayout["Right"].Update(panel);
    }

    public event Action? OnLog;

    public void AddLog(string message)
    {
        lock(_logLock)
        {
            _logs.Enqueue(message);
            while (_logs.Count > MaxLogLines) _logs.Dequeue();
        }
        OnLog?.Invoke();
    }
    
    public void UpdateBottom()
    {
        string logContent;
        lock(_logLock)
        {
            var panelWidth = Console.WindowWidth - 4;
            var visibleLogs = new List<string>();
            int currentHeight = 0;
            int maxContentHeight = 10;

            for (int i = _logs.Count - 1; i >= 0; i--)
            {
                var msg = _logs.ElementAt(i);
                var rawText = Markup.Remove(msg);
                
                int linesNeeded = (int)Math.Max(1, Math.Ceiling((double)rawText.Length / Math.Max(1, panelWidth)));

                if (currentHeight + linesNeeded > maxContentHeight)
                {
                    if (visibleLogs.Count == 0) 
                    {
                         visibleLogs.Add(msg);
                    }
                    break;
                }

                currentHeight += linesNeeded;
                visibleLogs.Insert(0, msg);
            }
            
            logContent = string.Join("\n", visibleLogs);
        }
        
        var panel = new Panel(new Markup(logContent))
            .Header("[bold]Log[/]")
            .Border(BoxBorder.Rounded)
            .Expand();
            
        _rootLayout["Bottom"].Update(panel);
    }
}
