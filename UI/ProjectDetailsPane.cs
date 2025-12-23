using Spectre.Console;
using Spectre.Console.Rendering;
using lazydotnet.Services;
using lazydotnet.UI.Components;

namespace lazydotnet.UI;

public class ProjectDetailsPane
{
    private readonly TabbedPane _tabs;
    private readonly NuGetDetailsTab _nugetTab;
    private readonly ProjectReferencesTab _refsTab;
    private readonly TestDetailsTab _testsTab;
    private readonly List<IProjectTab> _tabInstances = [];

    private string? _currentProjectPath;
    private string? _currentProjectName;

    public Action<string>? LogAction
    {
        get => _nugetTab.LogAction;
        set => _nugetTab.LogAction = value;
    }

    public Action? RequestRefresh { get; set; }

    public ProjectDetailsPane()
    {
        _nugetTab = new NuGetDetailsTab();
        _refsTab = new ProjectReferencesTab();
        _testsTab = new TestDetailsTab();

        _tabInstances.Add(_refsTab);
        _tabInstances.Add(_nugetTab);
        _tabInstances.Add(_testsTab);

        _tabs = new TabbedPane(_refsTab.Title, _nugetTab.Title, _testsTab.Title);
    }

    public int ActiveTab => _tabs.ActiveTab;

    public void NextTab()
    {
        _tabs.NextTab();
        TriggerLoad();
    }

    public void PreviousTab()
    {
        _tabs.PreviousTab();
        TriggerLoad();
    }

    public void MoveUp() => _tabInstances[_tabs.ActiveTab].MoveUp();

    public void MoveDown() => _tabInstances[_tabs.ActiveTab].MoveDown();

    public string? GetScrollIndicator() => _tabInstances[_tabs.ActiveTab].GetScrollIndicator();

    public void ClearData()
    {
        foreach (var tab in _tabInstances) tab.ClearData();
    }

    public void ClearForNonProject()
    {
        ClearData();
        _currentProjectPath = null;
        _currentProjectName = null;
    }

    public Task LoadProjectDataAsync(string projectPath, string projectName)
    {
        _currentProjectPath = projectPath;
        _currentProjectName = projectName;

        TriggerLoad();

        return Task.CompletedTask;
    }

    private void TriggerLoad()
    {
        if (_currentProjectPath != null && _currentProjectName != null)
        {
             var task = _tabInstances[_tabs.ActiveTab].LoadAsync(_currentProjectPath, _currentProjectName);
             _ = Task.Run(async () =>
             {
                 try { await task; } catch { }
                 RequestRefresh?.Invoke();
             });
        }
    }

    public async Task<bool> HandleKey(ConsoleKeyInfo key)
    {
        return await _tabInstances[_tabs.ActiveTab].HandleKey(key);
    }

    public IRenderable GetContent(int availableHeight, int availableWidth)
    {
         var activeInstance = _tabInstances[_tabs.ActiveTab];
         return activeInstance.GetContent(availableHeight, availableWidth);
    }
}
