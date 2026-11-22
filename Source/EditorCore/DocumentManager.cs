namespace Sekani.EditorCore;

public class DocumentManager
{
	private readonly IEditorContext _context;
	public DocumentManager(Clipboard clipboard, IEditorContext context)
	{
		_clipboard = clipboard;
		_context = context;
	}
	private Clipboard _clipboard;
	private List<DocumentView> _documentViews = [];

	public void OpenDocument(string path)
	{
		var _view = _documentViews.FirstOrDefault(p => p.Document.FilePath == path);
		if (_view is not null)
		{
			SetActive(_view);
			return;
		}
		var document = new SekaniDocument(path, _clipboard);
		var view = new DocumentView(document);
		_documentViews.Add(view);
		SetActive(view);
	}
	public void CloseDocument(string path)
	{

	}

	private void SetActive(DocumentView view)
	{
		_context.ActiveDocumentView = view;
	}
}

public class DocumentView
{
	public SekaniDocument Document { get; init; }

	public LineCache? LineCache { get; set; }
	// View state
	public double ScrollXOffset { get; set; } = 0;
	public double ScrollYOffset { get; set; } = 0;
	public double TargetScrollYOffset { get; set; } = 0;
	public int FirstVisibleLogicalLineForRestore { get; set; } = 0;
	public int PreferredDragScrollVisualColumn { get; set; } = 0;


	// Search state
	public IReadOnlyList<SearchHit> SearchHits { get; set; } = [];
	public int SearchHintActiveIndex { get; set; } = 0;

	public DocumentView(SekaniDocument document)
	{
		Document = document;
	}
}




