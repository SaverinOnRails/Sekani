using System.Buffers;
using System.ComponentModel;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
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
			var position = _pieceTable.GetCoordinate(PrimaryCursorRange.Head);
			position.TrailVisualLine = PrimaryCursorRange.TrailVisualLine;
			return position;
		}
	}
	private LineCache? _lineCache;
	private LineCache? _nonWrappingLineCache;
	private TreeSitterClient _treeSitterClient;

	public SelectionSet Selections { get; set; }


	public Range PrimaryCursorRange => Selections.Ranges[Selections.PrimaryIndex];
	public int LineCount => _pieceTable.LineFeedCount;
	public int BufferLength => _pieceTable.BufferLength;
	public bool TreeSitterClientActive => _useTreesitter && _treeSitterClient.Active;
	private int _preferredVisualColumn = 0;
	private bool _useTreesitter = true;


	public void SetCursor(Coordinate pos)
	{
		Selections = Selections.WithPrimaryRange(new(_pieceTable.GetOffset(pos)));
	}
	public void SetCursor(Range range)
	{
		Selections = Selections.WithPrimaryRange(range);
	}
	public void SetCursor(int anchor, int index, bool trailVisualLine = false)
	{
		Selections = Selections.WithRange(index, new(anchor, trailVisualLine));
	}

	public void SetCursor(List<Range> workingRanges, int anchor, int index, bool trailVisualLine = false)
	{
		workingRanges[index] = new(anchor, trailVisualLine);
	}

	public void MoveHead(int pos, int index, bool trailVisualLine = false)
	{
		Selections = Selections.WithHead(index, pos, GetPosAdvance, trailVisualLine);
	}

	private void MoveHead(List<Range> workingRanges, int pos, int index, bool trailVisualLine = false)
	{
		workingRanges[index] = workingRanges[index].MoveHead(pos, GetPosAdvance, trailVisualLine);
	}

	public void SetAnchorAndHead(int anchor, int head, bool trailVisualLine = false)
	{
		Selections = Selections.WithPrimaryRange(new(anchor, head, trailVisualLine));
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
		BeginTransaction();
		if (_useTreesitter)
		{
			_treeSitterClient = new(_pieceTable.Slice);
			_treeSitterClient.ParseBuffer("");
		}
		SubscribeToEvents();
	}

	~SekaniDocument()
	{
		if (_useTreesitter)
		{
			_treeSitterClient.Dispose();
		}
	}

	public SekaniDocument(string filePath, Clipboard? clipboard = null)
	{
		_filePath = filePath;
		var buffer = File.ReadAllText(filePath);
		InitialSelection();
		_pieceTable = new(buffer);
		_clipboard = clipboard;
		BeginTransaction();
		if (_useTreesitter)
		{
			_treeSitterClient = new(_pieceTable.Slice);
			_treeSitterClient.ParseBuffer(buffer);
		}
		SubscribeToEvents();
	}

	public void BeginTransaction()
	{
		if (_holdResetTransaction)
		{
			_holdResetTransaction = false;
			return;
		}
		_pieceTable.BeginNewTransaction(Selections);
	}

	public void HoldResetTransaction()
	{
		_holdResetTransaction = true;
	}

	public void CommitTransaction()
	{
		_pieceTable.CommitTransaction(Selections);
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
		_treeSitterClient.ApplyEdit(edit);
	}

	public void TypeChars(string text, bool invokePieceTable = true)
	{
		int delta = 0;
		ForEachCursor((workingRanges, index) =>
		{
			TypeCharsCore(workingRanges, index, text, ref delta);
		});
	}

	public void UpdatePreferredVisualColumn()
	{
		if (_lineCache is null)
			return;
		var pos = _pieceTable.GetCoordinate(PrimaryCursorRange.Head);
		var layout = _lineCache.GetOrCreate(pos.Line);

		if (layout is null)
			return;

		var visual = layout.GetVisualCoordinate(PrimaryCursorPosition);

		_preferredVisualColumn = visual.VisualCol;
	}

	private int? MoveCaretVertical(Range range, CaretVerticalDirection direction, out bool trailVisualLine)
	{
		trailVisualLine = false;
		if (_lineCache is null)
			throw new InvalidOperationException();

		var pos = _pieceTable.GetCoordinate(range.Head);
		pos.TrailVisualLine = range.TrailVisualLine;
		var layout = _lineCache.GetOrCreate(pos.Line);

		if (layout is null)
			return null;

		if (layout.VisualLines.Count > 1)
		{
			var visualLineIndex =
				layout.GetVisualCoordinate(pos).VisualLine;
			if (direction == CaretVerticalDirection.Down &&
				visualLineIndex < layout.VisualLines.Count - 1)
			{
				var targetVisualPos = new VisualCoordinate(
					_preferredVisualColumn,
					visualLineIndex + 1);
				Coordinate newpos = new(
					layout.GetLogicalColumn(targetVisualPos),
					pos.Line);
				if (newpos != pos)
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
					pos.Line)
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
				pos.Line - 1)
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
				pos.Line + 1);
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
		BeginTransaction();
		ForEachCursor((workingRanges, index) =>
		{
			for (int step = 0; step < count; step++)
			{
				var cursor = workingRanges[index];
				var head = cursor.Head;

				if (direction > 0 ? head >= _pieceTable.BufferLength : head <= 0)
					break;

				var offset = GetPosAdvance(head, direction);

				if (extendSelection)
					MoveHead(workingRanges, offset, index);
				else
					SetCursor(workingRanges, offset, index);

			}
		});

		UpdatePreferredVisualColumn();
	}

	private void DeleteCharsCore(List<Range> workingRanges, int cursorIndex, int startOffset, int length, ref int delta)
	{
		var deletePos = startOffset - delta;
		if (length > 0)
		{
			_pieceTable.Delete(deletePos, length);
			SetCursor(workingRanges, deletePos, cursorIndex);
			delta += length;
		}
	}
	public void DeleteSelection(bool noHistoryCommit = false)
	{
		BeginTransaction();
		var spans = new (int Start, int Length)[Selections.Count];
		for (int i = 0; i < Selections.Count; i++)
		{
			var textspan = Selections[i].GetTextSpan(GetPosAdvance);
			var start = textspan.Start;
			var end = Math.Min(textspan.End, BufferLength);
			spans[i] = (start, Math.Max(0, end - start));
		}
		int delta = 0;
		ForEachCursor((workingRanges, index) =>
		{
			var (start, length) = spans[index];
			DeleteCharsCore(workingRanges, index, start, length, ref delta);
		});
		if (!noHistoryCommit)
			CommitTransaction();
	}

	public void BackSpace(Coordinate pos)
	{
		int delta = 0;
		var spans = new (int Start, int Length)[Selections.Count];
		for (int i = 0; i < Selections.Count; i++)
		{
			var head = Selections[i].Head;
			if (head == 0)
			{
				spans[i] = (-1, -1);
				continue;
			}
			var start = PrevGraphemeBoundary(head);
			var length = head - start;
			spans[i] = (start, length);

		}
		ForEachCursor((workingRanges, index) =>
		{
			var (start, length) = spans[index];
			if (start != -1)
			{
				DeleteCharsCore(workingRanges, index, start, length, ref delta);
			}
		});
		UpdatePreferredVisualColumn();
	}

	public void CaretUp(int count, bool extendSelection) =>
		MoveCaretVertical(count, CaretVerticalDirection.Up, extendSelection);

	public void CaretDown(int count, bool extendSelection) =>
		MoveCaretVertical(count, CaretVerticalDirection.Down, extendSelection);

	private void MoveCaretVertical(int count, CaretVerticalDirection direction, bool extendSelection)
	{
		BeginTransaction();
		ForEachCursor((workingRanges, index) =>
		{
			for (int step = 0; step < count; step++)
			{
				var offset = MoveCaretVertical(workingRanges[index], direction, out bool trailVisualLine);
				if (offset is null) break;
				if (extendSelection)
				{
					MoveHead(workingRanges, offset.Value, index, trailVisualLine);
				}
				else
				{
					SetCursor(workingRanges, offset.Value, index, trailVisualLine);
				}
			}
		});
	}
	public int GetPosAdvance(int offset, int count)
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
		ForEachCursor((workingRanges, index) =>
		{
			for (int step = 0; step < count; step++)
			{
				var offset = workingRanges[index].Head;
				var wordFinder = GetWordFinderForMotion(motion);
				var next = wordFinder(offset, out int? anchor, motion.motionSubKind);
				if (next is not null && anchor is not null)
				{
					if (!extendSelection)
						workingRanges[index] = new(anchor.Value, next.Value);
					else
						MoveHead(workingRanges, next.Value, index);
				}
			}
		});
	}
	public LineCache CreateLineCache(int tabSize, bool wordWrap, int maxVisualColsPerLine)
	{
		_lineCache = new LineCache(_pieceTable, tabSize, wordWrap, maxVisualColsPerLine);
		_nonWrappingLineCache = new LineCache(_pieceTable, tabSize, false, 0);
		return _lineCache;
	}

	public void SelectAll()
	{
		var head = BufferLength;
		Selections = new([new(0, head)]);
	}

	public void Undo(int motionCount)
	{
		_pieceTable.DoUndo(out SelectionSet? selectionSet);
		if (selectionSet is not null)
		{
			Selections = new(selectionSet.Value);
		}
	}

	public void Redo(int motionCount)
	{
		_pieceTable.DoRedo(out SelectionSet? selectionSet);
		if (selectionSet is not null)
		{
			Selections = new(selectionSet.Value);
		}
	}
	public void AddNewLineUnderSelection()
	{
		BeginTransaction();
		var insertionOffsets = new int[Selections.Count];
		var text = Environment.NewLine;
		for (int i = 0; i < Selections.Count; i++)
		{
			var cursor = Selections[i];
			var coord = GetCoordinate(cursor.To);
			var line = coord.Line;
			var lineStartOffset = GetOffset(new Coordinate(0, line));
			var endOfLine = _lineCache!.GetOrCreate(line)!.LastGraphemeOffset();
			insertionOffsets[i] = lineStartOffset + endOfLine;
		}
		int delta = 0;
		ForEachCursor((workingRanges, index) =>
		{
			TypeCharsCore(workingRanges, index, insertionOffsets[index], insertionOffsets[index] + text.Length, text, ref delta);
		});
		UpdatePreferredVisualColumn();
	}

	private void TypeCharsCore(List<Range> workingRanges,
								int cursorIndex,
								string text,
								ref int delta)
	{
		var insertOffset = workingRanges[cursorIndex].Head;
		TypeCharsCore(workingRanges, cursorIndex, insertOffset, insertOffset + text.Length, text, ref delta);
	}

	private void TypeCharsCore(List<Range> workingRanges,
								int cursorIndex,
								int insertOffset,
								int setCursorTo,
								string text,
								ref int delta)
	{
		var insertPos = insertOffset + delta;
		_pieceTable.Insert(text, insertPos);
		workingRanges[cursorIndex] = new(setCursorTo + delta);
		// SetCursor(setCursorTo + delta, cursorIndex);
		delta += text.Length;
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
		ForEachCursor((workingRanges, index) =>
		{
			var cursor = workingRanges[index];
			var lower = cursor.From;
			var higher = cursor.To;
			if (!cursor.HasActiveSelection) return;
			workingRanges[index] = new(higher, lower);
		});
	}

	private void ForEachCursor(Action<List<Range>, int> action)
	{
		//cursors are already sorted
		List<Range> workingRanges = [.. Selections.Ranges];
		for (int i = 0; i < workingRanges.Count; i++)
		{
			action(workingRanges, i);
		}
		Selections = new SelectionSet(workingRanges, Selections.PrimaryIndex);
	}

	public void PlaceCursorAfterSelection()
	{
		// var lower = CaretRange.Lower;
		// var sethigh = GetHigherEnd(CaretRange);
		// MoveHead(sethigh);
	}

	public void PlaceCursorAtLineStart(bool extendSelection)
	{
		ForEachCursor((workingRanges, index) =>
		{
			var line = GetCoordinate(workingRanges[index].Head).Line;
			var text = _pieceTable.PrintCleanLine(line);
			int firstNonWhiteSpace = 0;

			for (int i = 0; i < text.Length; i++)
			{
				if (!char.IsWhiteSpace(text[i])) break;
				firstNonWhiteSpace++;
			}
			var offset = GetOffset(new Coordinate(firstNonWhiteSpace, line));
			if (extendSelection)
				MoveHead(workingRanges, offset, index);
			else
				SetCursor(workingRanges, offset, index);
		});
	}

	public void SelectLine(int count)
	{
		ForEachCursor((workingRanges, index) =>
		{
			for (int step = 0; step < count; step++)
			{
				var range = workingRanges[index];
				var pos = GetCoordinate(range.Head);
				var layout = _lineCache!.GetOrCreate(pos.Line);
				var linestart = GetOffset(new(0, pos.Line));
				var lineend = GetOffset(new(layout.GraphemeBoundaries.LastOrDefault(), pos.Line));

				if (range.From <= linestart && range.Head == lineend)
				{
					var nextline = pos.Line + 1;
					layout = _lineCache.GetOrCreate(nextline);
					if (layout is null) return;
					var nextlineend = layout.GraphemeBoundaries.LastOrDefault();
					MoveHead(workingRanges, GetOffset(new(nextlineend, nextline)), index);
				}
				else
				{
					if (range.From <= linestart)
					{
						MoveHead(workingRanges, lineend, index);
					}
					else
					{
						workingRanges[index] = new(linestart, lineend);
					}
				}
			}
		});
	}
	public void PlaceCursorAtLineEnd(bool extendSelection)
	{
		ForEachCursor((workingRanges, index) =>
		{
			var line = GetCoordinate(workingRanges[index].Head).Line;
			var layout = _lineCache!.GetOrCreate(line);
			if (layout is null) return;
			var lastBoundary = layout.LastGraphemeOffset();
			var offset = GetOffset(new Coordinate(lastBoundary, line));
			if (extendSelection)
				MoveHead(workingRanges, offset, index);
			else
				SetCursor(workingRanges, offset, index);
		});
	}

	// public int GetHigherEnd(Range range)
	// {
	// 	if (!range.HasActiveSelection || range.Head == range.To)
	// 	{
	// 		return NextGraphemeBoundary(range.To);
	// 	}
	// 	return range.To;
	// }


	public void AddNewLineAboveSelection()
	{
		BeginTransaction();
		var insertionOffsets = new int[Selections.Count];
		var text = Environment.NewLine;
		for (int i = 0; i < Selections.Count; i++)
		{
			var cursor = Selections[i];
			var coord = GetCoordinate(cursor.From);
			var line = coord.Line;
			var lineStartOffset = GetOffset(new Coordinate(0, line));
			insertionOffsets[i] = lineStartOffset;
		}
		int delta = 0;
		ForEachCursor((workingRanges, index) =>
		{
			TypeCharsCore(workingRanges, index, insertionOffsets[index], insertionOffsets[index] + text.Length - 1, Environment.NewLine, ref delta);
		});
		UpdatePreferredVisualColumn();
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
	//For tests
	internal string Print()
	{
		return _pieceTable.Print();
	}

	public void YankSelection()
	{
		var textspanend = PrimaryCursorRange.GetTextSpan(GetPosAdvance).End;
		int width = textspanend - PrimaryCursorRange.From;
		var buf = new char[textspanend - PrimaryCursorRange.From];
		var written = _pieceTable.Slice(PrimaryCursorRange.From, width, buf.AsSpan());
		var text = buf.AsSpan()[..written];
		_clipboard!.Yank(text.ToString());
	}

	public void PasteAfterCursor()
	{
		BeginTransaction();
		//TODO:fix this deadlock
		var text = _clipboard!.Paste().GetAwaiter().GetResult();
		if (text is null) return;
		int delta = 0;
		ForEachCursor((workingRanges, index) =>
		{
			TypeCharsCore(workingRanges, index, text, ref delta);
		});
		CommitTransaction();
	}

	public async Task<List<SearchHit>> FindInSelection(string text, SelectionSet originalSelectionSet)
	{
		_searchCancellationToken.Cancel();
		_searchCancellationToken.Dispose();
		_searchCancellationToken = new CancellationTokenSource();
		var token = _searchCancellationToken.Token;
		try
		{
			var searchTasks = new List<Task<List<SearchHit>>>(originalSelectionSet.Count);
			for (int i = 0; i < originalSelectionSet.Count; i++)
			{
				var cursor = originalSelectionSet[i];
				var textspan = cursor.GetTextSpan(GetPosAdvance);
				searchTasks.Add(_pieceTable.SearchAsync(text, textspan.Start, textspan.End, token));
			}
			IEnumerable<SearchHit>[] results = await Task.WhenAll(searchTasks);
			var searchHits = new List<SearchHit>();
			foreach (var hits in results)
			{
				searchHits.AddRange(hits);
			}

			// Create a selection for each result
			var newranges = new List<Range>(searchHits.Count);
			foreach (var p in searchHits)
			{
				var prev = PrevGraphemeBoundary(p.Offset + p.Length);
				newranges.Add(new Range(p.Offset, prev));
			}
			if (newranges.Count > 0)
			{
				Selections = new(newranges);
			}
			return searchHits;
		}
		catch (OperationCanceledException)
		{
			return [];
		}
	}
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
			var prev = PrevGraphemeBoundary(targetHit.Offset + targetHit.Length);
			SetAnchorAndHead(
				targetHit.Offset,
				prev);

			return (hits, targetIndex);
		}
		catch (OperationCanceledException)
		{
			return ([], 0);
		}
	}


	public void DuplicateRanges(int count = 1, CaretVerticalDirection direction = CaretVerticalDirection.Down)
	{
		if (_lineCache is null || _nonWrappingLineCache is null || count <= 0) return;

		List<Range> ranges = [];
		int primaryIndex = Selections.PrimaryIndex;

		for (int i = 0; i < Selections.Count; i++)
		{
			var range = Selections[i];
			bool isPrimary = i == Selections.PrimaryIndex;

			if (isPrimary)
			{
				primaryIndex = ranges.Count;
			}
			ranges.Add(range);

			var headCoord = GetCoordinate(range.Head);
			var anchorCoord = GetCoordinate(range.Anchor);

			int minLine = Math.Min(headCoord.Line, anchorCoord.Line);
			int maxLine = Math.Max(headCoord.Line, anchorCoord.Line);
			int height = maxLine - minLine + 1;

			int sels = 0;
			int step = 1;

			while (sels < count)
			{
				int lineDelta = step * height;

				int targetAnchorLine = direction == CaretVerticalDirection.Down
					? anchorCoord.Line + lineDelta
					: anchorCoord.Line - lineDelta;

				int targetHeadLine = direction == CaretVerticalDirection.Down
					? headCoord.Line + lineDelta
					: headCoord.Line - lineDelta;

				if (targetAnchorLine < 0 || targetHeadLine < 0 ||
					targetAnchorLine >= LineCount || targetHeadLine >= LineCount)
				{
					break;
				}

				if (!range.HasActiveSelection)
				{
					if (CanAlign(headCoord, new(0, targetHeadLine), out int alignedHeadCol))
					{
						var newPos = new Coordinate(alignedHeadCol, targetHeadLine);
						if (isPrimary)
						{
							primaryIndex = ranges.Count;
						}
						ranges.Add(new Range(GetOffset(newPos), range.TrailVisualLine));
						sels++;
					}
				}
				else
				{
					if (CanAlign(anchorCoord, new(0, targetAnchorLine), out int alignedAnchorCol) &&
						CanAlign(headCoord, new(0, targetHeadLine), out int alignedHeadCol))
					{
						var newAnchor = new Coordinate(alignedAnchorCol, targetAnchorLine);
						var newHead = new Coordinate(alignedHeadCol, targetHeadLine);

						if (isPrimary)
						{
							primaryIndex = ranges.Count;
						}
						ranges.Add(new Range(GetOffset(newAnchor), GetOffset(newHead), range.TrailVisualLine));
						sels++;
					}
				}

				if (direction == CaretVerticalDirection.Up &&
					targetAnchorLine == 0 && targetHeadLine == 0)
				{
					break;
				}

				step++;
			}
		}
		Selections = new(ranges, primaryIndex);
	}

	// Checks if two raw buffer coordinates align vertically
	// This is probably slow, but the easiest way to do this is just to layout the lines without wrapping and then we can trivially compare the visual columns
	private bool CanAlign(Coordinate root, Coordinate other, out int alignedLogicalColumnInOther)
	{
		alignedLogicalColumnInOther = -1;
		if (_nonWrappingLineCache is null) return false;
		var rootLayout = _nonWrappingLineCache.GetOrCreate(root.Line);
		var otherLayout = _nonWrappingLineCache.GetOrCreate(other.Line);
		var rootVisualCol = rootLayout!.GetVisualCoordinate(root).VisualCol;

		//Binary search for a logical offset with the same root visual col
		int low = 0;
		int high = otherLayout!.GraphemeBoundaries.Count - 1;
		while (low <= high)
		{
			int mid = low + (high - low) / 2;
			var visualCol = otherLayout
				.GetVisualCoordinate(new(mid, other.Line)).VisualCol;

			if (visualCol == rootVisualCol)
			{
				alignedLogicalColumnInOther = mid;
				return true;
			}
			if (visualCol < rootVisualCol)
				low = mid + 1;
			else
				high = mid - 1;
		}
		return false;
	}

	public void CollapseCursors()
	{
		List<Range> ranges = [];
		for (int i = 0; i < Selections.Count; i++)
		{
			var range = Selections[i];
			if (i != Selections.PrimaryIndex) continue;
			ranges.Add(range);
		}
		Selections = new(ranges);
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
