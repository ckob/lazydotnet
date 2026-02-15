using Spectre.Console;

namespace lazydotnet.Core;

public enum SearchMode
{
    Input,      // Typing the search query
    Navigation  // Navigating between matches after search executed
}

public class PanelSearchState
{
    public bool IsActive { get; set; }
    public SearchMode Mode { get; set; } = SearchMode.Input;
    public string Query { get; set; } = string.Empty;
    public List<int> MatchIndices { get; set; } = [];
    public int CurrentMatchIndex { get; set; } = -1;
}

public class SearchState
{
    private readonly Dictionary<int, PanelSearchState> _panelStates = [];
    private int _currentSearchPanel = -1;

    public bool IsActive => _currentSearchPanel >= 0 && GetCurrentState().IsActive;
    public SearchMode Mode => GetCurrentState().Mode;
    public string Query => GetCurrentState().Query;
    public List<int> MatchIndices => GetCurrentState().MatchIndices;
    public int CurrentMatchIndex => GetCurrentState().CurrentMatchIndex;
    public int ActivePanel => _currentSearchPanel;

    public event Action? OnSearchChanged;
    public event Action? OnExitSearch;

    private PanelSearchState GetCurrentState()
    {
        if (_currentSearchPanel < 0)
            return new PanelSearchState();

        if (!_panelStates.TryGetValue(_currentSearchPanel, out var state))
        {
            state = new PanelSearchState();
            _panelStates[_currentSearchPanel] = state;
        }
        return state;
    }

    private PanelSearchState GetOrCreateState(int panel)
    {
        if (!_panelStates.TryGetValue(panel, out var state))
        {
            state = new PanelSearchState();
            _panelStates[panel] = state;
        }
        return state;
    }

    public bool HasStateForPanel(int panel)
    {
        return _panelStates.ContainsKey(panel) && _panelStates[panel].Query.Length > 0;
    }

    public void SwitchToPanel(int panel)
    {
        _currentSearchPanel = panel;
        OnSearchChanged?.Invoke();
    }

    public void StartSearch(int panel)
    {
        _currentSearchPanel = panel;
        var state = GetOrCreateState(panel);
        state.IsActive = true;
        state.Mode = SearchMode.Input;
        state.Query = string.Empty;
        state.MatchIndices = [];
        state.CurrentMatchIndex = -1;
        OnSearchChanged?.Invoke();
    }

    public void RestartSearch(int panel)
    {
        // If switching to a different panel, deactivate current and activate new
        if (panel != _currentSearchPanel)
        {
            if (_currentSearchPanel >= 0 && _panelStates.TryGetValue(_currentSearchPanel, out var oldState))
            {
                oldState.IsActive = false;
            }
            _currentSearchPanel = panel;
        }

        var state = GetOrCreateState(panel);
        state.IsActive = true;
        state.Mode = SearchMode.Input;
        state.Query = string.Empty;
        state.MatchIndices = [];
        state.CurrentMatchIndex = -1;
        OnSearchChanged?.Invoke();
    }

    public void ExitSearch()
    {
        if (_currentSearchPanel >= 0 && _panelStates.TryGetValue(_currentSearchPanel, out var state))
        {
            state.IsActive = false;
            state.Mode = SearchMode.Input;
            state.Query = string.Empty;
            state.MatchIndices = [];
            state.CurrentMatchIndex = -1;
        }
        _currentSearchPanel = -1;
        OnExitSearch?.Invoke();
    }

    public void ExecuteSearch()
    {
        var state = GetCurrentState();
        state.Mode = SearchMode.Navigation;
        OnSearchChanged?.Invoke();
    }

    public void AppendChar(char c)
    {
        var state = GetCurrentState();
        if (!state.IsActive || state.Mode != SearchMode.Input) return;
        state.Query += c;
        OnSearchChanged?.Invoke();
    }

    public void Backspace()
    {
        var state = GetCurrentState();
        if (!state.IsActive || state.Mode != SearchMode.Input || state.Query.Length == 0) return;
        state.Query = state.Query[..^1];
        OnSearchChanged?.Invoke();
    }

    public void SetMatches(List<int> indices)
    {
        var state = GetCurrentState();
        state.MatchIndices = indices;
        state.CurrentMatchIndex = indices.Count > 0 ? 0 : -1;
    }

    public int? GetCurrentMatch()
    {
        var state = GetCurrentState();
        if (state.CurrentMatchIndex < 0 || state.CurrentMatchIndex >= state.MatchIndices.Count)
            return null;
        return state.MatchIndices[state.CurrentMatchIndex];
    }

    public void NextMatch()
    {
        var state = GetCurrentState();
        if (state.MatchIndices.Count == 0) return;
        state.CurrentMatchIndex = (state.CurrentMatchIndex + 1) % state.MatchIndices.Count;
    }

    public void PreviousMatch()
    {
        var state = GetCurrentState();
        if (state.MatchIndices.Count == 0) return;
        state.CurrentMatchIndex = (state.CurrentMatchIndex - 1 + state.MatchIndices.Count) % state.MatchIndices.Count;
    }

    public string GetStatusText()
    {
        var state = GetCurrentState();

        if (!state.IsActive)
            return string.Empty;

        if (state.Mode == SearchMode.Input)
        {
            return $"Search: {Markup.Escape(state.Query)}_";
        }

        // Navigation mode
        if (state.MatchIndices.Count == 0)
            return $"Search: No matches for '{Markup.Escape(state.Query)}' [dim]esc: Exit[/]";

        return $"Search: matches for '{Markup.Escape(state.Query)}' ({state.CurrentMatchIndex + 1} of {state.MatchIndices.Count}) [blue]n[/]: Next, [blue]N[/]: Previous, [dim]esc[/]: Exit";
    }
}
