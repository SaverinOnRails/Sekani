namespace Sekani.EditorCore;

public delegate Task<string?> TryGetTextAsyncDelegate();
public delegate Task SetTextAsyncDelegate(string? text);

//Simple clipboard impl that points to system clipboard, no registers yet
public sealed class Clipboard(
	TryGetTextAsyncDelegate tryGetTextAsync,
	SetTextAsyncDelegate setTextAsync)
{
	private readonly TryGetTextAsyncDelegate _tryGetSystemClipboardTextAsync = tryGetTextAsync;
	private readonly SetTextAsyncDelegate _setSystemClipboardTextAsync = setTextAsync;

	public void Yank(string text)
	{
		_setSystemClipboardTextAsync(text).GetAwaiter().GetResult();
	}

	public async Task<string?> Paste()
	{
		return await _tryGetSystemClipboardTextAsync();
	}
}
