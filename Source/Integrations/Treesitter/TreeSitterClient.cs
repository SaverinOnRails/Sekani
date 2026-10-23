using System.Runtime.InteropServices;
using System.Text;
namespace Sekani.Integrations.TreeSitter;

public class TreeSitterClient : IDisposable
{
	public bool Active { get; private set; } = false;
	private TSLanguage _tsLanguage = null!;
	private TSParser _tsParser = new();
	private TSTree? _tsTree;
	private TSQuery? _tsHighlightQuery;
	private string _treeSitterlib = "/home/noble/Documents/sekanitestfiles/treesitter/C/c.so";
	private string _treeSitterHighlightQuery = "/home/noble/Documents/sekanitestfiles/treesitter/C/highlights.scm";

	public TreeSitterClient()
	{
		LoadNative();
		if (_tsLanguage.Ptr == nint.Zero) return;
		_tsParser.set_language(_tsLanguage);
		var highlightsQuery = _tsLanguage.query_new(File.ReadAllText(_treeSitterHighlightQuery), out uint errorCode, out TSQueryError error);
		if (highlightsQuery is null) return;
		_tsHighlightQuery = highlightsQuery;
		Active = true;
	}
	private void LoadNative()
	{
		try
		{
			var libraryHandle = NativeLibrary.Load(_treeSitterlib);
			var functionPtr = NativeLibrary.GetExport(
				libraryHandle,
				"tree_sitter_c");
			var getLanguage =
				Marshal.GetDelegateForFunctionPointer<GetLanguageDelegate>(
					functionPtr);
			nint languagePtr = getLanguage();
			_tsLanguage = new(languagePtr);
		}
		catch (Exception ex)
		{
			Console.WriteLine(ex);
		}
	}

	public void ParseBuffer(string buf)
	{
		if (!Active) return;
		_tsTree = _tsParser.parse_string(null, buf);
	}

	public void DoEdit(TSInputEdit edit)
	{
		if (Active == false || _tsTree is null || _tsHighlightQuery is null) return;
		_tsTree.edit(edit);


	}

	public List<TreeSitterHighlightSpan> GetHighlights(int start, int end)
	{
		if (Active == false || _tsTree is null || _tsHighlightQuery is null) return [];
		using var cursor = new TSQueryCursor();
		var root = _tsTree.root_node();
		cursor.set_range((uint)start, (uint)end);
		cursor.exec(_tsHighlightQuery, root);
		List<TreeSitterHighlightSpan> spans = new();
		unsafe
		{
			while (cursor.next_capture(out TSQueryMatch match, out uint index))
			{
				var caps = (TSQueryCapture*)match.captures;
				var capture = caps[index];
				var node = capture.node;
				int node_start = (int)node.start_offset();
				int node_end = (int)node.end_offset();
				var name = _tsHighlightQuery.capture_name_for_id(capture.index, out uint _);
				spans.Add(new(name, node_start, node_end));
			}
		}
		return spans;
	}

	public void Dispose()
	{
		_tsLanguage.Dispose();
		_tsParser.Dispose();
		_tsHighlightQuery?.Dispose();
		_tsTree?.Dispose();
	}
}

public readonly record struct TreeSitterHighlightSpan(string Capture, int Start, int End);
