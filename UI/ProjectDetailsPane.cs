using lazydotnet.Core;
using Spectre.Console.Rendering;
using lazydotnet.UI.Components;
using lazydotnet.Services;

namespace lazydotnet.UI;

public class ProjectDetailsPane : IKeyBindable
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
    public Action<Modal>? RequestModal { get; set; }
    public Action<string>? RequestSelectProject { get; set; }

    public ProjectDetailsPane(SolutionService solutionService, NuGetService nuGetService, TestService testService, IEditorService editorService)
    {
        _nugetTab = new NuGetDetailsTab(nuGetService);
        _refsTab = new ProjectReferencesTab(solutionService, editorService);
        _testsTab = new TestDetailsTab(testService, editorService);

        _tabInstances.Add(_refsTab);
        _tabInstances.Add(_nugetTab);
        _tabInstances.Add(_testsTab);

        foreach (var tab in _tabInstances)
        {
            tab.RequestRefresh = () => RequestRefresh?.Invoke();
            tab.RequestModal = m => RequestModal?.Invoke(m);
            tab.RequestSelectProject = p => RequestSelectProject?.Invoke(p);
        }

        _tabs = new TabbedPane(_refsTab.Title, _nugetTab.Title, _testsTab.Title);
    }

    public int ActiveTab => _tabs.ActiveTab;

    public TestNode? GetSelectedTestNode()
    {
        return _testsTab.GetSelectedNode();
    }

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

    public IEnumerable<KeyBinding> GetKeyBindings()
    {
        yield return new KeyBinding("[", "prev tab", () =>
        {
            PreviousTab();
            return Task.CompletedTask;
        }, k => k.KeyChar == '[');

        yield return new KeyBinding("]", "next tab", () =>
        {
            NextTab();
            return Task.CompletedTask;
        }, k => k.KeyChar == ']' || k.Key == ConsoleKey.Tab);

        var activeTab = _tabInstances[_tabs.ActiveTab];
        foreach (var b in activeTab.GetKeyBindings())
        {
            yield return b;
        }
    }

    public async Task<bool> HandleInputAsync(ConsoleKeyInfo key, AppLayout layout)
    {
        var binding = GetKeyBindings().FirstOrDefault(b => b.Match(key));
        if (binding != null)
        {
            await binding.Action();
            layout.SetDetailsActiveTab(ActiveTab);
            return true;
        }
        return false;
    }

    public IRenderable GetContent(int availableHeight, int availableWidth)
    {
         var activeInstance = _tabInstances[_tabs.ActiveTab];
         return activeInstance.GetContent(availableHeight, availableWidth);
    }
}
