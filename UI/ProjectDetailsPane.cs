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
    private readonly List<IProjectTab> _tabInstances = new();

    private string? _currentProjectPath;

    public Action<string>? LogAction 
    { 
        get => _nugetTab.LogAction; 
        set => _nugetTab.LogAction = value; 
    }

    public ProjectDetailsPane(NuGetService nugetService, SolutionService solutionService)
    {
        _nugetTab = new NuGetDetailsTab(nugetService);
        _refsTab = new ProjectReferencesTab(solutionService);
        
        _tabInstances.Add(_nugetTab);
        _tabInstances.Add(_refsTab);
        
        _tabs = new TabbedPane(_nugetTab.Title, _refsTab.Title);
    }

    public int ActiveTab => _tabs.ActiveTab;

    public void NextTab() => _tabs.NextTab();

    public void PreviousTab() => _tabs.PreviousTab();

    public void MoveUp() => _tabInstances[_tabs.ActiveTab].MoveUp();

    public void MoveDown() => _tabInstances[_tabs.ActiveTab].MoveDown();

    public string? GetCounter() => _tabInstances[_tabs.ActiveTab].GetScrollIndicator();

    public void ClearData()
    {
        foreach (var tab in _tabInstances) tab.ClearData();
    }

    public void ClearForNonProject()
    {
        ClearData();
        _currentProjectPath = null;
    }

    public Task LoadProjectDataAsync(string projectPath, string projectName)
    {
        _currentProjectPath = projectPath;
        
        // Load both tabs independently and do NOT await them together. 
        // We want them to update their state as they finish.
        // The main loop will call GetContent repeatedly to show progress.
        
        _ = _nugetTab.LoadAsync(projectPath, projectName);
        _ = _refsTab.LoadAsync(projectPath, projectName);
        
        return Task.CompletedTask;
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
