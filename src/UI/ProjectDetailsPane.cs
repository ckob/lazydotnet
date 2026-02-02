using lazydotnet.Core;
using Spectre.Console;
using Spectre.Console.Rendering;
using lazydotnet.UI.Components;
using lazydotnet.Services;

namespace lazydotnet.UI;

public class ProjectDetailsPane : IKeyBindable
{
    private readonly TabbedPane _tabs;
    private readonly NuGetDetailsTab _nugetTab;
    private readonly TestDetailsTab _testsTab;
    private readonly List<IProjectTab> _tabInstances = [];

    private string? _currentProjectPath;
    private string? _currentProjectName;
    private CancellationTokenSource? _loadCts;

    public Action<string>? LogAction
    {
        get => _nugetTab.LogAction;
        set => _nugetTab.LogAction = value;
    }

    public Action? RequestRefresh { get; set; }
    public Action<Modal>? RequestModal { get; set; }
    public Action<string>? RequestSelectProject { get; set; }

    public ProjectDetailsPane(SolutionService solutionService, IEditorService editorService)
    {
        _nugetTab = new NuGetDetailsTab();
        var refsTab = new ProjectReferencesTab(solutionService, editorService);
        _testsTab = new TestDetailsTab(editorService);
        var executionTab = new ExecutionTab();

        _tabInstances.Add(refsTab);
        _tabInstances.Add(_nugetTab);
        _tabInstances.Add(_testsTab);
        _tabInstances.Add(executionTab);

        foreach (var tab in _tabInstances)
        {
            tab.RequestRefresh = () => RequestRefresh?.Invoke();
            tab.RequestModal = m => RequestModal?.Invoke(m);
            tab.RequestSelectProject = p => RequestSelectProject?.Invoke(p);
        }

        _tabs = new TabbedPane(_tabInstances.Select(t => t.Title).ToArray());
    }

    public int ActiveTab => _tabs.ActiveTab;

    public void ActivateExecutionTab()
    {
        _tabs.SetActiveTab(3);
        TriggerLoad();
    }

    public TestNode? GetSelectedTestNode()
    {
        return _testsTab.GetSelectedNode();
    }

    private void NextTab()
    {
        _tabs.NextTab();
        TriggerLoad();
    }

    private void PreviousTab()
    {
        _tabs.PreviousTab();
        TriggerLoad();
    }

    public bool OnTick() => _tabInstances[_tabs.ActiveTab].OnTick();

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
        if (_currentProjectPath == projectPath) return Task.CompletedTask;

        _currentProjectPath = projectPath;
        _currentProjectName = projectName;

        TriggerLoad();

        return Task.CompletedTask;
    }

    public async Task ReloadCurrentTabDataAsync()
    {
        if (_currentProjectPath != null && _currentProjectName != null)
        {
            await _tabInstances[_tabs.ActiveTab].LoadAsync(_currentProjectPath, _currentProjectName, force: true);
            RequestRefresh?.Invoke();
        }
    }

    private void TriggerLoad()
    {
        if (_currentProjectPath == null || _currentProjectName == null)
            return;

        var activeTab = _tabInstances[_tabs.ActiveTab];
        if (activeTab.IsLoaded(_currentProjectPath))
            return;

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        var path = _currentProjectPath;
        var name = _currentProjectName;

        _ = Task.Run(async () =>
        {
            try
            {
                await activeTab.LoadAsync(path, name);
                if (!token.IsCancellationRequested)
                {
                    RequestRefresh?.Invoke();
                }
            }
            catch
            {
                // ignored
            }
        }, token);
    }

    public IEnumerable<KeyBinding> GetKeyBindings()
    {
        yield return new KeyBinding("[", "prev tab", () =>
        {
            PreviousTab();
            return Task.CompletedTask;
        }, k => k.KeyChar == '[', false);

        yield return new KeyBinding("]", "next tab", () =>
        {
            NextTab();
            return Task.CompletedTask;
        }, k => k.KeyChar == ']' || k.Key == ConsoleKey.Tab, false);

        var activeTab = _tabInstances[_tabs.ActiveTab];
        foreach (var b in activeTab.GetKeyBindings())
        {
            yield return b;
        }
    }

    public string GetHeader()
    {
        var headers = new List<string>();
        for (var i = 0; i < _tabInstances.Count; i++)
        {
            var title = _tabInstances[i].Title;
            headers.Add(i == _tabs.ActiveTab ? $"[green]{Markup.Escape(title)}[/]" : $"[dim]{Markup.Escape(title)}[/]");
        }

        return string.Join(" - ", headers);
    }

    public IRenderable GetContent(int availableHeight, int availableWidth, bool isActive)
    {
        var activeInstance = _tabInstances[_tabs.ActiveTab];
        return activeInstance.GetContent(availableHeight, availableWidth, isActive);
    }
}
