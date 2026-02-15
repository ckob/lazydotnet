namespace lazydotnet.Core;

public interface ISearchable
{
    void StartSearch();
    void ExitSearch();
    List<int> UpdateSearchQuery(string query);
    void NextSearchMatch();
    void PreviousSearchMatch();
}
