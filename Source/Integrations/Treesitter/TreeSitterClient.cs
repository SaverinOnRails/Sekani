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
	public void Dispose()
	{
		_tsLanguage.Dispose();
		_tsParser.Dispose();
		_tsHighlightQuery?.Dispose();
		_tsTree?.Dispose();
	}

}
