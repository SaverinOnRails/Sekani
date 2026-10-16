using System.Globalization;
using System.Reflection.Metadata;

namespace Sekani.EditorCore;

public sealed class SekaniDocument
{
	public bool IsReadOnly { get; set; } = false;
	private SekaniBuffer _buffer = null;
	public Coordinate CaretPosition
	{
		get
		{
			var position = _pieceTable.GetCoordinate(CaretRange.Cursor);
			position.TrailVisualLine = CaretRange.TrailVisualLine;
			return position;
		}
	}
	public Range CaretRange { get; private set; } = new Range(0);
	public int LineCount => _pieceTable.LineFeedCount;
	private int _preferredVisualColumn = 0;

	public void SetCaretRange(Coordinate pos)
	{
		CaretRange = new(_pieceTable.GetOffset(pos));
	}
	public void SetCaretRange(int anchor)
	{
		CaretRange = new(anchor);
	}

	public void SetDocumentCaretRangeHead(int head)
	{
		var anchor = CaretRange.Anchor;
		CaretRange = new(anchor, head, head);
	}
	public void SetDocumentCaretRangeHead(Coordinate headpos)
	{
		var head = _pieceTable.GetOffset(headpos);
		var anchor = CaretRange.Anchor;
		CaretRange = new(anchor, head, head);
	}

	public int GetOffset(Coordinate pos) => _pieceTable.GetOffset(pos);
	public Coordinate GetCoordinate(int offset) => _pieceTable.GetCoordinate(offset);
	public IReadOnlyList<Line> Lines => _buffer.Lines;
	private LineCache? _lineCache;
	private string? _filePath = null;

	//piece table stuff;
	private PieceTable _pieceTable;

	public SekaniDocument()
	{
		_pieceTable = new("");
	}

	public SekaniDocument(string filePath)
	{
		_filePath = filePath;
		_pieceTable = new(File.ReadAllText(filePath));
		// TypeChars(File.ReadAllText(filePath), false);
	}

	public void TypeChars(string text, bool invokePieceTable = true)
	{
		TypeCharsCore(CaretRange.Cursor, text);

	}
	public void CaretRight()
	{
		if (CaretRange.Cursor < _pieceTable.BufferLength)
		{
			AdvanceCaretCol(1);
			UpdatePreferredVisualColumn();
		}
	}

	public void UpdatePreferredVisualColumn()
	{
		if (_lineCache is null)
			return;
		var pos = _pieceTable.GetCoordinate(CaretRange.Cursor);
		var layout = _lineCache.GetOrCreate(pos.Line);

		if (layout is null)
			return;

		var visual = layout.GetVisualCoordinate(CaretPosition);

		_preferredVisualColumn = visual.VisualCol;
	}

	private void MoveCaretVertical(CaretVerticalDirection direction)
	{
		if (_lineCache is null)
			throw new InvalidOperationException();

		var pos = CaretPosition;
		var layout = _lineCache.GetOrCreate(pos.Line);

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
				Coordinate newpos = new(
					layout.GetLogicalColumn(targetVisualPos),
					CaretPosition.Line)
				{
					TrailVisualLine = _preferredVisualColumn == 0
				};
				if (newpos != CaretPosition)
				{
					var offset = _pieceTable.GetOffset(newpos);
					CaretRange = new Range(offset, trailVisualLine: _preferredVisualColumn == 0);
					return;
				}
			}

