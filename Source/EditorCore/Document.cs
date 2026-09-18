namespace Sekani.EditorCore;

public sealed class SekaniDocument
{
	public bool IsReadOnly { get; set; } = false;
	private SekaniBuffer _buffer = new();
	public Coordinate CaretPosition { get; set; } = new(0, 0);
	private int _preferredVisualColumn = 0;
	public IReadOnlyList<Line> Lines => _buffer.Lines;
	private LineCache? _lineCache;
	private string? _filePath = null;

	public SekaniDocument()
	{

	}

	public SekaniDocument(string filePath)
	{
		_filePath = filePath;
		TypeChars(File.ReadAllText(filePath));
	}

	public void TypeChars(string text)
	{
		if (string.IsNullOrEmpty(text))
			return;

		var position = CaretPosition;

		if (position.Line < 0 || position.Col < 0)
			return;
		CaretPosition = _buffer.InsertText(
			position.Col,
			position.Line,
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

	public void UpdatePreferredVisualColumn()
	{
		if (_lineCache is null)
			return;

		var line = Lines[CaretPosition.Line];
		var layout = _lineCache.GetOrCreate(line);

		if (layout is null)
			return;

		var visual = layout.GetVisualCoordinate(CaretPosition);

		_preferredVisualColumn = visual.VisualCol;
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
				layout.GetVisualCoordinate(CaretPosition).VisualLine;
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

	private Coordinate RangeUpCursor(Coordinate coord)
	{
		if (coord.Col >= Lines[coord.Line].Text.Length)
		{
			var newLine = coord.Line + 1;
			if (newLine >= Lines.Count) _buffer.InsertText(coord.Col, coord.Line, Environment.NewLine); //if going up range needs a new line to be created, create it
			return new(0, newLine);
		}
		else
		{
			return new(coord.Col + 1, coord.Line);
		}
	}

	public void DeleteSelection()
	{
		var coord = CaretPosition;
		if (!coord.HasRange())
		{
			var pos = RangeUpCursor(coord);
			DeleteCore(pos);
		}
		else
		{
			var start = coord.LesserRangeEnd()!;
			var end = RangeUpCursor(coord.GreaterRangeEnd()!);
			var position = end;
			while (position != start)
			{
				var pos = DeleteCore(position);
				if (pos is null) return;
				position = pos;
			}
			CaretPosition = new(start);
		}
	}

	public void BackSpace(Coordinate pos)
	{
		var newcoord = DeleteCore(pos);
		if (newcoord is not null)
		{

			CaretPosition = newcoord;
			UpdatePreferredVisualColumn();
		}
	}
	private Coordinate? DeleteCore(Coordinate position)
	{
		if (position.Col > 0)
		{
			_buffer.Delete(position.Col, position.Line);
			return new Coordinate(
				position.Col - 1,
				position.Line);
		}
		else if (position.Line > 0)
		{
			var previousLine = _buffer.GetLine(position.Line - 1);

			if (previousLine is null)
				return null;
			var newCol = previousLine.Text.Length;
			_buffer.Delete(position.Col, position.Line);

			return new Coordinate(
				newCol,
				position.Line - 1);
		}
		else
		{
			return null;
		}
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


	public void AddNewLineUnderSelection()
	{
		var pos = CaretPosition;
		int lineIndex = pos.HasRange() ? pos.GreaterRangeEnd()!.Line : pos.Line;
		var line = Lines[lineIndex];
		_buffer.InsertText(
			line.Text.Length,
			lineIndex,
			Environment.NewLine);
		CaretPosition = new(0, lineIndex + 1);
	}

	public void TrySave()
	{
		try
		{
			if (_filePath is null) TryCreateFileAndSave();
			else
			{
				SaveCore();
			}
		}
		catch (Exception e)
		{
			Console.WriteLine(e.Message);
			return;
		}
	}

	private void TryCreateFileAndSave()
	{
		throw new NotImplementedException();
	}

	private void SaveCore()
	{
		if (_filePath is null) return;
		var text = _buffer.ToText();
		File.WriteAllText(_filePath, text);
	}

	public void PlaceCursorBeforeSelection()
	{
		if (!CaretPosition.HasRange()) return; //nothing to do
		var lesser = CaretPosition.LesserRangeEnd()!;
		Coordinate greater = new(CaretPosition.GreaterRangeEnd()!);
		CaretPosition.SetCoord(lesser);
		CaretPosition.RangeEnd = greater;
	}

	public void PlaceCursorAfterSelection()
	{
		if (!CaretPosition.HasRange())
		{
			CaretPosition.SetCoord(RangeUpCursor(CaretPosition));
			return;
		}
		var lesser = new Coordinate(CaretPosition.LesserRangeEnd()!);
		var greater = RangeUpCursor(CaretPosition.GreaterRangeEnd()!);
		CaretPosition.SetCoord(greater);
		CaretPosition.RangeEnd = lesser;
	}

	public void PlaceCursorAtLineStart()
	{
		var lineIndex = CaretPosition.Line;
		var line = Lines[lineIndex];
		var firstNonWhiteSpace = 0;
		for (int i = 0; i < line.Text.Length; i++)
		{
			if (!char.IsWhiteSpace(line.Text[i])) break;
			firstNonWhiteSpace++;
		}
		CaretPosition = new(firstNonWhiteSpace, lineIndex);
	}
	public void PlaceCursorAtLineEnd()
	{
		var lineIndex = CaretPosition.Line;
		var line = Lines[lineIndex];
		CaretPosition = new(line.Text.Length, lineIndex);
	}

	public void AddNewLineAboveSelection()
	{
		var pos = CaretPosition;
		int lineIndex = pos.HasRange() ? pos.LesserRangeEnd()!.Line : pos.Line;
		_buffer.InsertText(
			0,
			lineIndex,
			Environment.NewLine);
		CaretPosition = new(0, lineIndex);
	}
}

public enum CaretVerticalDirection
{
	Up,
	Down
}
