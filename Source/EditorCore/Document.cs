namespace Sekani.EditorCore;

public sealed class SekaniDocument
{
	public bool IsReadOnly { get; set; } = false;
	private SekaniBuffer _buffer = new();
	public LineCache LineCache { get; private set; }

	public IReadOnlyList<Line> Lines => _buffer.Lines;

	public SekaniDocument()
	{
		LineCache = new(_buffer);
	}

	public void TypeChars(string text, VisualCoordinate position)
	{
		var line = position.Line;
		var col = position.Col;

		if (line < 0 || col < 0 || string.IsNullOrEmpty(text)) return;
		_buffer.InsertText(line, col, text);

	}
}
