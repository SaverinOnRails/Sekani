namespace Sekani.EditorCore;

public sealed class SekaniDocument
{
	public bool IsReadOnly { get; set; } = false;
	private SekaniBuffer _buffer = new();
	public Coordinate CaretPosition { get; set; } = new(0, 0);
	private int _preferredLogicalColumn = 0;
	public IReadOnlyList<Line> Lines => _buffer.Lines;

	public void TypeChars(string text)
	{
		if (string.IsNullOrEmpty(text))
			return;

		var position = CaretPosition;

		if (position.Line < 0 || position.Col < 0)
			return;

		CaretPosition = _buffer.InsertText(
			position.Line,
			position.Col,
			text);

		_preferredLogicalColumn = CaretPosition.Col;
	}
	public void CaretRight()
	{
		var line = Lines[CaretPosition.Line];

		if (CaretPosition.Col < line.Text.Length)
		{
			AdvanceCaretCol(1);

			// Horizontal movement establishes a new preferred column.
			_preferredLogicalColumn = CaretPosition.Col;
			return;
		}

		if (CaretPosition.Line == Lines.Count - 1)
			return;

		CaretPosition = new Coordinate(
			0,
			CaretPosition.Line + 1);

		_preferredLogicalColumn = 0;
	}

	private void MoveCaretVertical(int direction)
	{
		var targetLineIndex = CaretPosition.Line + direction;

		if (targetLineIndex < 0 || targetLineIndex >= Lines.Count)
			return;

		var targetLine = Lines[targetLineIndex];

		CaretPosition = new Coordinate(
			Math.Min(_preferredLogicalColumn, targetLine.Text.Length),
			targetLineIndex);
	}

	public void CaretLeft()
	{
		if (CaretPosition.Col > 0)
		{
			AdvanceCaretCol(-1);
			_preferredLogicalColumn = CaretPosition.Col;
			return;
		}

		if (CaretPosition.Line == 0)
			return;

		var previousLine = Lines[CaretPosition.Line - 1];

		CaretPosition = new Coordinate(
			previousLine.Text.Length,
			CaretPosition.Line - 1);

		_preferredLogicalColumn = CaretPosition.Col;
	}

	public void Backspace()
	{
		var position = CaretPosition;

		if (position.Col > 0)
		{
			_buffer.Backspace(position.Line, position.Col);

			CaretPosition = new Coordinate(
				position.Col - 1,
				position.Line);
		}
		else if (position.Line > 0)
		{
			var previousLine = _buffer.GetLine(position.Line - 1);

			if (previousLine is null)
				return;

			var newCol = previousLine.Text.Length;

			_buffer.Backspace(position.Line, position.Col);

			CaretPosition = new Coordinate(
				newCol,
				position.Line - 1);
		}

		_preferredLogicalColumn = CaretPosition.Col;
	}

	public void CaretUp()
	{
		MoveCaretVertical(-1);
	}

	public void CaretDown()
	{
		MoveCaretVertical(1);
	}

	private void AdvanceCaretCol(int count)
	{
		CaretPosition = new(
			CaretPosition.Col + count,
			CaretPosition.Line);
	}
	public LineCache CreateLineCache(int tabSize, bool wordWrap, int maxVisualColsPerLine)
	{
		return new LineCache(_buffer, tabSize, wordWrap, maxVisualColsPerLine);
	}

	public void TypeChars(object newLine)
	{
		throw new NotImplementedException();
	}
}
