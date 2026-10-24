using System.Buffers;
using System.Drawing;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection.Metadata;
using System.Text;
using Sekani.Integrations.TreeSitter;

namespace Sekani.EditorCore;

public sealed class SekaniDocument
{
	public bool IsReadOnly { get; set; } = false;
	public Coordinate CaretPosition
	{
		get
		{
			var position = _pieceTable.GetCoordinate(CaretRange.Cursor);
			position.TrailVisualLine = CaretRange.TrailVisualLine;
			return position;
		}
	}
	private LineCache? _lineCache;
	private TreeSitterClient _treeSitterClient;

	public Range CaretRange { get; private set; } = new Range(0);
	public int LineCount => _pieceTable.LineFeedCount;
	public int BufferLength => _pieceTable.BufferLength;
	public bool TreeSitterClientActive => _treeSitterClient.Active;
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
	public void SetDocumentCaretRangeHead(int anchor, int head)
	{
		CaretRange = new(anchor, head, head);
	}

	public List<TreeSitterHighlightSpan> GetHighlights(int start, int end)
	{
		return _treeSitterClient.GetHighlights(start, end);
	}

	public int GetOffset(Coordinate pos) => _pieceTable.GetOffset(pos);
	public Coordinate GetCoordinate(int offset) => _pieceTable.GetCoordinate(offset);
	private string? _filePath = null;

	//piece table stuff;
	private PieceTable _pieceTable;

	public SekaniDocument()
	{
		_pieceTable = new("");
		_treeSitterClient = new(_pieceTable.Slice);
		_treeSitterClient.ParseBuffer("");
		SubscribeToEvents();
	}

	~SekaniDocument()
	{
		_treeSitterClient.Dispose();
	}

	public SekaniDocument(string filePath)
	{
		_filePath = filePath;
		var buffer = File.ReadAllText(filePath);
		_pieceTable = new(buffer);
		_treeSitterClient = new(_pieceTable.Slice);
		_treeSitterClient.ParseBuffer(buffer);
		SubscribeToEvents();
	}

	private void SubscribeToEvents()
	{
		_pieceTable.BufferNetChangedEvent += BufferNetChanged;
	}

	private void BufferNetChanged(object? sender, BufferNetChangeData e)
	{
		EditTreeSitter(e);
	}

	//inform tree-sitter of changes to the buffer in memory
	private void EditTreeSitter(BufferNetChangeData e)
	{
		//convert all offsets to bytes
		TSInputEdit edit;
		if (e.Operation == BufferOperation.Insertion)
		{
			var start_byte = e.StartOffset * sizeof(ushort);
			var old_end_byte = start_byte;
			var new_end_byte = start_byte + e.Length * sizeof(ushort);

			var start_coord = GetCoordinate(e.StartOffset);
			var start_point = new TSPoint((uint)start_coord.Line, (uint)start_coord.Col * sizeof(ushort));
			var old_end_point = start_point;
			var new_end_coord = GetCoordinate(e.StartOffset + e.Length);
			var new_end_point = new TSPoint((uint)new_end_coord.Line, (uint)new_end_coord.Col * sizeof(ushort));

			edit = new()
			{
				start_byte = (uint)start_byte,
				old_end_byte = (uint)old_end_byte,
				new_end_byte = (uint)new_end_byte,

				start_point = start_point,
				old_end_point = old_end_point,
				new_end_point = new_end_point
			};
		}
		else
		{
			var start_byte = e.StartOffset * sizeof(ushort);
			var old_end_byte = start_byte + (e.Length * sizeof(ushort));
			var new_end_byte = start_byte;

			var start_coord = GetCoordinate(e.StartOffset);
			var start_point = new TSPoint((uint)start_coord.Line, (uint)start_coord.Col * sizeof(ushort));
			var old_end_coord = e.OldEndPointBeforeDelete;
			var old_end_point = new TSPoint((uint)old_end_coord!.Line, (uint)old_end_coord.Col * sizeof(ushort));
			var new_end_point = start_point;
			edit = new()
			{
				start_byte = (uint)start_byte,
				old_end_byte = (uint)old_end_byte,
				new_end_byte = (uint)new_end_byte,

				start_point = start_point,
				old_end_point = old_end_point,
				new_end_point = new_end_point
			};
		}
		_treeSitterClient.DoEdit(edit);
	}

