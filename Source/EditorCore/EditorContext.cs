namespace Sekani.EditorCore;

public interface IEditorContext
{
    public string? MotionCountBuffer { get; }

    public void HandleCommand(string command);

    public Mode Mode { get; set; }

    public Clipboard Clipboard { get; }

    public ISmallInputPromptContext? SmallInputPromptContext { get; set; }

    public IBigInputPromptContext? BigInputPromptContext { get; set; }

    void RaisePanelClosed(object? sender);

    event EventHandler? PanelClosed;

    public DocumentView? ActiveDocumentView { get; set; }

    public DocumentManager DocumentManager { get; }

    public void OpenBufferPicker();

    public void OpenProjectFilePicker();

}

public interface IInputPromptContext
{
    public void Commit();
}
public interface ISmallInputPromptContext : IInputPromptContext;

public interface IBigInputPromptContext : IInputPromptContext;

public class BigInputPrompBufferPickerContext : IBigInputPromptContext
{
    public IReadOnlyList<string> OpenDocuments { get; init; }
    public string SelectedFile { get; set; } = "";
    private IEditorContext _editorContext;
    public BigInputPrompBufferPickerContext(List<string> openDocuments, IEditorContext context)
    {
        OpenDocuments = openDocuments;
        _editorContext = context;
    }
    public void Commit()
    {
        _editorContext.DocumentManager.OpenDocument(SelectedFile);
    }
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

    public event EventHandler? SearchHitsUpdated;

    public bool Committed { get; private set; } = false;

    private List<SearchHit> _searchHits = [];
    public List<SearchHit> SearchHits
    {
        get => _searchHits;
        set
        {
            _searchHits = value;
            SearchHitsUpdated?.Invoke(this, EventArgs.Empty);
        }
    }
    public int SearchHitOriginalIndex { get; set; }
}

public enum SmallInputPromptSearchType
{
    Buffer,
    Selection
}


