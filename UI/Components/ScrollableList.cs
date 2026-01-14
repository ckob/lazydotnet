namespace lazydotnet.UI.Components;

public class ScrollableList<T>
{
    private List<T> _items = [];

    public IReadOnlyList<T> Items => _items;
    public int SelectedIndex { get; private set; }

    private int ScrollOffset { get; set; }

    public int Count => _items.Count;
    public T? SelectedItem => SelectedIndex >= 0 && SelectedIndex < _items.Count ? _items[SelectedIndex] : default;

    public void SetItems(List<T> items)
    {
        _items = items;
        if (SelectedIndex == -1 || SelectedIndex >= _items.Count)
            SelectedIndex = _items.Count > 0 ? 0 : -1;
    }

    public void Clear()
    {
        _items.Clear();
        SelectedIndex = -1;
        ScrollOffset = 0;
    }

    public void MoveUp()
    {
        switch (SelectedIndex)
        {
            case -1 when _items.Count > 0:
                SelectedIndex = _items.Count - 1;
                return;
            case > 0:
                SelectedIndex--;
                break;
        }
    }

    public void MoveDown()
    {
        if (SelectedIndex < _items.Count - 1)
        {
            SelectedIndex++;
        }
    }

    public void Select(int index)
    {
        if (index >= 0 && index < _items.Count)
        {
            SelectedIndex = index;
        }
    }

    private void EnsureVisible(int visibleRows)
    {
        if (visibleRows <= 0) return;

        if (SelectedIndex == -1)
        {
            ScrollOffset = 0;
            return;
        }

        if (SelectedIndex < ScrollOffset)
            ScrollOffset = SelectedIndex;


        if (SelectedIndex >= ScrollOffset + visibleRows)
            ScrollOffset = SelectedIndex - visibleRows + 1;


        if (ScrollOffset < 0) ScrollOffset = 0;
        var maxOffset = Math.Max(0, _items.Count - visibleRows);
        if (ScrollOffset > maxOffset) ScrollOffset = maxOffset;
    }

    public (int start, int end) GetVisibleRange(int visibleRows)
    {
        EnsureVisible(visibleRows);
        var start = ScrollOffset;
        var end = Math.Min(ScrollOffset + visibleRows, _items.Count);
        return (start, end);
    }

    public string? GetScrollIndicator(int visibleRows)
    {
        return _items.Count <= visibleRows ? null : $"{SelectedIndex + 1} of {_items.Count}";
    }
}
