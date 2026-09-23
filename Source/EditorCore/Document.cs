using System.Globalization;
using System.Reflection.Metadata;
using System.Xml;
using Microsoft.VisualBasic;

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
		var layout = _lineCache.GetOrCreate(CaretPosition.Line);

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
		var layout = _lineCache.GetOrCreate(CaretPosition.Line);

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
			var targetLineLayout = _lineCache.GetOrCreate(CaretPosition.Line - 1);

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
			var targetLineLayout = _lineCache.GetOrCreate(CaretPosition.Line + 1);

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

	//Returns the next possible location of the cursor.
	// Guarantees the cursor can be here, not that a character exists here necessarily
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
	public void SelectToNextWord()
	{
		var pos = CaretPosition;
		var nextWordStart = FindWordStart(pos, out Coordinate? anchorPos);
		if (nextWordStart is not null && anchorPos is not null)
		{
			CaretPosition.RangeEnd = new(anchorPos);
			CaretPosition.SetCoord(nextWordStart);
		}
	}

	private Coordinate? FindWordStart(Coordinate pos, out Coordinate? anchorPos)
	{
		//are we at the end of the file?
		anchorPos = null;
		if (pos.Line >= Lines.Count - 1 && pos.Col >= Lines[pos.Line].Text.Length - 1) return null;
		anchorPos = StartAt(pos);
		var currentCol = anchorPos.Col;
		var currentLine = anchorPos.Line;
		var initialToken = CharTokenKind(IndexLineWithLineBreak(currentCol, currentLine));
		int prevCol = currentCol;
		while (true)
		{
			var next = Advance(ref currentCol, ref currentLine, out bool atEnd);
			if (atEnd)
			{
				return next;
			}
			var newToken = CharTokenKind(IndexLineWithLineBreak(currentCol, currentLine));
			if (CanEnd(ref initialToken, newToken))
			{
				return new(prevCol, currentLine);
			}
			prevCol = currentCol;
		}
	}

	//caller verifies bounds of this
	private Coordinate StartAt(Coordinate pos)
	{
		var col = pos.Col;
		var line = pos.Line;
		var currentToken = CharTokenKind(IndexLineWithLineBreak(pos.Col, pos.Line));
		{
			if (currentToken == SelectionTokenKind.Eol)
			{
				while (currentToken == SelectionTokenKind.Eol)
				{
					var advance = Advance(ref col, ref line, out bool atEnd);
					if (atEnd)
					{
						return advance;
					}
					currentToken = CharTokenKind(IndexLineWithLineBreak(col, line));
				}
				return new(col, line);
			}
		}
		var next = Advance(ref col, ref line, out bool _);
		var nextToken = CharTokenKind(IndexLineWithLineBreak(col, line));
		{
			if (nextToken == SelectionTokenKind.Eol)
			{
				while (nextToken == SelectionTokenKind.Eol)
				{
					var advance = Advance(ref col, ref line, out bool atEnd);
					if (atEnd)
					{
						return advance;
					}
					nextToken = CharTokenKind(IndexLineWithLineBreak(col, line));
				}
				return new(col, line);
			}
		}
		if (currentToken == nextToken || nextToken == SelectionTokenKind.Whitespace) return pos;
		return next;
	}


	//advances and skips empty lines
	private Coordinate Advance(ref int col, ref int line, out bool atEnd)
	{
		atEnd = false;
		col++;
		if (col > Lines[line].Text.Length)
		{
			if (line == Lines.Count - 1)
			{
				atEnd = true;
				return new(col - 1, line);
			}
			col = 0;
			line++;
		}
		return new(col, line);
	}
	private bool CanEnd(ref SelectionTokenKind originalToken, SelectionTokenKind newToken)
	{
		if (newToken == SelectionTokenKind.Whitespace)
		{
			originalToken = newToken;
			return false;
		}
		;
		if (newToken != originalToken) return true;
		return false;
	}

	//Since we pop out line breaks and helix motions needs them, this indexes a line and returns a line break where a line break can be
	private char IndexLineWithLineBreak(int col, int line)
	{
		try
		{
			if (col == Lines[line].Text.Length) return '\n';
			return Lines[line].Text[col];
		}
		catch
		{
			Console.WriteLine(col);
			Console.WriteLine(line);
			throw;
		}
	}


	private SelectionTokenKind CharTokenKind(char c)
	{
		if (c is '\n' or '\r' or '\u000B' or '\u000C' or '\u0085' or '\u2028' or '\u2029')
			return SelectionTokenKind.Eol;
		if (char.IsWhiteSpace(c))
			return SelectionTokenKind.Whitespace;
		if (char.IsLetterOrDigit(c) || c == '_')
			return SelectionTokenKind.AlphaNumerical;

		switch (char.GetUnicodeCategory(c))
		{
			case UnicodeCategory.ConnectorPunctuation:
			case UnicodeCategory.DashPunctuation:
			case UnicodeCategory.OpenPunctuation:
			case UnicodeCategory.ClosePunctuation:
			case UnicodeCategory.InitialQuotePunctuation:
			case UnicodeCategory.FinalQuotePunctuation:
			case UnicodeCategory.OtherPunctuation:
			case UnicodeCategory.MathSymbol:
			case UnicodeCategory.CurrencySymbol:
			case UnicodeCategory.ModifierSymbol:
				return SelectionTokenKind.Punctuation;
			default:
				return SelectionTokenKind.Unknown;
		}
	}
}

public enum CaretVerticalDirection
{
	Up,
	Down
}
enum SelectionTokenKind
{
	AlphaNumerical,
	Punctuation,
	Whitespace,
	Eol,
	Unknown,
}
