namespace Sekani.EditorCore;

public class DocumentManager
{
	private IEditorContext _context;
	public DocumentManager(Clipboard clipboard, IEditorContext context)
	{
		_clipboard = clipboard;
		_context = context;
	}
	private Clipboard _clipboard;
	private List<DocumentView> _documentViews = [];

	public void OpenDocument(string path)
	{
		var exists = _documentViews.Exists(p => p.Document.FilePath == path);
		if (exists) return;
		var document = new SekaniDocument(path, _clipboard);
		var view = new DocumentView(document);
		_documentViews.Add(view);
		SetActive(view);
	}

	public void SetActive(DocumentView view)
	{
		_context.ActiveDocumentView = view;
	}
}

public class DocumentView
{
	public SekaniDocument Document { get; init; }

	public LineCache LineCache { get; init; }

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
		//Create initial cache
		LineCache = document.CreateLineCache(1, false, 0);
	}
}