	public void TypeChars(string text, bool invokePieceTable = true)
	{
		TypeCharsCore(CaretRange.Cursor, text);
	}
	public void CaretRight(int count)
	{
		while (count > 0)
		{
			if (CaretRange.Cursor < _pieceTable.BufferLength)
			{
				CaretRange = new(AdvanceCaret(CaretRange.Cursor, 1));
				UpdatePreferredVisualColumn();
			}
			count--;
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
	public void CaretLeft(int count)
	{
		while (count > 0)
		{
			if (CaretRange.Cursor > 0)
			{
				CaretRange = new(AdvanceCaret(CaretRange.Cursor, -1));
				UpdatePreferredVisualColumn();
			}
			count--;
		}
	}

	public void DeleteSelection()
	{
		var lower = CaretRange.Lower;
		var sethigh = GetHigherEnd(CaretRange);
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
		var startOffset = PrevGraphemeBoundary(offset);
		var diff = offset - startOffset;
		_pieceTable.Delete(startOffset, diff);
		CaretRange = new(startOffset);
		UpdatePreferredVisualColumn();
	}

	public void CaretUp(int count)
	{
		while (count > 0)
		{
			MoveCaretVertical(CaretVerticalDirection.Up);
			count--;
		}
	}

	public void CaretDown(int count)
	{
		while (count > 0)
		{
			MoveCaretVertical(CaretVerticalDirection.Down);
			count--;
		}
	}

	private int AdvanceCaret(int offset, int count)
	{
		while (count > 0)
		{
			offset = NextGraphemeBoundary(offset);
			count--;
		}

		while (count < 0)
		{
			offset = PrevGraphemeBoundary(offset);
			count++;
		}
		return offset;
	}

	private int NextGraphemeBoundary(int offset)
	{
		var pos = GetCoordinate(offset);
		var line = pos.Line;
		var layout = _lineCache!.GetOrCreate(line);
		var nextcol = layout!.NextCol(pos.Col);
		int next = offset;
		if (nextcol is null)
		{
			next++;
		}
		else
		{
			next = GetOffset(new Coordinate(nextcol.Value, line));
		}
		return next;
	}

	//TODO: currently i believe the assumption that we can just decrease the offset by one would land nicely at the edge of the previous linebreak, i have to remember that this will break with windows style CRLF
	private int PrevGraphemeBoundary(int offset)
	{
		var pos = GetCoordinate(offset);
		var line = pos.Line;
		var layout = _lineCache!.GetOrCreate(line);
		var prevcol = layout!.PrevCol(pos.Col);
		int prev = offset;
		if (prevcol is null)
		{
			prev--;
		}
		else
		{
			prev = GetOffset(new Coordinate(prevcol.Value, line));
		}
		return prev;
	}
	//TODO: Yes, this entire word selection implmentation is profoundly shitty and illegible and entirely based on trial and error. I will fix it someday maybe.
	public void SelectToWordMotion(WordSelectMotion motion, int count)
	{
		while (count > 0)
		{
			var offset = CaretRange.Cursor;
			var wordFinder = GetWordFinderForMotion(motion);
			var next = wordFinder(offset, out int? anchor, motion.motionSubKind);
			if (next is not null && anchor is not null)
			{
				SetDocumentCaretRangeHead(anchor.Value, next.Value);
			}
			count--;
		}
	}
	public LineCache CreateLineCache(int tabSize, bool wordWrap, int maxVisualColsPerLine)
	{
		_lineCache = new LineCache(_pieceTable, tabSize, wordWrap, maxVisualColsPerLine);
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
		CaretRange = new(AdvanceCaret(CaretRange.Cursor, text.Length));
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
		var text = _pieceTable.Print();
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
		var sethigh = GetHigherEnd(CaretRange);
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
		if (layout is null) return;
		var lastBoundary = layout.GraphemeBoundaries.LastOrDefault();
		CaretRange = new(GetOffset(new Coordinate(lastBoundary, line)));
	}

	public int GetHigherEnd(Range range)
	{
		var lower = CaretRange.Lower;
		var higher = CaretRange.Higher;
		if (higher == lower + 1)
		{
			higher = NextGraphemeBoundary(lower);
		}
		var sethigh = higher == CaretRange.Cursor ? higher + 1 : higher;
		return sethigh;
	}


	public void AddNewLineAboveSelection()
	{
		var lower = CaretRange.Lower;
		var pos = GetCoordinate(lower);
		var insertpos = new Coordinate(0, pos.Line);
		TypeCharsCore(GetOffset(insertpos), Environment.NewLine);
		CaretRange = new(GetOffset(insertpos));
	}


	private delegate int? WordFinder(int offset, out int? anchorPos, MotionSubKind motionSubKind);
	private WordFinder GetWordFinderForMotion(WordSelectMotion motion)
	{
		return motion.motionKind switch
		{
			MotionKind.NextWordStart => FindNextWordStart,
			MotionKind.CurrentWordStart => FindCurrentWordStart,
			MotionKind.NextWordEnd => FindNextWordEnd,
			_ => throw new ArgumentOutOfRangeException(nameof(motion))
		};
	}

	private int? FindNextWordEnd(int offset, out int? anchorPos, MotionSubKind motionSubKind)
	{
		anchorPos = null;
		if (offset >= BufferLength)
			return null;
		anchorPos = FindAnchorPos(offset, MotionKind.NextWordEnd);
		var current = anchorPos.Value;
		if (current >= BufferLength)
			return current;
		var initialToken = CharTokenKind(GetGraphemeAtOffset(current));
		var prevOffset = current;
		while (true)
		{
			var next = AdvanceCaret(prevOffset, 1);
			if (next >= BufferLength)
				return next;
			var newToken = CharTokenKind(GetGraphemeAtOffset(next));
			if (CanEndWordEndScan(ref initialToken, newToken, motionSubKind))
			{
				if (prevOffset != offset) return prevOffset;
				initialToken = newToken;
				prevOffset = next;
				continue;
			}
			prevOffset = next;
		}
	}

	private int? FindNextWordStart(int offset, out int? anchorPos, MotionSubKind motionSubKind)
	{
		anchorPos = null;
		if (offset >= BufferLength) return null;
		anchorPos = FindAnchorPos(offset, MotionKind.NextWordStart);
		var current = anchorPos.Value;
		if (current >= BufferLength)
			return current;
		var initialToken = CharTokenKind(GetGraphemeAtOffset(current));
		var prevOffset = current;
		while (true)
		{
			var next = AdvanceCaret(prevOffset, 1);
			if (next >= BufferLength) return next;
			var newToken = CharTokenKind(GetGraphemeAtOffset(next));
			if (CanEndNextWordStartScan(ref initialToken, newToken, motionSubKind))
				return prevOffset;

			prevOffset = next;
		}
	}

	private int? FindCurrentWordStart(int offset, out int? anchorPos, MotionSubKind motionSubKind)
	{
		anchorPos = null;
		if (offset == 0) return null;
		anchorPos = FindAnchorPos(offset, MotionKind.CurrentWordStart);

		//since we're going back we can start from offset instead of anchor pos
		var current = offset;
		if (current == 0) return null;
		var initialToken = CharTokenKind(GetGraphemeAtOffset(current));
		var prevOffset = current;
		bool firstIter = true;
		while (true)
		{
			var next = AdvanceCaret(prevOffset, -1);
			if (next == 0) return next;
			var newToken = CharTokenKind(GetGraphemeAtOffset(next));
			if (firstIter)
			{
				firstIter = false;
				if (initialToken != SelectionTokenKind.Whitespace && newToken != initialToken)
				{
					initialToken = newToken;
					prevOffset = next;
					continue;
				}
			}
			if (CanEndCurrentWordStartScan(ref initialToken, newToken, motionSubKind))
			{
				if (prevOffset != offset) return prevOffset;
				initialToken = newToken;
				prevOffset = next;
				continue;
			}
			prevOffset = next;
		}
	}

	private int FindAnchorPos(int offset, MotionKind motionKind)
	{
		int advance = offset;
		int advanceWidth = motionKind == MotionKind.CurrentWordStart ? -1 : 1;
		var currentToken = CharTokenKind(GetGraphemeAtOffset(offset));
		{
			if (currentToken is SelectionTokenKind.Eol)
			{
				while (currentToken is SelectionTokenKind.Eol)
				{
					advance = AdvanceCaret(advance, advanceWidth);
					if (advance >= BufferLength) return advance;
					currentToken = CharTokenKind(GetGraphemeAtOffset(advance));
				}
				return motionKind == MotionKind.CurrentWordStart ? NextGraphemeBoundary(advance) : advance;
			}
		}
		var next = AdvanceCaret(advance, advanceWidth);
		var nextToken = CharTokenKind(GetGraphemeAtOffset(next));
		{
			advance = next;
			if (nextToken is SelectionTokenKind.Eol)
			{
				while (nextToken is SelectionTokenKind.Eol)
				{
					advance = AdvanceCaret(advance, advanceWidth);
					if (advance >= BufferLength) return advance;
					nextToken = CharTokenKind(GetGraphemeAtOffset(advance));
				}
				return motionKind == MotionKind.CurrentWordStart ? NextGraphemeBoundary(advance) : advance;
			}
		}

		if (motionKind is MotionKind.CurrentWordStart)
		{
			//block cursor semantics
			if (currentToken == nextToken || currentToken is SelectionTokenKind.Whitespace) return NextGraphemeBoundary(offset);
			return NextGraphemeBoundary(next);
		}
		if (motionKind is MotionKind.NextWordStart)
		{
			if (currentToken == nextToken || nextToken is SelectionTokenKind.Whitespace) return offset;
		}

		if (motionKind == MotionKind.NextWordEnd)
		{
			if (currentToken == SelectionTokenKind.Whitespace || currentToken == nextToken) return offset;
		}
		return next;
	}

	private bool CanEndCurrentWordStartScan(ref SelectionTokenKind originalToken, SelectionTokenKind newToken, MotionSubKind motionSubKind)
	{
		if (motionSubKind == MotionSubKind.Word)
		{

			if (originalToken != newToken && newToken == SelectionTokenKind.Eol)
			{
				return true;
			}
			if (originalToken == SelectionTokenKind.Whitespace || originalToken == SelectionTokenKind.Eol)
			{
				originalToken = newToken;
				return false;
			}
			if (newToken != originalToken)
			{
				return true;
			}
			return false;
		}
		else
		{
			if (originalToken != newToken && newToken == SelectionTokenKind.Eol)
			{
				return true;
			}
			if (originalToken == SelectionTokenKind.Whitespace || originalToken == SelectionTokenKind.Eol)
			{
				originalToken = newToken;
				return false;
			}
			return newToken == SelectionTokenKind.Whitespace || newToken == SelectionTokenKind.Eol;
		}
	}
	private bool CanEndNextWordStartScan(ref SelectionTokenKind originalToken, SelectionTokenKind newToken, MotionSubKind motionSubKind)
	{
		if (motionSubKind == MotionSubKind.Word)
		{
			if (newToken == SelectionTokenKind.Whitespace)
			{
				originalToken = newToken;
				return false;
			}
			if (newToken != originalToken) return true;
			return false;
		}
		else
		{
			if (newToken == SelectionTokenKind.Whitespace)
			{
				originalToken = newToken;
				return false;
			}
			return originalToken == SelectionTokenKind.Whitespace || newToken == SelectionTokenKind.Eol;
		}
	}

	private bool CanEndWordEndScan(ref SelectionTokenKind originalToken, SelectionTokenKind newToken, MotionSubKind motionSubKind)
	{
		if (motionSubKind == MotionSubKind.Word)
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
			if (newToken != originalToken) return true;
			return false;
		}
		else
		{
			if (newToken == SelectionTokenKind.Whitespace && originalToken != SelectionTokenKind.Whitespace || newToken == SelectionTokenKind.Eol)
			{
				return true;
			}
			if (originalToken == SelectionTokenKind.Whitespace && originalToken != newToken)
			{
				originalToken = newToken;
				return false;
			}
			if (newToken == SelectionTokenKind.Whitespace && originalToken != newToken) return true;
			return false;
		}
	}

	public ReadOnlySpan<char> GetGraphemeAtOffset(int offset)
	{
		var pos = GetCoordinate(offset);
		var layout = _lineCache!.GetOrCreate(pos.Line);
		return layout!.GraphemeAt(pos.Col);
	}

	private SelectionTokenKind CharTokenKind(ReadOnlySpan<char> grapheme)
	{
		if (grapheme.IsEmpty)
			return SelectionTokenKind.Unknown;
		if (grapheme.SequenceEqual("\n") ||
			grapheme.SequenceEqual("\r") ||
			grapheme.SequenceEqual("\r\n") ||
			grapheme.SequenceEqual("\u000B") ||
			grapheme.SequenceEqual("\u000C") ||
			grapheme.SequenceEqual("\u0085") ||
			grapheme.SequenceEqual("\u2028") ||
			grapheme.SequenceEqual("\u2029"))
			return SelectionTokenKind.Eol;

		if (grapheme.Trim().IsEmpty)
			return SelectionTokenKind.Whitespace;

		var status = Rune.DecodeFromUtf16(
			grapheme,
			out var rune,
			out _);

		if (status != OperationStatus.Done)
			return SelectionTokenKind.Unknown;

		if (Rune.IsLetterOrDigit(rune) || rune.Value == '_')
			return SelectionTokenKind.AlphaNumerical;

		switch (Rune.GetUnicodeCategory(rune))
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
		var builder = new StringBuilder();
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
public enum MotionKind
{
	NextWordStart,
	NextWordEnd,
	CurrentWordStart,
}
public enum MotionSubKind
{
	Word,
	WORD,
}

public readonly record struct WordSelectMotion(MotionKind motionKind, MotionSubKind motionSubKind);
