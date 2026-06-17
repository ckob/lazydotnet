using lazydotnet.Core;
using Spectre.Console;
using Spectre.Console.Rendering;
using lazydotnet.UI.Components;
using lazydotnet.Services;
using lazydotnet.Core.Configuration;
using Microsoft.Extensions.Options;

namespace lazydotnet.UI;

public class ProjectDetailsPane : IKeyBindable, ISearchable
{
    private readonly TabbedPane _tabs;
    private readonly NuGetDetailsTab _nugetTab;
    private readonly TestDetailsTab _testsTab;
    private readonly ExecutionTab _executionTab;
    private readonly List<IProjectTab> _tabInstances = [];

    private string? _currentProjectPath;
    private string? _currentProjectName;
    private CancellationTokenSource? _loadCts;

    public Action<string>? LogAction
    {
        get => _nugetTab.LogAction;
        set => _nugetTab.LogAction = value;
    }

    public Action? OnSearchRequested { get; set; }
    public Action? RequestRefresh { get; set; }
    public Action<Modal>? RequestModal { get; set; }
    public Action<string>? RequestSelectProject { get; set; }

    public ProjectDetailsPane(SolutionService solutionService, IEditorService editorService, IOptions<LazydotnetSettings> options)
    {
        var settings = options.Value.DetailsPane;

        _nugetTab = new NuGetDetailsTab();
        var refsTab = new ProjectReferencesTab(solutionService, editorService);
        _testsTab = new TestDetailsTab(editorService);
        _executionTab = new ExecutionTab();

        var allTabs = new List<(IProjectTab Tab, ITabSettings Config)>
        {
            (refsTab, settings.ReferencesTab),
            (_nugetTab, settings.NuGetsTab),
            (_testsTab, settings.TestsTab),
            (_executionTab, settings.ExecutionTab)
        };

        var orderedTabs = allTabs
            .Where(t => t.Config.Enabled)
            .OrderBy(t => t.Config.Position)
            .ToList();

        foreach (var (Tab, _) in orderedTabs)
        {
            _tabInstances.Add(Tab);
        }

        foreach (var tab in _tabInstances)
        {
            tab.RequestRefresh = () => RequestRefresh?.Invoke();
            tab.RequestModal = m => RequestModal?.Invoke(m);
            tab.RequestSelectProject = p => RequestSelectProject?.Invoke(p);
        }

        _tabs = new TabbedPane([.. _tabInstances.Select(t => t.Title)]);
    }

    public int ActiveTab => _tabs.ActiveTab;
    public string ActiveTabTitle => _tabInstances[_tabs.ActiveTab].Title;

    public void ActivateExecutionTab()
    {
        var index = _tabInstances.IndexOf(_executionTab);
        if (index >= 0)
        {
            _tabs.SetActiveTab(index);
            TriggerLoad();
        }
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
        if (activeTab?.IsLoaded(_currentProjectPath) != false)
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
        }, k => k.KeyChar == ']' || k is { Key: ConsoleKey.Tab, Modifiers: 0 }, false);

        yield return new KeyBinding("/", "search", () =>
        {
            OnSearchRequested?.Invoke();
            return Task.CompletedTask;
        }, k => k is { KeyChar: '/', Modifiers: 0 });

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

    public void StartSearch()
    {
        if (_tabInstances[_tabs.ActiveTab] is ISearchable searchable)
        {
            searchable.StartSearch();
        }
    }

    public void ExitSearch()
    {
        foreach (var tab in _tabInstances)
        {
            if (tab is ISearchable searchable)
            {
                searchable.ExitSearch();
            }
        }
    }

    public List<int> UpdateSearchQuery(string query)
    {
        if (_tabInstances[_tabs.ActiveTab] is ISearchable searchable)
        {
            return searchable.UpdateSearchQuery(query);
        }
        return [];
    }

    public void NextSearchMatch()
    {
        if (_tabInstances[_tabs.ActiveTab] is ISearchable searchable)
        {
            searchable.NextSearchMatch();
        }
    }

    public void PreviousSearchMatch()
    {
        if (_tabInstances[_tabs.ActiveTab] is ISearchable searchable)
        {
            searchable.PreviousSearchMatch();
        }
    }
}
