namespace Sekani.EditorCore;

public static class CommandNames
{
	public const string Write = "write";
	public const string BufferClose = "buffer-close";
	public const string BufferPicker = "buffer-picker";
	public const string FilePicker = "file-picker";
	public const string CommandPalette = "command-palette";
}

public class EditorCommands
{
	private static readonly List<string> PaletteCommands = [CommandNames.Write, CommandNames.BufferClose];

	private readonly IEditorContext _context;

	public EditorCommands(IEditorContext context)
	{
		_context = context;
	}

	public void Execute(string command)
	{
		switch (command)
		{
			case CommandNames.Write:
				WriteActiveBuffer();
				break;
			case CommandNames.BufferClose:
				CloseActiveBuffer();
				break;
			case CommandNames.BufferPicker:
				OpenBufferPicker();
				break;
			case CommandNames.FilePicker:
				OpenProjectFilePicker();
				break;
			case CommandNames.CommandPalette:
				OpenCommandPalette();
				break;
		}
	}

	private void WriteActiveBuffer()
	{
		if (_context.ActiveDocumentView is null) return;
		_context.ActiveDocumentView.Document.TrySave();
	}

	private void CloseActiveBuffer()
	{
		if (_context.ActiveDocumentView is null) return;
		var path = _context.ActiveDocumentView.Document.FilePath;
		_context.DocumentManager.CloseDocument(path);
	}

	private void OpenBufferPicker()
	{
		var openDocuments = _context.DocumentManager.GetOpenDocuments();
		_context.BigInputPromptContext = new BigInputPrompBufferPickerContext(openDocuments, _context);
	}

	private void OpenProjectFilePicker()
	{
		var files = _context.ProjectManager.AllFiles();
		_context.BigInputPromptContext = new BigInputPrompBufferPickerContext(files, _context);
	}

	private void OpenCommandPalette()
	{
		_context.BigInputPromptContext = new CommandPaletteContext(PaletteCommands, _context);
	}
}
