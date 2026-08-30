namespace Sekani.EditorCore;

public sealed class SekaniBuffer
{
	private List<Line> _lines;
	public IReadOnlyList<Line> Lines => _lines;

	public SekaniBuffer()
	{
		_lines = [new()];
	}

	public Line? GetLine(int lineIndex)
	{
		if (lineIndex >= _lines.Count || lineIndex < 0) return null;
		return _lines[lineIndex];
	}

	public Coordinate InsertText(int line, int col, string text)
	{
		if (string.IsNullOrEmpty(text))
			return new Coordinate(col, line);

		var insertLine = GetLine(line);

		if (insertLine is null)
			return new Coordinate(col, line);

		if (col < 0 || col > insertLine.Text.Length)
			return new Coordinate(col, line);

		var insertedLines = text.Split(
			Environment.NewLine,
			StringSplitOptions.None);

		if (insertedLines.Length == 1)
		{
			insertLine.InsertText(col, insertedLines[0]);
			RaiseBufferChangedEvent(line);

			return new Coordinate(
				col + insertedLines[0].Length,
				line);
		}

		var before = insertLine.Text[..col];
		var after = insertLine.Text[col..];

		insertLine.Text = before + insertedLines[0];
		RaiseBufferChangedEvent(line);

		for (int i = 1; i < insertedLines.Length; i++)
		{
			var newLine = new Line
			{
				Text = insertedLines[i]
			};

			_lines.Insert(line + i, newLine);
			RaiseBufferChangedEvent(line + i);
		}

		var lastLine = _lines[line + insertedLines.Length - 1];
		lastLine.Text += after;

		return new Coordinate(
			insertedLines[^1].Length,
			line + insertedLines.Length - 1);
	}
	private void RaiseBufferChangedEvent(int line)
	{
		BufferChangedEvent?.Invoke(this, new(line));
	}

	public void Backspace(int line, int col)
	{
		if (line < 0 || line >= _lines.Count)
			return;

		var currentLine = _lines[line];

		if (col > 0)
		{
			currentLine.Text = currentLine.Text.Remove(col - 1, 1);
			RaiseBufferChangedEvent(line);
			return;
		}

		if (line == 0)
			return;

		var previousLine = _lines[line - 1];

		previousLine.Text += currentLine.Text;
		_lines.RemoveAt(line);

		RaiseBufferChangedEvent(line - 1);
	}
	public event EventHandler<BufferChangeData>? BufferChangedEvent;
}

public record struct BufferChangeData(int Line);

