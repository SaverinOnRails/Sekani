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

	public void InsertText(int line, int col, string text)
	{
		if (string.IsNullOrEmpty(text))
			return;

		var insertLine = GetLine(line);

		if (insertLine is null)
			return;

		if (col < 0 || col > insertLine.Text.Length)
			return;

		var insertedLines = text.Split(
			Environment.NewLine,
			StringSplitOptions.None);

		if (insertedLines.Length == 1)
		{
			insertLine.InsertText(col, insertedLines[0]);
			RaiseBufferChangedEvent(line);
			return;
		}

		var before = insertLine.Text[..col];
		var after = insertLine.Text[col..];

		insertLine.Text = before + insertedLines[0];

		for (int i = 1; i < insertedLines.Length; i++)
		{
			var newLine = new Line
			{
				Text = insertedLines[i]
			};

			_lines.Insert(line + i, newLine);
			RaiseBufferChangedEvent(line + i);
		}
		_lines[line + insertedLines.Length - 1].Text += after;
	}
	private void RaiseBufferChangedEvent(int line)
	{
		BufferChangedEvent?.Invoke(this, new(line));
	}

	public event EventHandler<BufferChangeData>? BufferChangedEvent;
}

public record struct BufferChangeData(int Line);

