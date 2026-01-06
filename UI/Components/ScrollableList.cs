namespace lazydotnet.UI.Components;

public class ScrollableList<T>
{
    private List<T> _items = [];
    private int _selectedIndex = -1;
    private int _scrollOffset = 0;

    public IReadOnlyList<T> Items => _items;
    public int SelectedIndex => _selectedIndex;
    public int ScrollOffset => _scrollOffset;
    public int Count => _items.Count;
    public T? SelectedItem => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : default;

    public void SetItems(List<T> items)
    {
        _items = items;
        if (_selectedIndex >= _items.Count)
            _selectedIndex = _items.Count > 0 ? 0 : -1;
    }

    public void Reset()
    {
        _selectedIndex = -1;
        _scrollOffset = 0;
    }

    public void Clear()
    {
        _items.Clear();
        Reset();
    }

    public bool MoveUp()
    {
        if (_selectedIndex == -1 && _items.Count > 0)
        {
            _selectedIndex = _items.Count - 1;
            return true;
        }
        if (_selectedIndex > 0)
        {
            _selectedIndex--;
            return true;
        }
        return false;
    }

    public bool MoveDown()
    {
        if (_selectedIndex < _items.Count - 1)
        {
            _selectedIndex++;
            return true;
        }
        return false;
    }

    public void EnsureVisible(int visibleRows)
    {
        if (visibleRows <= 0) return;

        if (_selectedIndex == -1)
        {
            _scrollOffset = 0;
            return;
        }

        if (_selectedIndex < _scrollOffset)
            _scrollOffset = _selectedIndex;


        if (_selectedIndex >= _scrollOffset + visibleRows)
            _scrollOffset = _selectedIndex - visibleRows + 1;


        if (_scrollOffset < 0) _scrollOffset = 0;
        int maxOffset = Math.Max(0, _items.Count - visibleRows);
        if (_scrollOffset > maxOffset) _scrollOffset = maxOffset;
    }

    public (int start, int end) GetVisibleRange(int visibleRows)
    {
        EnsureVisible(visibleRows);
        int start = _scrollOffset;
        int end = Math.Min(_scrollOffset + visibleRows, _items.Count);
        return (start, end);
    }

    public string? GetScrollIndicator(int visibleRows)
    {
        if (_items.Count <= visibleRows) return null;
        return $"{_selectedIndex + 1} of {_items.Count}";
    }
}
