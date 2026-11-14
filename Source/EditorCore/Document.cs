using System.Buffers;
using System.Globalization;
using System.Text;
using Sekani.Integrations.TreeSitter;

namespace Sekani.EditorCore;

public sealed class SekaniDocument
{
	public bool IsReadOnly { get; set; } = false;
	private CancellationTokenSource _searchCancellationToken = new();
	private readonly Clipboard? _clipboard = null!;

	public Coordinate PrimaryCursorPosition
	{
		get
		{
			var position = _pieceTable.GetCoordinate(PrimaryCursorRange.Cursor);
			position.TrailVisualLine = PrimaryCursorRange.TrailVisualLine;
			return position;
		}
	}
	private LineCache? _lineCache;
	private TreeSitterClient _treeSitterClient;

	public SelectionSet Selections { get; private set; }

	public Range PrimaryCursorRange => Selections.Ranges[Selections.PrimaryIndex];
	public int LineCount => _pieceTable.LineFeedCount;
	public int BufferLength => _pieceTable.BufferLength;
	public bool TreeSitterClientActive => _useTreesitter && _treeSitterClient.Active;
	private int _preferredVisualColumn = 0;
	private bool _useTreesitter = true;


	public void SetCaretRange(Coordinate pos)
	{
		Selections.Ranges[Selections.PrimaryIndex] = new(_pieceTable.GetOffset(pos));
	}
	public void SetCaret(int anchor, bool trailVisualLine = false)
	{
		Selections.Ranges[Selections.PrimaryIndex] = new(anchor, trailVisualLine);
	}

	public void SetCaret(Range range)
	{
		Selections.Ranges[Selections.PrimaryIndex] = range;
	}

