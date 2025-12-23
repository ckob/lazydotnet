namespace lazydotnet.UI.Components;

public class TabbedPane
{
    private readonly string[] _tabNames;
    private int _activeTab = 0;

    public TabbedPane(params string[] tabNames)
    {
        if (tabNames.Length == 0)
            throw new ArgumentException("At least one tab is required", nameof(tabNames));
        _tabNames = tabNames;
    }

    public int ActiveTab => _activeTab;
    public int TabCount => _tabNames.Length;
    public string ActiveTabName => _tabNames[_activeTab];
    public IReadOnlyList<string> TabNames => _tabNames;

    public void NextTab()
    {
        _activeTab = (_activeTab + 1) % _tabNames.Length;
    }

    public void PreviousTab()
    {
        _activeTab = (_activeTab - 1 + _tabNames.Length) % _tabNames.Length;
    }

    public void SetTab(int index)
    {
        if (index >= 0 && index < _tabNames.Length)
            _activeTab = index;
    }

    public bool IsActiveTab(int index) => _activeTab == index;
}
