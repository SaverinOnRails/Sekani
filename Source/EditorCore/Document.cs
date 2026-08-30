namespace Sekani.EditorCore;

public sealed class SekaniDocument
{
	public bool IsReadOnly { get; set; } = false;
	private SekaniBuffer _buffer = new();
	public Coordinate CaretPosition { get; set; } = new(0, 0);
	private int _preferredVisualColumn = 0;
	public IReadOnlyList<Line> Lines => _buffer.Lines;
	private LineCache? _lineCache;

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

		UpdatePreferredVisualColumn();
	}
	public void CaretRight()
	{
		var line = Lines[CaretPosition.Line];

		if (CaretPosition.Col < line.Text.Length)
		{
			AdvanceCaretCol(1);
			UpdatePreferredVisualColumn();
			return;
		}

		if (CaretPosition.Line == Lines.Count - 1)
			return;

		CaretPosition = new Coordinate(
			0,
			CaretPosition.Line + 1);

		UpdatePreferredVisualColumn();
	}

	private void UpdatePreferredVisualColumn()
	{
		if (_lineCache is null)
			return;

		var line = Lines[CaretPosition.Line];
		var layout = _lineCache.GetOrCreate(line);

		if (layout is null)
			return;

		var visual = layout.GetVisualCoordinate(CaretPosition);

		_preferredVisualColumn = visual.Col;
	}

	private void MoveCaretVertical(CaretVerticalDirection direction)
	{
		if (_lineCache is null)
			throw new InvalidOperationException();

		var line = Lines[CaretPosition.Line];
		var layout = _lineCache.GetOrCreate(line);

		if (layout is null)
			return;

		if (layout.VisualLines.Count > 1)
		{
			var visualLineIndex =
				layout.GetVisualCoordinate(CaretPosition).Line;
			if (direction == CaretVerticalDirection.Down &&
				visualLineIndex < layout.VisualLines.Count - 1)
			{
				var targetVisualPos = new VisualCoordinate(
					_preferredVisualColumn,
					visualLineIndex + 1);
				CaretPosition = new(
					layout.GetLogicalColumn(targetVisualPos),
					CaretPosition.Line)
				{
					TrailVisualLine = _preferredVisualColumn == 0
				};

				return;
			}

			if (direction == CaretVerticalDirection.Up &&
				visualLineIndex > 0)
			{
				var targetVisualPos = new VisualCoordinate(
					_preferredVisualColumn,
					visualLineIndex - 1);

				CaretPosition = new(
					layout.GetLogicalColumn(targetVisualPos),
					CaretPosition.Line)
				{
					TrailVisualLine = _preferredVisualColumn == 0
				};
				return;
			}
		}
		if (direction == CaretVerticalDirection.Up)
		{
			if (CaretPosition.Line == 0)
				return;

			var targetLine = Lines[CaretPosition.Line - 1];
			var targetLineLayout = _lineCache.GetOrCreate(targetLine);

			if (targetLineLayout is null)
				return;

			var targetVisual = new VisualCoordinate(
				_preferredVisualColumn,
				targetLineLayout.VisualLines.Count - 1);

			CaretPosition = new(
				targetLineLayout.GetLogicalColumn(targetVisual),
				CaretPosition.Line - 1)
			{
				TrailVisualLine = _preferredVisualColumn == 0
			};
		}
		else
		{
			if (CaretPosition.Line >= Lines.Count - 1)
				return;

			var targetLine = Lines[CaretPosition.Line + 1];
			var targetLineLayout = _lineCache.GetOrCreate(targetLine);

			if (targetLineLayout is null)
				return;

			var targetVisual = new VisualCoordinate(
				_preferredVisualColumn,
				0);

			CaretPosition = new(
				targetLineLayout.GetLogicalColumn(targetVisual),
				CaretPosition.Line + 1);
		}
	}
	public void CaretLeft()
	{
		if (CaretPosition.Col > 0)
		{
			AdvanceCaretCol(-1);
			UpdatePreferredVisualColumn();
			return;
		}

		if (CaretPosition.Line == 0)
			return;

		var previousLine = Lines[CaretPosition.Line - 1];

		CaretPosition = new Coordinate(
			previousLine.Text.Length,
			CaretPosition.Line - 1);

		UpdatePreferredVisualColumn();
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

		UpdatePreferredVisualColumn();
	}

	public void CaretUp()
	{
		MoveCaretVertical(CaretVerticalDirection.Up);
	}

	public void CaretDown()
	{
		MoveCaretVertical(CaretVerticalDirection.Down);
	}

	private void AdvanceCaretCol(int count)
	{
		CaretPosition = new(
			CaretPosition.Col + count,
			CaretPosition.Line);
	}
	public LineCache CreateLineCache(int tabSize, bool wordWrap, int maxVisualColsPerLine)
	{
		_lineCache = new LineCache(_buffer, tabSize, wordWrap, maxVisualColsPerLine);
		return _lineCache;
	}

	public void TypeChars(object newLine)
	{
		throw new NotImplementedException();
	}
}

public enum CaretVerticalDirection
{
	Up,
	Down
}
