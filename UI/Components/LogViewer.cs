using Spectre.Console;
using Spectre.Console.Rendering;

namespace lazydotnet.UI.Components;

public class LogViewer
{
    private readonly List<string> _logs = [];
    private readonly Lock _lock = new();

    private int _scrollOffset = 0;

    private int _selectedIndex = -1;

    private const int MaxLogLines = 1000;

    public void AddLog(string message)
    {
        lock (_lock)
        {
            _logs.Add(message);
            if (_logs.Count > MaxLogLines)
            {
                _logs.RemoveAt(0);

                if (_selectedIndex >= 0) _selectedIndex = Math.Max(-1, _selectedIndex - 1);
                if (_scrollOffset > 0) _scrollOffset--;
            }
        }
    }

    public void MoveUp()
    {
        lock (_lock)
        {
            if (_logs.Count == 0) return;


            if (_selectedIndex == -1) _selectedIndex = _logs.Count - 1;
            else if (_selectedIndex > 0) _selectedIndex--;
        }
    }

    public void MoveDown()
    {
        lock (_lock)
        {
            if (_logs.Count == 0 || _selectedIndex == -1) return;

            if (_selectedIndex < _logs.Count - 1)
            {
                _selectedIndex++;
            }
            else
            {
                _selectedIndex = -1;
            }
        }
    }

    public void PageUp(int pageSize)
    {
        lock (_lock)
        {
            if (_logs.Count == 0) return;
            if (_selectedIndex == -1) _selectedIndex = _logs.Count - 1;

            _selectedIndex = Math.Max(0, _selectedIndex - pageSize);
        }
    }

    public void PageDown(int pageSize)
    {
        lock (_lock)
        {
            if (_logs.Count == 0 || _selectedIndex == -1) return;

            if (_selectedIndex + pageSize >= _logs.Count - 1)
                _selectedIndex = -1;
            else
                _selectedIndex += pageSize;
        }
    }

    public IRenderable GetContent(int height, bool isActive)
    {
        lock (_lock)
        {

            var visibleRows = Math.Max(1, height - 2);

            if (_selectedIndex == -1)
            {
                _scrollOffset = Math.Max(0, _logs.Count - visibleRows);
            }
            else
            {
                if (_selectedIndex < _scrollOffset)
                {
                    _scrollOffset = _selectedIndex;
                }
                else if (_selectedIndex >= _scrollOffset + visibleRows)
                {
                    _scrollOffset = _selectedIndex - visibleRows + 1;
                }
            }


            _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, Math.Max(0, _logs.Count - visibleRows)));


            int start = _scrollOffset;
            int end = Math.Min(_logs.Count, _scrollOffset + visibleRows);


            var table = new Table()
                .Border(TableBorder.None)
                .HideHeaders()
                .Expand()
                .AddColumn(new TableColumn("Log").NoWrap());

            for (int i = start; i < end; i++)
            {
                var msg = _logs[i];
                var isSelected = i == _selectedIndex;

                if (isSelected)
                {
                    string style = isActive ? "[black on white]" : "[black on silver]";
                    table.AddRow(new Markup($"{style}{msg}[/]"));
                }
                else
                {
                    table.AddRow(new Markup(msg));
                }
            }

            string header = isActive ? "[green]Log (Active)[/]" : "[dim]Log[/]";
            if (_selectedIndex != -1) header += " [yellow](Paused)[/]";

            return new Panel(table)
                .Header(header)
                .Border(BoxBorder.Rounded)
                .BorderColor(isActive ? Color.Green : Color.Grey)
                .Padding(0, 0, 0, 0)
                .Expand();
        }
    }
}