	public void MoveHead(int pos, bool trailVisualLine = false)
	{
		var r = PrimaryCursorRange;
		int anchor = r.Anchor;
		bool forward = r.Head > anchor;
		if (forward && pos < anchor)
		{
			anchor = GetPosAdvance(anchor, 1);
		}
		else if (!forward && pos >= anchor - 1)
		{
			anchor = GetPosAdvance(anchor, -1);
		}
		int head = pos < anchor ? pos : GetPosAdvance(pos, 1);
		Selections.Ranges[Selections.PrimaryIndex] = new Range(anchor, head, pos, trailVisualLine);
	}
	public void SetAnchorAndHead(int anchor, int head, bool trailVisualLine = false)
	{
		Selections.Ranges[Selections.PrimaryIndex] = new(anchor, head, head, trailVisualLine);
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
	private bool _holdResetTransaction = false;

	void InitialSelection()
	{
		Selections = new([new(0)], 0);
	}

	public SekaniDocument(Clipboard? clipboard = null)
	{
		_pieceTable = new("");
		_clipboard = clipboard;
		InitialSelection();
		BeginTransaction(PrimaryCursorRange);
		if (_useTreesitter)
		{
			_treeSitterClient = new(_pieceTable.Slice);
			_treeSitterClient.ParseBuffer("");
		}
		SubscribeToEvents();
	}

	~SekaniDocument()
	{
		InitialSelection();
		_treeSitterClient.Dispose();
	}

	public SekaniDocument(string filePath, Clipboard? clipboard = null)
	{
		_filePath = filePath;
		var buffer = File.ReadAllText(filePath);
		InitialSelection();
		_pieceTable = new(buffer);
		_clipboard = clipboard;
		BeginTransaction(PrimaryCursorRange);
		if (_useTreesitter)
		{
			_treeSitterClient = new(_pieceTable.Slice);
			_treeSitterClient.ParseBuffer(buffer);
		}
		SubscribeToEvents();
	}

	public void BeginTransaction(Range range)
	{
		if (_holdResetTransaction)
		{
			_holdResetTransaction = false;
			return;
		}
		_pieceTable.BeginNewTransaction(range);
	}

	public void HoldResetTransaction()
	{
		_holdResetTransaction = true;
	}

	private void CommitTransaction()
	{
		_pieceTable.CommitTransaction();
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
		if (!TreeSitterClientActive) return;
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
		TypeCharsCore(PrimaryCursorRange.Cursor, text);
	}

	public void UpdatePreferredVisualColumn()
	{
		if (_lineCache is null)
			return;
		var pos = _pieceTable.GetCoordinate(PrimaryCursorRange.Cursor);
		var layout = _lineCache.GetOrCreate(pos.Line);

		if (layout is null)
			return;

		var visual = layout.GetVisualCoordinate(PrimaryCursorPosition);

		_preferredVisualColumn = visual.VisualCol;
	}

	private int? MoveCaretVertical(CaretVerticalDirection direction, out bool trailVisualLine)
	{
		trailVisualLine = false;
		if (_lineCache is null)
			throw new InvalidOperationException();

		var pos = PrimaryCursorPosition;
		var layout = _lineCache.GetOrCreate(pos.Line);

		if (layout is null)
			return null;

		if (layout.VisualLines.Count > 1)
		{
			var visualLineIndex =
				layout.GetVisualCoordinate(PrimaryCursorPosition).VisualLine;
			if (direction == CaretVerticalDirection.Down &&
				visualLineIndex < layout.VisualLines.Count - 1)
			{
				var targetVisualPos = new VisualCoordinate(
					_preferredVisualColumn,
					visualLineIndex + 1);
				Coordinate newpos = new(
					layout.GetLogicalColumn(targetVisualPos),
					PrimaryCursorPosition.Line);
				if (newpos != PrimaryCursorPosition)
				{
					var offset = _pieceTable.GetOffset(newpos);

					// CaretRange = new Range(offset, trailVisualLine: _preferredVisualColumn == 0);
					trailVisualLine = _preferredVisualColumn == 0;
					return offset;
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
					PrimaryCursorPosition.Line)
				{
					TrailVisualLine = _preferredVisualColumn == 0
				};
				var offset = _pieceTable.GetOffset(newpos);
				// CaretRange = new Range(offset, trailVisualLine: _preferredVisualColumn == 0);
				trailVisualLine = _preferredVisualColumn == 0;
				return offset;
			}
		}
		if (direction == CaretVerticalDirection.Up)
		{
			if (pos.Line == 0)
				return null;

			var targetLine = pos.Line - 1;
			var targetLineLayout = _lineCache.GetOrCreate(targetLine);

			if (targetLineLayout is null)
				return null;

			var targetVisual = new VisualCoordinate(
				_preferredVisualColumn,
				targetLineLayout.VisualLines.Count - 1);

			Coordinate newpos = new(
				targetLineLayout.GetLogicalColumn(targetVisual),
				PrimaryCursorPosition.Line - 1)
			{
				TrailVisualLine = _preferredVisualColumn == 0
			};
			var offset = _pieceTable.GetOffset(newpos);
			// CaretRange = new Range(offset, trailVisualLine: _preferredVisualColumn == 0);
			trailVisualLine = _preferredVisualColumn == 0;
			return offset;
		}
		else
		{
			if (pos.Line > LineCount)
				return null;

			var targetLine = pos.Line + 1;
			var targetLineLayout = _lineCache.GetOrCreate(targetLine);

			if (targetLineLayout is null)
				return null;

			var targetVisual = new VisualCoordinate(
				_preferredVisualColumn,
				0);

			Coordinate newpos = new(
				targetLineLayout.GetLogicalColumn(targetVisual),
				PrimaryCursorPosition.Line + 1);
			if (newpos.Line >= _pieceTable.LineFeedCount) return null;
			var offset = _pieceTable.GetOffset(newpos);
			// CaretRange = new Range(offset);
			trailVisualLine = _preferredVisualColumn == 0;
			return offset;
		}
	}
	public void CaretRight(int count, bool extendSelection) =>
		MoveCaretHorizontal(count, 1, extendSelection);

	public void CaretLeft(int count, bool extendSelection) =>
		MoveCaretHorizontal(count, -1, extendSelection);

	private void MoveCaretHorizontal(int count, int direction, bool extendSelection)
	{
		BeginTransaction(PrimaryCursorRange);
		while (count-- > 0)
		{
			var cursor = PrimaryCursorRange.Cursor;

			if (direction > 0 ? cursor >= _pieceTable.BufferLength : cursor <= 0)
				break;

			var offset = GetPosAdvance(cursor, direction);

			if (extendSelection)
				MoveHead(offset);
			else
				SetCaret(offset);

			UpdatePreferredVisualColumn();
		}
	}
	public void DeleteSelection(bool noHistoryCommit = false)
	{
		BeginTransaction(PrimaryCursorRange);
		var lower = PrimaryCursorRange.Lower;
		var sethigh = GetHigherEnd(PrimaryCursorRange);
		//clamp sethigh
		sethigh = Math.Min(sethigh, _pieceTable.BufferLength);
		var delta = sethigh - lower;
		_pieceTable.Delete(lower, delta);
		Selections.Ranges[Selections.PrimaryIndex] = new(lower);
		if (!noHistoryCommit)
			CommitTransaction();
	}

	public void BackSpace(Coordinate pos)
	{
		var offset = PrimaryCursorRange.Cursor;
		if (offset == 0) return;
		var startOffset = PrevGraphemeBoundary(offset);
		var diff = offset - startOffset;
		_pieceTable.Delete(startOffset, diff);
		Selections.Ranges[Selections.PrimaryIndex] = new(startOffset);
		UpdatePreferredVisualColumn();
	}

	public void CaretUp(int count, bool extendSelection) =>
		MoveCaretVertical(count, CaretVerticalDirection.Up, extendSelection);

	public void CaretDown(int count, bool extendSelection) =>
		MoveCaretVertical(count, CaretVerticalDirection.Down, extendSelection);

	private void MoveCaretVertical(int count, CaretVerticalDirection direction, bool extendSelection)
	{
		BeginTransaction(PrimaryCursorRange);
		while (count-- > 0)
		{
			var offset = MoveCaretVertical(direction, out bool trailVisualLine);
			if (offset is null) break;
			if (extendSelection)
			{
				MoveHead(offset.Value, trailVisualLine);
			}
			else
			{
				SetCaret(offset.Value, trailVisualLine);
			}
		}
	}
	private int GetPosAdvance(int offset, int count)
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

	public int NextGraphemeBoundary(int offset)
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
	public int PrevGraphemeBoundary(int offset)
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
	public void SelectToWordMotion(WordSelectMotion motion, int count, bool extendSelection)
	{
		while (count > 0)
		{
			var offset = PrimaryCursorRange.Cursor;
			var wordFinder = GetWordFinderForMotion(motion);
			var next = wordFinder(offset, out int? anchor, motion.motionSubKind);
			if (next is not null && anchor is not null)
			{
				if (!extendSelection)
					SetAnchorAndHead(anchor.Value, next.Value);
				else
					MoveHead(next.Value);
			}
			count--;
		}
	}
	public LineCache CreateLineCache(int tabSize, bool wordWrap, int maxVisualColsPerLine)
	{
		_lineCache = new LineCache(_pieceTable, tabSize, wordWrap, maxVisualColsPerLine);
		return _lineCache;
	}

	public void SelectAll()
	{
		var head = BufferLength - 1;
		SetAnchorAndHead(0, head);
	}

	public void Undo(int motionCount)
	{
		_pieceTable.DoUndo(out Range? range);
		if (range is not null) Selections.Ranges[Selections.PrimaryIndex] = range.Value;
	}

	public void Redo(int motionCount)
	{
		_pieceTable.DoRedo(out Range? range);
		if (range is not null) Selections.Ranges[Selections.PrimaryIndex] = range.Value;
	}
	public void AddNewLineUnderSelection()
	{
		BeginTransaction(PrimaryCursorRange);
		var offset = PrimaryCursorRange.Width > 1 ? PrimaryCursorRange.Higher : PrimaryCursorRange.Lower;
		var pos = GetCoordinate(offset);
		var text = _pieceTable.PrintCleanLine(pos.Line);
		var insertpos = new Coordinate(text.Length, pos.Line);
		TypeCharsCore(GetOffset(insertpos), Environment.NewLine);
		var newpos = new Coordinate(0, pos.Line + 1);
		Selections.Ranges[Selections.PrimaryIndex] = new(GetOffset(newpos));
		// CommitTransaction();
	}

	private void TypeCharsCore(int offset, string text)
	{
		_pieceTable.Insert(text, offset);
		SetCaret(PrimaryCursorRange.Cursor + text.Length);
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
		var lower = PrimaryCursorRange.Lower;
		var higher = PrimaryCursorRange.Higher;
		if (!PrimaryCursorRange.HasActiveSelection) return;
		Selections.Ranges[Selections.PrimaryIndex] = new(higher, lower, lower);
	}

	public void PlaceCursorAfterSelection()
	{
		// var lower = CaretRange.Lower;
		// var sethigh = GetHigherEnd(CaretRange);
		// MoveHead(sethigh);
	}

	public void PlaceCursorAtLineStart(bool extendSelection)
	{
		var line = PrimaryCursorPosition.Line;
		var text = _pieceTable.PrintCleanLine(line);
		int firstNonWhiteSpace = 0;

		for (int i = 0; i < text.Length; i++)
		{
			if (!char.IsWhiteSpace(text[i])) break;
			firstNonWhiteSpace++;
		}
		var pos = new Coordinate(firstNonWhiteSpace, line);
		var offset = GetOffset(pos);
		if (extendSelection)
			MoveHead(offset);
		else
			SetCaret(offset);
	}

	public void SelectLine(int count)
	{
		while (count > 0)
		{
			var range = PrimaryCursorRange;
			var pos = GetCoordinate(range.Cursor);
			var layout = _lineCache!.GetOrCreate(pos.Line);
			var linestart = GetOffset(new(0, pos.Line));
			var lineend = GetOffset(new(layout.GraphemeBoundaries.LastOrDefault(), pos.Line));

			if (PrimaryCursorRange.Lower <= linestart && PrimaryCursorRange.Cursor == lineend)
			{
				var nextline = pos.Line + 1;
				layout = _lineCache.GetOrCreate(nextline);
				if (layout is null) return;
				var nextlineend = layout.GraphemeBoundaries.LastOrDefault();
				MoveHead(GetOffset(new(nextlineend, nextline)));
			}
			else
			{
				if (PrimaryCursorRange.Lower <= linestart)
				{
					MoveHead(lineend);
				}
				else
				{
					SetAnchorAndHead(linestart, lineend);
				}
			}
			count--;
		}
	}
	public void PlaceCursorAtLineEnd(bool extendSelection)
	{
		var line = PrimaryCursorPosition.Line;
		var layout = _lineCache!.GetOrCreate(line);
		if (layout is null) return;
		var lastBoundary = layout.GraphemeBoundaries.LastOrDefault();
		var offset = GetOffset(new Coordinate(lastBoundary, line));
		if (extendSelection)
			MoveHead(offset);
		else
			SetCaret(offset);
	}

	public int GetHigherEnd(Range range)
	{
		var lower = PrimaryCursorRange.Lower;
		var higher = PrimaryCursorRange.Higher;
		if (higher == lower + 1)
		{
			higher = NextGraphemeBoundary(lower);
		}
		var sethigh = higher == PrimaryCursorRange.Cursor ? higher + 1 : higher;
		return sethigh;
	}


	public void AddNewLineAboveSelection()
	{
		BeginTransaction(PrimaryCursorRange);
		var lower = PrimaryCursorRange.Lower;
		var pos = GetCoordinate(lower);
		var insertpos = new Coordinate(0, pos.Line);
		TypeCharsCore(GetOffset(insertpos), Environment.NewLine);
		Selections.Ranges[Selections.PrimaryIndex] = new(GetOffset(insertpos));
		// CommitTransaction();
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
			var next = GetPosAdvance(prevOffset, 1);
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
			var next = GetPosAdvance(prevOffset, 1);
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
			var next = GetPosAdvance(prevOffset, -1);
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
					advance = GetPosAdvance(advance, advanceWidth);
					if (advance >= BufferLength) return advance;
					currentToken = CharTokenKind(GetGraphemeAtOffset(advance));
				}
				return motionKind == MotionKind.CurrentWordStart ? NextGraphemeBoundary(advance) : advance;
			}
		}
		var next = GetPosAdvance(advance, advanceWidth);
		var nextToken = CharTokenKind(GetGraphemeAtOffset(next));
		{
			advance = next;
			if (nextToken is SelectionTokenKind.Eol)
			{
				while (nextToken is SelectionTokenKind.Eol)
				{
					advance = GetPosAdvance(advance, advanceWidth);
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
		Console.WriteLine(_pieceTable.Print());

	}

	public void YankSelection()
	{
		var higherend = GetHigherEnd(PrimaryCursorRange);
		int width = higherend - PrimaryCursorRange.Lower;
		var buf = new char[higherend - PrimaryCursorRange.Lower];
		var written = _pieceTable.Slice(PrimaryCursorRange.Lower, width, buf.AsSpan());
		var text = buf.AsSpan()[..written];
		_clipboard!.Yank(text.ToString());
	}

	public void PasteAfterCursor()
	{
		BeginTransaction(PrimaryCursorRange);
		var offset = PrimaryCursorRange.Cursor;
		var text = _clipboard!.Paste().GetAwaiter().GetResult();
		if (text is null) return;
		TypeCharsCore(offset, text);
		CommitTransaction();
	}

	//UNUSED
	// public List<SearchHit> Find(string text, int startOffset, int endOffset)
	// {
	// 	var hits = _pieceTable.Search(text, startOffset, endOffset, _searchCancellationToken);
	// 	if (hits.Count == 0) return [];
	// 	SetAnchorAndHead(hits[0].Offset, hits[0].Offset + hits[0].Length - 1);
	// 	return hits;
	// }

	//Search the whole document for the query
	public async Task<(List<SearchHit> Hits, int TargetIndex)> FindInDocumentAsync(
		string text,
		int offset)
	{
		_searchCancellationToken.Cancel();
		_searchCancellationToken = new();
		try
		{
			var hits = await _pieceTable.SearchAsync(
				text, 0, BufferLength, _searchCancellationToken.Token);

			if (hits.Count == 0)
				return ([], 0);

			int targetIndex = hits.FindIndex(p => p.Offset >= offset);
			if (targetIndex == -1)
				targetIndex = 0;

			var targetHit = hits[targetIndex];

			SetAnchorAndHead(
				targetHit.Offset,
				targetHit.Offset + targetHit.Length - 1);

			return (hits, targetIndex);
		}
		catch (OperationCanceledException)
		{
			return ([], 0);
		}
	}

	public void DuplicateRanges(int count = 1, CaretVerticalDirection direction = CaretVerticalDirection.Down)
	{
		if (_lineCache is null) return;
		List<Range> range = new();
		

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
