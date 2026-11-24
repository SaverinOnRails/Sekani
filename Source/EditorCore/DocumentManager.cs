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
		var view = _documentViews.FirstOrDefault(p => p.Document.FilePath == path);
		if (view is null) return;
		_documentViews.Remove(view);

		if (_context.ActiveDocumentView == view)
		{
			SetActive(_documentViews.Count > 0 ? _documentViews[^1] : null);
		}
		view.Document.Dispose();
	}

	private void SetActive(DocumentView? view)
	{
		_context.ActiveDocumentView = view;
	}

	//This will contain a lot more details eventually
	public List<string> GetOpenDocuments()
	{
		List<string> docs = [.. _documentViews.Select(p => p.Document.FilePath)];
		if (_context.ActiveDocumentView?.Document.FilePath is string activeFile)
		{
			var idx = docs.IndexOf(activeFile);
			if (idx > 0)
			{
				docs.RemoveAt(idx);
				docs.Insert(0, activeFile);
			}
		}
		return docs;
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




