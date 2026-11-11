namespace Sekani.EditorCore;

public interface IEditorContext
{
    public string? MotionCountBuffer { get; }

    public void HandleCommand(string command);

    public Mode Mode { get; set; }

    public Clipboard Clipboard { get; }

    public ISmallInputContext? SmallInputContext { get; set; }

    void RaisePanelClosed(object? sender);

    event EventHandler? PanelClosed;
}

public interface ISmallInputContext;

public class SearchSmallInputContext : ISmallInputContext
{
    public string? SearchTerm { get; set; }

    public event EventHandler<string?>? SearchTermUpdated;

    public void RaiseSearchTermUpdated() => SearchTermUpdated?.Invoke(this, SearchTerm);

    public Range OriginalCaretRange { get; set; }

    public bool CommittedSearch { get; set; } = false;

}


