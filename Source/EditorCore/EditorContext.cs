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

    public ProjectManager ProjectManager { get; }


}

public interface IInputPromptContext
{
    public void Commit();
}
public interface ISmallInputPromptContext : IInputPromptContext;

public interface IBigInputPromptContext : IInputPromptContext;

public abstract class BigInputPromptListContext : IBigInputPromptContext
{
    public IReadOnlyList<string> Items { get; }
    public string Selected { get; set; } = "";
    protected readonly IEditorContext _editorContext;

    protected BigInputPromptListContext(IReadOnlyList<string> items, IEditorContext context)
    {
        Items = items;
        _editorContext = context;
    }

    public abstract void Commit();
}

public class BigInputPrompBufferPickerContext : BigInputPromptListContext
{
    public BigInputPrompBufferPickerContext(IReadOnlyList<string> openDocuments, IEditorContext context)
        : base(openDocuments, context) { }

    public override void Commit()
    {
        _editorContext.DocumentManager.OpenDocument(Selected);
    }
}

public class CommandPaletteContext : BigInputPromptListContext
{
    public CommandPaletteContext(IReadOnlyList<string> commands, IEditorContext context)
        : base(commands, context) { }

    public override void Commit()
    {
        _editorContext.HandleCommand(Selected);
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