			if (direction == CaretVerticalDirection.Up &&
				visualLineIndex > 0)
			{
				var targetVisualPos = new VisualCoordinate(
					_preferredVisualColumn,
					visualLineIndex - 1);

				Coordinate newpos = new(
					layout.GetLogicalColumn(targetVisualPos),
					CaretPosition.Line)
				{
					TrailVisualLine = _preferredVisualColumn == 0
				};
				var offset = _pieceTable.GetOffset(newpos);
				CaretRange = new Range(offset, trailVisualLine: _preferredVisualColumn == 0);
				return;
			}
		}
		if (direction == CaretVerticalDirection.Up)
		{
			if (pos.Line == 0)
				return;

			var targetLine = pos.Line - 1;
			var targetLineLayout = _lineCache.GetOrCreate(targetLine);

			if (targetLineLayout is null)
				return;

			var targetVisual = new VisualCoordinate(
				_preferredVisualColumn,
				targetLineLayout.VisualLines.Count - 1);

			Coordinate newpos = new(
				targetLineLayout.GetLogicalColumn(targetVisual),
				CaretPosition.Line - 1)
			{
				TrailVisualLine = _preferredVisualColumn == 0
			};
			var offset = _pieceTable.GetOffset(newpos);
			CaretRange = new Range(offset, trailVisualLine: _preferredVisualColumn == 0);
		}
		else
		{
			if (pos.Line > LineCount)
				return;

			var targetLine = pos.Line + 1;
			var targetLineLayout = _lineCache.GetOrCreate(targetLine);

			if (targetLineLayout is null)
				return;

			var targetVisual = new VisualCoordinate(
				_preferredVisualColumn,
				0);

			Coordinate newpos = new(
				targetLineLayout.GetLogicalColumn(targetVisual),
				CaretPosition.Line + 1);
			if (newpos.Line >= _pieceTable.LineFeedCount) return;
			var offset = _pieceTable.GetOffset(newpos);
			CaretRange = new Range(offset);
		}
	}
	public void CaretLeft()
	{
		if (CaretRange.Cursor > 0)
		{
			AdvanceCaretCol(-1);
			UpdatePreferredVisualColumn();
		}
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
		var lower = CaretRange.Lower;
		var higher = CaretRange.Higher;
		var sethigh = higher == CaretRange.Cursor ? higher + 1 : higher;

		//clamp sethigh
		sethigh = Math.Min(sethigh, _pieceTable.BufferLength);
		var delta = sethigh - lower;
		_pieceTable.Delete(lower, delta);
		CaretRange = new(lower);
	}

	public void BackSpace(Coordinate pos)
	{
		var offset = CaretRange.Cursor;
		if (offset == 0) return;
		var startOffset = offset - 1;
		_pieceTable.Delete(startOffset, 1);
		AdvanceCaretCol(-1);
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
		var line = CaretPosition.Line;
		if (count == 1)
		{
			CaretRange = new(NextGraphemeBoundary(CaretRange.Cursor));
			return;
		}
		CaretRange = new(CaretRange.Cursor + count);
	}
	public int NextGraphemeBoundary(int offset)
	{
		var pos = GetCoordinate(offset);
		var line = pos.Line;
		var layout = _lineCache!.GetOrCreate(line);
		var next = new Coordinate(layout!.NextCol(pos.Col), line);
		return GetOffset(next);

	}
	public LineCache CreateLineCache(int tabSize, bool wordWrap, int maxVisualColsPerLine)
	{
		_lineCache = new LineCache(_buffer, _pieceTable, tabSize, wordWrap, maxVisualColsPerLine);
		return _lineCache;
	}


	public void AddNewLineUnderSelection()
	{
		var offset = CaretRange.Width > 1 ? CaretRange.Higher : CaretRange.Lower;
		var pos = GetCoordinate(offset);
		var text = _pieceTable.PrintCleanLine(pos.Line);
		var insertpos = new Coordinate(text.Length, pos.Line);
		TypeCharsCore(GetOffset(insertpos), Environment.NewLine);
		var newpos = new Coordinate(0, pos.Line + 1);
		CaretRange = new(GetOffset(newpos));
	}

	private void TypeCharsCore(int offset, string text)
	{
		_pieceTable.Insert(text, offset);
		AdvanceCaretCol(text.Length);
		UpdatePreferredVisualColumn();
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
		var lower = CaretRange.Lower;
		var higher = CaretRange.Higher;
		CaretRange = new(higher, lower, lower);
	}

	public void PlaceCursorAfterSelection()
	{
		var lower = CaretRange.Lower;
		var higher = CaretRange.Higher;
		var sethigh = higher == CaretRange.Cursor ? higher + 1 : higher;
		CaretRange = new(lower, sethigh, sethigh);
	}

	public void PlaceCursorAtLineStart()
	{
		var line = CaretPosition.Line;
		var text = _pieceTable.PrintCleanLine(line);
		int firstNonWhiteSpace = 0;

		for (int i = 0; i < text.Length; i++)
		{
			if (!char.IsWhiteSpace(text[i])) break;
			firstNonWhiteSpace++;
		}
		var pos = new Coordinate(firstNonWhiteSpace, line);
		CaretRange = new(GetOffset(pos));
	}
	public void PlaceCursorAtLineEnd()
	{
		var line = CaretPosition.Line;
		var layout = _lineCache!.GetOrCreate(line);
		if(layout is null) return;
		var lastBoundary = layout.GraphemeBoundaries.LastOrDefault();
		CaretRange = new(GetOffset(new Coordinate(lastBoundary, line)));
	}

	public void AddNewLineAboveSelection()
	{
		var higher = CaretRange.Higher;
		var pos = GetCoordinate(higher);
		var insertpos = new Coordinate(0, pos.Line);
		TypeCharsCore(GetOffset(insertpos), Environment.NewLine);
		CaretRange = new(GetOffset(insertpos));
	}
	public void SelectToNextWordStart()
	{
		var pos = CaretPosition;
		var nextWordStart = FindWordStart(pos, out Coordinate? anchorPos);
		if (nextWordStart is not null && anchorPos is not null)
		{
			CaretPosition.Anchor = new(anchorPos);
			CaretPosition.SetCoord(nextWordStart);
		}
	}

	public void SelectToNextWordEnd()
	{
		var pos = CaretPosition;
		var nextWordEnd = FindWordEnd(pos, out Coordinate? anchorPos);
		if (nextWordEnd is not null && anchorPos is not null)
		{
			CaretPosition.Anchor = new(anchorPos);
			CaretPosition.SetCoord(nextWordEnd);
		}
	}

	private Coordinate? FindWordEnd(Coordinate pos, out Coordinate? anchorPos)
	{
		//are we at the end of the file?
		anchorPos = null;
		if (pos.Line >= Lines.Count - 1 && pos.Col >= Lines[pos.Line].Text.Length - 1) return null;
		anchorPos = FindAnchorPos(pos, MotionKind.NextWordEnd);
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
			if (CanEndWordEndScan(ref initialToken, newToken))
			{
				return new(prevCol, currentLine);
			}
			prevCol = currentCol;
		}
	}


	private Coordinate? FindWordStart(Coordinate pos, out Coordinate? anchorPos)
	{
		//are we at the end of the file?
		anchorPos = null;
		if (pos.Line >= Lines.Count - 1 && pos.Col >= Lines[pos.Line].Text.Length - 1) return null;
		anchorPos = FindAnchorPos(pos, MotionKind.NextWordStart);
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
			if (CanEndWordStartScan(ref initialToken, newToken))
			{
				return new(prevCol, currentLine);
			}
			prevCol = currentCol;
		}
	}

	//caller verifies bounds of this
	private Coordinate FindAnchorPos(Coordinate pos, MotionKind motionKind)
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
		if (motionKind == MotionKind.NextWordStart)
		{
			if (currentToken == nextToken || nextToken == SelectionTokenKind.Whitespace) return pos;
		}
		if (motionKind == MotionKind.NextWordEnd)
		{
			if (currentToken == SelectionTokenKind.Whitespace || currentToken == nextToken) return pos;
		}
		return next;
	}


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
	private bool CanEndWordStartScan(ref SelectionTokenKind originalToken, SelectionTokenKind newToken)
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

	private bool CanEndWordEndScan(ref SelectionTokenKind originalToken, SelectionTokenKind newToken)
	{
		if (newToken == SelectionTokenKind.Whitespace && originalToken != SelectionTokenKind.Whitespace)
		{
			return true;
		}
		if (originalToken == SelectionTokenKind.Whitespace && originalToken != newToken)
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
	public void PrintPieceTable()
	{
		// Console.WriteLine(_pieceTable.Print());
		// Console.WriteLine($"Temp hard check of offset : {_pieceTable.TempHardLoopLogicalCoordinatedToOffset(CaretPosition)}");
		// Console.WriteLine($"Log check of offset : {_pieceTable.GetOffset(CaretPosition)}");
		// Console.WriteLine();
		// Console.WriteLine($"True coord {CaretPosition}");
		// Console.WriteLine($"Calculated coord {_pieceTable.GetCoordinate(_pieceTable.GetOffset(CaretPosition))}");
		// Console.WriteLine();
		// Console.WriteLine(_pieceTable.PrintCleanLine(CaretPosition.Line));
		// Console.WriteLine(GetCoordinate(CaretRange.Cursor));
		// Console.WriteLine(_pieceTable.LineCount());
		// Console.WriteLine(_pieceTable.LineFeedCount);
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
enum MotionKind
{
	NextWordStart,
	NextWordEnd,
}
