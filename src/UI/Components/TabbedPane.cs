namespace lazydotnet.UI.Components;

public class TabbedPane
{
    private readonly string[] _tabNames;

    public TabbedPane(params string[] tabNames)
    {
        if (tabNames.Length == 0)
            throw new ArgumentException("At least one tab is required", nameof(tabNames));
        _tabNames = tabNames;
    }

    public int ActiveTab { get; private set; }

    public void NextTab()
    {
        ActiveTab = (ActiveTab + 1) % _tabNames.Length;
    }

    public void PreviousTab()
    {
        ActiveTab = (ActiveTab - 1 + _tabNames.Length) % _tabNames.Length;
    }

    public void SetActiveTab(int index)
    {
        if (index >= 0 && index < _tabNames.Length)
        {
            ActiveTab = index;
        }
    }
}
