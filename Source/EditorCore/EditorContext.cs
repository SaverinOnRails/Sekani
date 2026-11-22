namespace Sekani.EditorCore;

public interface IEditorContext
{
    public string? MotionCountBuffer { get; }

    public void HandleCommand(string command);

    public Mode Mode { get; set; }

    public Clipboard Clipboard { get; }

    public ISmallInputPromptContext? SmallInputPromptContext { get; set; }

    void RaisePanelClosed(object? sender);

    event EventHandler? PanelClosed;

    public DocumentView? ActiveDocumentView { get; set; }

}

public interface ISmallInputPromptContext
{
    public void Commit();

}

public class SmallInputPromptSearchContext : ISmallInputPromptContext
{
    public SmallInputPromptSearchContext(Range originalRange, SmallInputPromptSearchType type, SelectionSet originalSelectionSet)
    {
        OriginalCaretRange = originalRange;
        Type = type;
        OriginalSelectionSet = originalSelectionSet;
    }
    public string? SearchTerm { get; set; }

    public SmallInputPromptSearchType Type { get; init; }

    public event EventHandler<string?>? SearchTermUpdated;

    public void RaiseSearchTermUpdated() => SearchTermUpdated?.Invoke(this, SearchTerm);

    public void Commit()
    {
        Committed = true;
    }

    public Range OriginalCaretRange { get; private set; }

    public SelectionSet OriginalSelectionSet { get; private set; }

    public bool Committed { get; private set; } = false;

    public List<SearchHit> SearchHits { get; set; } = [];

    public int SearchHitOriginalIndex { get; set; }
}

public enum SmallInputPromptSearchType
{
    Buffer,
    Selection
}


