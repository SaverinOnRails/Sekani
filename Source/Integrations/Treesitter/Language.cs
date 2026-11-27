using System.Runtime.InteropServices;

namespace Sekani.Integrations.TreeSitter;

internal static class TreeSitterLanguageRegistry
{
	private static readonly Dictionary<string, TreeSitterAssets> _loadedAssets = [];

	//Will be built dynamically from config
	private static List<Language> _languages = [
		new ("c-sharp","/home/noble/Documents/sekanitestfiles/treesitter/C#/c-sharp.so","tree_sitter_c_sharp", "/home/noble/Documents/sekanitestfiles/treesitter/C#/highlights.scm", ["cs"])
	];
	public static TreeSitterAssets? TryGetAssets(string extension)
	{
		var language = _languages.FirstOrDefault(p => p.Extensions.Contains(extension));
		if (language == default) return null;

		if (_loadedAssets.TryGetValue(language.Id, out var cached))
			return cached;

		var assets = TryLoadAssets(language);
		if (assets is not null)
			_loadedAssets[language.Id] = assets;
		return assets;
	}

	private static TreeSitterAssets? TryLoadAssets(Language language)
	{
		nint libraryHandle = nint.Zero;
		TSLanguage? tsLanguage = null;
		TSQuery? highlightQuery = null;
		try
		{
			libraryHandle = NativeLibrary.Load(language.LibraryPath);
			var functionPtr = NativeLibrary.GetExport(
				libraryHandle,
				language.EntryPointSymbol);
			var getLanguage =
				Marshal.GetDelegateForFunctionPointer<GetLanguageDelegate>(
					functionPtr);
			nint languagePtr = getLanguage();
			tsLanguage = new TSLanguage(languagePtr);
			highlightQuery = tsLanguage.query_new(
				File.ReadAllText(language.QueryPath),
				out _,
				out _);
			if (highlightQuery is null)
			{
				tsLanguage.Dispose();
				NativeLibrary.Free(libraryHandle);
				return null;
			}

			return new TreeSitterAssets(tsLanguage, highlightQuery, libraryHandle);
		}
		catch
		{
			highlightQuery?.Dispose();
			tsLanguage?.Dispose();
			if (libraryHandle != nint.Zero)
				NativeLibrary.Free(libraryHandle);
			return null;
		}
	}
}

internal sealed record TreeSitterAssets(
	TSLanguage Language,
	TSQuery HighlightQuery,
	nint LibraryHandle);

internal record struct Language(string Id,
	string LibraryPath,
	string EntryPointSymbol,
	string QueryPath,
	string[] Extensions);


